using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace NOXMFD
{
    // The colour rules behind CFG > UI's themes (issue 105): which colors.css tokens a player may
    // override, what a valid value is, the CSS an active theme serves, and the shareable export code.
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
            "--no-bg", "--no-panel-border", "--no-ink",
            // Accents
            "--no-squad-rgb", "--no-purple-rgb", "--no-blue-rgb", "--no-friendly-blue", "--no-hud-friendly",
            // Threats
            "--no-threat-white", "--no-threat-yellow", "--no-threat-red", "--no-jam-yellow-rgb",
            // Map & scope symbology
            "--no-route-cyan", "--no-reached-gray", "--no-target-orange", "--no-neutral-gray",
            "--no-nuclear-orange-rgb", "--no-hsd-pink-rgb", "--no-hsd-yellow-rgb",
        };

        private static readonly HashSet<string> TokenSet = new HashSet<string>(Tokens, StringComparer.Ordinal);
        private static readonly Regex HexPattern = new Regex(@"\A#[0-9a-fA-F]{6}\z", RegexOptions.CultureInvariant);

        internal const int MaxNameLength = 32;
        internal const string CodePrefix = "NOXT1:";
        // A 25-colour code is ~1 KB; anything far past that isn't one, so it's rejected before decoding.
        private const int MaxCodeLength = 4096;

        internal static bool IsToken(string? token) => token != null && TokenSet.Contains(token);

        // A value as stored and served: lowercase #rrggbb, or null when the token isn't editable or
        // the value isn't exactly a 6-digit hex colour.
        internal static string? Normalize(string? token, string? hex)
        {
            if (!IsToken(token) || hex == null || !HexPattern.IsMatch(hex)) return null;
            return hex.ToLowerInvariant();
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
        // written as the "r, g, b" triple colors.css uses. An empty theme is an empty stylesheet.
        internal static string BuildCss(IReadOnlyDictionary<string, string> colors)
        {
            var sb = new StringBuilder();
            foreach (string token in Tokens)
            {
                if (!colors.TryGetValue(token, out string? value) || Normalize(token, value) is not string hex) continue;
                if (sb.Length == 0) sb.Append(":root{");
                sb.Append(token).Append(':').Append(token.EndsWith("-rgb", StringComparison.Ordinal) ? Triple(hex) : hex).Append(';');
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
