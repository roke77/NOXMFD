# HUD rework — lit-panel layout, preset cards

**Status:** built on `feature/hud-rework`; verified in the `serve_web` harness, not yet in game.
The layout follows the approved "HUD B · Quick Recall" design from the AVN page alternatives study,
with the preset controls of the TGT rework ([tgt-rework.md](tgt-rework.md)) and PRESETS placed above
MODE.

## Layout, top to bottom

1. **Declutter** — WEAPONS, MINIMAP, FLIGHT, FEED in one row (two rows of two in a narrow pane).
2. **PRESETS (hold to save)** — five cards.
3. **MODE** — NAV, GUN, A2A in the first row; A2G, EW, LOG in the second.
4. **CATEGORIES** — FRIENDLY, ENEMY, AIRCRAFT, MISSILES, VEHICLES, BUILDINGS, SHIPS.
5. **VEHICLE TYPES** and 6. **BUILDING TYPES** — icon over label.

The shell keeps the bezel and navigation; the page keeps its side inset so nothing sits under the
shell's horizontal label. The panel scrolls, and its font size follows the pane width (11 px floor,
18 px ceiling), so every size below is in `em`. Below 700 px the grids drop to fewer columns.

## Controls

- **Lit-panel pushbuttons** — `.lit` in `shared/lit-panel.css`, shared with TGT's preset cards. A
  lamp above the label is dark when the control is off and glows (`0 0 8px` + `0 0 2px`) in its
  `--accent` when on. State is `aria-pressed`. Colours are the design's exact values; the ones with
  a shared token use it (`--no-white`, `--no-white-dim`, `--no-green-dim`, `--no-green-deep`,
  `--no-label`, `--no-red`).
- **Declutter** — a lit card is **SHOWN**. The backend flags are HIDE state, so the card inverts
  them; `declutter.set {group, on}` still carries the HIDE state (`weapon`, `minimap`, `boxes` for
  FLIGHT, `feed`).
- **Preset cards** — `services/preset-cards.js`, the component TGT uses, pointed at
  `/hud-presets` and the `preset.*` commands. Tap recalls (`preset.load {index}`); hold (500 ms, the
  TGT and PAD-cursor threshold) opens the SAVE PRESET dialog: SAVE sends `preset.save {wname,
  index}`, CLEAR `preset.delete {index}`. The current slot rides the `/hud-options` `preset` field;
  a change to it refetches the five names, so a recall or save from another display or a KEY-page
  bind moves the lit card. A completed hold never also recalls. A pending hold is dropped on
  pointer cancel, on leaving the card and on focus loss. Enter / Space on a focused card taps and
  holds the same way.
- **Mode** — exclusive; `hud.mode {index}`. The lit button follows the game's reported mode.
- **Categories** — the existing fixed order and index mapping; `hud.set {group:'category', index,
  on}`. FULL is maximized, MIN minimized. FRIENDLY and ENEMY show a faction dot (`#33ccff`,
  `--no-red`); the others show the captured `/hud-cat-icon` glyph. A MIN card dims its glyph.
- **Vehicle / building types** — built from the `/hud-options` lists, with the real `/tgt-icon` and
  `/building-icon` sprites and the existing name formatting (`IR_SAM` reads `IR SAM`);
  `hud.set {group:'vehicle'|'building', index, on}`. A section with no types is hidden.
- **PAD cursor** — a tap clicks the control under the crosshair; over a preset card it recalls, and
  a hold saves. The page listens for `cursor-held`, like TGT. The SAVE PRESET dialog's three
  buttons are cursor targets.

## Backend change

`preset.save` takes an optional `index` (1-5), like `tgt-preset.save`. Absent, it saves into the
current slot as before; present, it saves into that slot and makes it current. This is what lets a
hold on an empty slot save there.

## Decisions

- **Rename.** The LOAD picker's pencil and × are gone with the picker. Holding a card covers both:
  the dialog opens prefilled with the slot's name (retype and SAVE to rename; the save also
  re-captures the current filters) and CLEAR empties the slot. `preset.rename` stays a command but
  the page no longer sends it.
- **Dialog.** The hold opens TGT's keypad-style dialog rather than saving instantly under a
  placeholder name, so a slot always gets a real name.
- **`LayoutModal`** is no longer loaded by the HUD page; SAVE/LOAD LAYOUT keep it.
- **`preset-bar.js`** (the old "PRESET N: name" bar) is deleted; `preset-cards.js` replaced it and
  TGT's own copy.

## Still needs an in-game check

- **Save into a non-current slot.** Hold an unlit card, save, and confirm the game applies the
  save to that slot (`BepInEx/config/com.roque.NOXMFD.hud-presets.json` gains it) and the card
  lights.
- **Recall after a save from another browser or a KEY-page bind** moves the lit card within about a
  second.
- Declutter SHOWN/HIDDEN matches what the HUD shows, including FEED.
- The PAD cursor taps and holds a preset card and presses the dialog's buttons.
- Label and icon readability in a half-width split pane and the F-35 portals.

## Not covered

- The PAD cursor can press the dialog's buttons, but typing a name still needs a keyboard.
