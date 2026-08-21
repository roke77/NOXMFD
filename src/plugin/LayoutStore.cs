using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace NOXMFD
{
    internal sealed class Layout
    {
        public string Id = string.Empty;
        public string Name = string.Empty;
        public string Shell = string.Empty;   // "classic" | "f35" — informational only, shown in the LOAD picker
        // The browser's own serialized arrangement (CLASSIC's {splitMode,splitVariant,pages} or
        // F-35's {cells,pages}), as JSON TEXT — opaque to this store, which never parses its shape
        // beyond checking it's well-formed on save. Carried as a plain string field (escaped like
        // Name/Shell), the same shape CommandEnvelope.text/wpt.import already use for a JSON blob —
        // deliberately not a nested JSON value, since re-emitting an arbitrary parsed tree on the
        // read-back-from-disk path would need a general JSON writer this codebase doesn't have
        // (JsonLite is read-only by design). The browser does JSON.parse on it to apply a layout.
        public string DataJson = "{}";
    }

    // Saved shell layouts (issue #51) — SAVE/LOAD LAYOUT are plain browser-side keyboard shortcuts;
    // storage lives here, server-side, so a layout named on one browser shows up on every other one,
    // the same reasoning as RouteStore. Unlike RouteStore, there's no live game state to interpret —
    // a layout is just a named, opaque arrangement blob the browser itself produced and will restore.
    //
    // Static, plugin-lifetime (NOT mission-scoped) — layouts must survive a mission restart AND a
    // full game restart, and must be saved/loaded at the main menu.
    internal static class LayoutStore
    {
        private static List<Layout> _layouts = new List<Layout>();

        // Server-thread-readable cache, same threading contract as RouteStore.RoutesJson: every
        // mutator below runs on the Unity main thread only (CommandDispatcher.Drain), and rebuilds
        // this string synchronously as its last step. The HTTP server thread only ever reads the
        // reference, never the underlying List<Layout>, so no lock is needed.
        internal static volatile string LayoutsJson = "{\"layouts\":[]}";

        private static string FilePath =>
            Path.Combine(BepInEx.Paths.ConfigPath, "com.roque.NOXMFD.layouts.json");

        // ── lifecycle ────────────────────────────────────────────────────────────────────────

        public static void Load()
        {
            if (!File.Exists(FilePath)) return;
            try
            {
                string text = File.ReadAllText(FilePath);
                if (JsonLite.Parse(text) is Dictionary<string, object?> root)
                    _layouts = ParseLayouts(root.TryGetValue("layouts", out object? l) ? l : null);
            }
            catch (Exception ex)
            {
                Plugin.Log?.LogWarning($"[NOXMFD] layouts file unreadable, starting empty: {ex.Message}");
                _layouts = new List<Layout>();
            }
            LayoutsJson = BuildJson();
        }

        private static List<Layout> ParseLayouts(object? value)
        {
            var layouts = new List<Layout>();
            if (value is not List<object?> list) return layouts;
            foreach (object? item in list)
            {
                if (item is not Dictionary<string, object?> d) continue;
                string id = d.TryGetValue("id", out object? idv) ? (idv as string ?? string.Empty) : string.Empty;
                if (id.Length == 0) continue;
                string data = d.TryGetValue("data", out object? dv) ? (dv as string ?? string.Empty) : string.Empty;
                if (data.Length == 0) continue;   // no arrangement to restore — drop this entry, keep the rest
                layouts.Add(new Layout
                {
                    Id    = id,
                    Name  = d.TryGetValue("name", out object? nm) ? (nm as string ?? string.Empty) : string.Empty,
                    Shell = d.TryGetValue("shell", out object? sh) ? (sh as string ?? string.Empty) : string.Empty,
                    DataJson = data,
                });
            }
            return layouts;
        }

        private static void Save()
        {
            LayoutsJson = BuildJson();
            try { File.WriteAllText(FilePath, LayoutsJson); }
            catch (Exception ex) { Plugin.Log?.LogWarning($"[NOXMFD] failed to persist layouts: {ex.Message}"); }
        }

        private static string BuildJson()
        {
            var sb = new StringBuilder();
            sb.Append("{\"layouts\":[");
            for (int i = 0; i < _layouts.Count; i++)
            {
                if (i > 0) sb.Append(',');
                Layout l = _layouts[i];
                sb.Append("{\"id\":\"").Append(TelemetryServer.EscapeJson(l.Id))
                  .Append("\",\"name\":\"").Append(TelemetryServer.EscapeJson(l.Name))
                  .Append("\",\"shell\":\"").Append(TelemetryServer.EscapeJson(l.Shell))
                  .Append("\",\"data\":\"").Append(TelemetryServer.EscapeJson(l.DataJson))
                  .Append("\"}");
            }
            sb.Append("]}");
            return sb.ToString();
        }

        private static string FreshId() => "l_" + Guid.NewGuid().ToString("N");

        private static string UniqueName(string name, string? excludeId)
        {
            var taken = new HashSet<string>(StringComparer.Ordinal);
            foreach (Layout l in _layouts) if (l.Id != excludeId) taken.Add(l.Name);
            if (!taken.Contains(name)) return name;
            int n = 2;
            while (taken.Contains(name + " (" + n + ")")) n++;
            return name + " (" + n + ")";
        }

        // Saves a new named layout. dataJson is the browser's own serialized arrangement (opaque to
        // this store) — validated only as well-formed JSON (must parse to an object), same defensive
        // bar RouteStore.ImportRoute holds a pasted blob to. Rejects an empty name/blob rather than
        // silently substituting a default, since a save the pilot didn't name shouldn't appear to
        // have worked.
        public static bool SaveLayout(string? name, string? shell, string? dataJson)
        {
            if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(dataJson)) return false;
            if (JsonLite.Parse(dataJson) is not Dictionary<string, object?>) return false;

            _layouts.Add(new Layout
            {
                Id = FreshId(),
                Name = UniqueName(name!.Trim(), null),
                Shell = shell ?? string.Empty,
                DataJson = dataJson!,
            });
            Save();
            return true;
        }
    }
}
