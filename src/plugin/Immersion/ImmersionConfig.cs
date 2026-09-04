using BepInEx.Configuration;
using System.Threading;

namespace NOXMFD
{
    // Spawn-time defaults for radar/engine/master-arms; persisted, hidden from the F1 menu, controlled
    // from the KEY page's Immersion Options section instead. Read once per spawn (by HarmonyPatches),
    // not polled; in-flight Radar/Engine toggles are unaffected.
    internal static class ImmersionConfig
    {
        // Duck-typed attributes object ConfigurationManager reads via reflection (avoids a hard dependency).
        private sealed class ConfigurationManagerAttributes { public bool? Browsable; }
        private static readonly ConfigurationManagerAttributes Hidden =
            new ConfigurationManagerAttributes { Browsable = false };

        private static ConfigEntry<bool>? _radarOnOnStart;
        private static ConfigEntry<bool>? _engineOnOnStart;
        private static ConfigEntry<bool>? _masterArmsOnOnStart;
        private static ConfigEntry<bool>? _powerOnOnStart;
        private static ConfigEntry<bool>? _hudFiltersOnCombatMode;
        private static long _configVersion;

        internal static long ConfigVersion => Interlocked.Read(ref _configVersion);

        public static bool RadarOnOnStart       => _radarOnOnStart?.Value ?? true;
        public static bool EngineOnOnStart      => _engineOnOnStart?.Value ?? true;
        public static bool MasterArmsOnOnStart  => _masterArmsOnOnStart?.Value ?? true;
        public static bool PowerOnOnStart       => _powerOnOnStart?.Value ?? true;
        // Defaults OFF, unlike the others: an opt-in behavior, not a preserved default.
        public static bool HudFiltersOnCombatMode => _hudFiltersOnCombatMode?.Value ?? false;

        // Writing .Value persists immediately to the .cfg; the version wakes connected KEY clients.
        private static void Set(ConfigEntry<bool>? entry, bool value)
        {
            if (entry == null || entry.Value == value) return;
            entry.Value = value;
            Interlocked.Increment(ref _configVersion);
        }

        public static void SetRadarOnOnStart(bool v)         => Set(_radarOnOnStart, v);
        public static void SetEngineOnOnStart(bool v)        => Set(_engineOnOnStart, v);
        public static void SetMasterArmsOnOnStart(bool v)    => Set(_masterArmsOnOnStart, v);
        public static void SetPowerOnOnStart(bool v)         => Set(_powerOnOnStart, v);
        public static void SetHudFiltersOnCombatMode(bool v) => Set(_hudFiltersOnCombatMode, v);

        public static void Bind(ConfigFile config)
        {
            const string section = "Immersion Options";
            _radarOnOnStart = config.Bind(section, "RadarOnOnStart", true,
                new ConfigDescription("Radar starts ON when spawning in a new aircraft (the game's own default). Turn OFF for more immersion: radar starts off, arm it yourself.", null, Hidden));
            _engineOnOnStart = config.Bind(section, "EngineOnOnStart", true,
                new ConfigDescription("Engine starts ON when spawning in a new aircraft (the game's own default). Turn OFF for more immersion: engine starts off, start it yourself.", null, Hidden));
            _masterArmsOnOnStart = config.Bind(section, "MasterArmsOnOnStart", true,
                new ConfigDescription("Master Arm starts ON (unrestricted, today's behaviour) when spawning in a new aircraft. Turn OFF for more immersion: guns/missiles/bombs are blocked until you arm.", null, Hidden));
            _powerOnOnStart = config.Bind(section, "PowerOnOnStart", true,
                new ConfigDescription("Power starts ON (full HUD, today's behaviour) when spawning in a new aircraft. Turn OFF for more immersion: no in-cockpit HUD until you power up.", null, Hidden));
            _hudFiltersOnCombatMode = config.Bind(section, "HudFiltersOnCombatMode", false,
                new ConfigDescription("Switching combat mode to A/A or A/G forces the HUD's matching preset onto the HUD page, restoring your own values on returning to idle. Off by default — turn ON to have combat mode drive the HUD automatically.", null, Hidden));
        }
    }
}
