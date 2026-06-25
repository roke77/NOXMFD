namespace NOXMFD
{
    // Toggles for hiding native in-game HUD elements (so the web MFD can replace them without
    // on-screen clutter). These are deliberately plain mutable statics, NOT consts: a future
    // keybind / in-game UI / config-file binding can flip them at runtime and HudController will
    // hide-or-restore the affected elements within ~0.5s. For now they default to TRUE.
    //
    // DeclutterHud is the master switch for the whole set; the per-element flags below let you
    // carve out individual pieces. Effective hide = DeclutterHud && Hide<Element>.
    internal static class HudConfig
    {
        // Master switch for the whole declutter set.
        public static bool DeclutterHud = true;

        // Top-right readouts: weapon name + ammo (WeaponIndicator) and the countermeasure
        // count "48 / IR Flares" (CountermeasureIndicator). Both hidden by this one flag.
        public static bool HideWeaponAmmo = true;

        // Bottom-left corner minimap only (game class: DynamicMap). The full-screen M-key map and
        // the airbase-selection map at spawn are the same object maximized, and stay available.
        public static bool HideMinimap = true;

        // The boxed numeric readouts flanking the heading tape: heading (Bearing, "188°"),
        // airspeed (SpeedGauge, "1km/h") and radar/abs altitude (Altitude, "R[0,0m]").
        // Only the BOXED copies are hidden (the ones with an assigned 'border' Image); the
        // borderless boresight-following center readouts and the heading tape itself are kept.
        public static bool HideTopBoxes = true;
    }
}
