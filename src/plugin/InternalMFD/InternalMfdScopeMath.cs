using System;

namespace NOXMFD
{
    // Pure math shared by the internal MFD's scope-style pages (InternalMfdRwrPage,
    // InternalMfdHsdPage) — BCL-only so it can be linked into tools/tests without dragging in
    // UnityEngine, the same split TgpFullScreenMath.cs/TgpManualAimMath.cs use for their own
    // geometry. InternalMfdUi.PolarToLocal wraps PolarOffset's tuple into a Vector2 for its Unity-
    // facing callers; nothing here needs UnityEngine itself.
    internal static class InternalMfdScopeMath
    {
        // Converts an azimuth (clockwise from the nose — InternalMfdUi.Azimuth's own convention)
        // and a 0..1 distance fraction into an (x,y) offset radiusPx out from (0,0). This exact
        // sign/axis pairing (sin for X, cos for Y) is what every scope-style page's contact/missile/
        // threat placement depends on — a plausible-looking but wrong pairing (or a flipped sign)
        // still compiles and still draws something, just in the wrong place/direction, which is
        // exactly the class of bug RWR's own missile-dart direction shipped with once before being
        // traced through the source math by hand.
        internal static (float X, float Y) PolarOffset(float azimuthDeg, float distFrac, float radiusPx)
        {
            double rad = azimuthDeg * Math.PI / 180.0;
            double r = distFrac * radiusPx;
            return ((float)(Math.Sin(rad) * r), (float)(Math.Cos(rad) * r));
        }

        // Zero-padded 3-digit heading, wrapped into 0..359 (hsd.js's own pad3()). A plain `%` on a
        // negative heading (e.g. -10 % 360 == -10 in C#, not 350) would format as "-10" instead of
        // "350", so the wrap adds 360 before the second modulo.
        internal static string Pad3Heading(float headingDeg)
        {
            int h = (((int)Math.Round(headingDeg) % 360) + 360) % 360;
            return h.ToString("000");
        }
    }
}
