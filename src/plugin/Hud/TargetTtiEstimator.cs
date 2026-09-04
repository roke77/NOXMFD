using UnityEngine;

namespace NOXMFD
{
    // Live target-lock TTI estimator shared by the native HUD cue and the TGT telemetry rows.
    // It scans the player's own in-flight guided weapons and returns the shortest current
    // range/closing-speed estimate for the requested locked target.
    internal static class TargetTtiEstimator
    {
        internal static float ComputeTti(uint targetId, uint playerId)
        {
            if (!TargetUnitLookup.TryResolve(targetId, out Unit target)) return -1f;

            float best = -1f;
            foreach (Unit u in UnitRegistry.allUnits)
            {
                if (u is not Missile m || m.disabled) continue;
                if (m.ownerID.Id != playerId || !IsAssignedTo(m, targetId)) continue;

                float t = EstimateImpactTime(m, target);
                if (t >= 0f && (best < 0f || t < best)) best = t;
            }
            return best;
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
