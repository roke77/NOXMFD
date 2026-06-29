using BepInEx.Configuration;
using Rewired;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace NOXMFD
{
    // Dedicated countermeasure keybinds. Each key SELECTS its countermeasure and DEPLOYS it in one
    // press — no separate "cycle to it first" step — pinned to a fixed countermeasure instead of
    // whatever is currently cycled in:
    //   * Flares  — a tap pops one set; holding pops repeatedly (FlareEjector rate-limits itself).
    //   * Radar jammer — HOLD to jam. RadarJammer.Fire() only jams ~0.1s before auto-disabling, so we
    //     re-fire it every frame the key is held; a single tap is a brief blip.
    //
    // We deploy DIRECTLY (set activeIndex, then CountermeasureManager.DeployCountermeasure) rather than
    // via the game's countermeasureTrigger. That trigger is solely owned by the stock input loop
    // (PilotPlayerState), which force-clears it every frame its OWN "Countermeasures" button isn't
    // held — so setting it from here gets stomped before FixedUpdate can deploy. DeployCountermeasure
    // is exactly what the game's FixedUpdate calls while the trigger is on; we just call it ourselves.
    // ponytail: this fires on the local sim (host/single-player). In multiplayer a non-host client's
    // deploy may not replicate — the networked path is the trigger SyncVar we deliberately bypass.
    //
    // Both keys are ConfigEntry<KeyboardShortcut>, bound from Plugin.Awake, so they persist to the
    // plugin .cfg and are rebindable in the F1 (ConfigurationManager) menu. Default UNBOUND
    // (KeyboardShortcut.Empty) so they never clash with a stock bind until the player sets them.
    // Polled once per frame from TelemetryReader.Update — input is only valid on the Unity main thread.
    //
    // Joystick/HOTAS support: Nuclear Option drives input through Rewired, which owns the joystick, so
    // joystick buttons are invisible to the Unity legacy Input that KeyboardShortcut polls — and the
    // F1 menu's captured Unity JoystickButton* number doesn't line up with Rewired's own button index
    // anyway (XInput, for one, is offset). So a HOTAS button is configured as an explicit Rewired
    // button INDEX (the *JoystickButton ints below, -1 = off) read straight from Rewired, while the
    // keyboard/mouse key stays on the KeyboardShortcut. A countermeasure fires if EITHER source is on.
    internal static class CmKeybinds
    {
        private const byte Flare  = 1;   // same category mapping as TelemetryReader.GetSelectedCmCategory
        private const byte Jammer = 2;

        private static ConfigEntry<KeyboardShortcut>? _flareKey;
        private static ConfigEntry<KeyboardShortcut>? _jammerKey;
        private static ConfigEntry<int>? _flareJoyBtn;
        private static ConfigEntry<int>? _jammerJoyBtn;
        private static ConfigEntry<int>? _joyNumber;
        private static ConfigEntry<bool>? _logJoyButtons;

        // Called once from Plugin.Awake. Section + descriptions become the labels/tooltips in the menu.
        public static void Bind(ConfigFile config)
        {
            const string section = "Countermeasure Keybinds";
            _flareKey = config.Bind(section, "DispenseFlares", new KeyboardShortcut(),
                "Keyboard/mouse key: select + deploy IR flares. Tap to pop a set, hold to keep popping. No-op if the aircraft has no flares.");
            _jammerKey = config.Bind(section, "DispenseRadarJammer", new KeyboardShortcut(),
                "Keyboard/mouse key: select + activate the radar jammer. HOLD to jam (a tap only jams ~0.1s). No-op if the aircraft has no jammer.");
            _flareJoyBtn = config.Bind(section, "DispenseFlaresJoystickButton", -1,
                "Joystick/HOTAS button for flares, as a Rewired button INDEX (-1 = off). To find it: turn on LogJoystickButtons below, press the button in a mission, and read the index from the NOXMFD log line 'joy[..] button N DOWN'. Fires the same as the keyboard key above; set either or both.");
            _jammerJoyBtn = config.Bind(section, "DispenseRadarJammerJoystickButton", -1,
                "Joystick/HOTAS button for the radar jammer, as a Rewired button INDEX (-1 = off). See DispenseFlaresJoystickButton for how to find the index.");
            _joyNumber = config.Bind(section, "JoystickNumber", 0,
                "Which joystick the button indices above refer to: 0 = any joystick, 1 = first, 2 = second, ...");
            _logJoyButtons = config.Bind(section, "LogJoystickButtons", false,
                "When on, logs every joystick button index as you press it — use it to find your HOTAS button index for the fields above, then turn it back off.");
            Plugin.Log?.LogInfo($"[NOXMFD] CM keybinds bound: flares=key:{_flareKey.Value} joyBtn:{_flareJoyBtn.Value}, " +
                $"jammer=key:{_jammerKey.Value} joyBtn:{_jammerJoyBtn.Value}, joystick#{_joyNumber.Value}.");
        }

        // Once per frame on the main thread. Held-driven: every frame a key is down we select that
        // countermeasure and deploy it. Flares self-throttle (one set per ejectionInterval); the
        // jammer needs the per-frame re-fire to stay active.
        public static void Poll()
        {
            if (_flareKey == null) return;   // not bound yet

            if (_logJoyButtons!.Value) LogJoystickButtons();   // discovery aid for HOTAS button indices

            bool fHeld = Held(_flareKey, _flareJoyBtn!);
            bool jHeld = Held(_jammerKey!, _jammerJoyBtn!);
            if (!(fHeld || jHeld)) return;   // the common case — nothing held this frame

            GameManager.GetLocalAircraft(out Aircraft ac);
            if (ac == null || ac.disabled) return;
            CountermeasureManager mgr = ac.countermeasureManager;
            if (mgr == null) return;

            if (fHeld) Drive(ac, mgr, Flare);
            if (jHeld) Drive(ac, mgr, Jammer);
        }

        // Logs the Rewired joystick + button index for every button that goes down, so a HOTAS user can
        // discover the index to put in the *JoystickButton fields (Unity's JoystickButton* numbering and
        // Rewired's button index don't always agree). Gated by LogJoystickButtons — off for normal play.
        private static void LogJoystickButtons()
        {
            if (!ReInput.isReady) return;
            IList<Joystick> joys = ReInput.controllers.Joysticks;
            for (int i = 0; i < joys.Count; i++)
            {
                Joystick joy = joys[i];
                for (int b = 0; b < joy.buttonCount; b++)
                    if (joy.GetButtonDown(b))
                        Plugin.Log?.LogInfo($"[NOXMFD] joy[{i}] '{joy.name}' button {b} DOWN (buttonCount={joy.buttonCount}).");
            }
        }

        // Is this countermeasure's bind currently held? A keyboard/mouse key (KeyboardShortcut, Unity
        // input) OR an explicit Rewired joystick button index. Joystick KeyCodes in the KeyboardShortcut
        // are ignored — those go through the Rewired index instead.
        private static bool Held(ConfigEntry<KeyboardShortcut> kb, ConfigEntry<int> joyBtn)
        {
            KeyCode k = kb.Value.MainKey;
            bool kbd = k != KeyCode.None && k < KeyCode.JoystickButton0 && kb.Value.IsPressed();
            return kbd || JoyBtn(joyBtn.Value);
        }

        // Reads an explicit Rewired joystick button index (the number LogJoystickButtons prints),
        // honoring JoystickNumber (0 = any).
        private static bool JoyBtn(int button)
        {
            if (button < 0 || !ReInput.isReady) return false;
            IList<Joystick> joys = ReInput.controllers.Joysticks;
            if (joys == null || joys.Count == 0) return false;
            int joyNum = _joyNumber?.Value ?? 0;
            if (joyNum <= 0)   // any joystick
            {
                for (int i = 0; i < joys.Count; i++)
                    if (ButtonState(joys[i], button)) return true;
                return false;
            }
            int idx = joyNum - 1;
            return idx < joys.Count && ButtonState(joys[idx], button);
        }

        private static bool ButtonState(Joystick joy, int button) =>
            joy != null && button >= 0 && button < joy.buttonCount && joy.GetButton(button);

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
