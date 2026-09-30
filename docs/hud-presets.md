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

- [HUD](../man/hud.md) shows the 5 slots as **preset cards** under a **PRESETS (hold to save)**
  heading (layout in [hud-rework.md](hud-rework.md)). The lit card is the current slot; an empty
  slot reads **EMPTY** with a dashed border.
- **Tap** a card to apply that slot and make it current.
- **Hold** a card to open the **SAVE PRESET** dialog: **SAVE** captures the page's current live
  filters into that slot under the typed name (an empty slot included), **CLEAR** empties the slot
  back to empty, **CANCEL** closes it. Saving into a slot makes it current.
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
- **SAVE targets the named slot, or the current one when no index is sent.** "Current" is plain
  in-memory state (`HudPresetStore`'s own `_current`, default 1) — a UI selection, not saved data,
  so it isn't persisted and resets to 1 each session (the same reasoning `ImmersionState.CombatMode`
  resetting each spawn already established). `preset.save {wname, index}` saves into that slot and
  makes it current (the card-hold path, like `tgt-preset.save`); without `index` it saves into
  whichever slot is current. Loading a preset (keybind or a card tap) also changes it.
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
- **The cards and their dialog are shared with TGT.** `src/web/services/preset-cards.js` builds
  the five cards and the SAVE PRESET dialog for both pages, taking the endpoint (`/hud-presets` /
  `/tgt-presets`) and the command prefix (`preset` / `tgt-preset`); the styling is
  `src/web/shared/lit-panel.css`. The page keeps its own tap/hold arbitration and PAD-cursor routing.
  Names are edited by saving over a slot (hold, retype, SAVE); `preset.rename` remains a command
  but the page no longer sends it.
- **Fixed-slot/summary-JSON/persistence plumbing shared with `TgtPresetStore` via a small
  `PresetSlots` helper** (`src/plugin/Stores/PresetSlots.cs`) — see [TGT presets](tgt-presets.md)
  for the rest of the reasoning; only the game-specific capture/apply logic stayed in each store.
- **`Save`/`Rename` reject a whitespace-only name.** They used to check `string.IsNullOrEmpty(name)`
  before trimming, so a name of all spaces passed validation and was stored empty; both now check
  the *trimmed* result's length instead.

## What is built

| File | What |
|---|---|
| [`src/plugin/Stores/HudPresetStore.cs`](../src/plugin/Stores/HudPresetStore.cs) | The 5-slot library: `Save`/`Rename`/`Delete`/`LoadPreset`, persisted to `com.roque.NOXMFD.hud-presets.json`. `SelfCheck()` round-trips the disk JSON — the one pure, non-game-object-dependent slice, same reasoning as `JsonLite.SelfCheck`. |
| [`src/plugin/CommandDispatcher.cs`](../src/plugin/CommandDispatcher.cs) | `preset.save` / `.rename` / `.delete` / `.load` — `wname` for a name, `index` for a slot number 1-5 (optional on `save`). |
| [`src/plugin/Http/TelemetryServer.cs`](../src/plugin/Http/TelemetryServer.cs), [`ConfigEndpoint.cs`](../src/plugin/Http/ConfigEndpoint.cs) | `RefreshHudOptions` gained a `preset:{index,name}` field; `GET /hud-presets` serves the full 5-slot summary for the preset cards. |
| [`src/plugin/Input/Keybinds.cs`](../src/plugin/Input/Keybinds.cs) | 5 `DefFree` binds (**HUD Preset 1**-**5**), section `HUD Preset Keybinds` → displayed as **HUD PRESETS**. |
| [`src/web/pages/hud/hud.html`](../src/web/pages/hud/hud.html), [`hud.js`](../src/web/pages/hud/hud.js), [`hud.css`](../src/web/pages/hud/hud.css) | The preset section and dialog markup, the tap/hold arbitration and the PAD-cursor routing; `data.preset` (from `/hud-options`) drives the cards through `sync`. |
| [`src/web/services/preset-cards.js`](../src/web/services/preset-cards.js), [`preset-cards.test.js`](../src/web/services/preset-cards.test.js), [`src/web/shared/lit-panel.css`](../src/web/shared/lit-panel.css) | The shared cards, SAVE PRESET dialog and styling (also [TGT](tgt-presets.md)); the test drives recall, SAVE / CLEAR / CANCEL and the refetch rule. |
| [`src/plugin/Stores/PresetSlots.cs`](../src/plugin/Stores/PresetSlots.cs), [`tools/tests/PresetSlotsTests.cs`](../tools/tests/PresetSlotsTests.cs) | Shared BCL-only plumbing (slot creation, summary JSON, name validation, rename/delete, disk persistence) used by both `HudPresetStore` and `TgtPresetStore` — linked directly into `NOXMFD.Tests.csproj` via the same `LogWarning` seam `RouteStore.cs` uses. |
| [`tools/serve_web.py`](../tools/serve_web.py) | Stateful mock (`PRESETS`/`PRESET_STATE`), same shape as `LAYOUTS`; `preset.save` honours `index` — the name/list/rename/delete/current-slot machinery is fully exercised in the harness; the actual filter values behind a slot are not (there's no stateful `HUDOptions` mock, same pre-existing gap `hud.set`/`hud.mode` already have there). |

## Verification performed

- `dotnet build -c Release --no-incremental` — 0 errors.
- Harness round-trip (`serve_web.py`): tap sends `preset.load {index}`; hold opens "SAVE PRESET N"
  and the release sends nothing; SAVE sends `preset.save {wname, index}` (an empty slot included)
  and the card takes the name and lights; CLEAR sends `preset.delete {index}`; leaving the card or
  a pointer cancel drops a pending hold; Enter / Space on a focused card taps and holds the same way.
- KEY page renders all 5 new binds under a **HUD PRESETS** section with full key/joystick capture
  UI, same as any other ordinary bind.
- Full `*.test.js` suite passes, including `preset-cards.test.js`.

## Open questions

- None outstanding. The two features' one shared touchpoint (`CaptureIfIdle` on preset load) is a
  deliberate design decision, not an open question — see above.
