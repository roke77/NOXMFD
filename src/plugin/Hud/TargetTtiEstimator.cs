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
            if (targetId == 0) return -1f;
            if (!UnitRegistry.TryGetUnit(new PersistentID { Id = targetId }, out Unit target) ||
                target == null || target.disabled)
                return -1f;

            float best = -1f;
            foreach (Unit u in UnitRegistry.allUnits)
            {
                if (u is not Missile m || m.disabled) continue;
                if (m.ownerID.Id != playerId || m.targetID.Id != targetId) continue;

                float t = EstimateImpactTime(m, target);
                if (t >= 0f && (best < 0f || t < best)) best = t;
            }
            return best;
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
