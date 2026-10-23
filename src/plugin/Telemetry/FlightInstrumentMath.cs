using System;

namespace NOXMFD
{
    // Pure math behind the ADI/HSI telemetry fields (docs/adi-hsi-pages.md). Each formula mirrors
    // the game's own HUD app that shows the same value, so the pages agree with the cockpit.
    // TelemetryReader does the Unity reads and passes plain floats in.
    internal static class FlightInstrumentMath
    {
        // AoADisplay hides AoA below this airspeed, where the velocity direction is noise.
        internal const float AoaMinSpeed = 10f;

        // Unity euler angle (0..360) to a signed angle in (-180, 180].
        internal static float WrapSigned(float deg)
        {
            float a = deg % 360f;
            if (a > 180f) a -= 360f;
            else if (a <= -180f) a += 360f;
            return a;
        }

        // eulerAngles.x is positive nose-down (ArtificialHorizon flips it the same way).
        internal static float PitchDeg(float eulerX) => -WrapSigned(eulerX);

        // eulerAngles.z is positive left-wing-down; the pages want right-wing-down positive.
        internal static float RollDeg(float eulerZ) => -WrapSigned(eulerZ);

        // Angle of attack from the cockpit-local velocity, as AoADisplay. NaN below AoaMinSpeed.
        internal static float AoaDeg(float localVy, float localVz, float speed)
        {
            if (speed <= AoaMinSpeed) return float.NaN;
            return (float)(-Math.Atan2(localVy, localVz) * 180.0 / Math.PI);
        }

        // Sideslip: positive when the aircraft moves to the right of its nose, which is the side
        // the slip ball rolls to. NaN below AoaMinSpeed for the same reason as AoA.
        internal static float BetaDeg(float localVx, float localVz, float speed)
        {
            if (speed <= AoaMinSpeed) return float.NaN;
            return (float)(Math.Atan2(localVx, localVz) * 180.0 / Math.PI);
        }

        internal static float Mach(float speed, float speedOfSound) =>
            speedOfSound > 0f ? speed / speedOfSound : 0f;

        // Turn rate from the rigidbody's angular velocity around world up (rad/s), right positive:
        // Unity's left-handed +Y rotation turns north toward east, the way heading increases.
        internal static float TurnRateDeg(float angularVelocityUp) =>
            (float)(angularVelocityUp * 180.0 / Math.PI);
    }
}
