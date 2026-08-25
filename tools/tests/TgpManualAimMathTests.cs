using System;
using NOXMFD;

namespace NOXMFD.Tests
{
    public class TgpManualAimMathTests
    {
        private static void Near(float actual, float expected, float tolerance = 0.001f) =>
            Assert.True(Math.Abs(actual - expected) <= tolerance, $"got {actual}, expected {expected}");

        [Fact]
        public void Azimuth_elevation_round_trip_preserves_cardinal_directions()
        {
            var right = TgpManualAimMath.FromAzimuthElevation(90f, 0f);
            Near(right.X, 1f);
            Near(right.Y, 0f);
            Near(right.Z, 0f);

            (float az, float el) = TgpManualAimMath.ToAzimuthElevation(right.X, right.Y, right.Z);
            Near(az, 90f);
            Near(el, 0f);

            var up = TgpManualAimMath.FromAzimuthElevation(0f, 30f);
            (az, el) = TgpManualAimMath.ToAzimuthElevation(up.X, up.Y, up.Z);
            Near(az, 0f);
            Near(el, 30f);
        }

        [Fact]
        public void Nudge_direction_scales_angular_rate_by_current_fov()
        {
            var wide = TgpManualAimMath.NudgeDirection(0f, 0f, 1f,
                panInputX: 1f, panInputY: 0f, dt: 1f,
                desiredFov: 20f, maxFov: 20f,
                panSpeedDegPerSec: 60f, tiltSpeedDegPerSec: 45f, maxElevationDeg: 85f);
            var zoomed = TgpManualAimMath.NudgeDirection(0f, 0f, 1f,
                panInputX: 1f, panInputY: 0f, dt: 1f,
                desiredFov: 5f, maxFov: 20f,
                panSpeedDegPerSec: 60f, tiltSpeedDegPerSec: 45f, maxElevationDeg: 85f);

            Near(TgpManualAimMath.ToAzimuthElevation(wide.X, wide.Y, wide.Z).az, 60f);
            Near(TgpManualAimMath.ToAzimuthElevation(zoomed.X, zoomed.Y, zoomed.Z).az, 15f);
        }

        [Fact]
        public void Nudge_direction_clamps_elevation_short_of_the_poles()
        {
            var v = TgpManualAimMath.NudgeDirection(0f, 0f, 1f,
                panInputX: 0f, panInputY: 1f, dt: 10f,
                desiredFov: 20f, maxFov: 20f,
                panSpeedDegPerSec: 60f, tiltSpeedDegPerSec: 45f, maxElevationDeg: 85f);

            Near(TgpManualAimMath.ToAzimuthElevation(v.X, v.Y, v.Z).el, 85f);
        }

        [Fact]
        public void Zoom_axis_maps_full_travel_to_native_fov_limits()
        {
            Near(TgpManualAimMath.ZoomFromAxis(-1f, minFov: 0.25f, maxFov: 20f), 20f);
            Near(TgpManualAimMath.ZoomFromAxis(1f, minFov: 0.25f, maxFov: 20f), 0.25f);
            // Midpoint is the geometric mean of the FOV range (log-linear interpolation), not the
            // arithmetic mean — sqrt(0.25 * 20).
            Near(TgpManualAimMath.ZoomFromAxis(0f, minFov: 0.25f, maxFov: 20f), (float)Math.Sqrt(0.25 * 20));
            Near(TgpManualAimMath.ZoomFromAxis(2f, minFov: 0.25f, maxFov: 20f), 0.25f);
        }

        [Fact]
        public void Zoom_axis_moves_magnification_by_equal_ratios_not_equal_amounts()
        {
            // mag = 10 / fov (TgpManualControl.ComputeOverlaySample). Log-linear FOV interpolation
            // means equal axis steps produce equal *ratios* of mag change, not equal absolute
            // deltas — going from 1x to 2x should take the same amount of stick as 10x to 20x, so
            // the low-zoom range pilots actually use most isn't squeezed into a sliver of travel.
            float MagAt(float axis) => 10f / TgpManualAimMath.ZoomFromAxis(axis, minFov: 0.25f, maxFov: 20f);

            float magLow  = MagAt(-1f);   // widest FOV -> lowest mag
            float magMid  = MagAt(0f);
            float magHigh = MagAt(1f);    // narrowest FOV -> highest mag

            float ratioLow  = magMid / magLow;
            float ratioHigh = magHigh / magMid;
            Assert.True(Math.Abs(ratioHigh - ratioLow) < 0.01f,
                $"expected equal mag ratios across axis halves, got low-half={ratioLow:0.###}x high-half={ratioHigh:0.###}x");
        }
    }
}
