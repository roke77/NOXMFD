# Layout preset keybinds

## Goal

[Issue #90](https://github.com/roke77/NOXMFD/issues/90): load a specific saved layout from one
key or joystick button, instead of opening LOAD LAYOUT and picking it
([layout-save-load.md](layout-save-load.md)).

## Model: five shared slots, by position

LOAD LAYOUT only ever lists the layouts saved from the browser's own view (`layout-keydown.js`
filters `/layout-options` by `shell`), so a CLASSIC browser and an F-35 browser see two different
lists. The slots are therefore **positional and shared**: **Layout N** loads the Nth layout in
LOAD LAYOUT's list for whichever view the receiving browser shows. One key works in both views.

Consequences of binding a slot to a position rather than to one layout:

- Deleting a layout moves the ones below it up, and each takes over the key of the slot it lands
  in. Renaming changes nothing.
- A slot with no layout at that position does nothing.
- Only the first five rows of LOAD LAYOUT's list get a keybind box. Layouts past the fifth can't be
  bound.

## Registry rows

`Keybinds.cs` registers `layout-preset-1` … `layout-preset-5` (section **Layout Preset Keybinds**,
`KeybindConflict.LayoutSlotCount`) as ordinary `DefFree` rows: keyboard key plus joystick/HOTAS
button, persisted in the `.cfg` like every other bind and listed on the KEY page. The id is written
as a literal `"layout-preset-" + p` so `tools/keybinds_source.py` can read it for the preview
harness.

## Which browser loads

| Press | Path | Loads in |
|---|---|---|
| Key, browser focused | `layout-keybinds.js` `match()` → `'slot-N'` → `layout-keydown.js` `loadSlot(n)` | that browser only |
| Joystick button, or key while the game window has focus | `Keybinds.Poll()` → `DriveFree` → `TelemetryServer.MapAction("layout-preset-N")` → the SOI browser's `telemetry-source.js` posts `map-act` → the shell's `map-act` handler → `loadSlotAct(act)` → `loadSlot(n)` | the browser holding SOI; none if nothing holds SOI |

`map-act` normally goes to the focused page (`focusedCursorWindow()`). The shells (`mfd.js`,
`f35.js`) intercept `layout-preset-N` first, because a layout belongs to the whole shell, not to
the focused pane or portal.

The slots have no case in `remote-keybinds.js`. A key pressed in a browser is already handled in
that same browser, so there's nothing to relay. The coverage test
(`remote-keybinds-bind-coverage.test.js`) doesn't see loop-registered ids, the same as the TGT
preset slots.

## Setting a slot from LOAD LAYOUT

`layout-modal.js` `pickList` takes an `opts.rowExtra(item, index)` hook. `layout-keydown.js` uses
it to put `LayoutKeybinds.slotBox(n)` on rows 1-5. The box:

- shows the slot's key and/or joystick button (`F2 · J1 B7`, `ALT+1`), or `—`;
- on click, listens for the next key **and** arms the plugin's joystick capture
  (`keybind.arm-joy`), whichever comes first wins;
- takes Ctrl/Alt/Shift chords the same way as the KEY page (`KeybindsKeymap.captureStep`: it
  listens on keyup too, and shows `ALT+…` while only modifiers are held);
- Esc cancels and a bare Delete/Backspace clears both. Its keydown listener is window capture-phase, so it
  runs before the modal's document-level Escape (which would close the modal) and before the
  shell's own keydown matcher (which would fire the key's current action);
- stops listening once the config push shows the joystick capture ended (captured, refused, or
  moved to another bind), or when the modal closes.

It writes through the same `keybind.*` commands as the KEY page, so a change in either place shows
up in the other over the existing `keybinds-config-push`.

## Duplicate keys are refused

`KeybindConflict.cs` (pure, unit-tested in `KeybindConflictTests.cs`): when a Layout Preset slot is
on either side, a key or joystick button another bind already uses is refused. That covers giving a
slot a taken key and giving another bind a slot's key. Joystick number 0 ("any device") overlaps
every pinned device. `SetKeyBind` checks before writing. `CaptureJoyButton` checks before writing
and disarms on a refusal. The previous value stays.

Each refusal is recorded in `/keybinds-config`'s `rejected: {seq, bind, by}` (`by` is the other
bind's label), and `seq` goes up by one per refusal. The picker box and the KEY page show
`USED BY <LABEL>` in the cell that asked. The KEY page only shows it for a bind it assigned itself.

Binds outside the Layout Preset section still allow a shared key, as before. The `ponytail:`
comment in `KeybindConflict.cs` names the one-line change that would apply the rule to every bind.

## Verification

- `KeybindConflictTests.cs`: slot vs other bind in both directions, non-slot sharing still allowed,
  reassigning a slot its own key, joystick device overlap.
- `layout-keybinds.test.js`: slot keys match `'slot-N'`, unbound slots never match, config push
  updates slot keys.
- serve_web harness (`tools/serve_web.py` seeds six CLASSIC layouts and mirrors the refusal rule):
  - rows 1-5 get boxes and row 6 doesn't;
  - setting F1 works, and G (used by Flares) shows `USED BY FLARES`;
  - Esc cancels the capture without closing the modal;
  - F1/F2 load CLASSIC layouts 1/2;
  - a `map-act` for `layout-preset-4`/`-1` from the map iframe loads those layouts;
  - on F-35, slot 1 loads the F-35 layout and slot 3 (empty) does nothing.
- Not yet verified in game: a real joystick press reaching the SOI browser.
