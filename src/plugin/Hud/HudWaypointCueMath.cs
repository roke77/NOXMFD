using System;

namespace NOXMFD
{
    // Pure math for HudWaypointCue: route-space bearing/distance and heading-tape chevron placement.
    // Unity-facing code still owns world reads, RectTransforms, and Text rendering.
    internal static class HudWaypointCueMath
    {
        internal const float TapeArcDegrees = 90f;
        internal const float HalfArc = TapeArcDegrees * 0.5f;

        internal readonly struct TapePlacement
        {
            internal TapePlacement(bool onTape, float x, float y, float rotationZ)
            {
                OnTape = onTape;
                X = x;
                Y = y;
                RotationZ = rotationZ;
            }

            internal bool OnTape { get; }
            internal float X { get; }
            internal float Y { get; }
            internal float RotationZ { get; }
        }

        internal static float BearingDeg(float ownX, float ownZ, float waypointX, float waypointZ)
        {
            float dx = waypointX - ownX;
            float dz = waypointZ - ownZ;
            return Repeat((float)(Math.Atan2(dx, dz) * 180.0 / Math.PI), 360f);
        }

        internal static float RelativeDeg(float headingDeg, float bearingDeg)
        {
            return DeltaAngle(headingDeg, bearingDeg);
        }

        internal static float DistanceKm(float ownX, float ownZ, float waypointX, float waypointZ)
        {
            float dx = waypointX - ownX;
            float dz = waypointZ - ownZ;
            return (float)Math.Sqrt(dx * dx + dz * dz) / 1000f;
        }

        // On tape: place proportionally across the visible 90-degree strip. Off tape: pin to the
        // edge and rotate the chevron into a turn cue. The chevron artwork points down at 0 deg;
        // -90 aims it right, +90 aims it left.
        internal static TapePlacement PlaceBug(float relativeDeg, float tapeWidth, float tapeHeight)
        {
            float halfWidth = tapeWidth * 0.5f;
            float pixelsPerDegree = tapeWidth / TapeArcDegrees;

            bool onTape = Math.Abs(relativeDeg) <= HalfArc;
            float x = onTape ? relativeDeg * pixelsPerDegree : (relativeDeg > 0f ? halfWidth : -halfWidth);
            float y = tapeHeight * 0.5f + 2f;
            float rotationZ = onTape ? 0f : (relativeDeg > 0f ? -90f : 90f);
            return new TapePlacement(onTape, x, y, rotationZ);
        }

        private static float DeltaAngle(float current, float target)
        {
            float delta = Repeat(target - current, 360f);
            if (delta > 180f) delta -= 360f;
            return delta;
        }

        private static float Repeat(float t, float length)
        {
            return t - (float)Math.Floor(t / length) * length;
        }
    }
}
