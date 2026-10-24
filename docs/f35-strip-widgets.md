# F-35 master strip widgets

## Status

**Status: merged to `main` and released in 0.78.0.** Harness-verified and checked in game.

## Goal

The F-35 shell's master strip (the fixed bar across the top of the glass) holds widgets the pilot
picks in place, instead of fixed chrome. The arrangement is saved with each F-35
layout preset, so different presets can carry different strips, and LOAD restores the strip along
with the portals and their pages.

## Decisions (agreed with the user)

- **Fixed ends, slots in the middle.** The brand block (wordmark stacked over the name URL and
  connection status) is fixed at the left end; WAKE, FULLSCREEN and the edit pencil are fixed at the
  right end (`.ms-fixed`). Neither is a widget, so edit mode can't remove the way into it, and
  WAKE/FULLSCREEN keep working while editing. Between them sit two slots (`F35Strip.SLOTS`), each
  holding at most one widget and sharing the width evenly.
- **Editing in place, per slot.** The pencil toggles edit mode. Each slot then shows a dashed box at
  the strip's full height less a small top/bottom margin; an empty slot is one big **+** (the whole
  slot is the touch target) that opens the widget list for that slot, and a filled slot has an
  **X** in its top-right corner that empties it. No selection, no reordering.
- **"Configured" means which widget sits in which slot.** No per-widget settings in v1. The
  readouts follow the game's unit setting (`adi.metric`), the same as ADI.
- **Catalog v1:** six widgets — THRL/FUEL (stacked), RPM/HEAT (stacked, HEAT filled with the
  `avn` slice's `heatColor` as AVN does), AVIONICS FLAGS (all eight, each a lit toggle button in the
  WPN MODES style: white name, lamp, glyph; lamp and glyph take AvnStatusPolicy's colour, and a
  press is the same `avn.toggle` AVN sends), WPN MODES (below), FLIGHT DATA
  (ALT, R-ALT, TAS, HDG, V/S, G, MACH from the existing `adi` slice) and CLOCKS (browser local
  time; mission time of day and elapsed from the existing `mis` slice). No new telemetry.
- **WPN MODES** starts with the selected weapon: its name over WPN's ammo gauge over the round
  count (`F35Strip.selectedAmmo`, off the `loadout` slice's `items`/`selWeapon`, with WPN's
  pip-or-fill rule at 12 rounds of capacity), red when out of rounds, `--` with nothing selected.
  Then the one pressable part: two switches in the lit-button style (a lamp over each
  state's label, the current one glowing): MASTER (SAFE red / ARM green) and MODE (A/A / ALL / A/G,
  amber). Each whole switch is a button; a press sends the next state to the right, wrapping
  (`F35Strip.nextState`), through the existing `master-arms.set` / `combat-mode.set` commands. The
  lit state is read off the `loadout` slice, as WPN's own ARM/SAFE and A/A/A/G marks are, so it
  shows what the game applied rather than the press. MODE includes ALL as its own state, unlike
  WPN's A/A/A/G pair (where holding resets to ALL).
- **Persistence: presets plus last-used.** The slots are stored per browser in `localStorage`
  (`f35-strip`) on every edit, and `captureLayoutState` adds them to the F-35 layout blob as
  `strip: [id|null, id|null]`. `applyLayoutState` applies it when present; a layout saved before this feature has
  no `strip` and leaves the current strip alone. `LayoutStore.cs` treats the blob as opaque, so the
  plugin needs no change.

## Design

- `shell/f35/f35-strip.js` (pure, `f35-strip.test.js`): the slot count, the catalog, the default
  (`['gauges', 'flags']`), `normalize` (a stored or preset arrangement → exactly SLOTS entries of a
  known id or null, no id twice; `null` for a non-array), the slot edits (`place`/`clear`/
  `available`), and the readout formatters.
- Every widget element exists once — declared in `f35.html` under `data-widget`, or built by
  `f35.js` for the readouts — and `renderStrip` moves each into its slot, detaching the rest. The
  slot elements, each with its + and X buttons, are built once; CSS shows the right control only in
  edit mode.
  Detached widgets keep being fed by the existing updaters (gauges, flags, WPN MODES — each
  guarded to write only on a change), so a widget put back shows the latest state at once. The
  FLIGHT DATA and CLOCKS groups skip formatting entirely while off the strip and repaint from the
  cached slice on `renderStrip`.
- In edit mode a capture-phase click listener on `#ms-widgets` stops a tap on a widget before a
  flag's `avn.toggle`, and lets the slot's own + and X through.
- The ADD list reuses the shared `LayoutModal.open`.

## Known limits

- A slot clips a widget wider than it (`overflow: hidden`); the flags are the widest widget, so a
  narrow glass may cut them.
- The F-35 LOAD picker shows names only, so it doesn't preview a layout's strip.

## Verification

- `f35-strip.test.js`: normalize, slot edits, unit conversion, clocks.
- `serve_web.py` harness at 1800 px: brand block left and button group right, in and out of edit
  mode; dashed slots 6-8 px inside the strip; + fills a slot from the list (the other slot's widget
  not offered), X empties it; a flag tap in edit mode sends no `avn.toggle`; the strip survives a reload; SAVE writes
  `strip` into the layout blob and LOAD restores it after the strip was changed.
- WPN MODES in the harness: presses send `master-arms.set`/`combat-mode.set` with the next state,
  and the lit segment follows the mock's `loadout` (`__setFrame` in `tools/preview-mock.js`); the
  weapon block shows pips (AIM-9X 1/2), a fill (GUN 310/400), red when empty (0/1), and `--`.
- Checked in game: the strip's widgets against the cockpit — FLIGHT DATA, CLOCKS, HEAT's colour and
  WPN MODES.
