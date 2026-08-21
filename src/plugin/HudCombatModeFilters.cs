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
    // Re-pressing an already-active A/A or A/G is the documented way to discard those hand tweaks
    // and reload the mode's own preset — so OnCombatModeChanged runs on every SetCombatMode call,
    // not just an actual transition (issue #50 follow-up).
    //
    // Driven entirely through Keybinds.SetCombatMode, the one choke point both the WPN page and the
    // physical keybind (tap AND hold-to-reset) already funnel through — this class only reacts to
    // mode changes, it never reads input itself.
    internal static class HudCombatModeFilters
    {
        private static bool _hasBaseline;
        private static bool[] _categories = System.Array.Empty<bool>();
        private static bool[] _vehicles = System.Array.Empty<bool>();
        private static bool[] _buildings = System.Array.Empty<bool>();

        // A second, short-lived snapshot — separate storage from the idle baseline above, taken
        // immediately before EVERY combat-mode switch regardless of the opt-in toggle (see
        // CaptureBeforeSwitch/UndoNativeAutoToggleIfOff below). Confirmed via logging (2026-08-22):
        // with the toggle OFF, switching A/A<->A/G still changed HUD filter values — not from this
        // class (OnCombatModeChanged's own toggle-gated early return never even ran) but from the
        // GAME's own HUDOptions.AutomaticToggle, which the game itself invokes on ANY weapon-station
        // selection change, including the one WeaponSelectors.OnCombatModeChanged (issue #32) makes
        // unconditionally when the new mode disables the currently-selected weapon. That auto-switch
        // isn't gated by this feature's toggle at all, so its native HUD side effect wasn't either —
        // this snapshot exists purely to undo that side effect when the pilot hasn't opted in.
        private static bool _hasPreSwitch;
        private static bool[] _preSwitchCategories = System.Array.Empty<bool>();
        private static bool[] _preSwitchVehicles = System.Array.Empty<bool>();
        private static bool[] _preSwitchBuildings = System.Array.Empty<bool>();

        // CommandDispatcher.HudSet / HudMode call this after every player-driven HUD filter edit.
        public static void CaptureIfIdle()
        {
            if (ImmersionState.CombatMode == CombatMode.All) Capture(ref _categories, ref _vehicles, ref _buildings);
            _hasBaseline = true;
        }

        // TelemetryReader's 1 Hz slow tick, right after RefreshHudOptions — lazily takes whatever is
        // currently live as the baseline the first time HUDOptions exists this session, so there's
        // always something valid to restore to even if the player never touches the HUD page at all.
        public static void EnsureBootstrap()
        {
            if (!_hasBaseline)
            {
                Capture(ref _categories, ref _vehicles, ref _buildings);
                _hasBaseline = true;
            }
        }

        // Keybinds.SetCombatMode calls this BEFORE WeaponSelectors.OnCombatModeChanged, on every
        // call — the snapshot must predate the weapon auto-switch that can trigger the game's own
        // AutomaticToggle, whether or not this feature is turned on.
        public static void CaptureBeforeSwitch()
        {
            Capture(ref _preSwitchCategories, ref _preSwitchVehicles, ref _preSwitchBuildings);
            _hasPreSwitch = true;
        }

        // Keybinds.SetCombatMode calls this AFTER WeaponSelectors.OnCombatModeChanged and this
        // class's own OnCombatModeChanged. When the opt-in toggle is OFF, OnCombatModeChanged never
        // touched the HUD — so any change now present is the game's own native AutomaticToggle
        // reacting to the weapon switch, and gets undone back to the pre-switch snapshot. When the
        // toggle is ON, this is a no-op: OnCombatModeChanged already applied the intended state.
        public static void UndoNativeAutoToggleIfOff()
        {
            if (ImmersionConfig.HudFiltersOnCombatMode) return;
            if (!_hasPreSwitch) return;

            HUDOptions opt = SceneSingleton<HUDOptions>.i;
            if (opt == null) return;
            Restore(opt, _preSwitchCategories, _preSwitchVehicles, _preSwitchBuildings);
            opt.ApplyHUDSettings();
        }

        // Gated on ImmersionConfig.HudFiltersOnCombatMode (KEY page, default OFF) — a pilot who
        // hasn't opted in gets no HUD change at all on a mode switch (UndoNativeAutoToggleIfOff
        // above handles the one indirect way it could still happen); CaptureIfIdle/EnsureBootstrap
        // keep running regardless, so the baseline is already warm the moment this gets turned on
        // rather than restoring to a stale/empty snapshot.
        public static void OnCombatModeChanged(CombatMode mode)
        {
            if (!ImmersionConfig.HudFiltersOnCombatMode) return;

            HUDOptions opt = SceneSingleton<HUDOptions>.i;
            if (opt == null) return;

            if (mode == CombatMode.All)
            {
                if (!_hasBaseline) { Capture(ref _categories, ref _vehicles, ref _buildings); _hasBaseline = true; return; }
                Restore(opt, _categories, _vehicles, _buildings);
            }
            else
            {
                int modeIndex = (int)(mode == CombatMode.AirToAir ? HUDOptions.HUDMode.A2A : HUDOptions.HUDMode.A2G);
                if (modeIndex < opt.listModes.Count && opt.listModes[modeIndex] != null)
                    opt.ToggleButtons(opt.listModes[modeIndex]);
            }
            opt.ApplyHUDSettings();
        }

        private static void Capture(ref bool[] categories, ref bool[] vehicles, ref bool[] buildings)
        {
            HUDOptions opt = SceneSingleton<HUDOptions>.i;
            if (opt == null) return;

            categories = new bool[opt.listCategories.Count];
            for (int i = 0; i < categories.Length; i++) categories[i] = opt.listCategories[i].maximized;

            vehicles = new bool[opt.listVehicleTypes.Count];
            for (int i = 0; i < vehicles.Length; i++) vehicles[i] = opt.listVehicleTypes[i].status;

            buildings = new bool[opt.listBuildingTypes.Count];
            for (int i = 0; i < buildings.Length; i++) buildings[i] = opt.listBuildingTypes[i].status;
        }

        private static void Restore(HUDOptions opt, bool[] categories, bool[] vehicles, bool[] buildings)
        {
            int n = System.Math.Min(categories.Length, opt.listCategories.Count);
            for (int i = 0; i < n; i++) opt.listCategories[i].Set(categories[i]);

            n = System.Math.Min(vehicles.Length, opt.listVehicleTypes.Count);
            for (int i = 0; i < n; i++) opt.listVehicleTypes[i].Set(vehicles[i]);

            n = System.Math.Min(buildings.Length, opt.listBuildingTypes.Count);
            for (int i = 0; i < n; i++) opt.listBuildingTypes[i].Set(buildings[i]);
        }
    }
}
