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
            internal string       Callsign;
            internal int          Flight;
        }

        private static Role _role = Role.None;
        private static ulong  _leaderId;
        private static string _leaderName = string.Empty;

        // The squadron's own name, chosen by whoever starts it (SetCallsign, "X SQUAD" on the SQD
        // page). Empty until set — a squad can exist (via Invite's implicit creation) before its
        // leader gets around to naming it. Carried through every roster broadcast and a leadership
        // handoff so it's never lost partway through a squad's life; cleared in ResetToNone like
        // everything else here, by the same no-persistence design the whole class already has.
        private static string _callsign = string.Empty;

        // Squadron Callsign System (docs/squadron-transport.md's numbering section) — the flight
        // number the leader picks at CreateSquad time, 1-9. Editable later too (SetCallsign,
        // issue #47 follow-up) — re-numbering it re-numbers every member's own designation
        // immediately, since MEMBER never changes. Every member's own designation renders as
        // "<CALLSIGN> <FLIGHT>-<MEMBER>" (e.g. "TALON 1-2"), where MEMBER is the existing
        // join-order number (1 = leader) — this field only supplies the FLIGHT half. Carried
        // through every roster/invite/transfer envelope alongside the callsign.
        private static int _flight = 1;

        // For PlayerRoster.cs (issue #48, MAP squad-member tint) — every squadmate's SteamID,
        // NEVER including this pilot's own (own-ship already renders green regardless of faction,
        // MAP's own separate draw call, so it must never even be a candidate for the squad tint).
        // _members already excludes both the leader and self by construction (see the field's own
        // header comment above), so the only case needing a separate add is a MEMBER's own leader,
        // who isn't in _members at all.
        internal static IEnumerable<ulong> SquadmateSteamIds()
        {
            if (_role == Role.None) yield break;
            foreach (var m in _members) yield return m.Id;
            if (_role == Role.Member) yield return _leaderId;
        }

        // For RouteStore.cs to attribute an incoming shared route without the client having to pass
        // it through the payload itself — HandleData already only accepts data FROM the current
        // leader (from != _leaderId is rejected), so the leader's identity is already known
        // authoritatively server-side by the time a share arrives.
        internal static string LeaderName => _leaderName;

        // For TdStore.cs (Squad.IsLeader-gated leader-only actions) — no existing accessor exposes
        // Role itself since every prior caller only ever needed a specific action guarded inline.
        internal static bool IsLeader => _role == Role.Leader;

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

        // Us: invites we haven't answered yet, oldest first. A second (or third...) invite while
        // one is already pending used to silently replace it (last invite wins) — now it queues
        // instead, so a pilot never loses visibility of an earlier offer just because a later one
        // arrived. Accepting one clears the rest automatically (AcceptInvite declines them on the
        // pilot's behalf, since joining a squad is exclusive); each stays independently
        // accept/decline-able by its own leaderId in the meantime.
        private static readonly List<PendingInvite> _pendingReceived = new List<PendingInvite>();

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
                case "sqd.kick":            HandleKick(from); break;
                case "sqd.transfer":        HandleTransfer(from, payload); break;
                case "sqd.leader-changed":  HandleLeaderChanged(from, payload); break;
                case "sqd.data":            HandleData(from, payload); break;
                    // Unknown type: ignore rather than guess — a squadmate on a newer/older mod
                    // version may send a type this build doesn't understand yet.
            }
        }

        // ── Outbound actions (called from CommandDispatcher, main thread) ─────────

        // Explicitly starts a new squad with a chosen callsign and flight number — the SQD page's
        // own CREATE SQUAD button. Requires both up front rather than letting a squad exist unnamed
        // the way an earlier design (invite-implicitly-creates-a-squad) did; INVITE only appears on
        // the roster once this has made the pilot a leader. Flight is fixed for the squad's whole
        // life once set here — unlike the callsign, there is no later "change flight" action.
        internal static bool CreateSquad(string callsign, int flight)
        {
            if (_role != Role.None) return false;
            if (_pendingReceived.Count > 0) return false;   // decide our own pending invite(s) first
            string name = (callsign ?? string.Empty).Trim();
            if (name.Length == 0 || name.Length > 20) return false;
            if (flight < 1 || flight > 9) return false;
            _role = Role.Leader;
            _members.Clear();
            _callsign = name;
            _flight = flight;
            RebuildState();
            return true;
        }

        // Leader-only — CreateSquad must be called first (there is no more implicit squad-creation
        // via a first invite; see CreateSquad's own header comment).
        internal static bool Invite(ulong targetId, string targetName)
        {
            if (!Squadron.Ready || targetId == 0 || targetId == Squadron.SelfId()) return false;
            if (_role != Role.Leader) return false;
            if (ContainsMember(targetId) || _pendingSent.ContainsKey(targetId)) return true;   // idempotent

            _pendingSent[targetId] = new SentInvite { Name = targetName ?? string.Empty, SentAt = Time.unscaledTime };
            Squadron.OpenSession(targetId);
            Squadron.SendTo(targetId, "sqd.invite", InviteEnvelope());
            RebuildState();
            return true;
        }

        internal static bool AcceptInvite(ulong leaderId)
        {
            int idx = _pendingReceived.FindIndex(p => p.LeaderId == leaderId);
            if (idx < 0) return false;
            var inv = _pendingReceived[idx];
            _pendingReceived.RemoveAt(idx);

            // Joining one squad means declining every other outstanding offer — a pilot can only
            // ever be in one, so leaving the rest queued would just strand their senders waiting
            // out the full InviteTimeoutSeconds for no reason.
            foreach (var other in _pendingReceived) Squadron.SendTo(other.LeaderId, "sqd.decline", "{}");
            _pendingReceived.Clear();

            _role = Role.Member;
            _leaderId = inv.LeaderId;
            _leaderName = inv.LeaderName;
            _callsign = inv.Callsign;
            _flight = inv.Flight;
            _members.Clear();
            _members.AddRange(inv.Members);

            Squadron.SendTo(_leaderId, "sqd.accept", "{\"name\":\"" + Esc(SelfName()) + "\"}");
            RebuildState();
            return true;
        }

        internal static bool DeclineInvite(ulong leaderId)
        {
            int idx = _pendingReceived.FindIndex(p => p.LeaderId == leaderId);
            if (idx < 0) return false;
            _pendingReceived.RemoveAt(idx);
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

            Squadron.SendTo(newLeader, "sqd.transfer", TransferEnvelope(remaining));
            string leaderChanged = "{\"leaderId\":\"" + newLeader.ToString(CultureInfo.InvariantCulture) +
                                    "\",\"leaderName\":\"" + Esc(successor.Name) + "\"}";
            foreach (var m in remaining) Squadron.SendTo(m.Id, "sqd.leader-changed", leaderChanged);

            CancelAllPendingInvites();
            Squadron.CloseSessions(MemberIds());
            ResetToNone();
            return true;
        }

        // Renames the squadron and/or re-numbers its flight. Leader-only — CreateSquad already
        // requires both up front, so the only remaining use for this is the SQD page's EDIT button
        // on an existing squad. Flight is no longer fixed for the squad's whole life (issue #47
        // follow-up) — re-numbering it re-numbers every member's own "<CALLSIGN> <FLIGHT>-<MEMBER>"
        // designation immediately, since MEMBER (join order) never changes.
        internal static bool SetCallsign(string name, int flight)
        {
            if (_role != Role.Leader) return false;
            name = (name ?? string.Empty).Trim();
            if (name.Length == 0 || name.Length > 20) return false;
            if (flight < 1 || flight > 9) return false;
            _callsign = name;
            _flight = flight;
            RebuildState();
            BroadcastRoster();
            return true;
        }

        // Removes one member while the squad itself lives on — distinct from Disband (everyone) or
        // a member's own Leave (voluntary): this is the leader ending it for THEM specifically.
        // Tells the target via its own message (sqd.kick) rather than folding it into the next
        // sqd.roster broadcast — the remaining members learn about it that way already, but the
        // kicked pilot is no longer in _members by the time that broadcast goes out, so they'd never
        // see themselves drop off a roster they're not on anymore.
        internal static bool Kick(ulong memberId)
        {
            if (_role != Role.Leader) return false;
            int idx = RemoveMember(memberId);
            if (idx < 0) return false;
            // issue #47 follow-up audit's gap #3: the kicked member's own slot (idx + 2) and every
            // slot above it are now one position off from what td.js's squadSlots() will compute
            // against the shrunk roster — fix TdStore's assignments to match before anything reads
            // them (a poll, a DESIGNATE) with the old numbering.
            TdStore.RenumberAfterMemberRemoved(idx + 2);
            Squadron.SendTo(memberId, "sqd.kick", "{}");
            Squadron.CloseSession(memberId);
            BroadcastRoster();
            RebuildState();
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

        // Single-recipient sibling of SendData above (issue #47's Target Designator: different
        // members get different target sets, so a broadcast doesn't fit) — same envelope shape,
        // SendTo instead of SendToAll.
        internal static bool SendDataTo(ulong memberId, string dataType, string dataPayload)
        {
            if (_role != Role.Leader || !ContainsMember(memberId)) return false;
            string envelope = "{\"type\":\"" + Esc(dataType ?? string.Empty) +
                               "\",\"payload\":\"" + Esc(dataPayload ?? string.Empty) + "\"}";
            Squadron.SendTo(memberId, "sqd.data", envelope);
            return true;
        }

        // ── Inbound handlers ───────────────────────────────────────────────────────

        private static void HandleInvite(ulong from, string payload)
        {
            var obj = JsonLite.Parse(payload) as Dictionary<string, object?>;
            string leaderName = Str(obj, "leaderName");
            string callsign = Str(obj, "callsign");
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

            int flight = IntField(obj, "flight");
            var invite = new PendingInvite { LeaderId = from, LeaderName = leaderName, Members = members, Callsign = callsign, Flight = flight };
            // A second invite from the SAME leader (a retry, or their roster changed before we
            // answered) refreshes our copy in place rather than queuing a duplicate; a different
            // leader's invite queues alongside whatever's already pending.
            int idx = _pendingReceived.FindIndex(p => p.LeaderId == from);
            if (idx >= 0) _pendingReceived[idx] = invite; else _pendingReceived.Add(invite);
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
            _callsign = Str(obj, "callsign");
            _flight = IntField(obj, "flight");
            _members.Clear();
            _members.AddRange(ParseMembers(obj != null && obj.TryGetValue("members", out object? mv) ? mv : null));
            RebuildState();
        }

        private static void HandleLeave(ulong from)
        {
            if (_role != Role.Leader) return;
            int idx = RemoveMember(from);
            if (idx < 0) return;
            // Same renumbering as Kick() above — a voluntary leave shrinks the roster the same way.
            TdStore.RenumberAfterMemberRemoved(idx + 2);
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

        // Same treatment as HandleDisband — the squad this pilot was in is over FOR THEM either
        // way, so the read-only/pending-share cleanup (OnSquadEnded, now inside ResetToNone) applies
        // here too.
        private static void HandleKick(ulong from)
        {
            if (_role != Role.Member || from != _leaderId) return;
            Squadron.CloseSession(_leaderId);
            ResetToNone();
            SetNotice("You were removed from the squad by the leader.");
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
            _callsign = Str(obj, "callsign");
            _flight = IntField(obj, "flight");
            _members.Clear();
            _members.AddRange(members);
            _pendingSent.Clear();
            foreach (var m in _members) Squadron.OpenSession(m.Id);
            // This pilot's own MEMBER-side state (issue #47 follow-up audit's gap #2) — a received
            // designation snapshot, a route locked read-only from the old leader — is now stale: the
            // old leader is gone and this pilot is about to start acting as leader instead. Same
            // "the old relationship is over, whether or not the squad itself lives on" reasoning
            // HandleLeaderChanged already applies for every OTHER remaining member; the successor is
            // the one member who doesn't go through that path, so it has to happen here instead.
            RouteStore.OnSquadEnded();
            TdStore.OnSquadEnded();
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
            // Same treatment as the squad actually ending (OnSquadEnded's own header has the full
            // reasoning): SendData is leader-only, so the OLD leader just lost the ability to ever
            // push another update, permanently, whether or not the squad itself lives on.
            RouteStore.OnSquadEnded();
            TdStore.OnSquadEnded();
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

        // The one place squad membership actually ends (every path below funnels here) — so it's
        // also the one place to reset every OTHER subsystem scoped to "this pilot is in a squad"
        // (issue #47 follow-up audit). Before this, RouteStore.OnSquadEnded() was called ad hoc at
        // some call sites and missing from others (RelinquishLeadership, Disband, a leader leaving
        // with no members) — centralizing it here means every squad-ending path gets it for free,
        // the same guarantee TdStore.OnSquadEnded() already had. _notice/_noticeSeq intentionally
        // is NOT touched: SetNotice already gates display on `state.notice` being non-empty
        // (sqd.js), so clearing _notice here (not the monotonic _noticeSeq — resetting a sequence
        // counter risks a genuinely new future notice colliding with one the browser already saw)
        // stops a stale disband/kick notice from re-toasting on a page that loads fresh later,
        // possibly in a different mission, without disturbing the seq's own contract.
        private static void ResetToNone()
        {
            _role = Role.None;
            _leaderId = 0;
            _leaderName = string.Empty;
            _callsign = string.Empty;
            _flight = 1;
            _members.Clear();
            _pendingSent.Clear();
            _pendingReceived.Clear();
            _notice = string.Empty;
            TdStore.OnSquadEnded();
            RouteStore.OnSquadEnded();
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

        // Returns the removed member's index (0-based, matching _members' own join order), or -1 if
        // not found. Callers that also need to fix up TdStore's positional slot assignments (issue
        // #47 follow-up audit's gap #3 — slot = index + 2, td.js's own squadSlots() computes the
        // same number) need the index, not just a bool.
        private static int RemoveMember(ulong id)
        {
            for (int i = 0; i < _members.Count; i++)
            {
                if (_members[i].Id == id) { _members.RemoveAt(i); return i; }
            }
            return -1;
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

        // This pilot's own current aircraft type (unitName), same field the map icon and HUD
        // readouts already key off — "" whenever there's nothing to report (dead, ejected, not
        // spawned yet, or between missions). Used for SQD's own roster row when leading a squad;
        // a MEMBER's own row instead comes through PlayerRoster.AircraftFor like everyone else's,
        // since state.members already carries self when viewing as a member (see MembersJsonServed).
        private static string SelfAircraftUnitName() =>
            GameManager.GetLocalAircraft(out Aircraft ac) && ac != null && ac.definition != null
                ? (ac.definition.unitName ?? string.Empty) : string.Empty;

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

        // Same shape as MembersJson PLUS each member's current aircraft — SERVED view only (never
        // sent over Squadron: aircraft is a plain Player.Aircraft SyncVar every client can already
        // read locally via PlayerRoster, so there's nothing useful to relay peer-to-peer, and a
        // value relayed from someone else's client wouldn't be trustworthy anyway). Recomputed
        // fresh from PlayerRoster.AircraftFor on every /squad poll, same "" -> empty-column
        // fallback SQD's own header comment on this feature describes.
        private static string MembersJsonServed(IEnumerable<Member> members)
        {
            var sb = new StringBuilder("[");
            bool first = true;
            foreach (var m in members)
            {
                if (!first) sb.Append(',');
                first = false;
                sb.Append("{\"id\":\"").Append(m.Id.ToString(CultureInfo.InvariantCulture))
                  .Append("\",\"name\":\"").Append(Esc(m.Name))
                  .Append("\",\"aircraft\":\"").Append(Esc(PlayerRoster.AircraftFor(m.Id))).Append("\"}");
            }
            sb.Append(']');
            return sb.ToString();
        }

        // Leadership handoff (sqd.transfer) — carries the callsign and flight along too, so the new
        // leader's squad keeps its identity instead of reverting to unnamed/flight 1.
        private static string TransferEnvelope(IEnumerable<Member> members) =>
            "{\"members\":" + MembersJson(members) + ",\"callsign\":\"" + Esc(_callsign) +
            "\",\"flight\":" + _flight.ToString(CultureInfo.InvariantCulture) + "}";

        private static string RosterEnvelope() =>
            "{\"leaderId\":\"" + Squadron.SelfId().ToString(CultureInfo.InvariantCulture) +
            "\",\"leaderName\":\"" + Esc(SelfName()) +
            "\",\"callsign\":\"" + Esc(_callsign) +
            "\",\"flight\":" + _flight.ToString(CultureInfo.InvariantCulture) +
            ",\"members\":" + MembersJson(_members) + "}";

        private static string InviteEnvelope() =>
            "{\"leaderId\":\"" + Squadron.SelfId().ToString(CultureInfo.InvariantCulture) +
            "\",\"leaderName\":\"" + Esc(SelfName()) +
            "\",\"callsign\":\"" + Esc(_callsign) +
            "\",\"flight\":" + _flight.ToString(CultureInfo.InvariantCulture) +
            ",\"members\":" + MembersJson(_members) + "}";

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

        // JsonLite parses every number as a plain double (see its own header comment) — used for
        // the flight number carried in invite/roster/transfer envelopes. Falls back to 1 (a valid
        // flight) rather than 0 so a stale/older peer that never sends the field still parses to
        // something CreateSquad itself would have accepted.
        private static int IntField(Dictionary<string, object?>? obj, string key) =>
            obj != null && obj.TryGetValue(key, out object? v) && v is double d ? (int)d : 1;

        private static ulong ULongOf(string s) =>
            ulong.TryParse(s, NumberStyles.None, CultureInfo.InvariantCulture, out ulong v) ? v : 0;

        private static void RebuildState() { StateJson = BuildStateJson(); }

        private static string BuildStateJson()
        {
            string roleStr = _role switch { Role.Leader => "leader", Role.Member => "member", _ => "none" };
            var sb = new StringBuilder();
            sb.Append("{\"role\":\"").Append(roleStr).Append('"');
            sb.Append(",\"self\":\"").Append(Squadron.SelfId().ToString(CultureInfo.InvariantCulture)).Append('"');
            // This pilot's own display name — only the LEADER case actually needs it (a member
            // already gets their own name back via the roster's members list, same as everyone
            // else's), but it costs nothing to always include, so the client doesn't need to know
            // which role makes it meaningful.
            sb.Append(",\"selfName\":\"").Append(Esc(SelfName())).Append('"');
            // Same "only meaningful while leading" caveat as selfName — SelfAircraftUnitName's own
            // header comment covers why a MEMBER's own row doesn't need this field at all.
            sb.Append(",\"selfAircraft\":\"").Append(Esc(SelfAircraftUnitName())).Append('"');
            sb.Append(",\"leaderId\":\"").Append(_leaderId.ToString(CultureInfo.InvariantCulture)).Append('"');
            sb.Append(",\"leaderName\":\"").Append(Esc(_leaderName)).Append('"');
            // The LEADER's aircraft, as seen through THIS pilot's own same-faction visibility — only
            // meaningful as a MEMBER (as leader, the equivalent value is selfAircraft above); the
            // leader row never comes from MembersJsonServed at all (_members excludes the leader by
            // definition — see the field's own header comment), so it needs its own lookup here.
            sb.Append(",\"leaderAircraft\":\"")
              .Append(Esc(_role == Role.Member ? PlayerRoster.AircraftFor(_leaderId) : string.Empty)).Append('"');
            sb.Append(",\"callsign\":\"").Append(Esc(_callsign)).Append('"');
            sb.Append(",\"flight\":").Append(_flight.ToString(CultureInfo.InvariantCulture));
            sb.Append(",\"members\":").Append(MembersJsonServed(_members));
            sb.Append(",\"pendingInvites\":[");
            bool firstInv = true;
            foreach (var inv in _pendingReceived)
            {
                if (!firstInv) sb.Append(',');
                firstInv = false;
                sb.Append("{\"leaderId\":\"").Append(inv.LeaderId.ToString(CultureInfo.InvariantCulture))
                  .Append("\",\"leaderName\":\"").Append(Esc(inv.LeaderName))
                  .Append("\",\"members\":").Append(MembersJson(inv.Members)).Append('}');
            }
            sb.Append(']');
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
