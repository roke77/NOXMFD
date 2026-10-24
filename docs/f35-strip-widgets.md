# F-35 master strip widgets

## Goal

The F-35 shell's master strip (the fixed bar across the top of the glass) is a row of widgets the
pilot picks and orders in place, instead of fixed chrome. The arrangement is saved with each F-35
layout preset, so different presets can carry different strips, and LOAD restores the strip along
with the portals and their pages.

Branch: `feature/f35-topbar-widgets`.

## Decisions (agreed with the user)

- **Editing in place.** A pencil button at the strip's right end toggles edit mode. In edit mode
  every widget is outlined, a tap selects one (amber), and a tool group (◀ ▶ ✕ +) moves, removes or
  adds after the selection. ◀ ▶ buttons rather than drag, so it works by touch on a tablet with no
  extra gesture code. The pencil is not a widget, so the way into edit mode can't be removed.
- **"Configured" means which widgets and in what order.** No per-widget settings in v1. The
  readouts follow the game's unit setting (`adi.metric`), the same as ADI.
- **Catalog v1:** today's strip split into widgets (logo, connection, THRL, FUEL, each of the eight
  avionics flags, WAKE, FULLSCREEN), flight-data readouts from the existing `adi` slice (ALT, R-ALT,
  TAS, HDG, V/S, G, MACH), clocks (browser local time; mission time of day and elapsed from the
  existing `mis` slice), and a SPACER that takes the slack. No new telemetry.
- **Persistence: presets plus last-used.** The list is stored per browser in `localStorage`
  (`f35-strip`) on every edit, and `captureLayoutState` adds it to the F-35 layout blob as
  `strip: [ids]`. `applyLayoutState` applies it when present; a layout saved before this feature has
  no `strip` and leaves the current strip alone. `LayoutStore.cs` treats the blob as opaque, so the
  plugin needs no change.

## Design

- `shell/f35/f35-strip.js` (pure, `f35-strip.test.js`): the catalog, the default list, `normalize`
  (a stored or preset list → known ids, each once; `null` for a non-array), the list edits
  (`move`/`remove`/`add`/`available`), and the readout formatters.
- Every widget element exists once — declared in `f35.html` under `data-widget`, or built by
  `f35.js` for the readouts — and `renderStrip` re-parents them in list order, detaching the rest.
  Detached widgets keep being fed by the existing updaters, so a widget put back shows the latest
  state at once and the update code needed no change beyond holding `#ms-name` from load
  (`getElementById` can't find a detached element). Readouts skip painting while off the strip and
  repaint from the cached slice on `renderStrip`.
- Edit mode's tap-to-select is one capture-phase click listener on `#ms-widgets`; it stops the
  event before a flag's `avn.toggle` or the WAKE/FULLSCREEN handlers.
- The ADD list reuses the shared `LayoutModal.open`.
- The default list is the pre-feature strip, with THRL/FUEL now side by side (each its own widget)
  instead of stacked, and a SPACER where the flags' `margin-left: auto` used to push them right.

## Known limits

- A strip with more widgets than fit clips at its right end (`overflow: hidden`), as the fixed
  strip did at narrow widths; edit mode is where the pilot trims it.
- The F-35 LOAD picker shows names only, so it doesn't preview a layout's strip.

## Verification

- `f35-strip.test.js`: normalize, list edits, unit conversion, clocks.
- `serve_web.py` harness at 1800 px: default strip matches the old one; add/move/remove in edit
  mode; a flag tap in edit mode sends no `avn.toggle`; the strip survives a reload; SAVE writes
  `strip` into the layout blob and LOAD restores it after the strip was changed.
- Still needs a live-game check: ALT/R-ALT/TAS/HDG/V/S/G/MACH against the cockpit instruments in
  both unit settings, and MISSION TIME/ELAPSED against the MIS page in a single-player mission.
