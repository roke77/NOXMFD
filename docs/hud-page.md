# HUD page — a remote for the game's HUD OPTIONS

## Status

**Feature-complete in the harness** on `feat/hud-declutter` (issue #20); the end
-to-end in-game test of the full page is the remaining check.

- **Proven in game**: the plugin drives the in-cockpit HUD OPTIONS live — a
  toggle from the MFD re-renders the HUD immediately, no re-init.
- **Built**: `hud.set` / `hud.mode` commands, a `GET /hud-options` read endpoint,
  the `pages/hud/` page (full replica), and its entry points in both layouts —
  the F-35's MAIN HUD button and a bezel MAIN key.
- **Next**: load the finished page in game and confirm every group toggles as
  expected (only the live-apply of one category was tested in game; the rest is
  the same command path, verified in the harness).

## What it is

The game already has an on-MFD **HUD OPTIONS** screen (`HUDOptions`,
`SceneSingleton`). It controls how unit overlay icons appear on the HUD:
maximize a category and its icons show at full size, minimize it and they shrink
to a dot or vanish. This feature is a **browser MFD replica of that screen** —
the same controls, driven remotely, so the pilot declutters the HUD from the
external MFD instead of the in-cockpit one.

This is *not* the config-menu "HUD Settings" screen (`HUDSettings` /
`PlayerSettings`: gauges, HUD weapons, HMD sizing, colours). That is a separate
system, out of scope here.

## The model (from decompiling `HUDOptions`)

`HUDOptions.CheckMaximizeIcon(unit)` returns a scale that gates every unit's HUD
marker. Zero means the marker minimizes. Four kinds of control feed it:

- **Modes** — `listModes`, the tabs NAV / GUN / A2A / A2G / EW / LOG. Radio
  buttons; each carries a saved priority preset (`HUDOptions_Priorities`).
  Selecting one applies that preset's whole category/type configuration.
  `currentMode` only tracks `AutomaticToggle`'s weapon-driven auto-switches —
  a manual tab click (`ToggleButtons`) never sets it, so it desyncs the
  moment a player picks a mode by hand. The lit tab (`listModes[i].status`)
  is the state a manual click always updates, and what `/hud-options` reads.
- **Categories** — `listCategories`: FRIENDLY UNITS, ENEMY UNITS, AIRCRAFT,
  MISSILES, VEHICLES, BUILDINGS, SHIPS. Each has `maximized` + `Set(bool)`.
- **Vehicle types** — `listVehicleTypes`: TRUCK, UGV, LCV, AFV, MBT, ART, AAA,
  IR SAM, R SAM, RDR. Each a toggle with `status` + `Set(bool)`.
- **Building types** — `listBuildingTypes`: CIV, FAC, RDR, DEP, HGR, DEF, AMMO.

### What "minimize" looks like (`HUDUnitMarker.UpdateMaximized`)

- **maximized** → the full unit icon.
- **not maximized, enemy** → shrinks to the small `minimizedHostile` dot.
- **not maximized, friendly** → sprite set to null: effectively invisible.

Two catches that in-game testing surfaced:

- **Aircraft ignore the toggle.** `HUDUnitMarker` sets
  `alwaysMaximized = (unit is Aircraft)`, so aircraft always show at full size
  regardless of the AIRCRAFT category. This is why the first PoC (which toggled
  AIRCRAFT) looked like it did nothing — the command *was* working; aircraft
  simply opt out. Ground units go through the real path and respond.
- **A ~4 s grace period.** A newly-spotted unit stays maximized for ~4 s
  (`timeCreated`), so a toggle lands on units already established on the HUD, not
  ones just appearing.

## The plumbing (built)

Commands mirror the existing `tgt.*` shape and ride the same envelope
(`group` / `index` / `on`) and main-thread drain path:

- `hud.set {group, index, on}` — `group` is `category` | `vehicle` | `building`.
  Flips the addressed toggle and calls `ApplyHUDSettings()` so the HUD
  re-renders now rather than after the ~1 s idle refresh.
- `hud.mode {index}` — selects a mode tab via `ToggleButtons`, which does the
  radio flip and applies that mode's preset.

Reading is a fetch endpoint, not the stream — HUD options change only on a
toggle, so it is served on demand like `/config`:

- `GET /hud-options` → `{ mode, modes[], categories[bool], vehicles[{n,on}],
  buildings[{n,on}] }`. Built on the main thread by
  `TelemetryServer.RefreshHudOptions` on the 1 Hz tick and cached.

### Why category names are the page's, not the plugin's

Vehicle and building toggles carry real names — the game builds them from
`Encyclopedia.i.vehicleTypes[i].typeName` (parallel index), so the endpoint
emits `{n, on}` for those. **Categories have no per-entry display name** exposed
by `HUDOptions`; their order is assigned in the game's Unity inspector. So the
endpoint emits only their count + on/off, and the page carries the fixed labels
(FRIENDLY / ENEMY / AIRCRAFT / MISSILES / VEHICLES / BUILDINGS / SHIPS) by index.

This is the one hardcoded ordering in the feature. It is stable (a fixed
in-game screen), but if the game reorders categories the labels would drift. The
count is emitted as a sanity check; a mismatch should be surfaced rather than
mislabelled.

## Staged approach

1. **PoC — live-apply** ✅ done. One command toggling one category, wired to the
   F-35's greyed HUD button, to prove the HUD re-renders live. Confirmed: ground
   icons shrink/grow on toggle.
2. **Plugin foundation** ✅ done. The generalized `hud.set` / `hud.mode`
   commands and `/hud-options`, replacing the PoC's single hardcoded toggle.
   Compiles against the real `HUDOptions` API.
3. **The page** ✅ done — `pages/hud/`, a full replica: mode tabs, category
   toggles, vehicle + building sub-types. Renders from `/hud-options`; each
   control POSTs an `hud.*` command. Unlike the telemetry-pushed pages, HUD
   OPTIONS has no push channel, so the page polls `/hud-options` every 1.2 s
   (matching the plugin's own 1 Hz snapshot) instead of re-fetching only after
   its own writes — one loop catches a toggle pressed in game, a mode press
   rewriting many toggles at once, and a mission starting/ending. Clicks flip
   optimistically for instant feedback. Split-capable, like TGT/BDF/PAL — the page is self-driven
   (it polls `/hud-options` itself), so a split pane needs no shell forwarding.
4. **Both layouts** ✅ done — the F-35's `hud` action opens `/hud` (PoC stub
   removed); the bezel has an HUD key on MAIN (`BEZEL_EXTRAS`, right bank) opening
   the same `#page-frame` page. The MAIN back button comes from a new
   `NAV.hud = [{MAIN}]`, so neither layout special-cases the way back.

## The native-HUD declutter strip

Above the HUDOptions replica sits a one-row strip of three toggles — WEAPONS,
MINIMAP, FLIGHT — that drive the mod's **own** `HudDeclutter` feature
([HudDeclutter.cs](../src/plugin/HudDeclutter.cs)), not the game's `HUDOptions`.
It is a separate axis: HUDOptions gates which *unit icons* draw on the HUD; these
hide native HUD *widgets* (the top-right weapon/ammo/CM cluster, the corner
minimap, the boxed heading/speed/alt readouts). They share the page because both
are "declutter the HUD," but sit visually apart (their own row + divider, and
outline-only lit styling so they don't read as more mode tabs).

The live-apply was already proven before this page existed: `HudDeclutter`
re-reads its three `HudDeclutterConfig` flags every tick and hides/restores
within ~0.5 s (the minimap within a frame). Those flags used to be flipped from
the F1 ConfigurationManager menu; that path proved the live-apply, so wiring the
page to the same flags carried no new game-integration risk:

- **Command** — `declutter.set {group, on}`, `group` ∈ `weapon` | `minimap` |
  `boxes`, `on` = the desired **hide** state. The handler writes the matching
  `ConfigEntry.Value` (which persists to the .cfg); `HudDeclutter` applies it on
  its next tick — nothing to apply in the handler.
- **Read** — `/hud-options` gained a `declutter: {weapon, minimap, boxes}` object
  (each `true` = currently hidden), so the same 1.2 s poll that syncs the rest of
  the page also reflects a flag edited directly in the .cfg.
- **Page** — the strip inverts the reported hide flag to a lit/gray "shown on
  HUD" state, matching the rest of the page (lit green = visible). A click flips
  optimistically and POSTs `declutter.set` with the inverse (`on` = hide).

Once the page became the control surface, the three `ConfigEntry`s were marked
`Browsable = false` so they no longer show in the F1 menu — otherwise they'd be a
redundant second set of checkboxes. They still persist to the .cfg (so a declutter
choice survives a restart) and stay hand-editable there.

## Matching the in-game look

The page mirrors the in-game screen's colours and icons:

- **Colours.** Each category row carries an identity colour (`--cat`): green for
  the type categories, cyan for FRIENDLY, red for ENEMY — the row border, its
  name, and its lit MAXIMIZE all read in it, gray when minimized. Mode tabs are
  white outlines, the current one a green fill.
- **Sub-type icons.** The vehicle and building chips show the real captured type
  sprite over the label, like the TGT vehicle filter. Vehicles reuse the mod's
  existing `/tgt-icon` capture; buildings get their own `/building-icon` (a new
  `AssetCapture.TryCaptureBuildingTypeIcons` + server map), keyed apart because a
  name like `RDR` is both a vehicle and a building type. Both dim when their type
  is off and retry a few times if the sprite hasn't been captured yet.

**Not done: the category-row icons** (the plane / missile / tank / building /
ship glyphs beside AIRCRAFT…SHIPS). The game draws these from a UI Image in the
row prefab, not from anything `HUDOptions` exposes per category, so there is no
clean sprite to capture. Left off rather than faked; an inline-SVG approximation
(as the AVN flags do) is the fallback if they're wanted.

## Open questions

- **Modes vs. manual toggles.** Resolved — the page just resyncs (no per-toggle
  diff shown). Selecting a mode applies a preset that overwrites the category/
  type toggles; the continuous 1.2 s poll picks up the resulting state on its
  own, same as any other externally-driven change.
- **Persistence.** `HUDOptions.SaveSettings()` writes the current mode's preset
  to JSON. The page does not need to call it — toggles apply live and the game
  owns persistence — but a "save this as the mode default" control is a possible
  later nicety.
- **Category label drift.** See "Why category names are the page's". If it ever
  bites, the fix is to derive names plugin-side (faction flag for the first two,
  unit-definition type for the rest) and emit them like vehicles/buildings.
- **Reflecting in-game toggles.** Resolved — the page polls `/hud-options` every
  1.2 s (stage 3), so a change made from the in-cockpit screen shows on the MFD
  within ~1 s (the plugin's 1 Hz refresh tick), not only after the page's own
  writes.
