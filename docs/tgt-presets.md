# TGT presets — [issue #78](https://github.com/roke77/NOXMFD/issues/78)

**Status:** built, not yet in-game tested. The plugin's own build (`dotnet build -c Release`, 0
errors), the full `*.test.js`/`dotnet test` suite, and the harness (`tools/serve_web.py`) all
exercise the full save/load/rename/delete round-trip; applying a preset onto the live
`TargetListSelector` singleton itself is only testable in game.

## What it is

Up to 5 named presets of the [TGT](../man/tgt.md) page's own filters (faction, category, vehicle
type toggles, plus the LASER and HUD mode buttons), saved server-side so any connected browser can
save or load one — not tied to a single browser's own state. Fixed numbered slots (**PRESET 1**
through **PRESET 5**), not an arbitrary create/delete list: a slot always exists, only its
name/data start empty and can be cleared back to empty.

This is the same feature as [HUD presets](hud-presets.md), applied to TGT's filter set instead of
HUD's — same slot model, same UI pattern, same wire shape, swapping HUD's filter fields for TGT's.

- A bar on [TGT](../man/tgt.md#presets), between the header separator and the filter group (same
  placement as HUD's own), reads **PRESET N: name** (whichever slot is current, in `--no-label`
  white) plus solid `--no-green` **SAVE**/**LOAD** buttons — styled identically to HUD's own preset
  bar (`hud.css`), not TGT's dashed DATALINK/STALE mod-only accent.
- **SAVE** opens a name prompt; submitting captures the page's current live filters into the
  current slot under that name.
- **LOAD** opens a list of all 5; picking one applies it and makes it current, a pencil renames a
  slot in place, and **×** clears one back to empty.
- 5 new keybinds ([KEY](../man/keybinds.md#tgt-presets) page, **TGT PRESETS** section) recall a
  preset directly — real binds (both keyboard and joystick/HOTAS), same as HUD Presets.

## What a preset captures

The live state of `TargetListSelector` (the game singleton TGT's `tgt.set`/`tgt.only`/`tgt.reset`
commands already drive — see `CommandDispatcher.cs`'s `TgtSet`/`TgtResolve`/`TgtGroup`):

- `toggleFactionItems` — bool per entry
- `toggleUnitTypesItems` — bool per entry
- `toggleVehicleTypesItems` — bool per entry
- `toggleLaser` — single bool
- `toggleFollowHUD` — single bool

Same shape as `HudPresetStore`'s `Categories`/`Vehicles`/`Buildings` bool arrays, plus two extra
scalar bools for laser/HUD mode.

## Design decisions

- **Server captures its own live state — no client-supplied blob.** Same as HUD presets:
  `tgt-preset.save` carries only a name; the server snapshots `TargetListSelector`'s live toggle
  state itself.
- **SAVE always targets the current slot, never a client-picked index.** "Current" is plain
  in-memory state (`TgtPresetStore`'s own `_current`, default 1) — a UI selection, not saved data,
  so it isn't persisted and resets to 1 each session.
- **The raw filter state never leaves the server.** The `tgt` telemetry block's new `preset` field
  and the dedicated `/tgt-presets` endpoint both carry only `{index, name, hasData}` per slot.
- **A distinct `tgt-preset.*` command namespace, not a reused `preset.*`.** `CommandDispatcher`
  needs to route to the right store; `tgt-preset.save`/`.rename`/`.delete`/`.load` disambiguate from
  `HudPresetStore`'s identically-shaped `preset.*` commands.
- **The preset label rides the existing `tgt` telemetry block, not a poll.** Unlike HUD (a
  `/hud-options` fetch every 1.2s), TGT's filter state already streams through
  `TelemetryJson.TgtBlock` at the normal telemetry rate — folding `preset:{index,name}` in there
  keeps the label live without adding a second poll loop. `TelemetrySnapshot` carries
  `TgtPresetIndex`/`TgtPresetName` (captured on the main thread in `TelemetryReader`, alongside
  every other `Tgt*` field) rather than reading `TgtPresetStore` directly during JSON build, since
  that build can run off the main thread.
- **Re-pressing an already-current, empty slot still "selects" it**, and **loading a preset skips
  a `.Set()` call when the toggle already matches** (mirrors `CommandDispatcher.TgtSet`'s own
  no-op guard) — both identical reasoning to `HudPresetStore`.
- **Reused `LayoutModal`**, exactly as HUD presets did — `tgt.html` just also loads
  `layout-modal.css`/`.js`.
- **The SAVE/LOAD/`fetchPresetItems` wiring itself moved into a shared `preset-bar.js`**
  (`src/web/shell/shared/`) rather than being copy-pasted into `tgt.js` a second time — `hud.js`'s
  own inline version became the first user of the extraction, taking a `getPreset`/`setPreset` pair
  so each page keeps owning its own state shape (`data.preset` vs `state.preset`).
- **Fixed-slot/summary-JSON/persistence plumbing shared with `HudPresetStore` via a small
  `PresetSlots` helper** (`src/plugin/Stores/PresetSlots.cs`) — the two stores' game-specific
  capture/apply logic stayed separate; only the identical bookkeeping around it (empty-slot
  creation, the `{current,presets:[...]}` summary JSON, name trimming/validation, rename/delete,
  disk persistence) moved into one place.
- **`Apply()` sets HUD-follow BEFORE the faction/category/vehicle arrays, not after.** The game's
  `toggleFollowHUD.Set()` fires `OnToggleFollowHUD`, which itself calls `SetFilters()` (turning on)
  or `ResetFilters()` (turning off) — either would silently clobber the just-restored filter arrays
  if applied afterward. Found by an external review of 0.43.0; needs an in-game regression check
  covering both HUD-follow transitions (off→on, on→off) before it can be marked verified.
- **Faction/category/vehicle toggles are also captured by their own current label**, alongside the
  existing positional bool array, so `Apply()` can reconcile by name instead of index. The vehicle
  list in particular is built at runtime from `Encyclopedia.i.vehicleTypes` — a game update that
  inserts or reorders a vehicle type would otherwise apply a saved value to the wrong toggle. A
  preset saved before this existed has no name array on disk, so `ApplyToggles` falls back to the
  original positional behavior for it; a toggle whose label isn't found in a newer preset's saved
  names (added since that preset was saved) is left at its own current value rather than forced to
  an unrelated saved one.
- **Both stores now reject a whitespace-only name.** `Save`/`Rename` used to check
  `string.IsNullOrEmpty(name)` before trimming, so a name of all spaces passed validation and was
  stored empty; both now check the *trimmed* result's length.

## What is built

| File | What |
|---|---|
| [`src/plugin/Stores/TgtPresetStore.cs`](../src/plugin/Stores/TgtPresetStore.cs) | The 5-slot library: `Save`/`Rename`/`Delete`/`LoadPreset`, persisted to `com.roque.NOXMFD.tgt-presets.json`. `SelfCheck()` round-trips the disk JSON. |
| [`src/plugin/Stores/PresetSlots.cs`](../src/plugin/Stores/PresetSlots.cs), [`tools/tests/PresetSlotsTests.cs`](../tools/tests/PresetSlotsTests.cs) | Shared BCL-only plumbing (slot creation, summary JSON, name validation, rename/delete, disk persistence) used by both `TgtPresetStore` and `HudPresetStore` — linked directly into `NOXMFD.Tests.csproj` via the same `LogWarning` seam `RouteStore.cs` uses. |
| [`src/plugin/CommandDispatcher.cs`](../src/plugin/CommandDispatcher.cs) | `tgt-preset.save` / `.rename` / `.delete` / `.load` — `wname` for a name, `index` for a slot number 1-5. |
| [`src/plugin/Telemetry/TelemetrySnapshot.cs`](../src/plugin/Telemetry/TelemetrySnapshot.cs), [`TelemetryReader.cs`](../src/plugin/Telemetry/TelemetryReader.cs), [`TelemetryJson.cs`](../src/plugin/Telemetry/TelemetryJson.cs) | `TgtPresetIndex`/`TgtPresetName` captured per frame; `TgtBlock` gained a `preset:{index,name}` field. |
| [`src/plugin/Http/ConfigEndpoint.cs`](../src/plugin/Http/ConfigEndpoint.cs), [`TelemetryHttpRouter.cs`](../src/plugin/Http/TelemetryHttpRouter.cs) | `GET /tgt-presets` serves the full 5-slot summary for the LOAD picker. |
| [`src/plugin/Input/Keybinds.cs`](../src/plugin/Input/Keybinds.cs) | 5 `DefFree` binds (**TGT Preset 1**-**5**), section `TGT Preset Keybinds` → displayed as **TGT PRESETS**. |
| [`src/plugin/Plugin.cs`](../src/plugin/Plugin.cs) | `TgtPresetStore.Load`/`.SelfCheck` wired into startup, next to `HudPresetStore`'s own. |
| [`src/web/pages/tgt/tgt.html`](../src/web/pages/tgt/tgt.html), [`tgt.js`](../src/web/pages/tgt/tgt.js), [`tgt.css`](../src/web/pages/tgt/tgt.css) | The bar markup/styling and the page-specific glue (`getPreset`/`setPreset` reading/writing `state.preset`). PAD-cursor `CURSORABLE` extended to include the two new buttons. |
| [`src/web/shell/shared/preset-bar.js`](../src/web/shell/shared/preset-bar.js) | The actual SAVE/LOAD/`LayoutModal`/`fetchPresetItems` wiring — shared with [HUD presets](hud-presets.md), extracted here since TGT presets made it a second identical copy rather than a one-off. |
| [`tools/serve_web.py`](../tools/serve_web.py) | Stateful mock (`TGT_PRESETS`/`TGT_PRESET_STATE`), same shape as `PRESETS`/`PRESET_STATE` — the name/list/rename/delete/current-slot machinery is fully exercised; the preset label itself stays static in the harness (see below). |
| [`tools/preview-mock.js`](../tools/preview-mock.js) | Static `preset: {index:1, name:''}` added to the `tgt` mock block, for a sensible standalone render. |
| [`man/tgt.md`](../man/tgt.md), [`man/keybinds.md`](../man/keybinds.md) | Document the preset bar and the new **TGT PRESETS** keybind section. |

## Verification performed

- `dotnet build -c Release` — 0 errors (same 18-warning baseline as before).
- Full `tools/ci-check.ps1` — build, every `*.test.js` file, 291 `dotnet test` cases, route smoke,
  all green.
- Full harness round-trip (`serve_web.py`, live browser check): opened `/tgt` standalone, injected a
  synthetic `'tgt'` message to populate the panel, then SAVE opened the prompt and `tgt-preset.save`
  correctly persisted the name into the current slot (confirmed via `GET /tgt-presets`); LOAD listed
  all 5, rename worked in place (an empty slot's pencil turns it into an inline input), and picking
  a slot both sent `tgt-preset.load` and moved the mock's own `current` index server-side.
- KEY page renders all 5 new binds under a **TGT PRESETS** section, right after **HUD PRESETS**,
  with full key/joystick capture UI, same as any other ordinary bind — confirmed via `get_page_text`.
- Unlike HUD presets, the preset label itself does **not** move in the harness in response to a
  command: TGT's filter state (`tgt` block) is a *static* client-side mock in `preview-mock.js`
  (there being no stateful `TargetListSelector` mock the way `_hud_options()` has one), not a
  server-polled endpoint the way `/hud-options` is — so `tgt-preset.load`/`.save` update the Python
  mock's state (verifiable via `/tgt-presets`) without a way to feed that back into the static `tgt`
  frame. This mirrors every other `tgt.*` write command's own existing limitation in this harness
  (`tgt.set`/`.only`/`.reset` don't visibly move the filter toggles either) — real application onto
  a live `TargetListSelector` is only testable in game regardless.

## In-game verification recipe (persist-by-name)

`ApplyToggles`'s by-name reconciliation touches the live `TargetListSelector` singleton, so it can't
be unit tested directly (`TgtPresetStore.SelfCheck` only covers the JSON round-trip of the name
arrays — see "Persist by name" above). A real `Encyclopedia.vehicleTypes` reorder isn't reproducible
on demand, but the same effect can be simulated without a game update:

1. Save a TGT preset in-game (any name).
2. Quit to the main menu (so the file isn't rewritten mid-edit) and open
   `com.roque.NOXMFD.tgt-presets.json` in `BepInEx/config/`.
3. In that preset's `"vehicle"` array, swap two boolean values' positions — **without** touching the
   parallel `"vehicleNames"` array. This reproduces exactly what a game update reordering
   `Encyclopedia.vehicleTypes` would do to an old save: the name at index *i* no longer matches the
   value that was originally captured for that name.
4. Reload the mission, open TGT, LOAD that preset.
5. Check the BepInEx log for the temporary `TGT preset apply (vehicle): by-name reconciliation, N
   saved names, 0 unmatched` line (`TgtPresetStore.ApplyToggles`), and confirm in the UI that the
   correct vehicle toggles — by label, not position — ended up in the states you originally saved,
   not the scrambled positional order from the edited file. A `positional fallback` line instead
   means the names array didn't parse (check the edit didn't break the JSON), not that reconciliation
   itself failed.

Remove `ApplyToggles`'s two temporary `LogInfo` calls once this is confirmed.

## Open questions

- None outstanding — same "no open questions" position as HUD presets, for the same reason: the
  one HUD-presets design decision that doesn't carry over 1:1 (`HudCombatModeFilters.CaptureIfIdle`
  after a load) has no TGT equivalent to touch, since TGT has no analogous combat-mode-driven
  filter automation feature.
