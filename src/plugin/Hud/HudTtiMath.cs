using System;

namespace NOXMFD
{
    // Pure time-to-impact math for HudTtiCue (issue #67) — kept free of Unity/game types, same
    // treatment TgpManualAimMath/HudDirectionCueMath already get, so tools/tests can pin the
    // geometry and formatting without a live Nuclear Option install.
    //
    // Mirrors the game's own incoming-missile TTI approximation (AIPilotCombatModes.EvadeModeRadar,
    // _scratch/full/): straight-line range over closing speed, floored so a non-closing or
    // barely-closing geometry doesn't produce a huge or negative time. Same shape, just applied to
    // our own outgoing weapon against our own focused target instead of an incoming missile
    // against us.
    internal static class HudTtiMath
    {
        private const float MinClosingSpeedMps = 1f;

        // fromX/Y/Z: the weapon's position. toX/Y/Z: the target's position. relVelX/Y/Z: the
        // weapon's velocity minus the target's velocity (weapon relative to target). Returns -1
        // when the two points coincide — nothing meaningful to divide by, same "no read" the
        // caller already gives a target that's gone or a weapon that isn't tracking anything.
        internal static float TimeToImpact(
            float fromX, float fromY, float fromZ,
            float toX, float toY, float toZ,
            float relVelX, float relVelY, float relVelZ)
        {
            float dx = toX - fromX, dy = toY - fromY, dz = toZ - fromZ;
            float distance = MathF.Sqrt(dx * dx + dy * dy + dz * dz);
            if (distance <= 0f) return -1f;

            float invDist = 1f / distance;
            float closingSpeed = MathF.Max(
                dx * invDist * relVelX + dy * invDist * relVelY + dz * invDist * relVelZ,
                MinClosingSpeedMps);
            return distance / closingSpeed;
        }

        // "M:SS". Not clamped to a max — a silly multi-minute value is still meaningful (a very
        // long-range shot), just an unlikely one.
        internal static string FormatTti(float seconds)
        {
            int total = (int)MathF.Round(seconds);
            return (total / 60) + ":" + (total % 60).ToString("00");
        }
    }
}
