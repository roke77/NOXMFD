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
        // Read from a file in the themes folder: listed and selectable, but read-only, like DEFAULT.
        public bool FromFile;
    }

    // Saved colour themes for CFG > UI (issue 105). Server-side, like LayoutStore, so the active theme
    // is the same on every browser and device. DEFAULT is not stored: it is the absence of overrides,
    // always present and read-only, so a player can always get back to the stock colours.
    //
    // Themes can also come from JSON files a player drops in the themes folder (ThemesDir, next to
    // DOC's kneeboard folder). Those are read at load and on theme.rescan (the UI page sends it when
    // it opens), listed after the saved themes, and never written back: the folder is their source.
    //
    // Static, plugin-lifetime (NOT mission-scoped): themes are picked at the main menu as much as in
    // a mission. BCL-only behind the same ConfigDir/ThemesDir/LogWarning seam as RouteStore, so
    // tools/tests can drive the whole store.
    internal static class ThemeStore
    {
        internal const string DefaultId = "default";
        // ponytail: a fixed cap keeps the file, /themes and the SSE event small; raise it if players
        // ask for more.
        internal const int MaxThemes = 20;
        // ponytail: folder caps (count and size) bound the work of a rescan on every UI page open.
        internal const int MaxFileThemes = 50;
        private const long MaxFileBytes = 64 * 1024;

        internal static string? ConfigDir;
        internal static string? ThemesDir;
        internal static Action<string>? LogWarning;

        private static List<Theme> _themes = new List<Theme>();
        private static List<Theme> _fileThemes = new List<Theme>();
        private static string _activeId = DefaultId;
        // Files already reported as unusable, so a rescan on every UI open doesn't repeat the warning.
        private static readonly HashSet<string> _warnedFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Server-thread-readable caches, same threading contract as LayoutStore.LayoutsJson: mutators
        // run on the Unity main thread (CommandDispatcher.Drain) and rebuild both as their last step;
        // the HTTP and SSE threads only read the references.
        internal static volatile string StateJson = "{\"active\":\"default\",\"css\":\"\",\"themes\":[]}";
        internal static volatile string ActiveCss = string.Empty;

        private static string FilePath => Path.Combine(ConfigDir ?? ".", "com.roque.NOXMFD.themes.json");

        // ── lifecycle ────────────────────────────────────────────────────────────────────────

        public static void Load()
        {
            _themes = new List<Theme>();
            string active = DefaultId;
            if (File.Exists(FilePath))
            {
                try
                {
                    if (JsonLite.Parse(File.ReadAllText(FilePath)) is Dictionary<string, object?> root)
                    {
                        _themes = ParseThemes(root.TryGetValue("themes", out object? t) ? t : null);
                        active = root.TryGetValue("active", out object? a) ? (a as string ?? DefaultId) : DefaultId;
                    }
                }
                catch (Exception ex)
                {
                    LogWarning?.Invoke($"[NOXMFD] themes file unreadable, starting with DEFAULT: {ex.Message}");
                    _themes = new List<Theme>();
                }
            }
            _fileThemes = ScanFolder();
            _activeId = Find(active) != null ? active : DefaultId;
            Rebuild();
        }

        // Re-reads the themes folder (theme.rescan). If the active theme's file is gone, DEFAULT
        // becomes active and that choice is saved.
        public static bool Rescan()
        {
            _fileThemes = ScanFolder();
            if (Find(_activeId) == null && _activeId != DefaultId)
            {
                _activeId = DefaultId;
                Save();
            }
            else Rebuild();
            return true;
        }

        // Every *.json in ThemesDir that parses as a theme, sorted by file name. The id comes from the
        // file name, so the active theme survives a rescan and a game restart.
        private static List<Theme> ScanFolder()
        {
            var themes = new List<Theme>();
            if (string.IsNullOrEmpty(ThemesDir)) return themes;
            try
            {
                if (!Directory.Exists(ThemesDir)) return themes;
                string[] files = Directory.GetFiles(ThemesDir!, "*.json");
                Array.Sort(files, StringComparer.OrdinalIgnoreCase);
                foreach (string path in files)
                {
                    if (themes.Count >= MaxFileThemes) break;
                    string file = Path.GetFileName(path);
                    string? text = null;
                    try { if (new FileInfo(path).Length <= MaxFileBytes) text = File.ReadAllText(path); }
                    catch (Exception ex) { WarnFile(file, ex.Message); continue; }
                    if (ThemeColors.TryParseFileTheme(text, Path.GetFileNameWithoutExtension(path), out string name, out Dictionary<string, string> colors))
                    {
                        _warnedFiles.Remove(file);
                        themes.Add(new Theme { Id = "f_" + file.ToLowerInvariant(), Name = name, Colors = colors, FromFile = true });
                    }
                    else WarnFile(file, text == null ? "larger than 64 KB" : "not a theme file (needs a \"colors\" object with valid colours)");
                }
            }
            catch (Exception ex) { LogWarning?.Invoke($"[NOXMFD] themes folder unreadable: {ex.Message}"); }
            return themes;
        }

        private static void WarnFile(string file, string reason)
        {
            if (_warnedFiles.Add(file)) LogWarning?.Invoke($"[NOXMFD] skipped theme file {file}: {reason}");
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
            StateJson = BuildState();
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
        // shells apply it live), and every saved theme then every folder theme ("file": true), each
        // with its share code.
        private static string BuildState()
        {
            var sb = new StringBuilder();
            Theme? active = Find(_activeId);
            sb.Append("{\"active\":\"").Append(JsonLite.EscapeJson(active != null ? _activeId : DefaultId))
              .Append("\",\"css\":\"").Append(JsonLite.EscapeJson(ThemeColors.BuildCss(active?.Colors ?? new Dictionary<string, string>())))
              .Append("\",\"themes\":[");
            bool first = true;
            foreach (Theme t in AllThemes())
            {
                if (!first) sb.Append(',');
                first = false;
                AppendTheme(sb, t, withCode: true);
            }
            return sb.Append("]}").ToString();
        }

        private static IEnumerable<Theme> AllThemes()
        {
            foreach (Theme t in _themes) yield return t;
            foreach (Theme t in _fileThemes) yield return t;
        }

        private static void AppendTheme(StringBuilder sb, Theme t, bool withCode)
        {
            sb.Append("{\"id\":\"").Append(JsonLite.EscapeJson(t.Id))
              .Append("\",\"name\":\"").Append(JsonLite.EscapeJson(t.Name)).Append('"');
            if (withCode) sb.Append(",\"code\":\"").Append(ThemeColors.BuildCode(t.Name, t.Colors)).Append('"');
            if (t.FromFile) sb.Append(",\"file\":true");
            sb.Append(",\"colors\":").Append(ThemeColors.ColorsJson(t.Colors)).Append('}');
        }

        // Any theme, saved or from the folder (select, the active theme's colours).
        private static Theme? Find(string? id) => id == null ? null : (_themes.Find(t => t.Id == id) ?? _fileThemes.Find(t => t.Id == id));
        // Saved themes only: the ones rename, delete and colour edits may change.
        private static Theme? FindSaved(string? id) => id == null ? null : _themes.Find(t => t.Id == id);

        private static string FreshId() => "t_" + Guid.NewGuid().ToString("N");

        private static IEnumerable<string> TakenNames(string? excludeId)
        {
            yield return "DEFAULT";
            foreach (Theme t in AllThemes()) if (t.Id != excludeId) yield return t.Name;
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

        // A new theme, made active, starting from another theme's colours: the active one's when
        // fromId is null (DUPLICATE, and editing while DEFAULT or a folder theme is active, so neither
        // ever changes), or none when fromId is DEFAULT (NEW).
        public static bool Create(string? name, string? fromId = null)
        {
            if (ThemeColors.CleanName(name) is not string clean) return false;
            Theme? from = fromId == DefaultId ? null : Find(fromId ?? _activeId);
            return Add(clean, new Dictionary<string, string>(from?.Colors ?? new Dictionary<string, string>(), StringComparer.Ordinal));
        }

        public static bool Import(string? code)
        {
            if (!ThemeColors.TryParseCode(code, out string name, out Dictionary<string, string> colors)) return false;
            return Add(name, colors);
        }

        public static bool Rename(string? id, string? name)
        {
            Theme? theme = FindSaved(id);
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
            Theme? theme = FindSaved(_activeId);
            if (theme == null || ThemeColors.Normalize(token, hex) is not string value) return false;
            theme.Colors[token!] = value;
            Save();
            return true;
        }

        // token null or empty resets every colour of the active theme.
        public static bool ResetColor(string? token)
        {
            Theme? theme = FindSaved(_activeId);
            if (theme == null) return false;
            if (string.IsNullOrEmpty(token)) theme.Colors.Clear();
            else if (!theme.Colors.Remove(token!)) return false;
            Save();
            return true;
        }
    }
}
