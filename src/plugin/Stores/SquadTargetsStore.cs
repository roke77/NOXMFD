using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace NOXMFD
{
    // Issue #49 — HUD marks (Hud/HudSquadTargetMark.cs) showing which units the rest of the squad is
    // currently targeting. Star topology, same as everything else in docs/squadron-transport.md: a
    // non-leader member sends its own locked-target id set to the leader whenever it changes
    // (SquadTargets.cs ticks that); the leader aggregates its own ids plus every member's, and relays
    // the whole aggregate back out to every member. Every instance then has enough locally to answer,
    // per unit id, whether the leader is targeting it and whether any OTHER member is.
    //
    // Deliberately 100% BCL, no Squad/Unit/CommandDispatcher touchpoint — same testability seam
    // TdStore.cs/RouteStore.cs keep (tools/tests/NOXMFD.Tests.csproj compiles this file standalone).
    // SquadTargets.cs owns the live weapon-target-list read and the actual Squadron sends (via
    // Squad.cs, which alone owns the transport calls); this file only owns the aggregation/lookup.
    internal static class SquadTargetsStore
    {
        // This instance's own current lock set, as last told by SquadTargets.Tick(). Compared by
        // content on every SetSelfIds call so a steady lock (the common case, most ticks) costs one
        // set comparison and no broadcast — mirrors Presence.cs's own "only announce on tick" cadence
        // reasoning, just change-gated instead of interval-gated.
        private static HashSet<uint> _selfIds = new HashSet<uint>();

        // Leader-only: every member's last-reported lock set, keyed by their Steam id. Never includes
        // the leader's own ids (those live in _selfIds, same field whether this instance is leader or
        // member) or a departed member (RemoveMember, called from Squad.cs's Kick()/HandleLeave()).
        private static readonly Dictionary<ulong, HashSet<uint>> _memberIds = new Dictionary<ulong, HashSet<uint>>();

        // Member-only: the leader's own ids and the union of every OTHER member's ids (self already
        // excluded — ApplyAggregate is only ever called with THIS pilot's own Steam id to exclude), as
        // last relayed by the leader. Harmlessly unused while this instance is the leader itself: a
        // leader answers IsLeaderTargeting/IsOtherMemberTargeting from _selfIds/_memberIds directly
        // (see those methods below), never from these two.
        private static HashSet<uint> _relayedLeaderIds = new HashSet<uint>();
        private static HashSet<uint> _relayedOtherIds = new HashSet<uint>();

        // ── Self (every instance, leader or member) ────────────────────────────────

        // True if the set actually changed — the caller (SquadTargets.Tick) only broadcasts/relays on
        // a real change, not every tick.
        internal static bool SetSelfIds(HashSet<uint> ids)
        {
            if (_selfIds.SetEquals(ids)) return false;
            _selfIds = new HashSet<uint>(ids);
            return true;
        }

        // ── Leader-only ─────────────────────────────────────────────────────────────

        internal static bool SetMemberIds(ulong memberId, IEnumerable<uint> ids)
        {
            var next = new HashSet<uint>(ids);
            if (_memberIds.TryGetValue(memberId, out HashSet<uint>? prev) && prev.SetEquals(next)) return false;
            _memberIds[memberId] = next;
            return true;
        }

        internal static void RemoveMember(ulong memberId) => _memberIds.Remove(memberId);

        internal static string BuildAggregateJson()
        {
            var sb = new StringBuilder();
            sb.Append("{\"leader\":[").Append(string.Join(",", _selfIds.Select(id => id.ToString(CultureInfo.InvariantCulture)))).Append(']');
            sb.Append(",\"members\":{");
            bool first = true;
            foreach (var kv in _memberIds)
            {
                if (!first) sb.Append(',');
                first = false;
                sb.Append('"').Append(kv.Key.ToString(CultureInfo.InvariantCulture)).Append("\":[")
                  .Append(string.Join(",", kv.Value.Select(id => id.ToString(CultureInfo.InvariantCulture)))).Append(']');
            }
            sb.Append("}}");
            return sb.ToString();
        }

        // ── Member-only ─────────────────────────────────────────────────────────────

        // `selfSteamId` excludes this pilot's own entry from the relayed "members" map when building
        // _relayedOtherIds — the leader's aggregate lists every member including the one receiving it.
        internal static bool ApplyAggregate(string? json, ulong selfSteamId)
        {
            if (JsonLite.Parse(json ?? string.Empty) is not Dictionary<string, object?> obj) return false;
            var leaderIds = new HashSet<uint>();
            if (obj.TryGetValue("leader", out object? lv) && lv is List<object?> ll)
                foreach (object? item in ll) if (item is double d) leaderIds.Add(unchecked((uint)d));

            var otherIds = new HashSet<uint>();
            if (obj.TryGetValue("members", out object? mv) && mv is Dictionary<string, object?> members)
            {
                foreach (var kv in members)
                {
                    if (!ulong.TryParse(kv.Key, NumberStyles.Integer, CultureInfo.InvariantCulture, out ulong memberId)) continue;
                    if (memberId == selfSteamId) continue;   // never mark my own targets as "someone else's"
                    if (kv.Value is not List<object?> idList) continue;
                    foreach (object? item in idList) if (item is double d) otherIds.Add(unchecked((uint)d));
                }
            }

            _relayedLeaderIds = leaderIds;
            _relayedOtherIds = otherIds;
            return true;
        }

        // ── Query (HudSquadTargetMark.cs) ───────────────────────────────────────────

        // A leader instance answers from its own live state (never stale — it IS the source); a
        // member instance answers from the last relayed aggregate.
        internal static bool IsLeaderTargeting(uint id, bool isLeader) =>
            isLeader ? _selfIds.Contains(id) : _relayedLeaderIds.Contains(id);

        internal static bool IsOtherMemberTargeting(uint id, bool isLeader)
        {
            if (!isLeader) return _relayedOtherIds.Contains(id);
            foreach (var kv in _memberIds) if (kv.Value.Contains(id)) return true;
            return false;
        }

        // Cheap up-front check for HudSquadTargetMark.cs — lets it skip walking every native HUD
        // marker on a frame where nobody (leader or any other member) has anything locked at all,
        // which is the common case: outside a squad entirely, or in one where nobody's targeting
        // anything right now.
        internal static bool HasAnyRemoteTargets(bool isLeader)
        {
            if (isLeader)
            {
                if (_selfIds.Count > 0) return true;
                foreach (var kv in _memberIds) if (kv.Value.Count > 0) return true;
                return false;
            }
            return _relayedLeaderIds.Count > 0 || _relayedOtherIds.Count > 0;
        }

        // ── Squad lifecycle ──────────────────────────────────────────────────────────

        // Called from Squad.cs whenever this pilot's squad membership ends or changes leader
        // (ResetToNone / HandleLeaderChanged) — same reasoning TdStore.OnSquadEnded/
        // RouteStore.OnSquadEnded give: this data only means something within the squad session that
        // produced it.
        internal static void OnSquadEnded()
        {
            _selfIds = new HashSet<uint>();
            _memberIds.Clear();
            _relayedLeaderIds = new HashSet<uint>();
            _relayedOtherIds = new HashSet<uint>();
        }

        // Test-only: static fields are plugin-lifetime by design (same reasoning RouteStore.cs/
        // TdStore.cs's own ResetForTests give) — a standalone test project resets them between tests.
        internal static void ResetForTests() => OnSquadEnded();
    }
}
