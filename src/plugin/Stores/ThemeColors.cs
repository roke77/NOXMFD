using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace NOXMFD
{
    // The colour rules behind CFG > UI's themes (issue 105): which colors.css tokens a player may
    // override (plus the SOI ring's line style, width and spacing, picked from fixed options), what a
    // valid value is, the CSS an active theme serves, and the shareable export code.
    // A theme's values end up inside served CSS, so everything that reaches it — a /command, the
    // themes file, a pasted code — goes through Normalize first. BCL-only so tools/tests links it.
    internal static class ThemeColors
    {
        // The editable tokens, in panel order. A "-rgb" token is a triple in colors.css (its solid
        // colour, alpha washes and calculated shades all derive from it); the rest are plain colours.
        // Must match the UI page's own token table (pages/ui/ui-tokens.js, checked by its test).
        internal static readonly string[] Tokens =
        {
            // Core palette
            "--no-green-rgb", "--no-white-rgb", "--no-red-rgb", "--no-amber-rgb", "--no-gray-rgb",
            "--no-bg", "--no-panel-border", "--no-ink", "--no-label-rgb",
            // Accents
            "--no-squad-rgb", "--no-purple-rgb", "--no-blue-rgb", "--no-friendly-blue", "--no-hud-friendly",
            // Threats
            "--no-threat-white", "--no-threat-yellow", "--no-threat-red", "--no-jam-yellow-rgb",
            // Map & scope symbology
            "--no-route-cyan", "--no-reached-gray", "--no-target-orange", "--no-neutral-gray",
            "--no-nuclear-orange-rgb", "--no-hsd-pink-rgb", "--no-hsd-yellow-rgb",
            // SOI focus ring and cursor
            "--no-soi", "--no-soi-style", "--no-soi-width", "--no-soi-inset",
        };

        // Tokens picked from fixed options rather than set to a colour, each option written as
        // "word" or "word=served CSS value" (a theme stores the word). Not in colors.css: the shells read
        // them with the first option's value as the var() fallback. The UI page's table lists the same
        // options (ui-tokens.test.js checks).
        internal static readonly Dictionary<string, string[]> Options = new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            ["--no-soi-style"] = new[] { "solid", "dashed", "dotted", "double" },
            ["--no-soi-width"] = new[] { "sm=2px", "md=3px", "lg=4px" },
            ["--no-soi-inset"] = new[] { "none=0px", "sm=2px", "md=4px", "lg=6px" },
        };

        // What a theme file in the themes folder calls each token, index for index with Tokens: the UI
        // page's row label as a slug ("FRIENDLY (TGT / TD)" → "friendly-tgt-td"), or the key its table
        // names for the SOI rows, so a hand-edited file names a colour's role, not the colour the token
        // happens to be named after. ui-tokens.test.js checks these against the page's table.
        internal static readonly string[] FileKeys =
        {
            // Core palette
            "primary", "instrument", "alert", "caution", "inactive",
            "background", "panel-border", "text-on-highlight", "nav-label",
            // Accents
            "squad", "mod-controls", "mod-accent", "friendly-tgt-td", "friendly-hud",
            // Threats
            "search", "track", "lock", "jamming",
            // Map & scope symbology
            "route", "flown-route", "target", "neutral",
            "nuclear-zone", "hsd-symbology", "hsd-aa-rings",
            // SOI
            "soi", "soi-style", "soi-width", "soi-spacing",
        };

        private static readonly HashSet<string> TokenSet = new HashSet<string>(Tokens, StringComparer.Ordinal);
        private static readonly Dictionary<string, string> TokenByFileKey = BuildTokenByFileKey();

        private static Dictionary<string, string> BuildTokenByFileKey()
        {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < FileKeys.Length; i++) map[FileKeys[i]] = Tokens[i];
            return map;
        }
        private static readonly Regex HexPattern = new Regex(@"\A#[0-9a-fA-F]{6}\z", RegexOptions.CultureInvariant);

        internal const int MaxNameLength = 32;
        internal const string CodePrefix = "NOXT1:";
        // A full-theme code is ~1 KB; anything far past that isn't one, so it's rejected before decoding.
        private const int MaxCodeLength = 4096;

        internal static bool IsToken(string? token) => token != null && TokenSet.Contains(token);

        // A value as stored: lowercase #rrggbb, or for an option token one of its words in lower case;
        // null when the token isn't editable or the value isn't one of those.
        internal static string? Normalize(string? token, string? value)
        {
            if (!IsToken(token) || value == null) return null;
            if (Options.TryGetValue(token!, out string[]? options))
            {
                foreach (string option in options)
                {
                    string word = option.Split('=')[0];
                    if (string.Equals(word, value, StringComparison.OrdinalIgnoreCase)) return word;
                }
                return null;
            }
            return HexPattern.IsMatch(value) ? value.ToLowerInvariant() : null;
        }

        // The CSS an option token's stored word serves ("md" → "3px"; a plain word serves itself).
        private static string OptionCss(string token, string word)
        {
            foreach (string option in Options[token])
            {
                string[] parts = option.Split('=');
                if (parts[0] == word) return parts.Length > 1 ? parts[1] : word;
            }
            return word;
        }

        // A display name, or null when nothing is left after trimming.
        internal static string? CleanName(string? name)
        {
            string trimmed = (name ?? string.Empty).Trim();
            if (trimmed.Length == 0) return null;
            return trimmed.Length > MaxNameLength ? trimmed.Substring(0, MaxNameLength) : trimmed;
        }

        internal static string UniqueName(string name, IEnumerable<string> taken)
        {
            var set = new HashSet<string>(taken, StringComparer.Ordinal);
            if (!set.Contains(name)) return name;
            int n = 2;
            while (set.Contains(name + " (" + n + ")")) n++;
            return name + " (" + n + ")";
        }

        // The override stylesheet for a theme: one :root block, tokens in panel order, "-rgb" tokens
        // written as the "r, g, b" triple colors.css uses, option tokens as their CSS value. An empty
        // theme is an empty stylesheet.
        internal static string BuildCss(IReadOnlyDictionary<string, string> colors)
        {
            var sb = new StringBuilder();
            foreach (string token in Tokens)
            {
                if (!colors.TryGetValue(token, out string? value) || Normalize(token, value) is not string clean) continue;
                if (sb.Length == 0) sb.Append(":root{");
                string css = Options.ContainsKey(token) ? OptionCss(token, clean)
                    : token.EndsWith("-rgb", StringComparison.Ordinal) ? Triple(clean) : clean;
                sb.Append(token).Append(':').Append(css).Append(';');
            }
            if (sb.Length > 0) sb.Append('}');
            return sb.ToString();
        }

        private static string Triple(string hex) =>
            Convert.ToInt32(hex.Substring(1, 2), 16) + ", " +
            Convert.ToInt32(hex.Substring(3, 2), 16) + ", " +
            Convert.ToInt32(hex.Substring(5, 2), 16);

        internal static string ColorsJson(IReadOnlyDictionary<string, string> colors)
        {
            var sb = new StringBuilder("{");
            foreach (string token in Tokens)
            {
                if (!colors.TryGetValue(token, out string? value)) continue;
                if (sb.Length > 1) sb.Append(',');
                sb.Append('"').Append(token).Append("\":\"").Append(value).Append('"');
            }
            return sb.Append('}').ToString();
        }

        // Keeps only editable tokens with valid values, so a hand-edited file or a pasted code can't
        // smuggle anything into the served CSS.
        internal static Dictionary<string, string> ParseColors(object? value)
        {
            var colors = new Dictionary<string, string>(StringComparer.Ordinal);
            if (value is not Dictionary<string, object?> d) return colors;
            foreach (var kv in d)
                if (Normalize(kv.Key, kv.Value as string) is string hex) colors[kv.Key] = hex;
            return colors;
        }

        // A theme file from the themes folder: {"name": "...", "colors": {"primary": "#a6e22e", ...}}, keyed
        // by FileKeys (any case). The name falls back to the file's own name; a file with no valid colour
        // at all is rejected, since it's almost certainly not a theme file (or a typo in every key).
        internal static bool TryParseFileTheme(string? json, string fallbackName, out string name, out Dictionary<string, string> colors)
        {
            name = string.Empty;
            colors = new Dictionary<string, string>(StringComparer.Ordinal);
            if (JsonLite.Parse(json ?? string.Empty) is not Dictionary<string, object?> root) return false;
            if (root.TryGetValue("colors", out object? c) && c is Dictionary<string, object?> fileColors)
                foreach (var kv in fileColors)
                    if (TokenByFileKey.TryGetValue(kv.Key, out string? token) && Normalize(token, kv.Value as string) is string hex)
                        colors[token] = hex;
            if (colors.Count == 0) return false;
            if (CleanName(root.TryGetValue("name", out object? n) ? n as string : null) is string clean) name = clean;
            else if (CleanName(fallbackName) is string fallback) name = fallback;
            else return false;
            return true;
        }

        // Share code: the prefix (its digit is the format version) plus base64 of {"n":name,"c":{…}}.
        internal static string BuildCode(string name, IReadOnlyDictionary<string, string> colors)
        {
            string json = "{\"n\":\"" + JsonLite.EscapeJson(name) + "\",\"c\":" + ColorsJson(colors) + "}";
            return CodePrefix + Convert.ToBase64String(Encoding.UTF8.GetBytes(json));
        }

        internal static bool TryParseCode(string? code, out string name, out Dictionary<string, string> colors)
        {
            name = string.Empty;
            colors = new Dictionary<string, string>(StringComparer.Ordinal);
            string text = (code ?? string.Empty).Trim();
            if (text.Length > MaxCodeLength || !text.StartsWith(CodePrefix, StringComparison.Ordinal)) return false;
            string json;
            try { json = Encoding.UTF8.GetString(Convert.FromBase64String(text.Substring(CodePrefix.Length))); }
            catch (FormatException) { return false; }
            if (JsonLite.Parse(json) is not Dictionary<string, object?> root) return false;
            if (CleanName(root.TryGetValue("n", out object? n) ? n as string : null) is not string clean) return false;
            name = clean;
            colors = ParseColors(root.TryGetValue("c", out object? c) ? c : null);
            return true;
        }
    }
}
