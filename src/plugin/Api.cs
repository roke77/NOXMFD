namespace NOXMFD
{
    // Public extension API (docs/extensions-api.md). Callable from an extension's Awake()/Update()
    // on the Unity main thread; only routes bytes/JSON to the HTTP server, never touches game state.
    public static class Api
    {
        // Bump on breaking changes; extensions pin a minimum via BepInDependency MinimumVersion.
        public const int ApiVersion = 1;

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
        // json must already be valid JSON — never parsed or validated.
        public static void PublishSlice(string id, string json) => ExtensionRegistry.PublishSlice(id, json);

        // High-rate, change-gated value on its own SSE channel, for anything too laggy on the 10 Hz
        // frame. Arrives client-side as "ext-<eventName>". json must already be valid JSON.
        public static void PublishEvent(string eventName, string json) => ExtensionRegistry.PublishEvent(eventName, json);

        // Continuous MJPEG feed served at /ext/<id>/feed.mjpg. WantsMjpegFrames reports whether
        // anyone is subscribed, so capture work can be skipped when nobody's watching.
        public static void PushMjpegFrame(string id, byte[] jpg) => ExtensionRegistry.PushMjpegFrame(id, jpg);
        public static void ClearMjpegFrame(string id) => ExtensionRegistry.ClearMjpegFrame(id);
        public static bool WantsMjpegFrames(string id) => ExtensionRegistry.WantsMjpegFrames(id);
    }
}
