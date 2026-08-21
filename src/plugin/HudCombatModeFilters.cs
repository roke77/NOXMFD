namespace NOXMFD
{
    // Ties the HUD OPTIONS unit-icon filters (SceneSingleton<HUDOptions>) to weapons/combat mode
    // (issue #50): entering A/A or A/G force-loads that HUD mode tab's own saved preset (the same
    // one the player could pick by hand — HUDOptions.listModes[A2A]/[A2G]); returning to idle
    // restores whatever the player had configured before combat mode took over.
    //
    // The "before" state is a running snapshot, not a one-time capture: every hud.set/hud.mode
    // command (CommandDispatcher) updates it, but ONLY while combat mode is idle — so edits made
    // while A/A or A/G is forcing its own preset are allowed to change the live HUD (the player can
    // still fine-tune) without corrupting the baseline that gets restored on exit.
    //
    // Driven entirely through Keybinds.SetCombatMode, the one choke point both the WPN page and the
    // physical keybind (tap AND hold-to-reset) already funnel through — this class only reacts to
    // mode transitions, it never reads input itself.
    internal static class HudCombatModeFilters
    {
        private static bool _hasBaseline;
        private static bool[] _categories = System.Array.Empty<bool>();
        private static bool[] _vehicles = System.Array.Empty<bool>();
        private static bool[] _buildings = System.Array.Empty<bool>();

        // CommandDispatcher.HudSet / HudMode call this after every player-driven HUD filter edit.
        public static void CaptureIfIdle()
        {
            if (ImmersionState.CombatMode == CombatMode.All) Capture();
        }

        // TelemetryReader's 1 Hz slow tick, right after RefreshHudOptions — lazily takes whatever is
        // currently live as the baseline the first time HUDOptions exists this session, so there's
        // always something valid to restore to even if the player never touches the HUD page at all.
        public static void EnsureBootstrap()
        {
            if (!_hasBaseline) Capture();
        }

        // Keybinds.SetCombatMode calls this on every ACTUAL mode change (guarded there against
        // no-op reselects, so re-tapping an already-active mode never re-clobbers live edits made
        // within it).
        public static void OnCombatModeChanged(CombatMode mode)
        {
            HUDOptions opt = SceneSingleton<HUDOptions>.i;
            if (opt == null) return;

            if (mode == CombatMode.All)
            {
                Restore(opt);
            }
            else
            {
                int modeIndex = (int)(mode == CombatMode.AirToAir ? HUDOptions.HUDMode.A2A : HUDOptions.HUDMode.A2G);
                if (modeIndex < opt.listModes.Count && opt.listModes[modeIndex] != null)
                    opt.ToggleButtons(opt.listModes[modeIndex]);
            }
            opt.ApplyHUDSettings();
        }

        private static void Capture()
        {
            HUDOptions opt = SceneSingleton<HUDOptions>.i;
            if (opt == null) return;

            _categories = new bool[opt.listCategories.Count];
            for (int i = 0; i < _categories.Length; i++) _categories[i] = opt.listCategories[i].maximized;

            _vehicles = new bool[opt.listVehicleTypes.Count];
            for (int i = 0; i < _vehicles.Length; i++) _vehicles[i] = opt.listVehicleTypes[i].status;

            _buildings = new bool[opt.listBuildingTypes.Count];
            for (int i = 0; i < _buildings.Length; i++) _buildings[i] = opt.listBuildingTypes[i].status;

            _hasBaseline = true;
        }

        private static void Restore(HUDOptions opt)
        {
            if (!_hasBaseline) { Capture(); return; }   // nothing captured yet — current state IS the baseline

            int n = System.Math.Min(_categories.Length, opt.listCategories.Count);
            for (int i = 0; i < n; i++) opt.listCategories[i].Set(_categories[i]);

            n = System.Math.Min(_vehicles.Length, opt.listVehicleTypes.Count);
            for (int i = 0; i < n; i++) opt.listVehicleTypes[i].Set(_vehicles[i]);

            n = System.Math.Min(_buildings.Length, opt.listBuildingTypes.Count);
            for (int i = 0; i < n; i++) opt.listBuildingTypes[i].Set(_buildings[i]);
        }
    }
}
