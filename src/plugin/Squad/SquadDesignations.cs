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

        // SteamID → designation for a whole squad: the leader is 1, every member keeps the slot they
        // hold (2..n, with holes left by departures). Zero ids are skipped.
        internal static Dictionary<ulong, string> Build(string callsign, int flight, ulong leaderId, IReadOnlyList<(ulong Id, int Slot)> members)
        {
            var result = new Dictionary<ulong, string>();
            if (leaderId != 0) result[leaderId] = Format(callsign, flight, 1);
            foreach (var m in members)
                if (m.Id != 0) result[m.Id] = Format(callsign, flight, m.Slot);
            return result;
        }

        // "TALON 1-3 [F-16]" → "TALON 1-3 (SteamName) [F-16]" when unitName is `shown`'s own label
        // (also "TALON 1-3 pilot" after an ejection); null when it isn't.
        internal static string? InsertSteamName(string unitName, string shown, string steamName) =>
            shown.Length > 0 && unitName.StartsWith(shown + " ", System.StringComparison.Ordinal)
                ? shown + " (" + steamName + ")" + unitName.Substring(shown.Length)
                : null;

        // The slot a new member joins at: the lowest free one from 2 up, so a departure's hole is
        // filled before the squad grows.
        internal static int FirstFreeSlot(IEnumerable<int> taken)
        {
            var used = new HashSet<int>(taken);
            int slot = 2;
            while (used.Contains(slot)) slot++;
            return slot;
        }

        // Where the SQD roster's ▲ (dir -1) / ▼ (+1) takes a member at `slot`: the neighbouring slot,
        // held or empty, within 2..maxSlot (the highest held slot, so nobody can walk past the end of
        // the squad). -1 when it would leave that range.
        internal static int MoveTarget(int slot, int dir, int maxSlot)
        {
            int target = slot + dir;
            return dir != 0 && slot >= 2 && target >= 2 && target <= maxSlot ? target : -1;
        }
    }
}
