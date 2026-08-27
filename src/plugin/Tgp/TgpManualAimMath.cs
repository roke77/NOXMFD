using System;

namespace NOXMFD
{
    // Pure pan/tilt/zoom math for TgpManualControl. Kept free of Unity/game types so tools/tests
    // can pin the geometry without needing a live Nuclear Option install.
    internal static class TgpManualAimMath
    {
        internal readonly struct AimVector
        {
            public AimVector(float x, float y, float z)
            {
                X = x;
                Y = y;
                Z = z;
            }

            public float X { get; }
            public float Y { get; }
            public float Z { get; }
        }

        internal static (float az, float el) ToAzimuthElevation(float x, float y, float z)
        {
            float clampedY = Math.Max(-1f, Math.Min(1f, y));
            return (
                (float)(Math.Atan2(x, z) * 180.0 / Math.PI),
                (float)(Math.Asin(clampedY) * 180.0 / Math.PI));
        }

        internal static AimVector FromAzimuthElevation(float azimuthDeg, float elevationDeg)
        {
            double az = azimuthDeg * Math.PI / 180.0;
            double el = elevationDeg * Math.PI / 180.0;
            double cosEl = Math.Cos(el);
            return new AimVector(
                (float)(Math.Sin(az) * cosEl),
                (float)Math.Sin(el),
                (float)(Math.Cos(az) * cosEl));
        }

        internal static float ZoomScale(float desiredFov, float maxFov) => desiredFov / maxFov;

        // Interpolates geometrically (log-linear) in FOV, matching how physical camera zoom lenses
        // are calibrated: each unit of axis travel changes magnification by a constant *ratio*, not
        // a constant absolute amount. A linear-in-1/FOV scheme has the same flaw at a smaller scale:
        // it packs most of the useful low-zoom range (0.5x..~5x) into a small sliver of travel since
        // the full range runs all the way to 40x, still hard to control precisely at low zoom.
        // Log-linear instead makes going from 1x to 2x feel like the same amount of stick as going
        // from 10x to 20x, which is the range pilots actually work in and matches how zoom
        // rings/wheels are perceived as "linear" in practice.
        internal static float ZoomFromAxis(float normalized, float minFov, float maxFov)
        {
            float t = Math.Max(0f, Math.Min(1f, (normalized + 1f) * 0.5f));
            float logMin = (float)Math.Log(minFov);
            float logMax = (float)Math.Log(maxFov);
            float logFov = logMax + (logMin - logMax) * t;
            return (float)Math.Exp(logFov);
        }

        internal static AimVector NudgeDirection(float x, float y, float z,
            float panInputX, float panInputY, float dt,
            float desiredFov, float maxFov,
            float panSpeedDegPerSec, float tiltSpeedDegPerSec, float maxElevationDeg)
        {
            if (panInputX == 0f && panInputY == 0f) return new AimVector(x, y, z);

            float scale = ZoomScale(desiredFov, maxFov);
            (float azimuth, float elevation) = ToAzimuthElevation(x, y, z);
            azimuth += panInputX * panSpeedDegPerSec * scale * dt;
            elevation = Math.Max(-maxElevationDeg, Math.Min(maxElevationDeg,
                elevation + panInputY * tiltSpeedDegPerSec * scale * dt));
            return FromAzimuthElevation(azimuth, elevation);
        }
    }
}
