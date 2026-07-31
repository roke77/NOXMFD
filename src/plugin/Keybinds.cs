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
            // Exactly one of these is set for a digital (KeyEntry != null) bind. Drive needs a live
            // aircraft and is skipped without one; DriveFree runs regardless, for binds that act on the
            // mod rather than on the aeroplane (SOI) and so must work at the main menu, where there is
            // no aircraft at all. Both are null for an axis-only bind (docs/map-cursor.md) — it has no
            // digital press to dispatch; Poll() reads AxisValueNow via the stored reference instead,
            // the same way it already reads the four MAP direction binds' ActiveNow.
            public Action<Aircraft>? Drive;
            public Action? DriveFree;
            // Digital source (keyboard/mouse + joystick button) — null for an axis-only bind.
            public ConfigEntry<KeyboardShortcut>? KeyEntry;
            public ConfigEntry<int>? JoyEntry;                 // Rewired joystick button index source (-1 = off)
            public ConfigEntry<int>? JoyNumEntry;              // which joystick the index refers to (0 = any; pinned on capture)
            // Analog source (docs/map-cursor.md) — null unless this bind is axis-capable. Only the two
            // MAP cursor-axis rows have one today; a digital bind could carry both in principle, but
            // nothing needs that yet.
            public ConfigEntry<int>? AxisEntry;                // Rewired axis index source (-1 = off)
            public ConfigEntry<int>? AxisJoyNumEntry;          // which joystick the axis index refers to (0 = any; pinned on capture)
            public ConfigEntry<bool>? AxisInvertEntry;         // flip polarity — arbitrary per device
            public bool  ActiveNow;                            // per-frame scratch (digital), valid only inside Poll()
            public float AxisValueNow;                         // per-frame scratch (analog), valid only inside Poll()
        }

        private static readonly List<BindDef> _binds = new List<BindDef>();
        internal static IReadOnlyList<BindDef> Binds => _binds;   // for the /keybinds page JSON

        // The bind whose joystick entry is currently armed for capture (via ArmJoyCapture), or null.
        // While non-null, the next joystick button pressed is written into it (see CaptureJoyButton).
        // Mutually exclusive with _capturingAxis — arming one disarms the other, matching the page's
        // one-row-at-a-time capture UX.
        private static BindDef? _capturing;
        private static BindDef? _capturingAxis;
        internal static string? CapturingId   => (_capturing ?? _capturingAxis)?.Id;
        internal static string? CapturingKind => _capturing != null ? "joy" : (_capturingAxis != null ? "axis" : null);

        // The four MAP cursor direction binds, kept by reference so Poll() can read their ActiveNow
        // directly and fold them into one cursor vector (see the MAP Keybinds comment in Bind()).
        private static BindDef? _cursorUp, _cursorDown, _cursorLeft, _cursorRight;
        // The two MAP cursor axis binds (docs/map-cursor.md) — analog alternative to the four keys
        // above; a deflected axis overrides its keys for that component (Poll()).
        private static BindDef? _cursorAxisH, _cursorAxisV;

        // "Keep reading the stick while the game is unfocused" (see the .cfg description). Applied
        // live by ApplyBackgroundInput; the _bg* fields remember what to put back if it's turned off.
        private static ConfigEntry<bool>? _bgInput;
        private static bool _bgApplied, _bgPrevRun, _bgPrevIgnore;

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

            // MAP binds — act on the focused MAP display, so DefFree like SOI. Docs/map-cursor.md.
            // Cursor Up/Down/Left/Right are HELD (a velocity, not a one-shot action) and don't fit the
            // one-DriveFree-call-per-press shape the other binds use: Poll() reads their ActiveNow
            // directly (via the references captured below) and folds all four into one cursor vector
            // call, so their DriveFree here is intentionally a no-op — only Edge/held-vs-tap and the
            // config entries matter for them.
            const string map = "MAP Keybinds";
            _cursorUp    = DefFree(config, "cursor-up", map, "CursorUp", "Cursor Up", edge: false,
                "Move the map cursor up. Only acts while a MAP display is focused.", () => { });
            _cursorDown  = DefFree(config, "cursor-down", map, "CursorDown", "Cursor Down", edge: false,
                "Move the map cursor down. Only acts while a MAP display is focused.", () => { });
            _cursorLeft  = DefFree(config, "cursor-left", map, "CursorLeft", "Cursor Left", edge: false,
                "Move the map cursor left. Only acts while a MAP display is focused.", () => { });
            _cursorRight = DefFree(config, "cursor-right", map, "CursorRight", "Cursor Right", edge: false,
                "Move the map cursor right. Only acts while a MAP display is focused.", () => { });
            DefFree(config, "cursor-select", map, "CursorSelect", "Cursor Select", edge: true,
                "Select the contact under the map cursor. Only acts while a MAP display is focused.",
                () => TelemetryServer.CursorSelect());
            DefFree(config, "map-follow", map, "MapFollow", "Follow", edge: true,
                "Toggle FLW on the focused MAP display.",
                () => TelemetryServer.MapAction("toggle-follow"));
            DefFree(config, "map-zoom-in", map, "MapZoomIn", "Zoom In", edge: true,
                "Zoom in on the focused MAP display.",
                () => TelemetryServer.MapAction("zoom-in"));
            DefFree(config, "map-zoom-out", map, "MapZoomOut", "Zoom Out", edge: true,
                "Zoom out on the focused MAP display.",
                () => TelemetryServer.MapAction("zoom-out"));
            // Analog alternative to the four direction keys above — a HOTAS mini-stick/hat gives full
            // diagonal control the keys can't (only one axis can be held "active" at a time on a
            // digital pad). Axis-only: no keyboard/button source makes sense for a continuous value,
            // so these use AddAxis rather than DefFree — no Drive/DriveFree at all; Poll() reads
            // AxisValueNow via the stored references, exactly like the four direction keys' ActiveNow.
            _cursorAxisH = AddAxis(config, "cursor-axis-h", map, "CursorAxisH", "Cursor Horizontal",
                "Analog axis (HOTAS mini-stick/hat) driving the cursor left/right — overrides Cursor Left/Right when deflected. Only acts while a MAP display is focused.");
            _cursorAxisV = AddAxis(config, "cursor-axis-v", map, "CursorAxisV", "Cursor Vertical",
                "Analog axis driving the cursor up/down — overrides Cursor Up/Down when deflected. Only acts while a MAP display is focused.");

            // Deliberately NOT hidden (unlike the binds above, which the /keybinds page owns): this one
            // has no page of its own, and someone who needs it needs it in the F1 menu.
            _bgInput = config.Bind("Input", "InputWhenGameUnfocused", false,
                "Keep reading your HOTAS while the game window is NOT focused. Turn this ON if you run the MFD in a browser on the SAME PC: otherwise the game must stay focused for the stick to work, which leaves the browser in the background where it throttles its own redraw — and the map cursor stutters. Not needed for a tablet or phone, where the game keeps focus anyway. NOTE: while on, your stick also still flies the aircraft while you are clicking around in another window.");

            foreach (var b in _binds)
            {
                if (b.KeyEntry != null)
                    Plugin.Log?.LogInfo($"[NOXMFD] Keybind '{b.Id}': key={b.KeyEntry.Value}, joy={b.JoyEntry!.Value} (stick {b.JoyNumEntry!.Value}).");
                else
                    Plugin.Log?.LogInfo($"[NOXMFD] Keybind '{b.Id}': axis={b.AxisEntry!.Value} (stick {b.AxisJoyNumEntry!.Value}, invert={b.AxisInvertEntry!.Value}).");
            }
        }

        // Registers one functionality: binds its two config entries (keyboard + joystick, both hidden
        // from the F1 menu — the /keybinds page is the UI) and adds the registry row.
        private static BindDef Def(ConfigFile config, string id, string section, string key, string label,
                                bool edge, string description, Action<Aircraft> drive)
            => Add(config, id, section, key, label, edge, description, drive, null);

        // A bind that acts on the mod rather than on the aeroplane, so it needs no aircraft and works
        // at the main menu. Same row on the page — the difference is only which Poll() pass runs it.
        private static BindDef DefFree(ConfigFile config, string id, string section, string key, string label,
                                    bool edge, string description, Action drive)
            => Add(config, id, section, key, label, edge, description, null, drive);

        private static BindDef Add(ConfigFile config, string id, string section, string key, string label,
                                bool edge, string description, Action<Aircraft>? drive, Action? driveFree)
        {
            var b = new BindDef
            {
                Id = id, Section = section, Label = label, Description = description, Edge = edge,
                Drive = drive, DriveFree = driveFree,
                KeyEntry = config.Bind(section, key, new KeyboardShortcut(),
                    new ConfigDescription("Keyboard/mouse key: " + description, null, Hidden())),
                JoyEntry = config.Bind(section, key + "JoystickButton", -1,
                    new ConfigDescription("Joystick/HOTAS button (Rewired index, -1 = off): " + description, null, Hidden())),
                JoyNumEntry = config.Bind(section, key + "JoystickNumber", 0,
                    new ConfigDescription("Which joystick the button index refers to (0 = any; pinned to the captured device).", null, Hidden())),
            };
            _binds.Add(b);
            return b;
        }

        // An axis-only bind (docs/map-cursor.md): purely analog, no keyboard/button source and no
        // Drive/DriveFree dispatch (see the BindDef comment) — Poll() reads AxisValueNow directly via
        // the stored reference. section/key follow the same .cfg-identity convention as Add().
        private static BindDef AddAxis(ConfigFile config, string id, string section, string key, string label,
                                string description)
        {
            var b = new BindDef
            {
                Id = id, Section = section, Label = label, Description = description, Edge = false,
                AxisEntry = config.Bind(section, key + "JoystickAxis", -1,
                    new ConfigDescription("Joystick/HOTAS axis (Rewired index, -1 = off): " + description, null, Hidden())),
                AxisJoyNumEntry = config.Bind(section, key + "JoystickAxisNumber", 0,
                    new ConfigDescription("Which joystick the axis index refers to (0 = any; pinned to the captured device).", null, Hidden())),
                AxisInvertEntry = config.Bind(section, key + "JoystickAxisInvert", false,
                    new ConfigDescription("Invert the axis polarity — arbitrary per device.", null, Hidden())),
            };
            _binds.Add(b);
            return b;
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
            "MAP Keybinds"            => "MAP",
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
            "MAP Keybinds" =>
                "Act on the focused display only when it is showing MAP. Cursor Up/Down/Left/Right move " +
                "a crosshair over the map, or bind Cursor Horizontal/Vertical to a HOTAS axis for full " +
                "analog and diagonal control — a deflected axis overrides its two keys. Cursor Select " +
                "picks the contact under the cursor, same as a click or tap. Follow / Zoom In / Zoom Out " +
                "are direct binds for what the bezel's FLW and Z+/Z- keys already do.",
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
                if (b.Id != id || b.JoyEntry == null) continue;
                if (_capturing == null && _capturingAxis == null) EnableBackgroundInput();
                _capturing = b;
                _capturingAxis = null;   // mutually exclusive
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
                if (b.Id == id && b.JoyEntry != null) { b.JoyEntry.Value = -1; b.JoyNumEntry!.Value = 0; if (_capturing == b) Disarm(); return true; }
            return false;
        }

        // ── Axis capture (docs/map-cursor.md) ───────────────────────────────────────────────────────
        // Same shape as button capture — arm, settle a few frames, then accept whichever source moves
        // — but "moves" means deflects away from its REST position rather than an edge-down: a HOTAS
        // axis (unlike a button) usually already reads a nonzero value at rest (a throttle rarely
        // centers), so capture has to measure deflection FROM that snapshot, not from zero.
        private const float AxisCaptureThreshold = 0.5f;    // deflection from rest that counts as "moved"
        private const int   AxisSettleFrames     = 3;
        private static int _axisSettle;
        private static readonly Dictionary<(int joy, int axis), float> _axisRest = new Dictionary<(int, int), float>();

        internal static bool ArmAxisCapture(string id)
        {
            foreach (var b in _binds)
            {
                if (b.Id != id || b.AxisEntry == null) continue;
                if (_capturing == null && _capturingAxis == null) EnableBackgroundInput();
                _capturingAxis = b;
                _capturing = null;   // mutually exclusive
                _axisSettle = AxisSettleFrames;
                _axisRest.Clear();
                LogJoysticks();
                return true;
            }
            return false;
        }

        internal static void CancelAxisCapture() => Disarm();

        internal static bool ClearAxisBind(string id)
        {
            foreach (var b in _binds)
                if (b.Id == id && b.AxisEntry != null)
                {
                    b.AxisEntry.Value = -1; b.AxisJoyNumEntry!.Value = 0; b.AxisInvertEntry!.Value = false;
                    if (_capturingAxis == b) Disarm();
                    return true;
                }
            return false;
        }

        internal static bool SetAxisInvert(string id, bool invert)
        {
            foreach (var b in _binds)
                if (b.Id == id && b.AxisInvertEntry != null) { b.AxisInvertEntry.Value = invert; return true; }
            return false;
        }

        // Rewired ignores ALL joystick input while the app isn't focused (ignoreInputWhenAppNotInFocus
        // defaults on), and Unity throttles an unfocused app unless runInBackground is set. Together
        // that means a same-PC browser user has to keep the GAME focused for the stick to work — which
        // parks the browser in the background, where it throttles its own rAF and the cursor stutters.
        // Opt in and both go away. Cheap to call every frame: it no-ops once applied, and only retries
        // while Rewired isn't ready yet. Capture's own temporary override nests harmlessly — it pushes
        // the same direction and snapshots whatever is current.
        private static void ApplyBackgroundInput()
        {
            if (_bgInput == null || _bgInput.Value == _bgApplied) return;
            if (!ReInput.isReady) return;   // not up yet — try again next frame
            if (_bgInput.Value)
            {
                _bgPrevRun    = Application.runInBackground;
                _bgPrevIgnore = ReInput.configuration.ignoreInputWhenAppNotInFocus;
                Application.runInBackground = true;
                ReInput.configuration.ignoreInputWhenAppNotInFocus = false;
            }
            else
            {
                Application.runInBackground = _bgPrevRun;
                ReInput.configuration.ignoreInputWhenAppNotInFocus = _bgPrevIgnore;
            }
            _bgApplied = _bgInput.Value;
            Plugin.Log?.LogInfo($"[NOXMFD] InputWhenGameUnfocused = {_bgApplied}.");
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
            if (_capturing == null && _capturingAxis == null) return;
            _capturing = null;
            _capturingAxis = null;
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
            Plugin.Log?.LogInfo($"[NOXMFD] {CapturingKind} capture armed for '{CapturingId}': {joys.Count} joystick(s): {string.Join(", ", names)}");
        }

        // Once per frame on the main thread. CM keys are held-driven (deploy every frame held); gear keys
        // are edge-driven (act once per press). The local aircraft is fetched only if something fired.
        public static void Poll()
        {
            if (_binds.Count == 0) return;   // not bound yet
            ApplyBackgroundInput();

            // While a joy/axis entry is armed for capture, swallow the next button/deflection into it
            // (and don't let that same input also trigger an action this frame).
            if (_capturing != null)      { CaptureJoyButton(); return; }
            if (_capturingAxis != null)  { CaptureAxis(); return; }

            bool any = false;
            foreach (var b in _binds)
            {
                b.ActiveNow = Active(b);
                any |= b.ActiveNow;
            }

            // MAP cursor vector: assembled every frame, even an idle one, so releasing the last
            // direction key still reports (0,0) — the "nothing active" return below is only about
            // skipping the rest of Poll(), not about the cursor. SetCursorVector no-ops on repeat.
            // A deflected axis overrides its two keys for that component (docs/map-cursor.md) — keys
            // and an unbound/centered axis both read as 0, so "axis nonzero" is exactly "axis wins".
            float cx = 0, cy = 0;
            if (_cursorLeft!.ActiveNow)  cx -= 1;
            if (_cursorRight!.ActiveNow) cx += 1;
            if (_cursorUp!.ActiveNow)    cy -= 1;
            if (_cursorDown!.ActiveNow)  cy += 1;
            float ax = ReadAxis(_cursorAxisH!);
            float ay = ReadAxis(_cursorAxisV!);
            if (ax != 0f) cx = ax;
            if (ay != 0f) cy = ay;
            TelemetryServer.SetCursorVector(cx, cy);

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
                    _capturing!.JoyEntry!.Value = b;
                    _capturing.JoyNumEntry!.Value = i + 1;   // pin to the device it came from
                    Plugin.Log?.LogInfo($"[NOXMFD] captured joy[{i}] '{joy.name}' button {b} for keybind '{_capturing.Id}'.");
                    Disarm();   // also restores the background-input overrides
                    return;
                }
            }
        }

        // Runs on the main-thread Poll while an axis capture is armed. Settle window matches button
        // capture's, but "moved" here means deflected past AxisCaptureThreshold FROM the rest position
        // snapshotted at the end of settling — a HOTAS axis usually isn't centered at rest (a throttle
        // rarely is), so measuring from zero would either capture immediately (an off-center rest
        // already past the threshold) or never (a rest near ±1 with nowhere left to deflect FROM zero).
        private static void CaptureAxis()
        {
            if (!ReInput.isReady) return;
            IList<Joystick> joys = ReInput.controllers.Joysticks;

            if (_axisSettle > 0)
            {
                if (--_axisSettle == 0)
                    for (int i = 0; i < joys.Count; i++)
                        for (int a = 0; a < joys[i].axisCount; a++)
                            _axisRest[(i, a)] = joys[i].GetAxis(a);
                return;
            }

            for (int i = 0; i < joys.Count; i++)
            {
                Joystick joy = joys[i];
                for (int a = 0; a < joy.axisCount; a++)
                {
                    float rest = _axisRest.TryGetValue((i, a), out float r) ? r : 0f;
                    if (Math.Abs(joy.GetAxis(a) - rest) < AxisCaptureThreshold) continue;
                    _capturingAxis!.AxisEntry!.Value = a;
                    _capturingAxis.AxisJoyNumEntry!.Value = i + 1;   // pin to the device it came from
                    Plugin.Log?.LogInfo($"[NOXMFD] captured joy[{i}] '{joy.name}' axis {a} for keybind '{_capturingAxis.Id}'.");
                    Disarm();
                    return;
                }
            }
        }

        // Is this bind active this frame? A keyboard/mouse key (KeyboardShortcut, Unity input) OR an explicit
        // Rewired joystick button index. edge=false → held (IsPressed/GetButton, the CM keys); edge=true →
        // pressed-this-frame (IsDown/GetButtonDown, the gear keys). Joystick KeyCodes inside the
        // KeyboardShortcut are ignored — those go through the Rewired index instead. An axis-only bind
        // (KeyEntry null) has no digital press to report — Poll() reads its analog value separately.
        private static bool Active(BindDef bind)
        {
            if (bind.KeyEntry == null) return false;
            KeyCode k = bind.KeyEntry.Value.MainKey;
            bool kbd = k != KeyCode.None && k < KeyCode.JoystickButton0 &&
                       (bind.Edge ? bind.KeyEntry.Value.IsDown() : bind.KeyEntry.Value.IsPressed());
            return kbd || JoyBtn(bind.JoyEntry!.Value, bind.JoyNumEntry!.Value, bind.Edge);
        }

        // Reads a bind's analog axis, deadzoned and inverted, folded straight into the cursor vector —
        // 0 means "no axis bound, centered, or within the deadzone," which Poll() treats as "the keys
        // decide this component" (see the doc's "keys and axes coexist" note).
        // Small — this is a cursor, not a flight control: the pilot wants fine, slow slew available
        // near centre, and a stick good enough to bind here doesn't drift much. Raise it only if a
        // centred stick makes the crosshair wander. The response curve below covers most of what a
        // deadzone would otherwise be doing, since it already flattens the bottom of the travel.
        private const float AxisDeadzone = 0.03f;
        // Response curve. Full deflection always means full CURSOR_SPEED — the curve only shapes
        // everything below it: at 1 the axis is linear, at 2 half travel gives a quarter speed, at
        // 2.5 about a sixth. Higher trades top-end reach for fine placement near centre. Currently
        // linear so the other cursor changes can be judged without it in the way.
        private const float AxisCurve = 1f;
        private static float ReadAxis(BindDef bind)
        {
            int idx = bind.AxisEntry!.Value;
            if (idx < 0 || !ReInput.isReady) return 0f;
            IList<Joystick> joys = ReInput.controllers.Joysticks;
            int joyNum = bind.AxisJoyNumEntry!.Value;
            float raw = 0f;
            if (joyNum <= 0)   // any joystick with enough axes — same convention JoyBtn uses for buttons
            {
                for (int i = 0; i < joys.Count; i++)
                    if (idx < joys[i].axisCount) { raw = joys[i].GetAxis(idx); break; }
            }
            else
            {
                int j = joyNum - 1;
                if (j < joys.Count && idx < joys[j].axisCount) raw = joys[j].GetAxis(idx);
            }
            if (bind.AxisInvertEntry!.Value) raw = -raw;
            float mag = Math.Abs(raw);
            if (mag < AxisDeadzone) return 0f;
            // Rescale the live part of the travel back to a full 0..1 rather than returning the raw
            // value: otherwise the first movement past the deadzone jumps straight to AxisDeadzone
            // speed instead of easing up from nothing, which is the sub-pixel control the pilot
            // reaches for the axis to get. Sign is carried separately so both directions ease alike.
            float norm = (mag - AxisDeadzone) / (1f - AxisDeadzone);
            return Math.Sign(raw) * (float)Math.Pow(norm, AxisCurve);
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
