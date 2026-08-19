using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Steamworks;
using UnityEngine;

namespace NOXMFD
{
    // ── Squad protocol ───────────────────────────────────────────────────────────
    // Leader/member squad state built on top of Squadron.cs's raw Steam messaging
    // (docs/squadron-transport.md). Squadron.cs only moves bytes between two SteamIDs; this class
    // decides what those bytes MEAN — who can invite whom, who trusts whose roster broadcast, and
    // what "you're already in a squad" means. No persistence: everything here resets on plugin
    // restart, by design.
    //
    // Topology is a star centred on the leader: the leader holds an open Steam session with every
    // member, and members only ever talk to the leader, never to each other. This keeps the
    // membership graph simple (only the leader needs everyone else's SteamID) and matches the
    // "leader shares data with members" shape of every feature the transport doc lists.
    //
    // A player can be in at most one squad. Since there is no server, that rule is enforced socially:
    // every invite target checks their OWN local state and rejects (with a warning to their real
    // leader) rather than the sender ever being able to see it authoritatively — see HandleInvite.
    //
    // Leader dropout (crash, alt-F4, force-quit) is a known, accepted gap for v1: succession only
    // fires on a graceful Leave()/RelinquishLeadership(). An abruptly-vanished leader leaves the
    // squad stuck until members disband and re-form. No liveness/heartbeat check exists to detect it.
    internal static class Squad
    {
        internal enum Role { None, Leader, Member }

        internal sealed class Member
        {
            internal Member(ulong id, string name) { Id = id; Name = name; }
            internal ulong  Id   { get; }
            internal string Name { get; }
        }

        private struct PendingInvite
        {
            internal ulong        LeaderId;
            internal string       LeaderName;
            internal List<Member> Members;
        }

        private static Role _role = Role.None;
        private static ulong  _leaderId;
        private static string _leaderName = string.Empty;

        // For RouteStore.cs to attribute an incoming shared route without the client having to pass
        // it through the payload itself — HandleData already only accepts data FROM the current
        // leader (from != _leaderId is rejected), so the leader's identity is already known
        // authoritatively server-side by the time a share arrives.
        internal static string LeaderName => _leaderName;

        // Leader: who they lead. Member: the rest of the squad (not the leader, not self) — kept
        // current by the leader's sqd.roster broadcasts.
        private static readonly List<Member> _members = new List<Member>();

        private struct SentInvite
        {
            internal string Name;
            internal float  SentAt;   // Time.unscaledTime — for the timeout in CheckInviteTimeouts
        }

        // Leader only: invites sent, awaiting sqd.accept/sqd.decline/sqd.conflict, or a timeout if
        // the target never responds at all — e.g. they don't have the mod installed, so nothing in
        // their game ever answers the Steam session request. Index 0 of _members is always the
        // OLDEST accepted member (Add() only ever appends), which is what makes auto-succession by
        // join order a non-issue — no separate timestamp needed there.
        private static readonly Dictionary<ulong, SentInvite> _pendingSent = new Dictionary<ulong, SentInvite>();

        // How long an invite waits for ANY response before the leader gives up on it. There is no
        // delivery acknowledgment at the transport level, so a target with no mod installed looks
        // identical to one that's simply thinking it over until this fires.
        private const float InviteTimeoutSeconds = 15f;

        // Us: an invite we haven't answered yet. Only one at a time — a second invite while one is
        // pending simply replaces it (last invite wins); there is no queue.
        private static PendingInvite? _pendingReceived;

        // Server-thread-readable cache, same threading contract as RouteStore.RoutesJson: every
        // mutator below runs on the Unity main thread only (Drain(), or a command handler via
        // CommandDispatcher.Drain), and rebuilds this string synchronously as its last step.
        internal static volatile string StateJson = BuildStateJson();

        // A single most-recent notice (poach warning, invite conflict, squad disbanded) plus a
        // monotonic sequence — the browser shows a toast when it sees a new sequence, no history
        // needed beyond "the latest one." Folded into StateJson rather than a separate inbox.
        private static long   _noticeSeq;
        private static string _notice = string.Empty;

        // Leader-shared application payloads (docs/squadron-transport.md's "TBD data" — wpt.route is
        // the first experiment). A member-facing inbox, same Since(afterSeq) shape Squadron.cs uses,
        // kept separate from StateJson because these are discrete events, not state.
        private static readonly object _dataLock = new object();
        private static readonly List<(long Seq, string Type, string Payload)> _dataInbox = new List<(long, string, string)>();
        private static long _dataSeq;
        private const int DataInboxCapacity = 32;

        private static long _drainedSeq;   // our cursor into Squadron's raw inbox

        // ── Drain ────────────────────────────────────────────────────────────────
        // Called once per frame from MissionLifecycle, right after Squadron.Poll() — so squad
        // traffic (like the transport under it) flows at the main menu too, not only in a mission.
        internal static void Drain()
        {
            var inbound = Squadron.Since(_drainedSeq);
            foreach (var m in inbound)
            {
                _drainedSeq = m.Seq;
                try { Handle(m.From, m.Type, m.Payload); }
                catch (Exception ex) { Plugin.Log?.LogWarning($"[NOXMFD] Squad handle '{m.Type}' from {m.From} threw: {ex.Message}"); }
            }
            CheckInviteTimeouts();
        }

        // Drops any sent invite nobody has answered within InviteTimeoutSeconds. Cheap no-op when
        // there's nothing pending — only a leader with outstanding invites ever populates this dict.
        private static void CheckInviteTimeouts()
        {
            if (_pendingSent.Count == 0) return;
            float now = Time.unscaledTime;
            List<ulong>? expired = null;
            foreach (var kv in _pendingSent)
            {
                if (now - kv.Value.SentAt >= InviteTimeoutSeconds) (expired ??= new List<ulong>()).Add(kv.Key);
            }
            if (expired == null) return;
            foreach (ulong id in expired)
            {
                string name = _pendingSent[id].Name;
                _pendingSent.Remove(id);
                Squadron.CloseSession(id);
                SetNotice($"{name} didn't respond to the invite — they may not have the mod installed.");
            }
        }

        private static void Handle(ulong from, string type, string payload)
        {
            switch (type)
            {
                case "sqd.invite":          HandleInvite(from, payload); break;
                case "sqd.accept":          HandleAccept(from, payload); break;
                case "sqd.decline":         HandleDecline(from); break;
                case "sqd.conflict":        HandleConflict(from, payload); break;
                case "sqd.poach":           HandlePoach(from, payload); break;
                case "sqd.roster":          HandleRoster(from, payload); break;
                case "sqd.leave":           HandleLeave(from); break;
                case "sqd.disband":         HandleDisband(from); break;
                case "sqd.transfer":        HandleTransfer(from, payload); break;
                case "sqd.leader-changed":  HandleLeaderChanged(from, payload); break;
                case "sqd.data":            HandleData(from, payload); break;
                    // Unknown type: ignore rather than guess — a squadmate on a newer/older mod
                    // version may send a type this build doesn't understand yet.
            }
        }

        // ── Outbound actions (called from CommandDispatcher, main thread) ─────────

        // Inviting when not yet in a squad implicitly creates one and makes the inviter its leader —
        // there is no separate "create squad" step, matching the user-facing flow: the leader's
        // squad comes into being as the side effect of adding the first member.
        internal static bool Invite(ulong targetId, string targetName)
        {
            if (!Squadron.Ready || targetId == 0 || targetId == Squadron.SelfId()) return false;
            if (_role == Role.Member) return false;   // a plain member cannot invite; only a leader can
            if (_pendingReceived != null) return false;   // decide our own pending invite first
            if (_role == Role.None) { _role = Role.Leader; _members.Clear(); }
            if (ContainsMember(targetId) || _pendingSent.ContainsKey(targetId)) return true;   // idempotent

            _pendingSent[targetId] = new SentInvite { Name = targetName ?? string.Empty, SentAt = Time.unscaledTime };
            Squadron.OpenSession(targetId);
            Squadron.SendTo(targetId, "sqd.invite", InviteEnvelope());
            RebuildState();
            return true;
        }

        internal static bool AcceptInvite()
        {
            if (_pendingReceived == null) return false;
            var inv = _pendingReceived.Value;
            _role = Role.Member;
            _leaderId = inv.LeaderId;
            _leaderName = inv.LeaderName;
            _members.Clear();
            _members.AddRange(inv.Members);
            _pendingReceived = null;

            Squadron.SendTo(_leaderId, "sqd.accept", "{\"name\":\"" + Esc(SelfName()) + "\"}");
            RebuildState();
            return true;
        }

        internal static bool DeclineInvite()
        {
            if (_pendingReceived == null) return false;
            ulong leaderId = _pendingReceived.Value.LeaderId;
            _pendingReceived = null;
            Squadron.SendTo(leaderId, "sqd.decline", "{}");
            RebuildState();
            return true;
        }

        // Plain member leaving, or a leader with no other members (nothing to hand off).
        internal static bool Leave()
        {
            if (_role == Role.Member)
            {
                Squadron.SendTo(_leaderId, "sqd.leave", "{}");
                Squadron.CloseSession(_leaderId);
                ResetToNone();
                return true;
            }
            if (_role == Role.Leader && _members.Count == 0)
            {
                CancelAllPendingInvites();
                ResetToNone();
                return true;
            }
            return false;   // a leader WITH members must call RelinquishLeadership instead
        }

        // Leader leaving while members remain — hands off leadership first. successorId == null
        // means auto-pick: _members[0] is always the oldest-accepted member (Add() only appends).
        internal static bool RelinquishLeadership(ulong? successorId)
        {
            if (_role != Role.Leader || _members.Count == 0) return false;
            ulong newLeader = successorId ?? _members[0].Id;
            Member? successor = FindMember(newLeader);
            if (successor == null) return false;

            var remaining = new List<Member>();
            foreach (var m in _members) if (m.Id != newLeader) remaining.Add(m);

            Squadron.SendTo(newLeader, "sqd.transfer", MembersEnvelope(remaining));
            string leaderChanged = "{\"leaderId\":\"" + newLeader.ToString(CultureInfo.InvariantCulture) +
                                    "\",\"leaderName\":\"" + Esc(successor.Name) + "\"}";
            foreach (var m in remaining) Squadron.SendTo(m.Id, "sqd.leader-changed", leaderChanged);

            CancelAllPendingInvites();
            Squadron.CloseSessions(MemberIds());
            ResetToNone();
            return true;
        }

        internal static bool Disband()
        {
            if (_role != Role.Leader) return false;
            Squadron.SendToAll(MemberIds(), "sqd.disband", "{}");
            CancelAllPendingInvites();
            Squadron.CloseSessions(MemberIds());
            ResetToNone();
            return true;
        }

        // The generic "TBD payload" slot (docs/squadron-transport.md) — leader-only, broadcast to
        // every current member. wpt.route is the first concrete use (WPT's share button).
        internal static bool SendData(string dataType, string dataPayload)
        {
            if (_role != Role.Leader || _members.Count == 0) return false;
            string envelope = "{\"type\":\"" + Esc(dataType ?? string.Empty) +
                               "\",\"payload\":\"" + Esc(dataPayload ?? string.Empty) + "\"}";
            Squadron.SendToAll(MemberIds(), "sqd.data", envelope);
            return true;
        }

        // ── Inbound handlers ───────────────────────────────────────────────────────

        private static void HandleInvite(ulong from, string payload)
        {
            var obj = JsonLite.Parse(payload) as Dictionary<string, object?>;
            string leaderName = Str(obj, "leaderName");
            List<Member> members = ParseMembers(obj != null && obj.TryGetValue("members", out object? mv) ? mv : null);

            if (_role != Role.None)
            {
                // Already in a squad (as leader or member) — reject, and if we're a MEMBER, warn our
                // actual leader that someone tried to recruit us out from under them.
                string ourLeaderName = _role == Role.Leader ? SelfName() : _leaderName;
                Squadron.SendTo(from, "sqd.conflict", "{\"currentLeaderName\":\"" + Esc(ourLeaderName) + "\"}");
                if (_role == Role.Member)
                {
                    string poach = "{\"memberId\":\"" + Squadron.SelfId().ToString(CultureInfo.InvariantCulture) +
                                   "\",\"memberName\":\"" + Esc(SelfName()) +
                                   "\",\"byId\":\"" + from.ToString(CultureInfo.InvariantCulture) +
                                   "\",\"byName\":\"" + Esc(leaderName) + "\"}";
                    Squadron.SendTo(_leaderId, "sqd.poach", poach);
                }
                return;
            }

            _pendingReceived = new PendingInvite { LeaderId = from, LeaderName = leaderName, Members = members };
            RebuildState();
        }

        private static void HandleAccept(ulong from, string payload)
        {
            if (_role != Role.Leader || !_pendingSent.ContainsKey(from)) return;
            var obj = JsonLite.Parse(payload) as Dictionary<string, object?>;
            string name = Str(obj, "name");
            _pendingSent.Remove(from);
            if (!ContainsMember(from)) _members.Add(new Member(from, name));
            BroadcastRoster();
            RebuildState();
        }

        private static void HandleDecline(ulong from)
        {
            if (_role != Role.Leader || !_pendingSent.Remove(from)) return;
            Squadron.CloseSession(from);
            RebuildState();
        }

        private static void HandleConflict(ulong from, string payload)
        {
            if (_role != Role.Leader || !_pendingSent.TryGetValue(from, out SentInvite pending)) return;
            var obj = JsonLite.Parse(payload) as Dictionary<string, object?>;
            string currentLeader = Str(obj, "currentLeaderName");
            _pendingSent.Remove(from);
            Squadron.CloseSession(from);
            SetNotice($"{pending.Name} is already in {currentLeader}'s squad — invitation rejected.");
        }

        private static void HandlePoach(ulong from, string payload)
        {
            if (_role != Role.Leader) return;
            var obj = JsonLite.Parse(payload) as Dictionary<string, object?>;
            string memberName = Str(obj, "memberName");
            string byName     = Str(obj, "byName");
            if (!ContainsMember(from)) return;   // stale — not currently one of our members
            SetNotice($"{byName} tried to recruit {memberName}, who is already in your squad.");
        }

        private static void HandleRoster(ulong from, string payload)
        {
            if (_role != Role.Member || from != _leaderId) return;
            var obj = JsonLite.Parse(payload) as Dictionary<string, object?>;
            _leaderName = Str(obj, "leaderName");
            _members.Clear();
            _members.AddRange(ParseMembers(obj != null && obj.TryGetValue("members", out object? mv) ? mv : null));
            RebuildState();
        }

        private static void HandleLeave(ulong from)
        {
            if (_role != Role.Leader || !RemoveMember(from)) return;
            Squadron.CloseSession(from);
            BroadcastRoster();
            RebuildState();
        }

        private static void HandleDisband(ulong from)
        {
            if (_role != Role.Member || from != _leaderId) return;
            Squadron.CloseSession(_leaderId);
            ResetToNone();
            SetNotice("Your squad was disbanded by the leader.");
        }

        private static void HandleTransfer(ulong from, string payload)
        {
            // Anyone can send this, but it's only meaningful right after being named a successor —
            // there's no prior relationship to check it against, so accept it at face value. The
            // worst a bad actor gets from forging this is making us open sessions with a bogus
            // member list, which costs nothing (SendTo to a nonexistent/uninterested peer just fails).
            var obj = JsonLite.Parse(payload) as Dictionary<string, object?>;
            var members = ParseMembers(obj != null && obj.TryGetValue("members", out object? mv) ? mv : null);
            _role = Role.Leader;
            _leaderId = 0;
            _leaderName = string.Empty;
            _members.Clear();
            _members.AddRange(members);
            _pendingSent.Clear();
            foreach (var m in _members) Squadron.OpenSession(m.Id);
            BroadcastRoster();
            RebuildState();
        }

        private static void HandleLeaderChanged(ulong from, string payload)
        {
            if (_role != Role.Member || from != _leaderId) return;
            var obj = JsonLite.Parse(payload) as Dictionary<string, object?>;
            _leaderId = ULongOf(Str(obj, "leaderId"));
            _leaderName = Str(obj, "leaderName");
            // Roster stays as last known until the new leader's own sqd.roster confirms it.
            RebuildState();
        }

        private static void HandleData(ulong from, string payload)
        {
            if (_role != Role.Member || from != _leaderId) return;
            var obj = JsonLite.Parse(payload) as Dictionary<string, object?>;
            string dataType = Str(obj, "type");
            string dataPayload = Str(obj, "payload");
            if (dataType.Length == 0) return;
            lock (_dataLock)
            {
                _dataSeq++;
                _dataInbox.Add((_dataSeq, dataType, dataPayload));
                if (_dataInbox.Count > DataInboxCapacity) _dataInbox.RemoveRange(0, _dataInbox.Count - DataInboxCapacity);
            }
        }

        // ── Data inbox (for the HTTP/SSE layer) ───────────────────────────────────

        internal static List<(long Seq, string Type, string Payload)> DataSince(long afterSeq)
        {
            var outp = new List<(long, string, string)>();
            lock (_dataLock) { foreach (var m in _dataInbox) if (m.Seq > afterSeq) outp.Add(m); }
            return outp;
        }

        internal static long LatestDataSeq() { lock (_dataLock) { return _dataSeq; } }

        // ── Helpers ────────────────────────────────────────────────────────────────

        private static void BroadcastRoster() => Squadron.SendToAll(MemberIds(), "sqd.roster", RosterEnvelope());

        private static void CancelAllPendingInvites()
        {
            foreach (ulong id in _pendingSent.Keys) Squadron.CloseSession(id);
            _pendingSent.Clear();
        }

        private static void ResetToNone()
        {
            _role = Role.None;
            _leaderId = 0;
            _leaderName = string.Empty;
            _members.Clear();
            _pendingSent.Clear();
            _pendingReceived = null;
            RebuildState();
        }

        private static void SetNotice(string text)
        {
            _noticeSeq++;
            _notice = text;
            RebuildState();
        }

        private static bool ContainsMember(ulong id) => FindMember(id) != null;

        private static Member? FindMember(ulong id)
        {
            foreach (var m in _members) if (m.Id == id) return m;
            return null;
        }

        private static bool RemoveMember(ulong id)
        {
            for (int i = 0; i < _members.Count; i++)
            {
                if (_members[i].Id == id) { _members.RemoveAt(i); return true; }
            }
            return false;
        }

        private static IEnumerable<ulong> MemberIds()
        {
            foreach (var m in _members) yield return m.Id;
        }

        private static string SelfName()
        {
            try { return Squadron.Ready ? SteamFriends.GetPersonaName() : string.Empty; }
            catch { return string.Empty; }
        }

        private static string Esc(string s) => TelemetryServer.EscapeJson(s ?? string.Empty);

        private static string MembersJson(IEnumerable<Member> members)
        {
            var sb = new StringBuilder("[");
            bool first = true;
            foreach (var m in members)
            {
                if (!first) sb.Append(',');
                first = false;
                sb.Append("{\"id\":\"").Append(m.Id.ToString(CultureInfo.InvariantCulture))
                  .Append("\",\"name\":\"").Append(Esc(m.Name)).Append("\"}");
            }
            sb.Append(']');
            return sb.ToString();
        }

        private static string MembersEnvelope(IEnumerable<Member> members) =>
            "{\"members\":" + MembersJson(members) + "}";

        private static string RosterEnvelope() =>
            "{\"leaderId\":\"" + Squadron.SelfId().ToString(CultureInfo.InvariantCulture) +
            "\",\"leaderName\":\"" + Esc(SelfName()) +
            "\",\"members\":" + MembersJson(_members) + "}";

        private static string InviteEnvelope() =>
            "{\"leaderId\":\"" + Squadron.SelfId().ToString(CultureInfo.InvariantCulture) +
            "\",\"leaderName\":\"" + Esc(SelfName()) +
            "\",\"members\":" + MembersJson(_members) + "}";

        private static List<Member> ParseMembers(object? arr)
        {
            var result = new List<Member>();
            if (arr is List<object?> list)
            {
                foreach (var item in list)
                {
                    if (item is Dictionary<string, object?> d)
                    {
                        ulong id = ULongOf(Str(d, "id"));
                        if (id != 0) result.Add(new Member(id, Str(d, "name")));
                    }
                }
            }
            return result;
        }

        private static string Str(Dictionary<string, object?>? obj, string key) =>
            obj != null && obj.TryGetValue(key, out object? v) && v is string s ? s : string.Empty;

        private static ulong ULongOf(string s) =>
            ulong.TryParse(s, NumberStyles.None, CultureInfo.InvariantCulture, out ulong v) ? v : 0;

        private static void RebuildState() { StateJson = BuildStateJson(); }

        private static string BuildStateJson()
        {
            string roleStr = _role switch { Role.Leader => "leader", Role.Member => "member", _ => "none" };
            var sb = new StringBuilder();
            sb.Append("{\"role\":\"").Append(roleStr).Append('"');
            sb.Append(",\"self\":\"").Append(Squadron.SelfId().ToString(CultureInfo.InvariantCulture)).Append('"');
            sb.Append(",\"leaderId\":\"").Append(_leaderId.ToString(CultureInfo.InvariantCulture)).Append('"');
            sb.Append(",\"leaderName\":\"").Append(Esc(_leaderName)).Append('"');
            sb.Append(",\"members\":").Append(MembersJson(_members));
            if (_pendingReceived != null)
            {
                var inv = _pendingReceived.Value;
                sb.Append(",\"pendingInvite\":{\"leaderId\":\"").Append(inv.LeaderId.ToString(CultureInfo.InvariantCulture))
                  .Append("\",\"leaderName\":\"").Append(Esc(inv.LeaderName))
                  .Append("\",\"members\":").Append(MembersJson(inv.Members)).Append('}');
            }
            else sb.Append(",\"pendingInvite\":null");
            sb.Append(",\"pendingSent\":[");
            bool first = true;
            foreach (var kv in _pendingSent)
            {
                if (!first) sb.Append(',');
                first = false;
                sb.Append("{\"id\":\"").Append(kv.Key.ToString(CultureInfo.InvariantCulture))
                  .Append("\",\"name\":\"").Append(Esc(kv.Value.Name)).Append("\"}");
            }
            sb.Append(']');
            sb.Append(",\"noticeSeq\":").Append(_noticeSeq.ToString(CultureInfo.InvariantCulture));
            sb.Append(",\"notice\":\"").Append(Esc(_notice)).Append("\"}");
            return sb.ToString();
        }
    }
}
