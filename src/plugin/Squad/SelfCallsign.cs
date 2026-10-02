using BepInEx.Configuration;

namespace NOXMFD
{
    // This pilot's own callsign (issue #107, docs/self-callsign.md): "CALLSIGN F-N", set on the SQD
    // page, kept in the BepInEx config so it survives a restart (unlike a squad membership), and sent
    // to the whole faction in the identity record (Presence.cs). The config entries are hidden from
    // the F1 menu; SQD is the one place to change them. The rules are PilotCallsign's.
    internal static class SelfCallsign
    {
        // Duck-typed attributes object ConfigurationManager reads via reflection (avoids a hard dependency).
        private sealed class ConfigurationManagerAttributes { public bool? Browsable; }
        private static readonly ConfigurationManagerAttributes Hidden =
            new ConfigurationManagerAttributes { Browsable = false };

        private static ConfigEntry<string>? _name;
        private static ConfigEntry<int>? _flight;
        private static ConfigEntry<int>? _number;

        // What the config held at Bind, or last Set: empty Name = none set. A hand-edited, invalid
        // config reads as none.
        internal static string Name { get; private set; } = string.Empty;
        internal static int Flight { get; private set; } = 1;
        internal static int Number { get; private set; } = 1;

        internal static bool IsSet => Name.Length > 0;
        internal static string Designation => IsSet ? SquadDesignations.Format(Name, Flight, Number) : string.Empty;

        internal static void Bind(ConfigFile config)
        {
            const string section = "Squad";
            _name = config.Bind(section, "Callsign", string.Empty,
                new ConfigDescription("Your own callsign, set on the SQD page.", null, Hidden));
            _flight = config.Bind(section, "CallsignFlight", 1,
                new ConfigDescription("The flight number of your callsign (1-9), set on the SQD page.", null, Hidden));
            _number = config.Bind(section, "CallsignNumber", 1,
                new ConfigDescription("The number of your callsign (1-9), set on the SQD page.", null, Hidden));
            if (PilotCallsign.TryCreate(_name.Value, _flight.Value, _number.Value, out string name))
            {
                Name = name; Flight = _flight.Value; Number = _number.Value;
            }
        }

        // false (nothing changed) when the callsign is out of range. Writing the entries persists them at once.
        internal static bool Set(string? name, int flight, int number)
        {
            if (!PilotCallsign.TryCreate(name, flight, number, out string clean)) return false;
            Name = clean; Flight = flight; Number = number;
            if (_name != null) _name.Value = clean;
            if (_flight != null) _flight.Value = flight;
            if (_number != null) _number.Value = number;
            return true;
        }
    }
}
