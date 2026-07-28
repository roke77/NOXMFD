# Extended Keybinds page — implementation plan

**Status: planning** (feature branch `extended-keybinds`). This document is the working plan;
it becomes a normal feature doc (like `hud-page.md`) once the page ships.

## Goal

A full standalone page at **`/keybinds`** (same level as `/f35` — a page you open directly,
not an MFD split pane) where the user configures the mod's *extended keybinds*: bindings for
cockpit functionality the game itself has no keybind for. Today these can only be configured
through the F1 (ConfigurationManager) menu; the page **replaces** that — the F1 menu is no
longer the keybind UI, new binds never get F1 entries, and the existing four binds' F1
plumbing goes away.

## What exists already

All extended-keybind machinery lives in `src/plugin/Keybinds.cs`:

- Each bind = one BepInEx `ConfigEntry<KeyboardShortcut>` (keyboard/mouse) **plus** one
  `ConfigEntry<int>` (Rewired joystick button index, `-1` = off). A bind fires if either
  source is active. Persisted automatically in the plugin `.cfg`.
- Joystick capture: an entry is "armed", then the main-thread `Poll()` writes the next
  joystick button pressed into it (`CaptureJoyButton`), pinning `JoystickNumber` to the
  device. Keyboard capture today is ConfigurationManager's own key-capture widget.
- Polled once per frame from `MissionLifecycle.Update` (main thread, survives scene loads).

The binds that exist today — these seed the page's table:

| Functionality | Behaviour | Driven |
|---|---|---|
| Dispense Flares | select + deploy IR flares; hold to keep popping | held |
| Activate Radar Jammer | select + activate jammer; HOLD to jam | held |
| Gear Up | raise gear (no-op if up / moving / on ground) | edge |
| Gear Down | lower gear (no-op if down / moving / on ground) | edge |

## New binds: weapon cycling + firing (the two soft selectors)

Five new functions ship with the page, all built on one idea: the mod keeps **two background
("soft") selections** alive at all times — one over a *gun*, one over a *missile-or-bomb* —
independent of the game's single active-weapon selection. Cycle keys move the soft
selections; the fire keys commit them (select + activate in one press):

| Functionality | Behaviour | Driven |
|---|---|---|
| Cycle Guns | advance the gun soft-selector through the loadout's guns | edge |
| Cycle Missiles | advance the release soft-selector through missiles **and rockets** | edge |
| Cycle Bombs | advance the release soft-selector through bombs | edge |
| Gun Trigger | make the soft-selected gun (or the first gun found) the active weapon and fire it; hold for continuous fire | held |
| Weapon Release | make the soft-selected missile/bomb (or the first found) the active weapon and release it | edge |

Design decisions:

- **Two selectors, three cycle keys.** The gun selector is moved only by Cycle Guns. The
  *release* selector is a single pointer moved by two keys, each constraining it to its
  class: Cycle Missiles walks the missile/rocket entries, Cycle Bombs walks the bomb
  entries. Whichever was cycled last is what Weapon Release fires.
- **Classification comes from the game's own `WeaponInfo` public bools** (no reflection):
  `gun`, `missile`, `bomb` (+`glideBomb`). Rockets carry none of the guided flags — the
  release bucket is `missile || bomb || glideBomb ||` *unclassified ordnance* (not gun, not
  jammer/cargo/troops/sling/hideInDisplay). Verify a stock rocket pod's flags in-game
  before locking the rule (investigation item).
- **Selectors point at loadout entries, not stations** — the same aggregated-by-name list
  the WPN page shows (`BuildLoadout`). Committing a selection resolves name → first live
  station, reusing the exact station-resolution + `SetActiveStation` + `ShowWeaponStation`
  sequence `CommandDispatcher.WeaponSelect` already implements (extract it to a shared
  helper rather than duplicating).
- **Firing goes through `WeaponManager.Fire()`**, the same public entry the stock trigger
  drives — it already handles safety (`SafetyIsOn`), guns-linked (`FireGuns`), salvo, and
  the network-correct launch path. Gun Trigger calls it every frame held (`Ready()`
  rate-limits, matching stock gun behaviour); Weapon Release is one press = one release.
- **Selectors follow the active selection.** When the game's active weapon is (or becomes)
  a gun, the gun selector snaps to it; likewise a missile/bomb snaps the release selector.
  So the fire keys always commit what the pilot most recently chose — by cycle key *or* by
  the stock weapon-cycle — and never surprise-switch away from a manually selected weapon.
- **Lifecycle:** selectors reset to "first of class" on aircraft change / loadout change,
  and clamp when an entry disappears. Cycling doesn't skip empty weapons (`Ready()` simply
  won't fire them), same as the stock cycle.

### WPN page: soft-selection display

Both soft selections are visible on the WPN page as an **outline box** over the
corresponding weapon entry label — same geometry as the active-weapon box but stroked, not
filled. When a soft selection coincides with the actively selected weapon (the filled green
box), the outline is suppressed — the filled box already says it.

Plumbing: the telemetry snapshot gains the two soft-selected entry names next to
`SelWeapon`; the shell mirrors them into the existing `{type:'wpn', items, selWeapon}`
message it posts to WPN iframes; `wpn.js` adds an outline class to matching entries. Colors
from theme tokens (`--no-green` for the outline, matching the filled box's stroke).

Relevant web plumbing that already exists (`TelemetryServer.cs`):

- Per-page routes (`/avn`, `/hud`, …) serving embedded assets via `ServeAssetRel`.
- `/config` GET endpoint serving JSON config to the web client.
- `/command` POST channel: threadpool thread parses + enqueues, `CommandDispatcher` executes
  on the Unity main thread — exactly the shape needed for "set this bind" writes.

## Page design

Route `/keybinds` → `src/web/pages/keybinds/keybinds.{html,css,js}`, served with
`ServeAssetRel`, styled with the `theme.css` `--no-*` tokens to match the in-game menus.

One table, one row per functionality:

| column | content |
|---|---|
| **Function** | name + one-line description (the `ConfigEntry` description text, served by the plugin — single source of truth) |
| **Keyboard** | current key (or `—`); click to capture: the cell shows *press a key…*, the page listens for the browser `keydown`, maps it to a Unity `KeyCode`, POSTs it. `Esc` cancels, a Clear control unbinds. |
| **Joystick / HOTAS** | current button (`button 5` / `—`); click to arm plugin-side capture, cell shows *press a button…* until the plugin reports the captured index. Clear control unbinds. |

Rows are grouped under their existing config sections (Countermeasures, Landing Gear) as
sub-headers, mirroring how the game's own controls menu groups actions.

Capture responsibilities are deliberately split:

- **Keyboard is captured in the browser.** While the user is looking at the page, keyboard
  focus is on the browser — the game never sees the key. Browser `KeyboardEvent.code` →
  Unity `KeyCode` via a small static mapping table in `keybinds.js` (letters/digits are
  mechanical; F-keys, numpad, punctuation enumerated once). Unmappable keys are rejected
  with a visible "unsupported key" flash.
- **Joystick is captured by the plugin** through the existing `CaptureJoyButton` flow.
  Rewired's index numbering is the only one that matches playback (browser Gamepad API
  indices don't line up — same reason the F1 menu can't use Unity's `JoystickButton*`).

## Plugin work

1. **Bind registry refactor in `Keybinds.cs`.** Replace the eight parallel `ConfigEntry`
   fields with a table of bind definitions
   (`{ id, section, label, edge, keyEntry, joyEntry, drive(Aircraft) }`). `Poll()` iterates
   it; the JSON endpoint serializes it; adding a future keybind becomes one table row.
   **The F1 menu is retired for keybinds**: delete the `JoyCaptureDrawer`/`DrawJoyCapture`
   custom-drawer machinery and mark every keybind entry `Browsable = false` so it disappears
   from the F1 menu entirely. The `ConfigEntry`s themselves stay — they're still the
   persistence layer (`.cfg` file), just no longer a UI. Existing user `.cfg` values carry
   over untouched since the entry keys don't change.
2. **`GET /keybinds-config`** — JSON: for each bind `{ id, section, label, description,
   key, joyButton }`, plus `capturing` (id currently armed, or null). The page polls this
   (~4 Hz while open, matching how other pages refresh non-stream data) — it's also how
   the joystick-capture result comes back.
3. **`/command` additions** (main-thread via `CommandDispatcher`):
   - `keybind.set-key { bind, key }` — validate the KeyCode name, write the entry
     (`""`/`"None"` clears).
   - `keybind.arm-joy { bind }` / `keybind.cancel-joy` — arm/disarm the existing capture.
   - `keybind.clear-joy { bind }` — set the joy entry to `-1`.

   Persistence is free: writing a `ConfigEntry.Value` saves the `.cfg`.
4. **Keyboard capture moves fully to the page too** — with the F1 menu retired there is no
   ConfigurationManager key-capture widget anymore; the browser `keydown` flow above is the
   only way to set a keyboard bind, which is fine since it's also the better one.

## Risks / open questions

- **Joystick capture with the game unfocused.** The user is focused on the browser while
  arming capture; Unity may not pump joystick input in the background. Rewired with native
  input generally does receive background input, but this needs a real-hardware test first
  thing. Fallback if it fails: `Application.runInBackground` is already true for the
  telemetry loop — if Rewired still won't, instruct "keep the game window focused, then
  press the button" in the armed cell.
- **Bind conflicts** with stock game binds aren't detected (the game's Rewired maps would
  have to be diffed). Out of scope for v1; the defaults stay unbound, same as today.
- Multi-stick HOTAS: unchanged known ceiling — `JoystickNumber` is shared across binds
  (pinned on capture); per-bind device support stays the documented upgrade path.
- **Rocket classification**: confirm what flags a stock unguided rocket pod carries on
  `WeaponInfo` (expected: none of gun/missile/bomb) so the release-bucket rule is right.
- **Stock trigger path**: confirm `PilotPlayerState` drives `WeaponManager.Fire()` for the
  stock fire button (decompile it into `decompiled/`) so Gun Trigger / Weapon Release are
  byte-for-byte the same behaviour, including multiplayer correctness.

## Milestones

1. Bind registry refactor in `Keybinds.cs` + F1 menu retirement (drawers deleted, entries
   hidden; playback behaviour unchanged, `.cfg` values preserved).
2. `GET /keybinds-config` + the three `/command` handlers.
3. `/keybinds` page: table rendering from the JSON, keyboard capture + KeyCode mapping.
4. Joystick capture wiring + the background-input test on real hardware.
5. Soft-selector engine: classification, the two selectors, follow-active-selection,
   lifecycle resets; then the five new binds as registry rows driving it.
6. WPN page outline display (snapshot fields → shell mirror → `wpn.js` outline class).
7. Polish: grouping headers, clear controls, unsupported-key feedback, README entry.

Future keybinds (beyond this branch, but the registry must make them one-row additions):
whatever comes next — e.g. dedicated autopilot modes, lights, canopy — each is a new
`drive()` + two config entries in the table, zero page changes.
