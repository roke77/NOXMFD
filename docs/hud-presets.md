# HUD presets — [issue #50](https://github.com/roke77/NOXMFD/issues/50) follow-up

**Status:** merged to `main`, not yet in-game tested. The plugin's own build (`dotnet build -c Release`, 0
errors) and the harness (`tools/serve_web.py`) both exercise the full save/load/rename/delete
round-trip; applying a preset onto the live `HUDOptions` singleton itself is only testable in game.

## What it is

Up to 5 named presets of the HUD page's own filters (every category/vehicle/building toggle),
saved server-side so any connected browser can save or load one — not tied to a single browser's
own state, unlike a plain client-side save. Fixed numbered slots (**PRESET 1** through **PRESET
5**), not an arbitrary create/delete list like [SAVE/LOAD LAYOUT](layout-save-load.md): a slot
always exists, only its name/data start empty and can be cleared back to empty.

- A bottom bar on [HUD](../man/hud.md) reads **PRESET N: name** (whichever slot is current) plus
  **SAVE**/**LOAD** buttons.
- **SAVE** opens a name prompt; submitting captures the page's current live filters into the
  current slot under that name.
- **LOAD** opens a list of all 5; picking one applies it and makes it current, a pencil renames a
  slot in place, and **×** clears one back to empty.
- 5 new keybinds (KEY page, **HUD PRESETS** section) recall a preset directly — real binds (both
  keyboard and joystick/HOTAS), unlike SAVE/LOAD LAYOUT's keyboard-only pair, since there's no
  modal to pop here: pressing one is the whole action.

This is a distinct feature from [HUD filter automation on combat mode](radar-master-arms.md#hud-filter-automation-on-combat-mode-issue-50):
that one ties the HUD's own *built-in* NAV/GUN/A2A/A2G/EW/LOG mode presets to weapons mode; these 5
are the player's *own* named presets, saved/loaded independently of combat mode entirely. They
share only the same underlying `HUDOptions` fields to read/write, plus one cross-feature detail
noted below.

## Design decisions

- **Server captures its own live state — no client-supplied blob.** Unlike `LayoutStore` (whose
  `data` is opaque JSON the browser produced and the server just stores), HUD filter state is game
  state the plugin already reads/writes directly (`HUDOptions.listCategories`/`listVehicleTypes`/
  `listBuildingTypes`). `preset.save` carries only a name; the server snapshots its own state, the
  same three arrays [`HudCombatModeFilters`](radar-master-arms.md) already snapshots for its own
  idle baseline.
- **SAVE always targets the current slot, never a client-picked index.** "Current" is plain
  in-memory state (`HudPresetStore`'s own `_current`, default 1) — a UI selection, not saved data,
  so it isn't persisted and resets to 1 each session (the same reasoning `ImmersionState.CombatMode`
  resetting each spawn already established). Loading a preset (keybind or the LOAD list's `onPick`)
  is what changes it; the wire protocol only ever needs a name for save and a slot index for
  rename/delete/load.
- **The raw filter arrays never leave the server.** `/hud-options`'s new `preset` field and the
  dedicated `/hud-presets` endpoint both carry only `{index, name, hasData}` per slot — a browser
  picks a preset by index, and `preset.load` applies the arrays straight into `HUDOptions` on the
  plugin side. There's no reason to ship 7+10+7 booleans down to a page that only ever displays a
  name and forwards a click.
- **Re-pressing an already-current, empty slot still "selects" it.** `LoadPreset` always updates
  `_current`, even when the slot has no data yet — so a pilot can press "preset 3" then SAVE into
  it without ever having loaded data there first (`HudPreset.HasData` gates only whether the live
  HUD actually changes, not whether the slot becomes current).
- **Loading a preset counts as a player-driven HUD edit for the *other* feature's baseline.**
  `LoadPreset` calls `HudCombatModeFilters.CaptureIfIdle()` after applying — while combat mode is
  idle, loading a preset updates that feature's own idle baseline too, so it isn't silently
  discarded the next time A/A or A/G exits back to idle. The two features stay otherwise
  independent; this is the one place they touch.
- **Reused `LayoutModal` rather than building a new modal system.** `src/web/shell/layout-modal.js`
  already generalized past its "layout" name — `prompt`/`pickList` take no layout-specific
  arguments — so `hud.html` just also loads it (`/assets/shell/layout-modal.css`/`.js`) and calls
  the same two functions SAVE/LOAD LAYOUT use. One addition was needed: an optional `item.display`
  field on a `pickList` row (falls back to `item.name` — every existing caller is unaffected), so
  the LOAD list can show "PRESET N: name" without that prefix leaking into the rename input, which
  edits the raw `item.name`.

## What is built

| File | What |
|---|---|
| [`src/plugin/Stores/HudPresetStore.cs`](../src/plugin/Stores/HudPresetStore.cs) | The 5-slot library: `Save`/`Rename`/`Delete`/`LoadPreset`, persisted to `com.roque.NOXMFD.hud-presets.json`. `SelfCheck()` round-trips the disk JSON — the one pure, non-game-object-dependent slice, same reasoning as `JsonLite.SelfCheck`. |
| [`src/plugin/CommandDispatcher.cs`](../src/plugin/CommandDispatcher.cs) | `preset.save` / `.rename` / `.delete` / `.load` — `wname` for a name, `index` for a slot number 1-5. |
| [`src/plugin/Http/TelemetryServer.cs`](../src/plugin/Http/TelemetryServer.cs) | `RefreshHudOptions` gained a `preset:{index,name}` field; new `GET /hud-presets` serves the full 5-slot summary for the LOAD picker. |
| [`src/plugin/Input/Keybinds.cs`](../src/plugin/Input/Keybinds.cs) | 5 `DefFree` binds (**HUD Preset 1**-**5**), section `HUD Preset Keybinds` → displayed as **HUD PRESETS**. |
| [`src/web/pages/hud/hud.html`](../src/web/pages/hud/hud.html), [`hud.js`](../src/web/pages/hud/hud.js), [`hud.css`](../src/web/pages/hud/hud.css) | The bottom bar, SAVE/LOAD wiring, `fetchPresetItems` (on-demand `/hud-presets` fetch for the LOAD list only — the bottom label rides the existing `/hud-options` poll). |
| [`src/web/shell/layout-modal.js`](../src/web/shell/layout-modal.js) | `item.display` addition (backward compatible). |
| [`tools/serve_web.py`](../tools/serve_web.py) | Stateful mock (`PRESETS`/`PRESET_STATE`), same shape as `LAYOUTS` — the name/list/rename/delete/current-slot machinery is fully exercised in the harness; the actual filter values behind a slot are not (there's no stateful `HUDOptions` mock, same pre-existing gap `hud.set`/`hud.mode` already have there). |

## Verification performed

- `dotnet build -c Release --no-incremental` — 0 errors.
- Full harness round-trip (`serve_web.py`): SAVE opens the prompt, submitting stores the name
  server-side and updates the bottom label; LOAD lists all 5, rename and delete both work in place
  without closing the modal, and picking a slot updates the current index + label.
- KEY page renders all 5 new binds under a **HUD PRESETS** section with full key/joystick capture
  UI, same as any other ordinary bind.
- Full `*.test.js` suite passes (no new JS test — the new client code is DOM/fetch glue, same
  category as the rest of `hud.js`, which carries none either).

## Open questions

- None outstanding. The two features' one shared touchpoint (`CaptureIfIdle` on preset load) is a
  deliberate design decision, not an open question — see above.
