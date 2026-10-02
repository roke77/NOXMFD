namespace NOXMFD
{
    // Callsign designations (docs/squad-callsign-names.md, docs/self-callsign.md) - pure, BCL-only,
    // so tools/tests links it directly. sqd.js's designation() renders the same format.
    internal static class SquadDesignations
    {
        // "<CALLSIGN> <FLIGHT>-<NUMBER>", e.g. "TALON 1-3": a pilot's own callsign. "SQD" stands in
        // for a missing callsign, the same fallback sqd.js shows.
        internal static string Format(string callsign, int flight, int number) =>
            (string.IsNullOrEmpty(callsign) ? "SQD" : callsign) + " " + flight + "-" + number;

        // "VIPER 2-1 (DeckJockey)" for a pilot with a callsign, else just the Steam name; the callsign
        // alone when there is no Steam name. For notices and shares that name a pilot.
        internal static string Label(string designation, string steamName) =>
            designation.Length == 0 ? steamName : steamName.Length == 0 ? designation : designation + " (" + steamName + ")";

        // "TALON 1-3 [F-16]" -> "TALON 1-3 (SteamName) [F-16]" when unitName is `shown`'s own label
        // (also "TALON 1-3 pilot" after an ejection); null when it isn't.
        internal static string? InsertSteamName(string unitName, string shown, string steamName) =>
            shown.Length > 0 && unitName.StartsWith(shown + " ", System.StringComparison.Ordinal)
                ? shown + " (" + steamName + ")" + unitName.Substring(shown.Length)
                : null;
    }
}
