namespace NOXMFD
{
    // Public extension API — see docs/extensions-api.md. A hard BepInDependency on NOXMFD is
    // the intended integration path (as opposed to McBridge.cs/RcBridge.cs's REFLECTION soft
    // dependency, which exists for the opposite direction: NOXMFD depending on a mod that has
    // never heard of it and never will). Every member here is safe to call from an extension's
    // own Awake()/Update() on the Unity main thread; nothing here touches game state itself —
    // it only routes bytes/JSON between an extension and the HTTP server.
    public static class Api
    {
        // Bump when a breaking change lands. Extensions pin a minimum via their own
        // [BepInDependency("com.roque.NOXMFD", MinimumVersion = "X.Y.Z")] — BepInEx itself then
        // refuses to load a stale extension rather than it half-working against a shape that
        // moved out from under it. This constant is for an extension that wants to branch on it
        // at runtime too, the same role McBridge.ApiVersion plays from the other side.
        public const int ApiVersion = 1;

        // relPath is "" for the page's own HTML (served at /ext/<id> and /ext/<id>/); anything
        // else is an asset under that (served at /ext/<id>/<relPath>). Return null for "not
        // found" (produces a 404). NOXMFD infers Content-Type from relPath's extension the same
        // way it does for its own pages, so an extension never needs to know MIME types itself.
        public delegate byte[]? AssetResolver(string relPath);

        // The raw POST body from /ext/<id>/command, called on the Unity main thread once per
        // queued request (docs/extensions-api.md, surface #3) — an extension parses this
        // against its own envelope shape; NOXMFD never inspects it. Exceptions are caught and
        // logged, same as CommandDispatcher's own handlers.
        public delegate void CommandHandler(string json);

        // Registers an extension. `id` becomes its route (/ext/<id>[/...]), its command endpoint
        // (/ext/<id>/command, only if `command` is non-null) and its EXT sub-nav entry's action;
        // `label` is that entry's display text. Returns false (logged) if `id` is already
        // registered — call once, from Awake().
        public static bool RegisterExtension(string id, string label, AssetResolver resolve, CommandHandler? command = null)
            => ExtensionRegistry.Register(id, label, resolve, command);

        // Mostly for tests / a clean shutdown path — BepInEx doesn't hot-reload plugins, so a
        // running extension has no ordinary reason to call this itself.
        public static void UnregisterExtension(string id) => ExtensionRegistry.Unregister(id);

        // Publishes this extension's telemetry — spliced into the outgoing 10 Hz frame under
        // "ext":{"<id>":<json>} every tick, at whatever cadence the caller updates it (last-
        // write-wins, same shape as RouteStore.RoutesJson). `json` must already be valid JSON;
        // NOXMFD never parses or validates it, only splices it in verbatim.
        public static void PublishSlice(string id, string json) => ExtensionRegistry.PublishSlice(id, json);

        // Publishes a high-rate, change-gated value on its own SSE channel — for something that
        // would feel laggy riding the 10 Hz frame (generalizes the "rcaim" pattern in
        // TelemetryServer). Arrives client-side as its own named event, "ext-<eventName>" — the
        // prefix is added server-side, so a caller can't collide with a built-in event name.
        // `json` must already be valid JSON. NOTE: no browser-side page currently subscribes to
        // this automatically — see docs/extensions-api.md's open questions.
        public static void PublishEvent(string eventName, string json) => ExtensionRegistry.PublishEvent(eventName, json);

        // A continuous MJPEG video feed, served at /ext/<id>/feed.mjpg — same shape as
        // TgpFeed/RcFeed's own capture-and-push pattern (Blit → AsyncGPUReadback → EncodeToJPG
        // → PushMjpegFrame), generalized to a runtime-registered id instead of a hardcoded page.
        // Call PushMjpegFrame from your own capture loop; WantsMjpegFrames tells you whether
        // anyone is currently subscribed, so you can skip capture work entirely while nobody's
        // watching, the same way TgpFeed/RcFeed gate on TelemetryServer.WantsTgpFrames today.
        public static void PushMjpegFrame(string id, byte[] jpg) => ExtensionRegistry.PushMjpegFrame(id, jpg);
        public static void ClearMjpegFrame(string id) => ExtensionRegistry.ClearMjpegFrame(id);
        public static bool WantsMjpegFrames(string id) => ExtensionRegistry.WantsMjpegFrames(id);
    }
}
