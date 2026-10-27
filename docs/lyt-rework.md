# LYT rework

## Goal

LYT (the layout manager, reached from CFG) is a chooser drawn only on the bezel's left keys today.
The rework gives it a real page, `/lyt`, in the CLASSIC shell's `#page-frame`, built up from the
three alternatives in `_scratch/claude_designs/AVN page alternatives full.html`:

- **LYT B · Gallery** — the LAYOUTS title and the CLASSIC / F-35 cards.
- **LYT A · Inline Manager** — the SAVED rows: LOAD, rename, delete and the Layout 1-5 key box
  beside each saved layout.
- **LYT C · Builder** — not started.

## Step 1 (this branch)

`src/web/pages/lyt/`: the LAYOUTS title with the two shell cards, then the SAVED section.

- **Cards.** CLASSIC is the shell hosting the page, so it is lit. A tap asks the shell to switch
  (`{mfd:true, type:'lyt-act', act:'classic'|'f35'}` → `mfd.js` `chooseShell`). The page is full view only and has no F-35 counterpart: the
  F-35 keeps its own picker.
- **KEYBINDS.** Two entries side by side, SAVE and LOAD: the `layout-save` / `layout-load` binds
  (keyboard only), set through `LayoutKeybinds.slotBox('save'|'load')`. It listens for a key only
  and shows `USED BY <bind>` when another bind holds it.
- **SAVE LAYOUT key (CLASSIC).** Opens the layout dialog (`shell/shared/layout-edit-dialog.js`, used by
  both the shell and this page; its look is `shared/preset-dialog.css`, split out of `lit-panel.css`
  because the bezel uses `.lit` for its key flash). Top to bottom: a name entry, a `KEY FOR THIS
  LAYOUT` box, one lit toggle per pane of the current screen (starting from the display's live SOI
  membership), a message, and the five slots as rows (`pages/lyt/lyt-row.js`, `lyt-rows.css`, shared
  with this page) each with a black-and-green radio button. The green button follows the chosen slot:
  a taken slot reads REPLACE and writes the current arrangement over that layout through
  `layout.update` (same id and position, so it keeps its Layout N key); the first empty slot reads SAVE
  and stores a new layout through `layout.save` (preselected when one exists). Later empty slots are
  shown dimmed and not selectable: the library is a list that appends, so a slot can't be skipped to.
  With no name or no selection the entry shows an error and the dialog stays open.
  The key box only records a keyboard key or chord (`LayoutKeybinds.pendingKeyBox`); on submit it is
  bound to the chosen slot (`setSlotKey`, which also clears that slot's joystick button once
  accepted). A key another bind already holds is refused by the plugin and the slot keeps its old key.
  The F-35 shell keeps its name prompt.
- **LOAD LAYOUT key (CLASSIC).** Opens the SAVED section as a popup (`shell/shared/layout-load-dialog.js`):
  the same rows as this page, built by the same code (`LytRow.renderList`: key box, edit, delete,
  LOAD, then the empty slots). LOAD closes the popup and applies the layout (`loadById`); edit opens the
  layout dialog above it; the list refreshes after an edit or delete. Escape closes the top dialog
  only. It replaces the old name-list picker and its live SOI checkboxes on CLASSIC (the per-layout
  SOI toggles in the edit dialog take their place); the F-35 shell keeps the old picker.
- **Plugin unreachable.** `LayoutStore.list()` resolves `{layouts: [], failed: true}` when the plugin
  can't be reached. The save dialog then shows a notice and a CLOSE button instead of an empty library to
  save into, the load popup shows the same message instead of rows, and the LYT page keeps the rows
  it has.
- **Save tip.** One line between KEYBINDS and SAVED: set up the screen, then press the SAVE key.
- **SAVED rows.** The CLASSIC layouts from `/layout-options`, in list order. Position is the Layout
  N keybind slot (`docs/layout-preset-keybinds.md`), so rows 1-5 carry a key box and later rows say
  NO KEY SLOT.
  - The key box is `LayoutKeybinds.slotBox`, the same control LOAD LAYOUT shows: click it, press a
    key or joystick button, and it shows the bound key (`F1`, `F1 · J1 B7`); unbound reads `SET KEY`. A slot
    holds a key or a joystick button, never both.
  - LOAD posts `{act:'load', id}`; the shell applies the layout (`loadById` in
    `layout-keydown.js`).
  - The pencil opens an edit dialog in the TGT/HUD SAVE PRESET style (`shared/preset-dialog.css`): the
    layout's number in the title, its name in the text entry, and one lit toggle per pane (TOP/BOTTOM
    or LEFT/RIGHT for a split, FULL VIEW for F_VIEW) for its place in the SOI rotation. SAVE writes
    the new name and the arrangement's `soi: [bool, …]` together through
    `layout.update` (`LayoutStore.UpdateLayout`: same id, same list position). Loading a layout
    that carries `soi` sends `soi.include` for each pane (`applyLayoutState` in `mfd.js`); one
    without leaves the current membership alone. Layouts saved from the bezel carry no `soi`.
  - A row's thumbnail draws each pane green, or gray when the layout leaves it out of the SOI
    rotation.
  - Delete calls `LayoutStore.remove`.
  - The list refetches when `LayoutStore` reports a change (`layouts-changed`, posted to the
    shell's iframes). The plugin applies a command on the next main-thread drain, so the page waits
    300 ms after the change before it reads.
- Saving is the keyboard shortcut (configured on KEY).
- `lyt-slots.js` (pure, `lyt-slots.test.js`) turns a saved layout into the row's split name, pane
  rectangles and page names.

The bezel's only label on this page is MAIN (top of the left bank), back to MAIN. CLASSIC, F-35,
SAVE and LOAD are gone from the bezel: the cards and rows replace the first, second and fourth, and
saving is the keyboard shortcut only.

## Step 2 — F-35 (issue #111)

- **LYT in an F-35 portal.** The F-35's own chooser (two cards plus SAVE/LOAD buttons over the whole
  glass) is gone. LYT in the CFG group opens `/lyt?f35` as an ordinary portal page
  (`LayoutPages.F35.lyt`); `?f35` marks the F-35 card and lists the F-35 layouts. The page's
  `lyt-act` messages route to the portal that sent them: the F-35 card sends that portal back to
  MAIN, CLASSIC goes to `/`, LOAD rebuilds the glass (`loadById`).
- **Labels.** The CFG switch on LYT (MAIN/HUD/KEY/LYT/UI, LYT marked) moved from the bezel's
  `BEZEL_EXTRAS.lyt` into a shared `NAV.lyt`, so both layouts draw the same labels.
- **F-35 rows.** `LytSlots.describeLayout` reads the F-35 arrangement (`{cells, pages}`): one
  full-height column per portal, as wide as its span, labelled `N PORTALS`; cells the glass can't
  take read as four single portals, as `applyLayoutState` rebuilds them. Loading an F-35 layout
  applies its `soi` flags like CLASSIC.
- **Row wrapping.** A row's key box, edit, delete and LOAD form one group that wraps below the text
  when the row is narrow (a one-slot portal), so the name and pages keep the width.
- **Narrow portals.** The LAYOUTS cards and the SAVE/LOAD key boxes stack when a column would be under
  220px, and LYT's labels stand upright like the other CFG pages' (`UPRIGHT_LABELS`).
- **Thumbnails.** On the F-35 (`body[data-shell="f35"]`, set by `lyt.js`) a row's thumbnail is
  100×58 rather than CLASSIC's 58×58 square. In both shells a pane's page name is centred and breaks
  onto centred lines when wider than its pane, instead of being clipped.
- **MENU.** The strip's EDIT, WAKE and FULLSCREEN buttons fold into one MENU popover, which also
  carries SAVE LAYOUT, LOAD LAYOUT (the F-35's existing dialogs, for a screen with no keyboard), PIN
  and SWAP. PIN pins the whole glass (`LayoutPin` learns `{cells, pages}`), is lit while the glass
  matches the pin, and rides in saved layouts and the session state; SWAP's cycle is
  `LayoutPin.swapStep`, shared with the bezel.

## Live-game checks

- In-game: LOAD from a row applies the layout; a joystick button set through a row's key box loads
  that layout from any page.
- In-game: a layout saved with a pane left out of SOI rotation skips it in SOI Next/Prev after LOAD;
  the plugin build with `layout.update` is needed for the dialog's SAVE.
- In-game (F-35): a layout whose portals were toggled out of SOI on the LYT page's edit dialog skips
  them in SOI Next/Prev after LOAD.

## Not built yet

- The builder (LYT C) and LYT A's ON SCREEN NOW card.
- The F-35 shell still uses the live SOI checkboxes in its LOAD LAYOUT picker.
- A page PAD cursor for HOTAS use.
