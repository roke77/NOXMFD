# TGT rework — lit-panel layout, preset cards, WPN/TTI columns, action dock

**Status:** built on `feature/tgt-rework`; verified in the `serve_web` harness, not yet in game.
The layout follows the approved "TGT A · Lit Panel" design (the AVN page alternatives study).

## Layout, top to bottom

1. The existing TARGET SELECTION header and its tap/hold hint.
2. **PRESETS (hold to save)** — five cards.
3. **FILTERS** — faction beside LASER, categories beside HUD, vehicle types beneath.
4. The scrolling target table.
5. The DETAILED/COMPACT toggle, right-aligned under the table.
6. The action dock: **RESET FILTERS**, **CLEAR TARGETS**, **CLEAR DATALINK**, **CLEAR STALE**.

Shell and bezel styling stay with the shell; the page owns everything between the header and the
dock. The dock and toggle sit outside the table's scroller, so they stay visible however long the
list is. The separators above the table and above the dock are gone; row dividers stay.

## Controls

- **Lit-panel pushbuttons.** Preset cards, filter cells and LASER/HUD share one look: a small lamp
  above the label, unlit when off, glowing (`0 0 8px` + `0 0 2px`) in the control's accent when on.
  Green by default, blue for FRIENDLY, red for ENEMY. Vehicle types keep icon-over-label with the
  lamp beneath. Colours reuse the shared `--no-*` tokens where they match; the few that don't
  (`--tgt-off-fill`, `--tgt-lit-fill`, `--tgt-off-text`, `--tgt-off-lamp`, `--tgt-cell-border`)
  live in `tgt.css`.
- **Preset cards** — `tgt-presets.js` (unit-tested); see [tgt-presets.md](tgt-presets.md). Tap recalls; hold opens the SAVE PRESET
  dialog for that slot: an amber name entry with CANCEL (white), CLEAR (red, empties the slot) and
  SAVE (green). The dialog is the page's own, in the design of MAP's TGP slew keypad and
  NOAutopilot's target keypad (scrim, bordered panel, `--no-amber` entry), not the shared
  `LayoutModal` the layout and HUD-preset prompts still use. The five cards mirror
  `GET /tgt-presets`; the current slot's index/name already rides the `tgt` telemetry block, and a
  change to it triggers a refetch of the list. The PAD cursor taps and holds a card the same way.
- **Action dock** — every button's `data-cmd` is its `tgt.*` command (`reset`, `clear`,
  `clear-datalink`, `clear-stale`), so one click handler covers all four and the PAD cursor just
  clicks the button. CLEAR DATALINK / CLEAR STALE show `· N` when N rows would be dropped, computed
  from the list (STALE implies DATALINK, so the datalink count includes the stale rows, as the
  plugin's bulk deselect does). No count is shown at zero, like `TARGETS (N)`.

## Target table

Columns: **TARGETS → WPN → TTI → [TD] → SRC → RNG → GRID → [SPD → ALT → HDG]**, all left-aligned.
The header and every row share one `grid-template-columns` and one side inset, inside the same
scroller, so they stay aligned with a scrollbar showing. The TD column (squad leader) fills an
optional track through `--tl-td-col`, and `--tl-min` is the matching floor below which the table
scrolls horizontally instead of compressing.

DETAILED prioritises the name: with three more columns to fit, every side column is sized to its
content in units of `--tl-fs` (the row font size, shared with the header so both grids resolve to
the same tracks) and TARGETS takes all the width that frees up.

- **WPN / TTI** are plain amber text; both are blank when nothing of the player's is tracking the
  row. The boxed TTI badge on the name is gone.
- **SRC** is a boxed badge: SENSOR green, DATALINK purple, STALE off-white.
- The focused row keeps its amber outline with a `rgba(255,170,0,.06)` fill; the grey outline when
  the PAD cursor owns Select is unchanged.

## Backend changes

- `lockedTargetWpn` — a `string[]` parallel to `lockedTargetIds` / `lockedTargetTti`: the
  `definition.unitName` of the missile whose estimate is that lock's TTI, `""` when none.
  `TargetTtiEstimator.ComputeAll` fills it in the same pass; see [hud-tti-estimate.md](hud-tti-estimate.md).
- `tgt-preset.save` takes an optional `index` (1-5). Absent, it saves into the current slot as
  before; present, it saves into that slot and makes it current.

## Verification performed

- `tools/ci-check.ps1`: release build, every `*.test.js` (including a WPN case in
  `telemetry-source.test.js` and `tgt-presets.test.js` for the cards and dialog), `dotnet test`,
  route smoke.
- Harness (`tools/serve_web.py`): tap recalls (`tgt-preset.load {index}`), hold opens
  "SAVE PRESET N" and the release sends nothing, SAVE submits `tgt-preset.save {wname, index}`
  (an empty name is refused inline), CLEAR sends `tgt-preset.delete {index}` and is disabled on an
  empty slot, CANCEL / Escape close, Enter in the entry saves; the four dock buttons send `tgt.reset` / `tgt.clear` / `tgt.clear-datalink` /
  `tgt.clear-stale`; a filter tap sends `tgt.set` and a hold `tgt.only`; header/row column offsets
  are identical with the scrollbar showing; the dock and toggle stay in view at 900×900, 500×640
  and 500×330 (a half-width pane wraps the dock labels rather than clipping them).

## Still needs an in-game check

- **WPN name.** A live shot's `definition.unitName` reads sensibly in the column and matches the
  weapon fired; with two weapons on one target, WPN follows the smaller TTI.
- **Save into a non-current slot.** Hold a card that isn't lit, save, and confirm the game applies
  the save to that slot (the preset file under `BepInEx/config/` gains it) and the card lights.
- **Recall after a save from another browser or a KEY-page bind** moves the lit card within about a
  second (the telemetry `preset` change → refetch path).
- **CLEAR DATALINK / CLEAR STALE counts** match what each button actually drops.
- The PAD cursor taps and holds a preset card, and the designation dock still answers ADD /
  REPLACE / DISMISS.

## Not covered

- `man/images/TGT.png`, `TGT_DETAILED.png` and the two `TGT_TD_DOCK*.png` show the previous layout.
- The PAD cursor can press the dialog's three buttons, but typing a name still needs a keyboard.
