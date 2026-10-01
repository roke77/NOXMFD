using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace NOXMFD
{
    internal sealed class Theme
    {
        public string Id = string.Empty;
        public string Name = string.Empty;
        // Only the tokens this theme overrides; any other token keeps its colors.css default.
        public Dictionary<string, string> Colors = new Dictionary<string, string>(StringComparer.Ordinal);
    }

    // Saved colour themes for CFG > UI (issue 105). Server-side, like LayoutStore, so the active theme
    // is the same on every browser and device. DEFAULT is not stored: it is the absence of overrides,
    // always present and read-only, so a player can always get back to the stock colours.
    //
    // Static, plugin-lifetime (NOT mission-scoped): themes are picked at the main menu as much as in
    // a mission. BCL-only behind the same ConfigDir/LogWarning seam as RouteStore, so tools/tests can
    // drive the whole store.
    internal static class ThemeStore
    {
        internal const string DefaultId = "default";
        // ponytail: a fixed cap keeps the file, /themes and the SSE event small; raise it if players
        // ask for more.
        internal const int MaxThemes = 20;

        internal static string? ConfigDir;
        internal static Action<string>? LogWarning;

        private static List<Theme> _themes = new List<Theme>();
        private static string _activeId = DefaultId;

        // Server-thread-readable caches, same threading contract as LayoutStore.LayoutsJson: mutators
        // run on the Unity main thread (CommandDispatcher.Drain) and rebuild both as their last step;
        // the HTTP and SSE threads only read the references.
        internal static volatile string StateJson = BuildState(new List<Theme>(), DefaultId);
        internal static volatile string ActiveCss = string.Empty;

        private static string FilePath => Path.Combine(ConfigDir ?? ".", "com.roque.NOXMFD.themes.json");

        // ── lifecycle ────────────────────────────────────────────────────────────────────────

        public static void Load()
        {
            _themes = new List<Theme>();
            _activeId = DefaultId;
            if (File.Exists(FilePath))
            {
                try
                {
                    if (JsonLite.Parse(File.ReadAllText(FilePath)) is Dictionary<string, object?> root)
                    {
                        _themes = ParseThemes(root.TryGetValue("themes", out object? t) ? t : null);
                        string active = root.TryGetValue("active", out object? a) ? (a as string ?? DefaultId) : DefaultId;
                        _activeId = Find(active) != null ? active : DefaultId;
                    }
                }
                catch (Exception ex)
                {
                    LogWarning?.Invoke($"[NOXMFD] themes file unreadable, starting with DEFAULT: {ex.Message}");
                    _themes = new List<Theme>();
                    _activeId = DefaultId;
                }
            }
            Rebuild();
        }

        private static List<Theme> ParseThemes(object? value)
        {
            var themes = new List<Theme>();
            if (value is not List<object?> list) return themes;
            foreach (object? item in list)
            {
                if (themes.Count >= MaxThemes) break;
                if (item is not Dictionary<string, object?> d) continue;
                string id = d.TryGetValue("id", out object? idv) ? (idv as string ?? string.Empty) : string.Empty;
                if (id.Length == 0 || id == DefaultId) continue;
                if (ThemeColors.CleanName(d.TryGetValue("name", out object? nm) ? nm as string : null) is not string name) continue;
                themes.Add(new Theme { Id = id, Name = name, Colors = ThemeColors.ParseColors(d.TryGetValue("colors", out object? c) ? c : null) });
            }
            return themes;
        }

        private static void Rebuild()
        {
            StateJson = BuildState(_themes, _activeId);
            ActiveCss = ThemeColors.BuildCss(Find(_activeId)?.Colors ?? new Dictionary<string, string>());
        }

        private static void Save()
        {
            Rebuild();
            var sb = new StringBuilder();
            sb.Append("{\"active\":\"").Append(JsonLite.EscapeJson(_activeId)).Append("\",\"themes\":[");
            for (int i = 0; i < _themes.Count; i++)
            {
                if (i > 0) sb.Append(',');
                AppendTheme(sb, _themes[i], withCode: false);
            }
            sb.Append("]}");
            // Back up whatever was on disk BEFORE overwriting it — see RouteStore.Save's comment.
            try { ConfigBackup.BackupIfExists(FilePath); File.WriteAllText(FilePath, sb.ToString()); }
            catch (Exception ex) { LogWarning?.Invoke($"[NOXMFD] failed to persist themes: {ex.Message}"); }
        }

        // What GET /themes and the "themes" SSE event carry: the active id, the CSS it serves (the
        // shells apply it live), and every saved theme with its share code.
        private static string BuildState(List<Theme> themes, string activeId)
        {
            var sb = new StringBuilder();
            Theme? active = themes.Find(t => t.Id == activeId);
            sb.Append("{\"active\":\"").Append(JsonLite.EscapeJson(active != null ? activeId : DefaultId))
              .Append("\",\"css\":\"").Append(JsonLite.EscapeJson(ThemeColors.BuildCss(active?.Colors ?? new Dictionary<string, string>())))
              .Append("\",\"themes\":[");
            for (int i = 0; i < themes.Count; i++)
            {
                if (i > 0) sb.Append(',');
                AppendTheme(sb, themes[i], withCode: true);
            }
            return sb.Append("]}").ToString();
        }

        private static void AppendTheme(StringBuilder sb, Theme t, bool withCode)
        {
            sb.Append("{\"id\":\"").Append(JsonLite.EscapeJson(t.Id))
              .Append("\",\"name\":\"").Append(JsonLite.EscapeJson(t.Name)).Append('"');
            if (withCode) sb.Append(",\"code\":\"").Append(ThemeColors.BuildCode(t.Name, t.Colors)).Append('"');
            sb.Append(",\"colors\":").Append(ThemeColors.ColorsJson(t.Colors)).Append('}');
        }

        private static Theme? Find(string? id) => id == null ? null : _themes.Find(t => t.Id == id);

        private static string FreshId() => "t_" + Guid.NewGuid().ToString("N");

        private static IEnumerable<string> TakenNames(string? excludeId)
        {
            yield return "DEFAULT";
            foreach (Theme t in _themes) if (t.Id != excludeId) yield return t.Name;
        }

        private static bool Add(string name, Dictionary<string, string> colors)
        {
            if (_themes.Count >= MaxThemes) return false;
            var theme = new Theme { Id = FreshId(), Name = ThemeColors.UniqueName(name, TakenNames(null)), Colors = colors };
            _themes.Add(theme);
            _activeId = theme.Id;
            Save();
            return true;
        }

        // ── commands (main thread) ───────────────────────────────────────────────────────────

        // A new theme starting from the active one's colours, made active. Editing while DEFAULT is
        // active goes through here first, so DEFAULT itself never changes.
        public static bool Create(string? name)
        {
            if (ThemeColors.CleanName(name) is not string clean) return false;
            return Add(clean, new Dictionary<string, string>(Find(_activeId)?.Colors ?? new Dictionary<string, string>(), StringComparer.Ordinal));
        }

        public static bool Import(string? code)
        {
            if (!ThemeColors.TryParseCode(code, out string name, out Dictionary<string, string> colors)) return false;
            return Add(name, colors);
        }

        public static bool Rename(string? id, string? name)
        {
            Theme? theme = Find(id);
            if (theme == null || ThemeColors.CleanName(name) is not string clean) return false;
            theme.Name = ThemeColors.UniqueName(clean, TakenNames(theme.Id));
            Save();
            return true;
        }

        public static bool Delete(string? id)
        {
            if (_themes.RemoveAll(t => t.Id == id) == 0) return false;
            if (_activeId == id) _activeId = DefaultId;
            Save();
            return true;
        }

        public static bool Select(string? id)
        {
            if (id != DefaultId && Find(id) == null) return false;
            _activeId = id!;
            Save();
            return true;
        }

        public static bool SetColor(string? token, string? hex)
        {
            Theme? theme = Find(_activeId);
            if (theme == null || ThemeColors.Normalize(token, hex) is not string value) return false;
            theme.Colors[token!] = value;
            Save();
            return true;
        }

        // token null or empty resets every colour of the active theme.
        public static bool ResetColor(string? token)
        {
            Theme? theme = Find(_activeId);
            if (theme == null) return false;
            if (string.IsNullOrEmpty(token)) theme.Colors.Clear();
            else if (!theme.Colors.Remove(token!)) return false;
            Save();
            return true;
        }
    }
}
