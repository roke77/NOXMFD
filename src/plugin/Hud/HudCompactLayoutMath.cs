using System;

namespace NOXMFD
{
    // Pure layout data for HudCompactLayout: the F-16-style target spot and size of each native HUD
    // readout block, in degrees from the nose marker, plus the degree-to-canvas conversions. Targets
    // are angular so the block keeps its place against the pitch ladder, which scales with FOV.
    internal static class HudCompactLayoutMath
    {
        internal readonly struct Block
        {
            internal Block(string reference, float xDeg, float yDeg, float size, params string[] members)
            {
                Reference = reference;
                XDeg = xDeg;
                YDeg = yDeg;
                Size = size;
                Members = members;
            }

            // Path (under HUDCenter, or under the aircraft's HUDAppManager) of the object whose
            // original position is the block's visual anchor.
            internal string Reference { get; }
            internal float XDeg { get; }
            internal float YDeg { get; }
            // Scale of the members and of their spacing from the anchor; 1 is the game's size.
            internal float Size { get; }
            // Objects moved together, keeping the prefab's spacing inside the block.
            internal string[] Members { get; }
        }

        // ponytail: one fixed preset, tuned in game on the FS-12 Revoker prefab at 90° vertical FOV.
        // Other airframes share the readout names but may need their own offsets; a per-aircraft table
        // is the upgrade path.
        internal static readonly Block[] F16Preset =
        {
            new Block("speedGauge/Airspeed", -9.5f, 0.5f, 0.5f,
                "speedGauge/Airspeed", "AoAGauge/AOA", "machIndicator/MachDisplay", "Gindicator"),
            new Block("Altitude", 9.5f, -0.5f, 0.5f, "Altitude", "Climbrate"),
            new Block("compass", 0f, 12.75f, 0.55f, "compass", "BearingObject"),
            // The AoA indexer sits inside the fuel arc in the prefab; they move as one.
            new Block("fuelGauge/fuelArc", -24f, 0f, 1f, "fuelGauge", "AoAIndexer"),
            new Block("throttleGauge_AB/throttleArc", 24f, 0f, 1f, "throttleGauge_AB", "GearIndicator/gearIcon"),
            new Block("GameTimeObject", -13f, -14f, 1f, "GameTimeObject"),
            new Block("OtherTimeObject", 13f, -14f, 1f, "OtherTimeObject"),
            // Another mod's autopilot readout; skipped when absent.
            new Block("AP_CombinedOverlay", -16f, -19f, 1f, "AP_CombinedOverlay"),
        };

        // FlightHud maps pitch onto the ladder texture as uv.y = -pitch/180 + c, so the full texture
        // height spans 180 degrees.
        internal const float LadderTextureDeg = 180f;

        // localScale that makes the ladder conformal at the screen center: a window windowUnits tall
        // showing uvHeight of the texture gets the camera's units-per-degree. FlightHud's own
        // 50/fov is a linear stand-in that is only exact near 60° FOV (about 15% too large at 90°).
        internal static float TrueLadderScale(float uvHeight, float windowUnits, float canvasHeight, float verticalFovDeg)
        {
            const double rad = Math.PI / 180.0;
            double unitsPerDeg = canvasHeight * 0.5 * rad / Math.Tan(verticalFovDeg * 0.5 * rad);
            return (float)(uvHeight * LadderTextureDeg * unitsPerDeg / windowUnits);
        }

        // Canvas units from screen center for an angle off the camera axis. Gnomonic projection:
        // half the canvas height maps to tan(fov/2).
        internal static float DegToUnits(float deg, float canvasHeight, float verticalFovDeg)
        {
            const double rad = Math.PI / 180.0;
            return (float)(canvasHeight * 0.5 * Math.Tan(deg * rad) / Math.Tan(verticalFovDeg * 0.5 * rad));
        }
    }
}
