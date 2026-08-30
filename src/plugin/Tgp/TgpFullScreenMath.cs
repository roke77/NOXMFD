namespace NOXMFD
{
    // Pure math for TgpFullScreen (docs/tgp-full-screen.md). BCL-only so it can be linked into
    // tools/tests without dragging in TMPro/UnityEngine.UI, the same split TgpManualAimMath.cs uses
    // for TgpManualControl's own geometry.
    internal static class TgpFullScreenMath
    {
        // tgp.js's web needle rotates by (bearing + 180): its CSS shape hangs DOWN from the compass
        // center by default, so it needs the extra half-turn to point up at bearing 0. This needle
        // is built with a bottom pivot and grows UP from the compass center instead (TgpFullScreen's
        // BuildBottomLeft), already pointing the right way at bearing 0 — adding +180 here would
        // double-flip it, which is exactly the bug the first pass shipped with (copied the web
        // formula without accounting for the opposite pivot/growth direction).
        internal static float NeedleRotationDegrees(float bearingDeg) => -bearingDeg;
    }
}
