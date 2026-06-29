using BepInEx.Configuration;
using Rewired;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace NOXMFD
{
    // The plugin's gameplay keybinds. Two groups today, each rebindable in the F1 (ConfigurationManager)
    // menu and persisted to the plugin .cfg, default UNBOUND so they never clash with a stock bind:
    //
    // Countermeasure keys — each SELECTS its countermeasure and DEPLOYS it in one press, pinned to a
    // fixed CM instead of whatever is currently cycled in (HELD-driven):
    //   * Flares  — a tap pops one set; holding pops repeatedly (FlareEjector rate-limits itself).
    //   * Radar jammer — HOLD to jam. RadarJammer.Fire() only jams ~0.1s before auto-disabling, so we
    //     re-fire it every frame the key is held; a single tap is a brief blip.
    //   We deploy DIRECTLY (set activeIndex, then CountermeasureManager.DeployCountermeasure) rather than
    //   via the game's countermeasureTrigger. That trigger is solely owned by the stock input loop
    //   (PilotPlayerState), which force-clears it every frame its OWN "Countermeasures" button isn't held
    //   — so setting it from here gets stomped before FixedUpdate can deploy. DeployCountermeasure is
    //   exactly what the game's FixedUpdate calls while the trigger is on; we just call it ourselves.
    //   ponytail: this fires on the local sim (host/single-player). In multiplayer a non-host client's
    //   deploy may not replicate — the networked path is the trigger SyncVar we deliberately bypass.
    //
    // Gear up / gear down keys — dedicated raise/lower (the stock bind is a single toggle), EDGE-driven
    // (one action per press). Each mirrors the stock gear logic (PilotPlayerState): act only on a fully
    // locked gear and only while airborne (radarAlt > 0.2, the game's anti-ground-collapse guard), via
    // the canonical Aircraft.SetGear(bool) — which is network-correct (it sends the ServerRpc), and gear
    // is edge-driven so there's no per-frame stomp to fight. A gear mid-transition matches neither locked
    // state, so the key no-ops; pressing GearUp when already up (or GearDown when already down) no-ops too.
    //
    // Polled once per frame from MissionLifecycle.Update (the persistent host, so the joystick-button
    // capture works at the main menu before a mission exists) — input is only valid on the main thread.
    //
    // Joystick/HOTAS support: Nuclear Option drives input through Rewired, which owns the joystick, so
    // joystick buttons are invisible to the Unity legacy Input that KeyboardShortcut polls — and the
    // F1 menu's captured Unity JoystickButton* number doesn't line up with Rewired's own button index
    // anyway (XInput, for one, is offset). So a HOTAS button is configured as an explicit Rewired button
    // INDEX (the *JoystickButton ints, -1 = off) read straight from Rewired, captured live via the Set
    // button in the menu, while the keyboard/mouse key stays on the KeyboardShortcut. A bind fires if
    // EITHER source is on. ponytail: JoystickNumber is shared across all binds (pinned on capture), so a
    // multi-device HOTAS with binds on different physical sticks is unsupported — upgrade path is a
    // per-bind joystick number. Single stick (the common case) works.
    internal static class Keybinds
    {
        private const byte Flare  = 1;   // same category mapping as TelemetryReader.GetSelectedCmCategory
        private const byte Jammer = 2;

        private static ConfigEntry<KeyboardShortcut>? _flareKey;
        private static ConfigEntry<KeyboardShortcut>? _jammerKey;
        private static ConfigEntry<int>? _flareJoyBtn;
        private static ConfigEntry<int>? _jammerJoyBtn;
        private static ConfigEntry<KeyboardShortcut>? _gearUpKey;
        private static ConfigEntry<KeyboardShortcut>? _gearDownKey;
        private static ConfigEntry<int>? _gearUpJoyBtn;
        private static ConfigEntry<int>? _gearDownJoyBtn;
        private static ConfigEntry<int>? _joyNumber;

        // The joystick-button entry currently armed for capture (set by its "Set" drawer button), or null.
        // While non-null, the next joystick button pressed is written into it (see CaptureJoyButton).
        private static ConfigEntry<int>? _capturing;

        // Called once from Plugin.Awake. Section + descriptions become the labels/tooltips in the menu.
        public static void Bind(ConfigFile config)
        {
            const string cm = "Countermeasure Keybinds";
            _flareKey = config.Bind(cm, "DispenseFlares", new KeyboardShortcut(),
                "Keyboard/mouse key: select + deploy IR flares. Tap to pop a set, hold to keep popping. No-op if the aircraft has no flares.");
            _jammerKey = config.Bind(cm, "DispenseRadarJammer", new KeyboardShortcut(),
                "Keyboard/mouse key: select + activate the radar jammer. HOLD to jam (a tap only jams ~0.1s). No-op if the aircraft has no jammer.");
            _flareJoyBtn = config.Bind(cm, "DispenseFlaresJoystickButton", -1,
                new ConfigDescription(
                    "Joystick/HOTAS button for flares, as a Rewired button INDEX (-1 = off). Click Set, then press the button on your stick to capture it. Fires the same as the keyboard key above; set either or both.",
                    null, JoyCaptureDrawer()));
            _jammerJoyBtn = config.Bind(cm, "DispenseRadarJammerJoystickButton", -1,
                new ConfigDescription(
                    "Joystick/HOTAS button for the radar jammer, as a Rewired button INDEX (-1 = off). Click Set, then press the button on your stick to capture it.",
                    null, JoyCaptureDrawer()));

            const string gear = "Landing Gear Keybinds";
            _gearUpKey = config.Bind(gear, "GearUp", new KeyboardShortcut(),
                "Keyboard/mouse key: raise the landing gear (tap). No-op if the gear is already up, still moving, or while on the ground.");
            _gearDownKey = config.Bind(gear, "GearDown", new KeyboardShortcut(),
                "Keyboard/mouse key: lower the landing gear (tap). No-op if the gear is already down, still moving, or while on the ground.");
            _gearUpJoyBtn = config.Bind(gear, "GearUpJoystickButton", -1,
                new ConfigDescription(
                    "Joystick/HOTAS button to raise the gear, as a Rewired button INDEX (-1 = off). Click Set, then press the button on your stick to capture it.",
                    null, JoyCaptureDrawer()));
            _gearDownJoyBtn = config.Bind(gear, "GearDownJoystickButton", -1,
                new ConfigDescription(
                    "Joystick/HOTAS button to lower the gear, as a Rewired button INDEX (-1 = off). Click Set, then press the button on your stick to capture it.",
                    null, JoyCaptureDrawer()));

            _joyNumber = config.Bind(cm, "JoystickNumber", 0,
                new ConfigDescription(
                    "Which joystick the button indices refer to (0 = any, 1 = first, ...). Set automatically to the device you captured from — hidden because you never need to touch it by hand.",
                    null, new ConfigurationManagerAttributes { Browsable = false }));
            Plugin.Log?.LogInfo($"[NOXMFD] Keybinds bound: flares={_flareKey.Value}/joy{_flareJoyBtn.Value}, " +
                $"jammer={_jammerKey.Value}/joy{_jammerJoyBtn.Value}, gearUp={_gearUpKey.Value}/joy{_gearUpJoyBtn.Value}, " +
                $"gearDown={_gearDownKey.Value}/joy{_gearDownJoyBtn.Value}, joystick#{_joyNumber.Value}.");
        }

        // Once per frame on the main thread. CM keys are held-driven (deploy every frame held); gear keys
        // are edge-driven (act once per press). The local aircraft is fetched only if something fired.
        public static void Poll()
        {
            if (_flareKey == null) return;   // not bound yet

            // While the F1 menu has a joy field armed for capture, swallow the next button into it
            // (and don't let that same press also trigger an action this frame).
            if (_capturing != null) { CaptureJoyButton(); return; }

            bool fHeld    = Active(_flareKey,     _flareJoyBtn!,    edge: false);
            bool jHeld    = Active(_jammerKey!,   _jammerJoyBtn!,   edge: false);
            bool gearUp   = Active(_gearUpKey!,   _gearUpJoyBtn!,   edge: true);
            bool gearDown = Active(_gearDownKey!, _gearDownJoyBtn!, edge: true);
            if (!(fHeld || jHeld || gearUp || gearDown)) return;   // common case — nothing this frame

            GameManager.GetLocalAircraft(out Aircraft ac);
            if (ac == null || ac.disabled) return;

            if (fHeld || jHeld)
            {
                CountermeasureManager mgr = ac.countermeasureManager;
                if (mgr != null)
                {
                    if (fHeld) Drive(ac, mgr, Flare);
                    if (jHeld) Drive(ac, mgr, Jammer);
                }
            }

            if (gearUp || gearDown) DriveGear(ac, gearUp, gearDown);
        }

        // Dedicated gear raise/lower, mirroring the stock toggle (PilotPlayerState.cs): only changes a
        // fully locked gear, and only while airborne (radarAlt > 0.2 — the game's anti-ground-collapse
        // guard). SetGear is the canonical, network-correct entry. A gear mid-transition (Extending/
        // Retracting) matches neither locked state and is left alone — exactly the requested no-op spec.
        private static void DriveGear(Aircraft ac, bool up, bool down)
        {
            if (ac.radarAlt <= 0.2f) return;
            if (up   && ac.gearState == LandingGear.GearState.LockedExtended)  ac.SetGear(false);   // raise if down
            if (down && ac.gearState == LandingGear.GearState.LockedRetracted) ac.SetGear(true);    // lower if up
        }

        // CustomDrawer for a *JoystickButton entry: renders the current index plus Set/Clear buttons in the
        // F1 (ConfigurationManager) menu. "Set" arms this entry for capture; the actual button read happens
        // in CaptureJoyButton on the main-thread Poll (OnGUI fires multiple times per frame, so polling the
        // edge there is unreliable). Tagged onto the entry via ConfigurationManagerAttributes — a no-op if
        // ConfigurationManager isn't installed. Returns a fresh attribute bag per entry (each needs its own).
        private static ConfigurationManagerAttributes JoyCaptureDrawer() =>
            new ConfigurationManagerAttributes { HideDefaultButton = true, CustomDrawer = DrawJoyCapture };

        private static void DrawJoyCapture(ConfigEntryBase entry)
        {
            var e = (ConfigEntry<int>)entry;
            bool armed = ReferenceEquals(_capturing, e);
            GUILayout.Label(armed ? "press a button…" : (e.Value < 0 ? "(off)" : "button " + e.Value),
                GUILayout.Width(110));
            if (GUILayout.Button(armed ? "cancel" : "Set", GUILayout.ExpandWidth(true)))
                _capturing = armed ? null : e;
            if (GUILayout.Button("Clear", GUILayout.ExpandWidth(false)))
            {
                e.Value = -1;
                if (armed) _capturing = null;
            }
        }

        // Runs on the main-thread Poll while a joy field is armed. Writes the first joystick button that goes
        // down into the armed entry, records which joystick it came from (JoystickNumber), and disarms. The
        // captured index is exactly what JoyBtn() reads back, so capture and live-poll use the same numbering.
        private static void CaptureJoyButton()
        {
            if (!ReInput.isReady) return;
            IList<Joystick> joys = ReInput.controllers.Joysticks;
            for (int i = 0; i < joys.Count; i++)
            {
                Joystick joy = joys[i];
                for (int b = 0; b < joy.buttonCount; b++)
                    if (joy.GetButtonDown(b))
                    {
                        _capturing!.Value = b;
                        if (_joyNumber != null) _joyNumber.Value = i + 1;   // pin to the device it came from
                        Plugin.Log?.LogInfo($"[NOXMFD] captured joy[{i}] '{joy.name}' button {b} for {_capturing.Definition.Key}.");
                        _capturing = null;
                        return;
                    }
            }
        }

        // Is this bind active this frame? A keyboard/mouse key (KeyboardShortcut, Unity input) OR an explicit
        // Rewired joystick button index. edge=false → held (IsPressed/GetButton, the CM keys); edge=true →
        // pressed-this-frame (IsDown/GetButtonDown, the gear keys). Joystick KeyCodes inside the
        // KeyboardShortcut are ignored — those go through the Rewired index instead.
        private static bool Active(ConfigEntry<KeyboardShortcut> kb, ConfigEntry<int> joyBtn, bool edge)
        {
            KeyCode k = kb.Value.MainKey;
            bool kbd = k != KeyCode.None && k < KeyCode.JoystickButton0 &&
                       (edge ? kb.Value.IsDown() : kb.Value.IsPressed());
            return kbd || JoyBtn(joyBtn.Value, edge);
        }

        // Reads an explicit Rewired joystick button index (the number the Set drawer captures), honoring
        // JoystickNumber (0 = any). edge selects GetButtonDown (tap) vs GetButton (held).
        private static bool JoyBtn(int button, bool edge)
        {
            if (button < 0 || !ReInput.isReady) return false;
            IList<Joystick> joys = ReInput.controllers.Joysticks;
            if (joys == null || joys.Count == 0) return false;
            int joyNum = _joyNumber?.Value ?? 0;
            if (joyNum <= 0)   // any joystick
            {
                for (int i = 0; i < joys.Count; i++)
                    if (ButtonState(joys[i], button, edge)) return true;
                return false;
            }
            int idx = joyNum - 1;
            return idx < joys.Count && ButtonState(joys[idx], button, edge);
        }

        private static bool ButtonState(Joystick joy, int button, bool edge) =>
            joy != null && button >= 0 && button < joy.buttonCount &&
            (edge ? joy.GetButtonDown(button) : joy.GetButton(button));

        // Select this countermeasure (activeIndex; the game's UpdateHUD syncs the readout) and fire the
        // active station now. No-op if the airframe carries no countermeasure of that category.
        private static void Drive(Aircraft ac, CountermeasureManager mgr, byte category)
        {
            int idx = IndexOfCategory(mgr, category);
            if (idx < 0) return;
            try
            {
                mgr.activeIndex = (byte)idx;
                mgr.DeployCountermeasure(ac);
            }
            catch (Exception ex) { Plugin.Log?.LogWarning($"[NOXMFD] CM keybind (cat={category}) threw: {ex.Message}"); }
        }

        // Finds the station index whose first countermeasure is the requested category. Mirrors the
        // read-path reflection in TelemetryReader.GetSelectedCmCategory (the station list and each
        // station's countermeasure are both private). Returns -1 if no station matches.
        private static FieldInfo?  _stationsField;
        private static MethodInfo? _getFirstMethod;
        private static int IndexOfCategory(CountermeasureManager mgr, byte category)
        {
            try
            {
                if (_stationsField == null)
                    _stationsField = typeof(CountermeasureManager)
                        .GetField("countermeasureStations", BindingFlags.NonPublic | BindingFlags.Instance);
                if (_stationsField?.GetValue(mgr) is not IList list || list.Count == 0) return -1;

                for (int i = 0; i < list.Count; i++)
                {
                    object station = list[i];
                    if (station == null) continue;
                    if (_getFirstMethod == null)
                        _getFirstMethod = station.GetType()
                            .GetMethod("GetFirstCountermeasure", BindingFlags.Public | BindingFlags.Instance);
                    if (_getFirstMethod?.Invoke(station, null) is not Countermeasure cm) continue;
                    if (category == Flare  && cm is FlareEjector) return i;
                    if (category == Jammer && cm is RadarJammer)  return i;
                }
                return -1;
            }
            catch { return -1; }
        }
    }
}
