using System.Collections.Generic;

namespace NOXMFD
{
    // Issue #49 — HUD marks (Hud/HudSquadTargetMark.cs) showing which units the rest of the squad is
    // currently targeting. This class is the live-game glue: reading the local weapon target list and
    // driving the actual Squad.cs outbound calls, the same split TdStore.cs/CommandDispatcher's
    // TdAcquireAll already keep between pure data (SquadTargetsStore.cs) and Unit/UnitRegistry access.
    internal static class SquadTargets
    {
        private static readonly HashSet<uint> _scratch = new HashSet<uint>();

        // Called once per second from TelemetryReader's slow tick, right next to Presence.Tick/
        // PlayerRoster.Refresh — same cadence, no dedicated timer of its own. A lock list only
        // changes when the pilot selects/deselects a target, so 1 Hz is ample (docs/squadron-
        // transport.md's own reasoning for why this whole family of updates is change-driven, not a
        // continuous stream).
        internal static void Tick()
        {
            if (!Squadron.Ready) return;
            if (!Squad.IsLeader && !Squad.IsMember) return;   // not in a squad — nothing to report

            _scratch.Clear();
            if (GameManager.GetLocalAircraft(out Aircraft ac) && ac != null && ac.weaponManager != null)
            {
                List<Unit> targets = ac.weaponManager.GetTargetList();
                if (targets != null)
                    foreach (Unit u in targets)
                        if (u != null) _scratch.Add(u.persistentID.Id);
            }

            if (!SquadTargetsStore.SetSelfIds(_scratch)) return;   // no change — nothing to send

            if (Squad.IsLeader) Squad.RelayLocksAggregate();
            else Squad.SendLocks(BuildIdsJson(_scratch));
        }

        private static string BuildIdsJson(HashSet<uint> ids)
        {
            var sb = new System.Text.StringBuilder("[");
            bool first = true;
            foreach (uint id in ids)
            {
                if (!first) sb.Append(',');
                first = false;
                sb.Append(id.ToString(System.Globalization.CultureInfo.InvariantCulture));
            }
            sb.Append(']');
            return sb.ToString();
        }
    }
}
