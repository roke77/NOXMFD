using BepInEx.Configuration;

namespace NOXMFD
{
    // Start-state settings for docs/radar-master-arms.md (issue #32): whether a freshly-spawned
    // aircraft's radar/engine/master-arms begin ON (today's behaviour, the default) or OFF (the
    // more-immersive alternative). Each is a persisted, hidden-from-F1-menu ConfigEntry<bool>, same
    // shape as HudDeclutterConfig — the KEY page's new "Immersion options" section is the control
    // surface, not the F1 menu.
    //
    // These only gate the ONE-TIME spawn default (applied by HarmonyPatches, before the game's own
    // spawn code would otherwise turn radar/ignition on) — they are read once per spawn, not polled.
    // The in-flight Radar ON/OFF and Engine ON/OFF keybinds are unaffected by these and always use
    // the existing CmdToggleRadar()/CmdToggleIgnition() calls regardless of this setting.
    internal static class ImmersionConfig
    {
        // See HudDeclutterConfig for why this shape: ConfigurationManager reads Browsable off a
        // duck-typed attributes object by reflection; declaring our own avoids a hard dependency.
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
        // HudCombatModeFilters' own on/off switch (issue #50 follow-up) — unlike the three above,
        // this one defaults OFF: forcing HUD filters on combat mode is a new, opinionated behavior
        // a pilot has to opt into, not today's-behavior-preserved-by-default like the others.
        public static bool HudFiltersOnCombatMode => _hudFiltersOnCombatMode?.Value ?? false;

        // Runtime setters for the KEY page's toggles (keybind.set-radar-on-start etc.).
        // Writing .Value persists the choice to the .cfg immediately.
        public static void SetRadarOnOnStart(bool v)      { if (_radarOnOnStart      != null) _radarOnOnStart.Value      = v; }
        public static void SetEngineOnOnStart(bool v)     { if (_engineOnOnStart     != null) _engineOnOnStart.Value     = v; }
        public static void SetMasterArmsOnOnStart(bool v) { if (_masterArmsOnOnStart != null) _masterArmsOnOnStart.Value = v; }
        public static void SetHudFiltersOnCombatMode(bool v) { if (_hudFiltersOnCombatMode != null) _hudFiltersOnCombatMode.Value = v; }

        // Called once from Plugin.Awake with the plugin's ConfigFile.
        public static void Bind(ConfigFile config)
        {
            const string section = "Immersion Options";
            _radarOnOnStart = config.Bind(section, "RadarOnOnStart", true,
                new ConfigDescription("Radar starts ON when spawning in a new aircraft (the game's own default). Turn OFF for more immersion: radar starts off, arm it yourself.", null, Hidden));
            _engineOnOnStart = config.Bind(section, "EngineOnOnStart", true,
                new ConfigDescription("Engine starts ON when spawning in a new aircraft (the game's own default). Turn OFF for more immersion: engine starts off, start it yourself.", null, Hidden));
            _masterArmsOnOnStart = config.Bind(section, "MasterArmsOnOnStart", true,
                new ConfigDescription("Master Arms starts ON (unrestricted, today's behaviour) when spawning in a new aircraft. Turn OFF for more immersion: guns/missiles/bombs are blocked until you arm.", null, Hidden));
            _hudFiltersOnCombatMode = config.Bind(section, "HudFiltersOnCombatMode", false,
                new ConfigDescription("Switching combat mode to A/A or A/G forces the HUD's matching preset onto the HUD page, restoring your own values on returning to idle. Off by default — turn ON to have combat mode drive the HUD automatically.", null, Hidden));
        }
    }
}
