using BepInEx.Configuration;

namespace NOXMFD
{
    // Toggles for hiding native in-game HUD elements (so the web MFD can replace them without
    // on-screen clutter). Each flag is backed by a BepInEx ConfigEntry, bound once from
    // Plugin.Awake via Bind(). That does three things:
    //   * persists the values to BepInEx/config/com.roque.NOXMFD.cfg,
    //   * registers them with BepInEx so any config UI can edit them in-game. The de-facto one is
    //     ConfigurationManager (the F1 settings menu, also surfaced by NOMM): our entries appear
    //     under a "NO XMFD" section with a checkbox each, no NOMM-specific code required,
    //   * lets the values be flipped at runtime — HudDeclutter reads these properties every tick,
    //     so a toggle in the F1 menu hides-or-restores the affected elements within ~0.5s.
    //
    // ConfigurationManager is a soft dependency: if it isn't installed the settings still persist
    // and still work, they just lack the in-game UI. Until Bind() runs the properties fall back to
    // the defaults, so HudDeclutter behaves correctly even before/without binding.
    //
    // Each per-element flag stands alone — hiding that element = FeaturesActive && Hide<Element>.
    internal static class HudDeclutterConfig
    {
        private static ConfigEntry<bool>? _hideWeaponAmmo;
        private static ConfigEntry<bool>? _hideMinimap;
        private static ConfigEntry<bool>? _hideTopBoxes;

        // Top-right readouts: weapon name + ammo (WeaponIndicator) and the countermeasure
        // count "48 / IR Flares" (CountermeasureIndicator). Both hidden by this one flag.
        public static bool HideWeaponAmmo => _hideWeaponAmmo?.Value ?? false;

        // Bottom-left corner minimap only (game class: DynamicMap). The full-screen M-key map and
        // the airbase-selection map at spawn are the same object maximized, and stay available.
        public static bool HideMinimap => _hideMinimap?.Value ?? false;

        // The boxed numeric readouts flanking the heading tape: heading (Bearing, "188°"),
        // airspeed (SpeedGauge, "1km/h") and radar/abs altitude (Altitude, "R[0,0m]").
        // Only the BOXED copies are hidden (the ones with an assigned 'border' Image); the
        // borderless boresight-following center readouts and the heading tape itself are kept.
        public static bool HideTopBoxes => _hideTopBoxes?.Value ?? false;

        // Called once from Plugin.Awake with the plugin's ConfigFile. The section + description
        // strings become the labels/tooltips shown in the in-game config menu.
        public static void Bind(ConfigFile config)
        {
            const string section = "HUD Declutter";
            _hideWeaponAmmo = config.Bind(section, "HideWeaponAmmo", false,
                "Hide the top-right weapon name / ammo and countermeasure count readouts.");
            _hideMinimap = config.Bind(section, "HideMinimap", false,
                "Hide the bottom-left corner minimap. The full-screen M-key map and airbase-selection map stay available.");
            _hideTopBoxes = config.Bind(section, "HideTopBoxes", false,
                "Hide the boxed heading / airspeed / altitude readouts flanking the heading tape. The center boresight readouts are kept.");
        }
    }
}
