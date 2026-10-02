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
    }
}
