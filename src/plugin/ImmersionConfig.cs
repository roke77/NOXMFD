using BepInEx.Configuration;

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
        private static ConfigEntry<bool>? _hudFiltersOnCombatMode;

        public static bool RadarOnOnStart       => _radarOnOnStart?.Value ?? true;
        public static bool EngineOnOnStart      => _engineOnOnStart?.Value ?? true;
        public static bool MasterArmsOnOnStart  => _masterArmsOnOnStart?.Value ?? true;
        // Defaults OFF, unlike the others: an opt-in behavior, not a preserved default.
        public static bool HudFiltersOnCombatMode => _hudFiltersOnCombatMode?.Value ?? false;

        // Writing .Value persists immediately to the .cfg.
        public static void SetRadarOnOnStart(bool v)      { if (_radarOnOnStart      != null) _radarOnOnStart.Value      = v; }
        public static void SetEngineOnOnStart(bool v)     { if (_engineOnOnStart     != null) _engineOnOnStart.Value     = v; }
        public static void SetMasterArmsOnOnStart(bool v) { if (_masterArmsOnOnStart != null) _masterArmsOnOnStart.Value = v; }
        public static void SetHudFiltersOnCombatMode(bool v) { if (_hudFiltersOnCombatMode != null) _hudFiltersOnCombatMode.Value = v; }

        public static void Bind(ConfigFile config)
        {
            const string section = "Immersion Options";
            _radarOnOnStart = config.Bind(section, "RadarOnOnStart", true,
                new ConfigDescription("Radar starts ON when spawning in a new aircraft (the game's own default). Turn OFF for more immersion: radar starts off, arm it yourself.", null, Hidden));
            _engineOnOnStart = config.Bind(section, "EngineOnOnStart", true,
                new ConfigDescription("Engine starts ON when spawning in a new aircraft (the game's own default). Turn OFF for more immersion: engine starts off, start it yourself.", null, Hidden));
            _masterArmsOnOnStart = config.Bind(section, "MasterArmsOnOnStart", true,
                new ConfigDescription("Master Arm starts ON (unrestricted, today's behaviour) when spawning in a new aircraft. Turn OFF for more immersion: guns/missiles/bombs are blocked until you arm.", null, Hidden));
            _hudFiltersOnCombatMode = config.Bind(section, "HudFiltersOnCombatMode", false,
                new ConfigDescription("Switching combat mode to A/A or A/G forces the HUD's matching preset onto the HUD page, restoring your own values on returning to idle. Off by default — turn ON to have combat mode drive the HUD automatically.", null, Hidden));
        }
    }
}
