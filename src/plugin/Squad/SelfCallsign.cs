using System.Collections.Generic;
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

        private static ConfigEntry<string>? _favorites;
        private static readonly List<string> _favoriteList = new List<string>();

        // Callsign names starred on SQD, pinned at the top of its pickers (PilotCallsign's rules):
        // only those in the active list (CustomCallsigns), the rest stay stored.
        internal static List<string> Favorites => PilotCallsign.Visible(_favoriteList, CustomCallsigns.Names);

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
            _favorites = config.Bind(section, "FavoriteCallsigns", string.Empty,
                new ConfigDescription("Callsign names pinned at the top of the SQD pickers, comma-separated, starred on the SQD page.", null, Hidden));
            _favoriteList.Clear();
            _favoriteList.AddRange(PilotCallsign.ParseFavorites(_favorites.Value));
        }

        // false (nothing changed) when the name is invalid, already in/out, or the list is full.
        internal static bool SetFavorite(string? name, bool on)
        {
            if (!PilotCallsign.SetFavorite(_favoriteList, name, on, CustomCallsigns.Names)) return false;
            if (_favorites != null) _favorites.Value = PilotCallsign.FormatFavorites(_favoriteList);
            return true;
        }

        // false (nothing changed) when the callsign is out of range. Writing the entries persists them at once.
        internal static bool Set(string? name, int flight, int number)
        {
            if (!PilotCallsign.TryCreate(name, flight, number, out string clean)) return false;
            Store(clean, flight, number);
            return true;
        }

        // Back to none, so the pilot shows under their Steam name again.
        internal static void Clear() => Store(string.Empty, 1, 1);

        private static void Store(string name, int flight, int number)
        {
            Name = name; Flight = flight; Number = number;
            if (_name != null) _name.Value = name;
            if (_flight != null) _flight.Value = flight;
            if (_number != null) _number.Value = number;
        }
    }
}
