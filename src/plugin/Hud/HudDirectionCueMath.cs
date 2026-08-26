using System;

namespace NOXMFD
{
    // Pure screen-rectangle math for HudTgpCue. Unity owns the world-to-screen projection; this
    // helper only decides whether the projected point is inside the safe viewport and, if not,
    // where its edge arrow belongs. Kept BCL-only so rear-view discontinuities can be unit-tested.
    internal static class HudDirectionCueMath
    {
        private const float DirectionEpsilon = 0.0001f;

        internal readonly struct Placement
        {
            public bool  OnScreen { get; }
            public float X { get; }
            public float Y { get; }
            public float AngleDeg { get; }
            public float StableDirectionX { get; }
            public float StableDirectionY { get; }

            internal Placement(bool onScreen, float x, float y, float angleDeg,
                float stableDirectionX, float stableDirectionY)
            {
                OnScreen = onScreen;
                X = x;
                Y = y;
                AngleDeg = angleDeg;
                StableDirectionX = stableDirectionX;
                StableDirectionY = stableDirectionY;
            }
        }

        internal static bool TryPlace(float projectedX, float projectedY, bool behindCamera,
            float screenWidth, float screenHeight, float edgeInset,
            float previousDirectionX, float previousDirectionY,
            out Placement placement)
        {
            placement = default;
            if (!Finite(projectedX) || !Finite(projectedY) || !Finite(screenWidth) ||
                !Finite(screenHeight) || !Finite(edgeInset) ||
                screenWidth <= 0f || screenHeight <= 0f || edgeInset < 0f)
                return false;

            float halfWidth = screenWidth * 0.5f;
            float halfHeight = screenHeight * 0.5f;
            float safeHalfWidth = halfWidth - edgeInset;
            float safeHalfHeight = halfHeight - edgeInset;
            if (safeHalfWidth <= 0f || safeHalfHeight <= 0f) return false;

            float dx = projectedX - halfWidth;
            float dy = projectedY - halfHeight;

            // WorldToScreenPoint mirrors X/Y when depth is negative. Flip the centre-relative
            // vector back so the edge cue points toward the shortest visible turn, matching the
            // native ObjectiveOverlay/HUDFunctions convention.
            if (behindCamera)
            {
                dx = -dx;
                dy = -dy;
            }

            bool onScreen = !behindCamera &&
                Math.Abs(dx) <= safeHalfWidth && Math.Abs(dy) <= safeHalfHeight;

            float directionX = dx;
            float directionY = dy;
            if (directionX * directionX + directionY * directionY <= DirectionEpsilon)
            {
                if (Finite(previousDirectionX) && Finite(previousDirectionY) &&
                    previousDirectionX * previousDirectionX + previousDirectionY * previousDirectionY >
                    DirectionEpsilon)
                {
                    directionX = previousDirectionX;
                    directionY = previousDirectionY;
                }
                else
                {
                    // Exactly rearward has no inherent screen-edge direction. Bottom is the stable
                    // cold-start fallback; subsequent rear crossings retain the last real direction.
                    directionX = 0f;
                    directionY = -1f;
                }
            }

            float length = (float)Math.Sqrt(directionX * directionX + directionY * directionY);
            float stableX = directionX / length;
            float stableY = directionY / length;

            if (onScreen)
            {
                placement = new Placement(true, projectedX, projectedY, 0f, stableX, stableY);
                return true;
            }

            float edgeScale = Math.Max(Math.Abs(directionX) / safeHalfWidth,
                Math.Abs(directionY) / safeHalfHeight);
            if (!Finite(edgeScale) || edgeScale <= 0f) return false;

            float edgeX = halfWidth + directionX / edgeScale;
            float edgeY = halfHeight + directionY / edgeScale;
            float angle = (float)(Math.Atan2(directionY, directionX) * 180.0 / Math.PI);
            placement = new Placement(false, edgeX, edgeY, angle, stableX, stableY);
            return true;
        }

        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
