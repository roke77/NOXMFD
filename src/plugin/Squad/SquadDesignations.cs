using System.Collections.Generic;

namespace NOXMFD
{
    // Squadron Callsign System designations (docs/squad-callsign-names.md) — pure, BCL-only, so
    // tools/tests links it directly. sqd.js's squadDesignation() renders the same format.
    internal static class SquadDesignations
    {
        // "<CALLSIGN> <FLIGHT>-<MEMBER>", e.g. "TALON 1-3". "SQD" stands in for a callsign not yet
        // received, the same fallback sqd.js shows.
        internal static string Format(string callsign, int flight, int member) =>
            (string.IsNullOrEmpty(callsign) ? "SQD" : callsign) + " " + flight + "-" + member;

        // SteamID → designation for a whole squad: the leader is 1, memberIds[i] is i + 2 — the same
        // positional numbering the SQD roster and TD's slots use. Zero ids are skipped.
        internal static Dictionary<ulong, string> Build(string callsign, int flight, ulong leaderId, IReadOnlyList<ulong> memberIds)
        {
            var result = new Dictionary<ulong, string>();
            if (leaderId != 0) result[leaderId] = Format(callsign, flight, 1);
            for (int i = 0; i < memberIds.Count; i++)
                if (memberIds[i] != 0) result[memberIds[i]] = Format(callsign, flight, i + 2);
            return result;
        }

        // "TALON 1-3 [F-16]" → "TALON 1-3 (SteamName) [F-16]" when unitName is `shown`'s own label
        // (also "TALON 1-3 pilot" after an ejection); null when it isn't.
        internal static string? InsertSteamName(string unitName, string shown, string steamName) =>
            shown.Length > 0 && unitName.StartsWith(shown + " ", System.StringComparison.Ordinal)
                ? shown + " (" + steamName + ")" + unitName.Substring(shown.Length)
                : null;

        // Swaps list[index] with its neighbour one step in `dir` (-1 up, +1 down). False, list
        // untouched, when either position falls outside the list.
        internal static bool TrySwap<T>(List<T> list, int index, int dir)
        {
            int target = index + dir;
            if (dir == 0 || index < 0 || index >= list.Count || target < 0 || target >= list.Count) return false;
            (list[index], list[target]) = (list[target], list[index]);
            return true;
        }
    }
}
