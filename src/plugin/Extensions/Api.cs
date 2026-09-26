namespace NOXMFD
{
    // Public extension API (docs/extensions-api.md). Callable from an extension's Awake()/Update()
    // on the Unity main thread; only routes bytes/JSON to the HTTP server, never touches game state.
    public static class Api
    {
        // Bump on breaking changes; extensions pin a minimum via BepInDependency MinimumVersion.
        // SetFactionColorOverride/SetUnitTypeColorOverride/SetUnitColorOverride reject any hex that
        // isn't exactly #RRGGBB/#RRGGBBAA (IconColorRegistry.IsValidHex) — the reason this field
        // carries this value. 4 adds SetUnitColorOverride/ClearUnitColorOverride
        // (docs/atc-extension-support.md item 2). 5 adds SetSelectedUnit (item 3). 6 adds
        // SetSelectedUnitTrack, the item 3 follow-on that lets an extension put MAP into
        // follow-this-unit mode instead of only highlighting it. 7 adds the WPT route/steer-point
        // reads and SetActiveRouteNextIndex, for coupling an autopilot to NOXMFD's route (issue #86).
        public const int ApiVersion = 7;

        // Called on an HTTP worker: relPath "" is the page's own HTML (/ext/<id>); otherwise it is
        // an asset under that path. Return null for 404. Content-Type is inferred from its path suffix.
        public delegate byte[]? AssetResolver(string relPath);

        // Raw POST body from /ext/<id>/command, called on the Unity main thread. Exceptions caught and logged.
        public delegate void CommandHandler(string json);

        // id becomes the route (/ext/<id>[/...]), command endpoint (if command non-null), and EXT
        // nav entry action; label is the nav display text. Returns false if id is already registered.
        public static bool RegisterExtension(string id, string label, AssetResolver resolve, CommandHandler? command = null)
            => ExtensionRegistry.Register(id, label, resolve, command);

        public static void UnregisterExtension(string id) => ExtensionRegistry.Unregister(id);

        // Spliced into the outgoing 10 Hz frame under "ext":{"<id>":<json>}; last-write-wins.
        // json must be a complete JSON value. Invalid input is rejected and the previous value stays live.
        public static void PublishSlice(string id, string json) => ExtensionRegistry.PublishSlice(id, json);

        // High-rate, change-gated value on its own SSE channel, for anything too laggy on the 10 Hz
        // frame. Arrives client-side as "ext-<eventName>". json must be a complete JSON value.
        public static void PublishEvent(string eventName, string json) => ExtensionRegistry.PublishEvent(eventName, json);

        // Continuous MJPEG feed served at /ext/<id>/feed.mjpg. WantsMjpegFrames reports whether
        // anyone is subscribed, so capture work can be skipped when nobody's watching.
        public static void PushMjpegFrame(string id, byte[] jpg) => ExtensionRegistry.PushMjpegFrame(id, jpg);
        public static void ClearMjpegFrame(string id) => ExtensionRegistry.ClearMjpegFrame(id);
        public static bool WantsMjpegFrames(string id) => ExtensionRegistry.WantsMjpegFrames(id);

        // Live override for MAP's three base faction tints (docs/vanilla-icons-plus-extension.md),
        // superseding TelemetryReader's once-per-session GameAssets read. Any hex left null falls
        // back to that read. Call again whenever the source colors change — there is no polling,
        // the next telemetry frame picks up the new value. An invalid color is rejected without
        // replacing the current override and logs a warning (IconColorRegistry.cs) — the bool
        // IconColorRegistry itself returns isn't surfaced here to keep this call fire-and-forget,
        // same as every other Api.cs method.
        public static void SetFactionColorOverride(string? friendlyHex, string? enemyHex, string? neutralHex)
            => IconColorRegistry.SetFactionOverride(friendlyHex, enemyHex, neutralHex);

        public static void ClearFactionColorOverride() => IconColorRegistry.ClearFactionOverride();

        // Per-unit-type MAP icon color, keyed by the same type name a contact's "t" field and the
        // icon lookup (/icon?type=) already use — no separate classification needed on NOXMFD's
        // side. factionFilter restricts the override to one faction (0 neutral/1 friendly/2
        // enemy); null applies regardless of faction. Rejection behaves as described above.
        public static void SetUnitTypeColorOverride(string unitType, string hex, int? factionFilter = null)
            => IconColorRegistry.SetTypeOverride(unitType, hex, factionFilter);

        public static void ClearUnitTypeColorOverride(string unitType) => IconColorRegistry.ClearTypeOverride(unitType);

        // Per-unit-INSTANCE MAP icon ring (docs/atc-extension-support.md item 2) — additive to the
        // faction/type color above, not a replacement: MAP draws this as a ring around the icon, so
        // the unit's own faction/type identification stays visible underneath (the ATC extension's
        // own status-on-MAP requirement, issue #89 section 5, is why this exists). id is the same
        // one a contact's "id" field / UnitInfo.Id already carry. Rejection behaves the same as the
        // two overrides above: an invalid id/hex is rejected without replacing the current value and
        // logs a warning (IconColorRegistry.cs).
        public static void SetUnitColorOverride(uint id, string hex) => IconColorRegistry.SetIdOverride(id, hex);

        public static void ClearUnitColorOverride(uint id) => IconColorRegistry.ClearIdOverride(id);

        // A unit id MAP should highlight/pan attention to (docs/atc-extension-support.md item 3) —
        // deliberately separate from weapon targeting: MAP's own click-to-select issues a real
        // target.select command, which this never touches. 0 clears it. One-way today: MAP reads
        // this every frame, nothing on MAP's own side writes it back yet.
        public static void SetSelectedUnit(uint id) => SharedSelection.Set(id);

        // Puts MAP into follow-this-unit mode: while on, MAP re-centers each frame on whatever
        // SetSelectedUnit last set (dropping its own FLW/follow-player mode the same way a pilot's
        // FLW key would), the same way it already re-centers on the player. off leaves MAP's pan
        // alone; it does not restore FLW. No effect until a non-zero id is also selected.
        public static void SetSelectedUnitTrack(bool on) => SharedSelection.SetTrack(on);

        // WPT navigation (docs/extensions-api.md section 8). RouteStore stays the only authority on
        // route progress, and its data is main-thread only — call these from Update(), never from the
        // asset resolver. An active route owns navigation even when complete; only with no active
        // route does the selected steer point apply (same priority as the HUD waypoint cue).
        public static NavRoute? GetActiveRoute() => RouteStore.GetActiveRouteSnapshot();

        public static NavPoint? GetActiveSteerPoint() => RouteStore.GetActiveSteerPointSnapshot();

        // Changes whenever the route library or selection changes (edits, activation, proximity
        // advance). Re-read GetActiveRoute/GetActiveSteerPoint only when this differs from last time.
        public static int RouteRevision => RouteStore.Revision;

        // Jumps the active route's progress to index (clamped to 0..Points.Length), as WPT's
        // per-waypoint reset does. False when no route is active.
        public static bool SetActiveRouteNextIndex(int index) => RouteStore.ResetWaypoint(index);
    }
}
