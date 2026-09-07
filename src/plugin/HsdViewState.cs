using UnityEngine;

namespace NOXMFD
{
    // Shared HSD view (CEN/DEP mode + range-ladder index) — server-authoritative so the internal
    // MFD's own HSD pane (InternalMfdHsdPage) can track whatever the external web HSD page is
    // currently showing, instead of a fixed range/mode. Set from CommandDispatcher's
    // "hsd.set-view" (hsd.js's saveRange(), the one choke point both its range-step and CEN/DEP
    // toggle funnel through) and read every tick into TelemetrySnapshot.
    //
    // In-memory only, not BepInEx-persisted: hsd.js's own state is sessionStorage-scoped (resets
    // each browser session), so there's nothing to persist across a game restart here either — this
    // just starts at the same defaults hsd.js itself starts at.
    internal static class HsdViewState
    {
        // hsd.js: CEN_RANGE_NM/DEP_RANGE_NM both have exactly 5 entries.
        private const int RangeCount = 5;

        internal static bool Dep { get; private set; }
        internal static int RangeIdx { get; private set; } = 2; // hsd.js's own default

        internal static void Set(bool dep, int rangeIdx)
        {
            Dep = dep;
            RangeIdx = Mathf.Clamp(rangeIdx, 0, RangeCount - 1);
        }
    }
}
