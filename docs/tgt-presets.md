# TGT presets — [issue #78](https://github.com/roke77/NOXMFD/issues/78)

**Status:** planning. Nothing implemented yet — this is the design doc the implementation follows.

## What it is

Up to 5 named presets of the [TGT](../man/tgt.md) page's own filters (faction, category, vehicle
type toggles, plus the LASER and HUD mode buttons), saved server-side so any connected browser can
save or load one — not tied to a single browser's own state. Fixed numbered slots (**PRESET 1**
through **PRESET 5**), not an arbitrary create/delete list: a slot always exists, only its
name/data start empty and can be cleared back to empty.

This is the same feature as [HUD presets](hud-presets.md), applied to TGT's filter set instead of
HUD's. That implementation is the template — same slot model, same UI pattern, same wire shape —
swapping HUD's filter fields for TGT's.

## UI

- A bottom bar on TGT reads **PRESET N: name** (whichever slot is current) plus **SAVE**/**LOAD**
  buttons, same placement/style as HUD's.
- **SAVE** opens a name prompt (reuse `src/web/shell/shared/layout-modal.js`'s `prompt`);
  submitting captures the page's current live filters into the current slot under that name.
- **LOAD** opens a list of all 5 (reuse `layout-modal.js`'s `pickList`, with `item.display` for the
  "PRESET N: name" label); picking one applies it and makes it current, a pencil renames a slot in
  place, and **×** clears one back to empty.
- 5 new keybinds (KEY page, **TGT PRESETS** section) recall a preset directly — real binds
  (keyboard and joystick/HOTAS), same as **HUD Preset 1**-**5**.

## What a preset captures

The live state of `TargetListSelector` (the game singleton TGT's `tgt.set`/`tgt.only`/`tgt.reset`
commands already drive — see `CommandDispatcher.cs`'s `TgtSet`/`TgtResolve`/`TgtGroup`):

- `toggleFactionItems` — bool per entry (`.status`)
- `toggleUnitTypesItems` — bool per entry (`.status`)
- `toggleVehicleTypesItems` — bool per entry (`.status`)
- `toggleLaser` — single bool (`.status`)
- `toggleFollowHUD` — single bool (`.status`)

Same shape as `HudPresetStore`'s `Categories`/`Vehicles`/`Buildings` bool arrays, plus two extra
scalar bools for laser/HUD mode.

## Design decisions (mirror HUD presets)

- **Server captures its own live state — no client-supplied blob.** `preset.save` carries only a
  name; the server snapshots `TargetListSelector`'s live toggle state itself, the same way
  `HudPresetStore.Save` snapshots `HUDOptions`.
- **SAVE always targets the current slot, never a client-picked index.** "Current" is plain
  in-memory state (default 1), not persisted, resets to 1 each session. Loading a preset (keybind
  or the LOAD list's `onPick`) is what changes it.
- **The raw filter arrays never leave the server.** The new store's summary JSON (folded into
  TGT's existing telemetry payload, plus a dedicated `/tgt-presets` endpoint for the LOAD picker)
  carries only `{index, name, hasData}` per slot — a browser picks a preset by index, and
  `preset.load` applies the arrays straight into `TargetListSelector` on the plugin side.
- **Re-pressing an already-current, empty slot still "selects" it.** `LoadPreset` always updates
  the current index, even when the slot has no data yet, so a pilot can press "preset 3" then SAVE
  into it without ever having loaded data there first.
- **A distinct command namespace from HUD's.** TGT presets need their own `preset.*` commands
  (or a `tgt-preset.*` namespace if `preset.*` is reused, to disambiguate from `HudPresetStore`) —
  decide naming during implementation so `CommandDispatcher` can route to the right store.

## What needs to change (mirrors HUD presets' file list)

| File | What |
|---|---|
| `src/plugin/Stores/TgtPresetStore.cs` (new) | The 5-slot library: `Save`/`Rename`/`Delete`/`LoadPreset`, persisted to its own `com.roque.NOXMFD.tgt-presets.json`. `SelfCheck()` round-trips the disk JSON, same as `HudPresetStore.SelfCheck`. |
| `src/plugin/CommandDispatcher.cs` | New `preset.*` (or namespaced) handlers for save/rename/delete/load, routed to `TgtPresetStore` instead of `HudPresetStore`. |
| `src/plugin/Http/TelemetryServer.cs`, a TGT HTTP endpoint | TGT's existing state payload gains a `preset:{index,name}` field; a new `GET /tgt-presets` serves the full 5-slot summary for the LOAD picker. |
| `src/plugin/Input/Keybinds.cs` | 5 `DefFree` binds (**TGT Preset 1**-**5**), new section `TGT Preset Keybinds` → displayed as **TGT PRESETS**. |
| `src/web/pages/tgt/tgt.html`, `tgt.js`, `tgt.css` | The bottom bar, SAVE/LOAD wiring, an on-demand preset-list fetch for the LOAD list only. |
| `tools/serve_web.py` | Stateful mock, same shape as HUD presets' `PRESETS`/`PRESET_STATE`. |
| `man/tgt.md`, `man/keybinds.md` | Document the bottom bar and the new **TGT PRESETS** keybind section. |

## Verification (once implemented)

- `dotnet build -c Release` — 0 errors.
- Full harness round-trip (`tools/serve_web.py`): SAVE opens the prompt, submitting stores the name
  server-side and updates the bottom label; LOAD lists all 5, rename and delete both work in place,
  picking a slot updates the current index + label.
- KEY page renders all 5 new binds under a **TGT PRESETS** section with full key/joystick capture
  UI, same as any other ordinary bind.
- Full `*.test.js` suite green.
- In-game: saving/loading a preset actually applies the faction/category/vehicle/laser/HUD toggles
  onto the live TGT filter panel.
