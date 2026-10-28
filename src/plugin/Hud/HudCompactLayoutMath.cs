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

        // Instruments with their own placement/size sliders in HudCompactConfig.
        internal static readonly string[] Groups = { "Speed", "Altitude", "Heading" };

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

        // Canvas units from screen center for an angle off the camera axis. Gnomonic projection:
        // half the canvas height maps to tan(fov/2).
        internal static float DegToUnits(float deg, float canvasHeight, float verticalFovDeg)
        {
            const double rad = Math.PI / 180.0;
            return (float)(canvasHeight * 0.5 * Math.Tan(deg * rad) / Math.Tan(verticalFovDeg * 0.5 * rad));
        }
    }
}
