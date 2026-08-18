namespace NOXMFD
{
    // The in-game HUD waypoint cue's data source (docs/hud-waypoint-indicator.md).
    //
    // Waypoints live in the browser's localStorage (src/web/pages/wpt/waypoints-store.js), so this
    // is the mod's only browser -> plugin STATE flow — every other flow is plugin -> browser, or
    // browser -> plugin as a one-shot command. The store publishes its active waypoint here through
    // the wpt.active command whenever a route is edited, stepped, switched, or auto-advanced.
    //
    // Static, NOT mission-scoped like TelemetryReader / HudDeclutter. A mission restart tears those
    // down, and the browser gets no event it could use to notice and republish — the pilot's route
    // hasn't changed just because the mission reloaded, so the cue shouldn't silently go dark. A
    // game restart does clear this (fresh process), and the browser republishes off the SSE hello
    // (telemetry-source.js) to cover that case.
    //
    // X/Z are floating-origin-corrected WORLD coordinates (transform.position - Datum.originPosition,
    // the frame TelemetryReader publishes as world.x/world.z and the browser stores waypoints in),
    // NOT raw Unity positions — those drift as the world re-centers.
    //
    // ponytail: last writer wins. Two displays each own their own route list, so whichever posted
    // most recently defines the cue. Arbitrating between them needs a policy that doesn't exist yet
    // (the envelope's `cid` identifies the reporter, but not which one is authoritative); moving
    // routes into the plugin (Option 2 in docs/hud-waypoint-indicator.md) dissolves the question
    // instead of answering it, and is the real upgrade path.
    internal static class HudWaypointState
    {
        public static bool   Active;
        public static float  X;
        public static float  Z;
        public static string Name = string.Empty;
        public static int    Index;   // 0-based position in the route; the cue displays Index + 1

        public static void Set(bool active, float x, float z, string name, int index)
        {
            Active = active;
            X      = x;
            Z      = z;
            Name   = name ?? string.Empty;
            Index  = index;
        }
    }
}
