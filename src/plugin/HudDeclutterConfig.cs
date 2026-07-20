using BepInEx.Configuration;

namespace NOXMFD
{
    // Toggles for hiding native in-game HUD elements (so the web MFD can replace them without
    // on-screen clutter). Each flag is backed by a BepInEx ConfigEntry, bound once from
    // Plugin.Awake via Bind(). The ConfigEntry earns its keep two ways:
    //   * it persists the values to BepInEx/config/com.roque.NOXMFD.cfg, so a declutter choice
    //     survives a restart, and
    //   * it lets the values be flipped at runtime — HudDeclutter reads these properties every tick,
    //     and the MFD's HUD page writes them via the declutter.set command (CommandDispatcher), so
    //     a toggle there hides-or-restores the affected element within ~0.5s.
    //
    // The entries are marked Browsable=false, so they DON'T appear in the F1 ConfigurationManager
    // menu — the HUD page is the sole control surface now, and a menu checkbox would just be a
    // redundant second one. The .cfg stays hand-editable for anyone who wants it.
    //
    // Until Bind() runs the properties fall back to the defaults, so HudDeclutter behaves correctly
    // even before/without binding. Each per-element flag stands alone: Hide<Element> is the whole
    // condition for hiding that element.
    internal static class HudDeclutterConfig
    {
        // ConfigurationManager reads this tag off a ConfigEntry's description (by duck-typed field
        // name, via reflection) to hide the entry from its menu. It is NOT a reference to the
        // ConfigurationManager plugin — the convention is to declare your own same-named class with
        // just the fields you use, so there's no hard/soft dependency to take on.
        private sealed class ConfigurationManagerAttributes { public bool? Browsable; }
        private static readonly ConfigurationManagerAttributes Hidden =
            new ConfigurationManagerAttributes { Browsable = false };

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

        // Runtime setters for the web MFD's declutter toggles (declutter.set command). Writing .Value
        // persists the choice to the cfg; HudDeclutter reads the property each tick and hides/restores
        // within one interval. A no-op before Bind() (it can't be called meaningfully outside a
        // mission anyway).
        public static void SetHideWeaponAmmo(bool v) { if (_hideWeaponAmmo != null) _hideWeaponAmmo.Value = v; }
        public static void SetHideMinimap(bool v)    { if (_hideMinimap    != null) _hideMinimap.Value    = v; }
        public static void SetHideTopBoxes(bool v)   { if (_hideTopBoxes   != null) _hideTopBoxes.Value   = v; }

        // Called once from Plugin.Awake with the plugin's ConfigFile. Each entry is bound hidden
        // (Hidden tag) so it persists to the .cfg without showing in the F1 menu; the descriptions
        // stay as the .cfg comments for anyone editing the file directly.
        public static void Bind(ConfigFile config)
        {
            const string section = "HUD Declutter";
            _hideWeaponAmmo = config.Bind(section, "HideWeaponAmmo", false,
                new ConfigDescription("Hide the top-right weapon name / ammo and countermeasure count readouts.", null, Hidden));
            _hideMinimap = config.Bind(section, "HideMinimap", false,
                new ConfigDescription("Hide the bottom-left corner minimap. The full-screen M-key map and airbase-selection map stay available.", null, Hidden));
            _hideTopBoxes = config.Bind(section, "HideTopBoxes", false,
                new ConfigDescription("Hide the boxed heading / airspeed / altitude readouts flanking the heading tape. The center boresight readouts are kept.", null, Hidden));
        }
    }
}
