using System;
using System.Collections.Generic;
using System.IO;

namespace NOXMFD
{
    // A player's own callsign list (issue #102): BepInEx/plugins/NOXMFD/callsigns.txt replaces the SQD
    // pickers' built-in list (callsigns.js) when it holds at least one valid name. Read once at
    // startup and sent in the SQD state. BCL-only so tools/tests links it directly.
    internal static class CustomCallsigns
    {
        internal static Action<string>? LogWarning;

        // The file's names, sorted; null = no usable file, the page keeps its built-in list.
        internal static IReadOnlyList<string>? Names { get; private set; }

        internal static void Load(string path)
        {
            Names = null;
            if (!File.Exists(path)) return;
            string text;
            try { text = File.ReadAllText(path); }
            catch (Exception e)
            {
                LogWarning?.Invoke($"callsigns.txt unreadable, using the built-in callsign list: {e.Message}");
                return;
            }
            var skipped = new List<string>();
            List<string> names = Parse(text, skipped);
            foreach (string s in skipped) LogWarning?.Invoke($"callsigns.txt: skipped {s}");
            if (names.Count == 0)
            {
                LogWarning?.Invoke("callsigns.txt has no valid callsign, using the built-in callsign list");
                return;
            }
            Names = names;
        }

        // Comma-separated values, each one or more words of A-Z letters (runs of spaces between words
        // collapse to one), at most PilotCallsign.MaxChars. Upper-cased, deduped and sorted; `skipped`
        // gets every refused value with its reason. A value spanning lines is refused rather than
        // joined, since it's most likely two names missing a comma.
        internal static List<string> Parse(string text, List<string> skipped)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var names = new List<string>();
            foreach (string part in text.Split(','))
            {
                string value = part.Trim();
                if (value.Length == 0) continue;   // blank, e.g. after a trailing comma
                string[] words = value.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                string name = string.Join(" ", words).ToUpperInvariant();
                string? why = value.IndexOfAny(new[] { '\r', '\n' }) >= 0 ? "spans lines, missing comma?"
                    : !LettersOnly(words) ? "letters only"
                    : name.Length > PilotCallsign.MaxChars ? $"over {PilotCallsign.MaxChars} characters"
                    : null;
                if (why != null) skipped.Add($"'{value}' ({why})");
                else if (seen.Add(name)) names.Add(name);
            }
            names.Sort(StringComparer.Ordinal);
            return names;
        }

        private static bool LettersOnly(string[] words)
        {
            foreach (string w in words)
                foreach (char ch in w)
                    if (!(ch >= 'A' && ch <= 'Z') && !(ch >= 'a' && ch <= 'z')) return false;
            return true;
        }
    }
}
