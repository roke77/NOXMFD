# Extended Keybinds page (/keybinds)

A standalone page (like `/f35` — opened directly, not an MFD split pane) for binding the
mod's *extended keybinds*: cockpit functions the game has no native keybind for. It is the
only keybind UI — the F1 (ConfigurationManager) menu shows none of these entries, though
the values still persist in the plugin `.cfg`.

One flat table, one row per function:

| column | content |
|---|---|
| **Function** | the function name; hover for a description |
| **Keyboard** | current key; click to capture, `Esc` cancels, `×` clears |
| **Joystick / HOTAS** | current button as `J<stick> B<button>`; click to arm capture, `×` clears |

## The binds

| Function | Behaviour | Driven |
|---|---|---|
| Flares | select + deploy IR flares; hold to keep popping | held |
| Jammer | select + activate the radar jammer; HOLD to jam | held |
| Gear Up / Gear Down | dedicated raise/lower (stock bind is a toggle); no-op if already there, mid-transition, or on the ground | edge |
| Cycle Guns / Missiles / Bombs | select within the class — see below | edge |
| Gun Trigger / Weapon Release | two-stage class fire keys — see below | held |

### Weapon selectors (WeaponSelectors.cs)

Alongside the game's single active-weapon selection, the mod remembers a **gun** choice and
a **missile-or-bomb** choice ("soft selections", pointing at the same aggregated loadout
entries the WPN page lists). Classification uses the game's public `WeaponInfo` flags:
`gun`; `bomb`/`glideBomb`; missiles are the `missile` flag plus flagless launched ordnance
(rockets carry no flag), excluding jammer/cargo/troops/sling.

- **Cycle keys select.** With the active weapon in the key's class, each press advances to
  the next entry and makes it active; from another class, the first press recalls the
  class's remembered weapon (or its first) and activates it, and the next press cycles.
  Cycling skips depleted entries; a fully depleted class makes the key a no-op.
- **Fire keys are two-stage across classes.** When the active weapon isn't the key's class,
  a press only *switches* to the class's weapon — bringing up the right reticle — and that
  same hold never fires; release and press again to fire. In-class, hold to keep firing
  (`WeaponManager.Fire()` per frame; the game's `Ready()` rate-limits, so it feels like the
  stock trigger, guns-linked included).
- **Selectors follow the pilot.** Selecting a weapon by any means (stock cycle, WPN page
  tap) snaps the matching selector to it, so the fire keys always commit the most recent
  choice. Stale names after an aircraft change fall back to the first of the class.
- **WPN page display:** both soft selections show as a stroked outline around the entry
  label (red when empty), suppressed when the entry is the actively selected weapon — the
  filled box already says it. A selection change also pages the WPN view (bezel and F-35)
  to the page holding it.

## Capture

Capture is split by source, because each side can only see its own input:

- **Keyboard is captured in the browser** — while you're on the page, keyboard focus is on
  the browser and the game never sees the key. The `KeyboardEvent.code` maps to a Unity
  `KeyCode` name (letters/digits/F-keys/numpad mechanically, the rest via a small table in
  `keybinds.js`); unmappable keys flash UNSUPPORTED. Mouse buttons are not capturable — a
  click is how the page is driven.
- **Joystick is captured by the plugin** (`Keybinds.ArmJoyCapture`) — only Rewired's button
  numbering matches playback (a browser Gamepad index doesn't line up; XInput, for one, is
  offset). While armed, the plugin overrides `Application.runInBackground` and Rewired's
  `ignoreInputWhenAppNotInFocus` so the stick stays live with the browser focused, and
  restores both on disarm. Buttons already held at arm time are excluded — a latched
  toggle switch (VPC mode selectors etc.) would otherwise be "captured" instantly; flip it
  off and on again while armed to bind it deliberately. Each bind pins its own joystick
  number (`0` = any), so a multi-device HOTAS can spread binds across sticks.

## Plumbing

- `GET /keybinds-config` — the bind registry (id, label, description, current key + joy
  button/stick) plus which bind is armed for capture. The page polls it at 600 ms; the
  poll is also how a capture result arrives.
- `POST /command`: `keybind.set-key { bind, key }` (`""`/`"None"` clears),
  `keybind.arm-joy { bind }`, `keybind.cancel-joy`, `keybind.clear-joy { bind }`. Commands
  drain on the main thread from `MissionLifecycle.Update` (persistent), so the page works
  at the main menu too.
- The registry lives in `Keybinds.cs`: one `BindDef` row per function (config entries,
  edge/held mode, drive action). Adding a keybind is one `Def()` call — the page, the
  JSON, and the polling all pick it up from the registry.
- `tools/serve_web.py` carries a stateful mock of the endpoint and commands (including a
  simulated stick capture), so the whole page is drivable in the harness without the game.
