using System.Collections.Generic;
using UnityEngine;

namespace NOXMFD
{
    // Live target-lock TTI estimator shared by the native HUD cue and the TGT telemetry rows.
    // It scans the player's own in-flight guided weapons and returns the shortest current
    // range/closing-speed estimate for the requested locked target.
    internal static class TargetTtiEstimator
    {
        // TEMPORARY diagnostic (issue #67 follow-up: TTI still not showing for bomb drops after
        // widening the match to MissileSeeker.targetUnit). Logs once per missile instance — keyed
        // by its persistentID, so a long session doesn't repeat the same line every 4 Hz poll —
        // reporting exactly what this estimator sees for each of the player's own in-flight guided
        // weapons: seeker type, Missile.targetID, and the reflected targetUnit. Remove once the
        // real failure point (never gets a target at all vs. gets one but ID mismatch vs. something
        // else) is confirmed from a real bomb-drop log. LogInfo, not LogDebug — visible without
        // raising the BepInEx log level.
        private static readonly HashSet<uint> _loggedMissileIds = new HashSet<uint>();

        internal static float ComputeTti(uint targetId, uint playerId)
        {
            if (!TargetUnitLookup.TryResolve(targetId, out Unit target)) return -1f;

            float best = -1f;
            foreach (Unit u in UnitRegistry.allUnits)
            {
                if (u is not Missile m || m.disabled) continue;
                if (m.ownerID.Id != playerId) continue;
                LogDiagnosticOnce(m);
                if (!IsAssignedTo(m, targetId)) continue;

                float t = EstimateImpactTime(m, target);
                if (t >= 0f && (best < 0f || t < best)) best = t;
            }
            return best;
        }

        private static void LogDiagnosticOnce(Missile m)
        {
            uint id = m.persistentID.Id;
            if (!_loggedMissileIds.Add(id)) return;

            string seekerType = "none";
            string targetUnitDesc = "n/a (no seeker component)";
            MissileSeeker? seeker = m.GetComponent<MissileSeeker>();
            if (seeker != null)
            {
                seekerType = seeker.GetSeekerType();
                Unit? tu = MissileSeekerAccess.GetTargetUnit(seeker);
                targetUnitDesc = tu == null ? "null" : $"{tu.persistentID.Id} ({tu.GetType().Name})";
            }
            Plugin.Log?.LogInfo(
                $"[NOXMFD] TTI diag: missile {id} seeker='{seekerType}' targetID={m.targetID.Id} " +
                $"(valid={m.targetID.IsValid}) targetUnit={targetUnitDesc}");
        }

        // Missile.targetID alone under-reports badly (see MissileSeekerAccess.cs): it only reflects
        // a live, currently-confirmed seeker track, cleared on every routine dropout and unset for a
        // radar missile's entire midcourse phase. The seeker's own persistent targetUnit survives
        // those gaps, so match on either -- an active lock (targetID) is always also a targetUnit
        // match (every SetTarget call passes targetUnit itself), so this only ever widens matches,
        // never narrows them; the targetID check stays as a cheap fast path and a fallback for the
        // rare case GetComponent<MissileSeeker> comes back empty.
        private static bool IsAssignedTo(Missile m, uint targetId)
        {
            if (m.targetID.Id == targetId) return true;
            MissileSeeker? seeker = m.GetComponent<MissileSeeker>();
            return seeker != null && MissileSeekerAccess.GetTargetUnit(seeker) is Unit tu && tu.persistentID.Id == targetId;
        }

        private static float EstimateImpactTime(Missile missile, Unit target)
        {
            if (missile.rb == null || target.rb == null) return -1f;
            GlobalPosition from = missile.GlobalPosition(), to = target.GlobalPosition();
            Vector3 relVel = missile.rb.velocity - target.rb.velocity;
            return HudTtiMath.TimeToImpact(from.x, from.y, from.z, to.x, to.y, to.z, relVel.x, relVel.y, relVel.z);
        }
    }
}
