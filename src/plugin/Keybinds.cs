using BepInEx.Configuration;
using Rewired;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace NOXMFD
{
    // The plugin's gameplay keybinds — cockpit functionality the game has no native bind for. Each bind
    // pairs a keyboard/mouse key (ConfigEntry<KeyboardShortcut>) with an optional Rewired joystick/HOTAS
    // button index (ConfigEntry<int>, -1 = off); the bind fires if EITHER source is active. All persisted
    // in the plugin .cfg, default unbound so they never clash with a stock bind.
    //
    // Binds live in a REGISTRY (_binds): one BindDef row per functionality carrying its config entries,
    // held/edge mode, and the drive action. Poll() iterates it, and the /keybinds page serializes it —
    // adding a keybind is adding one Def() call in Bind(). The F1 (ConfigurationManager) menu is NOT the
    // keybind UI: every entry is tagged Browsable=false, and the /keybinds web page is where binds are
    // set (keyboard captured in the browser, joystick captured here via ArmJoyCapture).
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
    // joystick buttons are invisible to the Unity legacy Input that KeyboardShortcut polls — and a
    // browser Gamepad API button number doesn't line up with Rewired's own button index anyway (XInput,
    // for one, is offset). So a HOTAS button is configured as an explicit Rewired button INDEX read
    // straight from Rewired, captured live plugin-side (ArmJoyCapture → next button pressed wins), while
    // the keyboard/mouse key stays on the KeyboardShortcut. Each bind carries its own joystick number
    // (pinned to the device the button was captured from; 0 = any), so a multi-device HOTAS can spread
    // binds across physical sticks.
    internal static class Keybinds
    {
        private const byte Flare  = 1;   // same category mapping as TelemetryReader.GetSelectedCmCategory
        private const byte Jammer = 2;

        // One functionality row: its config entries (the persistence layer), how it's driven, and what it
        // does. Label/Description are the user-facing strings the /keybinds page renders.
        internal sealed class BindDef
        {
            public string Id;                                  // stable id for the web API ("flares", "gear-up", ...)
            public string Section;                             // grouping header on the page (and .cfg section)
            public string Label;                               // row name on the page
            public string Description;                         // row tooltip on the page
            public bool Edge;                                  // true = fire once per press; false = fire every frame held
            // Exactly one of these is set. Drive needs a live aircraft and is skipped without one;
            // DriveFree runs regardless, for binds that act on the mod rather than on the aeroplane
            // (SOI) and so must work at the main menu, where there is no aircraft at all.
            public Action<Aircraft>? Drive;
            public Action? DriveFree;
            public ConfigEntry<KeyboardShortcut> KeyEntry;     // keyboard/mouse source
            public ConfigEntry<int> JoyEntry;                  // Rewired joystick button index source (-1 = off)
            public ConfigEntry<int> JoyNumEntry;               // which joystick the index refers to (0 = any; pinned on capture)
            public bool ActiveNow;                             // per-frame scratch, valid only inside Poll()
        }

        private static readonly List<BindDef> _binds = new List<BindDef>();
        internal static IReadOnlyList<BindDef> Binds => _binds;   // for the /keybinds page JSON

        // The bind whose joystick entry is currently armed for capture (via ArmJoyCapture), or null.
        // While non-null, the next joystick button pressed is written into it (see CaptureJoyButton).
        private static BindDef? _capturing;
        internal static string? CapturingId => _capturing?.Id;

        // Called once from Plugin.Awake. Section/key names are the .cfg identity — existing user configs
        // carry over. Descriptions surface as row tooltips on the /keybinds page.
        public static void Bind(ConfigFile config)
        {
            const string cm   = "Countermeasure Keybinds";
            const string gear = "Landing Gear Keybinds";

            Def(config, "flares", cm, "DispenseFlares", "Flares", edge: false,
                "Select + deploy IR flares. Tap to pop a set, hold to keep popping. No-op if the aircraft has no flares.",
                ac => { var mgr = ac.countermeasureManager; if (mgr != null) Drive(ac, mgr, Flare); });
            Def(config, "jammer", cm, "ActivateRadarJammer", "Jammer", edge: false,
                "Select + activate the radar jammer. HOLD to jam (a tap only jams ~0.1s). No-op if the aircraft has no jammer.",
                ac => { var mgr = ac.countermeasureManager; if (mgr != null) Drive(ac, mgr, Jammer); });
            Def(config, "gear-up", gear, "GearUp", "Gear Up", edge: true,
                "Raise the landing gear. No-op if the gear is already up, still moving, or while on the ground.",
                ac => DriveGear(ac, up: true, down: false));
            Def(config, "gear-down", gear, "GearDown", "Gear Down", edge: true,
                "Lower the landing gear. No-op if the gear is already down, still moving, or while on the ground.",
                ac => DriveGear(ac, up: false, down: true));

            // Weapon soft-selector binds — see WeaponSelectors.cs for the model (two background
            // selections: one gun, one missile-or-bomb; cycle keys move them, fire keys commit them).
            const string wpn = "Weapon Keybinds";
            Def(config, "cycle-guns", wpn, "CycleGuns", "Cycle Guns", edge: true,
                "Select a gun.",
                WeaponSelectors.CycleGun);
            Def(config, "cycle-missiles", wpn, "CycleMissiles", "Cycle Missiles", edge: true,
                "Select a missile or rocket.",
                WeaponSelectors.CycleMissile);
            Def(config, "cycle-bombs", wpn, "CycleBombs", "Cycle Bombs", edge: true,
                "Select a bomb.",
                WeaponSelectors.CycleBomb);
            Def(config, "gun-trigger", wpn, "GunTrigger", "Gun Trigger", edge: false,
                "Fire your gun; HOLD for continuous fire. With a non-gun selected, the first press only switches to the gun — press again to fire.",
                WeaponSelectors.FireGun);
            Def(config, "weapon-release", wpn, "WeaponRelease", "Weapon Release", edge: false,
                "Release your missile/bomb; HOLD to keep releasing. With a gun selected, the first press only switches to it — press again to release.",
                WeaponSelectors.FireRelease);

            // SOI binds — they drive the mod's own displays rather than the aeroplane, so they are
            // DefFree (no aircraft needed) and work at the main menu. See docs/keybinds-page.md.
            const string soi = "SOI Keybinds";
            DefFree(config, "soi-next", soi, "SoiNext", "SOI Next", edge: true,
                "Move focus to the next display.",
                () => TelemetryServer.SoiCycle(1));
            DefFree(config, "soi-prev", soi, "SoiPrev", "SOI Prev", edge: true,
                "Move focus to the previous display.",
                () => TelemetryServer.SoiCycle(-1));
            DefFree(config, "soi-nav-up", soi, "SoiNavUp", "Nav Up", edge: true,
                "Move the cursor up the focused display's key labels.",
                () => TelemetryServer.SoiAction("up"));
            DefFree(config, "soi-nav-down", soi, "SoiNavDown", "Nav Down", edge: true,
                "Move the cursor down the focused display's key labels.",
                () => TelemetryServer.SoiAction("down"));
            DefFree(config, "soi-select", soi, "SoiSelect", "Select", edge: true,
                "Press the label the cursor is on, as if you had clicked that key.",
                () => TelemetryServer.SoiAction("select"));

            foreach (var b in _binds)
                Plugin.Log?.LogInfo($"[NOXMFD] Keybind '{b.Id}': key={b.KeyEntry.Value}, joy={b.JoyEntry.Value} (stick {b.JoyNumEntry.Value}).");
        }

        // Registers one functionality: binds its two config entries (keyboard + joystick, both hidden
        // from the F1 menu — the /keybinds page is the UI) and adds the registry row.
        private static void Def(ConfigFile config, string id, string section, string key, string label,
                                bool edge, string description, Action<Aircraft> drive)
            => Add(config, id, section, key, label, edge, description, drive, null);

        // A bind that acts on the mod rather than on the aeroplane, so it needs no aircraft and works
        // at the main menu. Same row on the page — the difference is only which Poll() pass runs it.
        private static void DefFree(ConfigFile config, string id, string section, string key, string label,
                                    bool edge, string description, Action drive)
            => Add(config, id, section, key, label, edge, description, null, drive);

        private static void Add(ConfigFile config, string id, string section, string key, string label,
                                bool edge, string description, Action<Aircraft>? drive, Action? driveFree)
        {
            _binds.Add(new BindDef
            {
                Id = id, Section = section, Label = label, Description = description, Edge = edge,
                Drive = drive, DriveFree = driveFree,
                KeyEntry = config.Bind(section, key, new KeyboardShortcut(),
                    new ConfigDescription("Keyboard/mouse key: " + description, null, Hidden())),
                JoyEntry = config.Bind(section, key + "JoystickButton", -1,
                    new ConfigDescription("Joystick/HOTAS button (Rewired index, -1 = off): " + description, null, Hidden())),
                JoyNumEntry = config.Bind(section, key + "JoystickNumber", 0,
                    new ConfigDescription("Which joystick the button index refers to (0 = any; pinned to the captured device).", null, Hidden())),
            });
        }

        // Keybind entries persist in the .cfg but never show in the F1 menu — the page owns the UI.
        private static ConfigurationManagerAttributes Hidden() =>
            new ConfigurationManagerAttributes { Browsable = false };

        // Display titles for the page's section headers, keyed by .cfg section (the cfg names are
        // persistence identity and can't change).
        internal static string SectionTitle(string section) => section switch
        {
            "Countermeasure Keybinds" => "COUNTERMEASURES",
            "Landing Gear Keybinds"   => "GEAR",
            "Weapon Keybinds"         => "WEAPONS",
            "SOI Keybinds"            => "SOI",
            _ => section,
        };

        // Optional note rendered under a section header — behaviour shared by the section's binds,
        // so the per-bind descriptions stay short. Keyed by .cfg section like SectionTitle.
        internal static string SectionNote(string section) => section switch
        {
            "Weapon Keybinds" =>
                "Cycle keys select the last soft-selected weapon of their type, or the first in the list. " +
                "Repeated presses cycle to the next one, skipping depleted weapons. " +
                "Cycling to a different type leaves the current one soft-selected.",
            "SOI Keybinds" =>
                "One display at a time is the sensor of interest — it rings itself in white, and these " +
                "keys drive it. Nothing is focused until you press SOI Next or Prev; from there they " +
                "cycle through the open displays. These are the only keys that work without an aircraft.",
            _ => null,
        };

        // ── Bind writes (driven by the /keybinds page via CommandDispatcher, main thread) ───────────
        // Set a bind's keyboard key from its Unity KeyCode name; "" / "None" clears. Rejects unknown
        // ids, unparseable names, and joystick KeyCodes (those go through the Rewired index instead).
        internal static bool SetKeyBind(string id, string keyName)
        {
            KeyCode key = KeyCode.None;
            bool clear = string.IsNullOrEmpty(keyName) || keyName == "None";
            if (!clear && (!Enum.TryParse(keyName, ignoreCase: true, out key) || key >= KeyCode.JoystickButton0))
                return false;
            foreach (var b in _binds)
                if (b.Id == id)
                {
                    b.KeyEntry.Value = clear ? new KeyboardShortcut() : new KeyboardShortcut(key);
                    return true;
                }
            return false;
        }

        // ── Joystick capture (driven by the /keybinds page) ─────────────────────────────────────────
        // Arm capture for a bind id: the next joystick button pressed is written into its joy entry.
        // Returns false for an unknown id. Called from the main thread (CommandDispatcher).
        //
        // The pilot is focused on the BROWSER while arming, so the game window is unfocused — and by
        // default both Unity (runInBackground) and Rewired (ignoreInputWhenAppNotInFocus) drop input
        // for an unfocused app, which made capture silently see nothing. While armed, both are
        // overridden so stick input keeps flowing; the previous values are restored on disarm.
        private static bool _prevRunInBackground;
        private static bool _prevIgnoreUnfocused;

        // Latched physical switches (VPC toggles, mode selectors) read as freshly-pressed buttons the
        // moment input resumes, which instantly "captured" whatever switch happened to be ON. So after
        // arming we ignore a few settle frames, snapshot every button already held into _latched, and
        // only accept a press of a button that was seen up first. A latched button released and pressed
        // again while armed becomes capturable (it leaves _latched on release).
        private const int SettleFrames = 3;
        private static int _captureSettle;
        private static readonly HashSet<(int joy, int btn)> _latched = new HashSet<(int, int)>();

        internal static bool ArmJoyCapture(string id)
        {
            foreach (var b in _binds)
            {
                if (b.Id != id) continue;
                if (_capturing == null) EnableBackgroundInput();
                _capturing = b;
                _captureSettle = SettleFrames;
                _latched.Clear();
                LogJoysticks();
                return true;
            }
            return false;
        }

        internal static void CancelJoyCapture() => Disarm();

        internal static bool ClearJoyBind(string id)
        {
            foreach (var b in _binds)
                if (b.Id == id) { b.JoyEntry.Value = -1; b.JoyNumEntry.Value = 0; if (_capturing == b) Disarm(); return true; }
            return false;
        }

        private static void EnableBackgroundInput()
        {
            _prevRunInBackground = Application.runInBackground;
            Application.runInBackground = true;
            if (ReInput.isReady)
            {
                _prevIgnoreUnfocused = ReInput.configuration.ignoreInputWhenAppNotInFocus;
                ReInput.configuration.ignoreInputWhenAppNotInFocus = false;
            }
        }

        private static void Disarm()
        {
            if (_capturing == null) return;
            _capturing = null;
            Application.runInBackground = _prevRunInBackground;
            if (ReInput.isReady)
                ReInput.configuration.ignoreInputWhenAppNotInFocus = _prevIgnoreUnfocused;
        }

        // One log line per arm: what Rewired can see right now — if the stick is missing here, the
        // problem is device-level (not connected / not recognized), not the capture flow.
        private static void LogJoysticks()
        {
            if (!ReInput.isReady) { Plugin.Log?.LogWarning("[NOXMFD] joy capture armed but Rewired is not ready."); return; }
            IList<Joystick> joys = ReInput.controllers.Joysticks;
            var names = new List<string>(joys.Count);
            foreach (Joystick j in joys) names.Add($"'{j.name}' ({j.buttonCount} buttons)");
            Plugin.Log?.LogInfo($"[NOXMFD] joy capture armed for '{_capturing?.Id}': {joys.Count} joystick(s): {string.Join(", ", names)}");
        }

        // Once per frame on the main thread. CM keys are held-driven (deploy every frame held); gear keys
        // are edge-driven (act once per press). The local aircraft is fetched only if something fired.
        public static void Poll()
        {
            if (_binds.Count == 0) return;   // not bound yet

            // While a joy entry is armed for capture, swallow the next button into it (and don't let
            // that same press also trigger an action this frame).
            if (_capturing != null) { CaptureJoyButton(); return; }

            bool any = false;
            foreach (var b in _binds)
            {
                b.ActiveNow = Active(b);
                any |= b.ActiveNow;
            }
            if (!any) return;   // common case — nothing this frame

            // Aircraft-free binds first (SOI): they drive the mod's own displays, so they have to work
            // at the main menu — the aircraft check below would otherwise swallow them.
            foreach (var b in _binds)
                if (b.ActiveNow && b.DriveFree != null) b.DriveFree();

            GameManager.GetLocalAircraft(out Aircraft ac);
            if (ac == null || ac.disabled) return;

            foreach (var b in _binds)
                if (b.ActiveNow && b.Drive != null) b.Drive(ac);
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

        // Runs on the main-thread Poll while a joy capture is armed. Writes the first joystick button that
        // goes down into the armed bind's entry, records which joystick it came from (JoystickNumber), and
        // disarms. The captured index is exactly what JoyBtn() reads back, so capture and live-poll use the
        // same numbering.
        private static void CaptureJoyButton()
        {
            if (!ReInput.isReady) return;
            IList<Joystick> joys = ReInput.controllers.Joysticks;

            // Settle window: give Rewired a few frames after the background-input flip, then record
            // every button already held (latched switches) as excluded.
            if (_captureSettle > 0)
            {
                if (--_captureSettle == 0)
                    for (int i = 0; i < joys.Count; i++)
                        for (int b = 0; b < joys[i].buttonCount; b++)
                            if (joys[i].GetButton(b)) _latched.Add((i, b));
                return;
            }

            for (int i = 0; i < joys.Count; i++)
            {
                Joystick joy = joys[i];
                for (int b = 0; b < joy.buttonCount; b++)
                {
                    if (!joy.GetButton(b)) { _latched.Remove((i, b)); continue; }   // seen up → capturable again
                    if (_latched.Contains((i, b)) || !joy.GetButtonDown(b)) continue;
                    _capturing!.JoyEntry.Value = b;
                    _capturing.JoyNumEntry.Value = i + 1;   // pin to the device it came from
                    Plugin.Log?.LogInfo($"[NOXMFD] captured joy[{i}] '{joy.name}' button {b} for keybind '{_capturing.Id}'.");
                    Disarm();   // also restores the background-input overrides
                    return;
                }
            }
        }

        // Is this bind active this frame? A keyboard/mouse key (KeyboardShortcut, Unity input) OR an explicit
        // Rewired joystick button index. edge=false → held (IsPressed/GetButton, the CM keys); edge=true →
        // pressed-this-frame (IsDown/GetButtonDown, the gear keys). Joystick KeyCodes inside the
        // KeyboardShortcut are ignored — those go through the Rewired index instead.
        private static bool Active(BindDef bind)
        {
            KeyCode k = bind.KeyEntry.Value.MainKey;
            bool kbd = k != KeyCode.None && k < KeyCode.JoystickButton0 &&
                       (bind.Edge ? bind.KeyEntry.Value.IsDown() : bind.KeyEntry.Value.IsPressed());
            return kbd || JoyBtn(bind.JoyEntry.Value, bind.JoyNumEntry.Value, bind.Edge);
        }

        // Reads an explicit Rewired joystick button index (the number capture writes), honoring the
        // bind's joystick number (0 = any). edge selects GetButtonDown (tap) vs GetButton (held).
        private static bool JoyBtn(int button, int joyNum, bool edge)
        {
            if (button < 0 || !ReInput.isReady) return false;
            IList<Joystick> joys = ReInput.controllers.Joysticks;
            if (joys == null || joys.Count == 0) return false;
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
