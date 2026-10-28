using System;

namespace NOXMFD
{
    // Pure layout data for HudCompactLayout: the F-16-style target spot of each native HUD readout
    // block, in degrees from the nose marker, and the degrees-to-canvas-units conversion. Targets are
    // angular so the block keeps its place against the pitch ladder, which the game rescales with FOV.
    internal static class HudCompactLayoutMath
    {
        internal readonly struct Block
        {
            internal Block(string group, string reference, float xDeg, float yDeg, params string[] members)
            {
                Group = group;
                Reference = reference;
                XDeg = xDeg;
                YDeg = yDeg;
                Members = members;
            }

            // Groups entry whose sliders also apply, or "" for the global ones only.
            internal string Group { get; }
            // Path (under HUDCenter, or under the aircraft's HUDAppManager) of the object whose
            // original position is the block's visual anchor.
            internal string Reference { get; }
            internal float XDeg { get; }
            internal float YDeg { get; }
            // Objects shifted together by the same delta, keeping the prefab's spacing inside the block.
            internal string[] Members { get; }
        }

        // Instruments with their own placement/size sliders in HudCompactConfig, with the slider
        // defaults: the tuned adjustment (degrees, size) applied on top of the preset spot below.
        internal static readonly (string Name, float OffsetX, float OffsetY, float Size)[] Groups =
        {
            ("Speed", 6.5f, -1.5f, 0.5f),
            ("Altitude", -6.5f, -0.5f, 0.5f),
            ("Heading", 0f, -5.25f, 0.55f),
        };

        // ponytail: one fixed preset, tuned against the FS-12 Revoker prefab at 90° vertical FOV.
        // Other airframes share the readout names but may need their own offsets; a per-aircraft table
        // is the upgrade path. The pitch ladder spans about ±12° at 90° FOV, so the side blocks sit
        // just outside it.
        internal static readonly Block[] F16Preset =
        {
            new Block("Speed", "speedGauge/Airspeed", -16f, 2f,
                "speedGauge/Airspeed", "AoAGauge/AOA", "machIndicator/MachDisplay", "Gindicator"),
            new Block("Altitude", "Altitude", 16f, 0f, "Altitude", "Climbrate"),
            new Block("Heading", "compass", 0f, 18f, "compass", "BearingObject"),
            // The AoA indexer sits inside the fuel arc in the prefab; they move as one.
            new Block("", "fuelGauge/fuelArc", -24f, 0f, "fuelGauge", "AoAIndexer"),
            new Block("", "throttleGauge_AB/throttleArc", 24f, 0f, "throttleGauge_AB", "GearIndicator/gearIcon"),
            new Block("", "GameTimeObject", -13f, -14f, "GameTimeObject"),
            new Block("", "OtherTimeObject", 13f, -14f, "OtherTimeObject"),
            // Another mod's autopilot readout; skipped when absent.
            new Block("", "AP_CombinedOverlay", -16f, -19f, "AP_CombinedOverlay"),
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
