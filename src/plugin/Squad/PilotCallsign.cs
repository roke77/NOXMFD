using System;
using System.Collections.Generic;

namespace NOXMFD
{
    // A pilot's own callsign, "CALLSIGN F-N" (issue #107, docs/self-callsign.md): a name from the SQD
    // picker plus two numbers 1-9. Pure and BCL-only so tools/tests links it directly. The same name
    // rule covers a squad's callsign, and a received record is held to it too: it checks the SHAPE
    // (length, control characters), not membership of the picker's list, which can grow (#102).
    internal static class PilotCallsign
    {
        internal const int MaxChars = 20;   // the squad callsign limit too
        internal const int MinNumber = 1;
        internal const int MaxNumber = 9;

        // The trimmed name, or null when it isn't a valid callsign name.
        internal static string? CleanName(string? name)
        {
            string s = (name ?? string.Empty).Trim();
            if (s.Length == 0 || s.Length > MaxChars) return null;
            foreach (char ch in s) if (ch < 0x20 || ch == 0x7f) return null;
            return s;
        }

        internal static bool NumberOk(int n) => n >= MinNumber && n <= MaxNumber;

        // Normalises a callsign typed or sent by a client: false (and an empty name) when any part is out of range.
        internal static bool TryCreate(string? name, int flight, int number, out string callsign)
        {
            string? clean = CleanName(name);
            bool ok = clean != null && NumberOk(flight) && NumberOk(number);
            callsign = ok ? clean! : string.Empty;
            return ok;
        }

        // Favorite callsign names, pinned at the top of the SQD pickers in the order they were
        // starred. Stored as one comma-separated config string, so a name with a comma is refused
        // (the picker list has none). Favorites outside the active list (CustomCallsigns) stay
        // stored, hidden, for when that list is back; the cap counts the visible ones only.
        internal const int MaxFavorites = 3;

        // A hand-edited config is tolerated: invalid names and repeats (any case) are dropped. Not
        // capped here, since hidden favorites don't count; Visible caps what's shown.
        internal static List<string> ParseFavorites(string? stored)
        {
            var list = new List<string>();
            foreach (string part in (stored ?? string.Empty).Split(','))
            {
                string? clean = FavoriteName(part);
                if (clean != null && IndexOf(list, clean) < 0) list.Add(clean);
            }
            return list;
        }

        internal static string FormatFavorites(IEnumerable<string> favorites) => string.Join(",", favorites);

        // The favorites the pickers show: those in `active` (null = the built-in list, which the
        // plugin doesn't hold, so every name), at most MaxFavorites.
        internal static List<string> Visible(IEnumerable<string> favorites, IReadOnlyList<string>? active)
        {
            var shown = new List<string>();
            foreach (string f in favorites)
                if (shown.Count < MaxFavorites && (active == null || IndexOf(active, f) >= 0)) shown.Add(f);
            return shown;
        }

        // Adds (on) or removes a name; false when nothing changed — already in/out, invalid, not in
        // `active`, or the visible list is full.
        internal static bool SetFavorite(List<string> favorites, string? name, bool on, IReadOnlyList<string>? active = null)
        {
            string? clean = FavoriteName(name);
            if (clean == null) return false;
            int at = IndexOf(favorites, clean);
            if (!on)
            {
                if (at < 0) return false;
                favorites.RemoveAt(at);
                return true;
            }
            if (at >= 0 || (active != null && IndexOf(active, clean) < 0)
                || Visible(favorites, active).Count >= MaxFavorites) return false;
            favorites.Add(clean);
            return true;
        }

        private static string? FavoriteName(string? name)
        {
            string? clean = CleanName(name);
            return clean == null || clean.IndexOf(',') >= 0 ? null : clean;
        }

        private static int IndexOf(IReadOnlyList<string> list, string name)
        {
            for (int i = 0; i < list.Count; i++)
                if (string.Equals(list[i], name, StringComparison.OrdinalIgnoreCase)) return i;
            return -1;
        }
    }
}
