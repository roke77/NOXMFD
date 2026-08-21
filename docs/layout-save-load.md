# Save/load shell layout

## Status

Shipped on branch `feature/layout-save-load` (uncommitted). [Issue #51](https://github.com/roke77/NOXMFD/issues/51).

## The problem

`mfd.js`/`f35.js` reset the shell's arrangement to hardcoded defaults on every page load —
CLASSIC's `splitVariant`/`panePages` and the F-35's `portals` array are plain in-memory JS
variables. Only the top-level CLASSIC-vs-F-35 choice survives a reload, via one
`localStorage.setItem('layout', name)` call each shell makes. A pilot who splits the screen, or
merges F-35 portals into a favourite arrangement, rebuilds it by hand every session.

## Requirements

- **SAVE LAYOUT**: captures the active layout's arrangement and which page each pane/portal
  shows, prompts for a name via a modal, and stores it.
- **LOAD LAYOUT**: opens a modal listing every saved layout by name; picking one applies it
  immediately.
- Multiple named layouts coexist — a small library, not a single "remember my last state" slot.

## Design decisions

**Storage: server-side (`LayoutStore.cs`), not per-browser `localStorage`.** Same reasoning as
the waypoint-route fix (`docs/hud-waypoint-indicator.md`): a layout named on one browser has to
look the same on every other connected browser, so one process has to own the library. Unlike
`RouteStore.cs`, there's no live game state to interpret — a layout is just a named blob the
browser itself produces and will restore, so `LayoutStore` never parses CLASSIC's vs. F-35's
shape. Each saved layout is `{id, name, shell, data}`, where `data` is the browser's own
`JSON.stringify`'d arrangement carried as a plain string field (escaped like any other string),
the same shape `wpt.import`'s pasted blob already uses — re-emitting a nested JSON value read
back off disk would need a general JSON writer this codebase doesn't have (`JsonLite` is a
read-only parser by design), so a string field sidesteps that entirely. `SaveLayout` only checks
the blob parses as a JSON object; it never inspects further.

**Browser-side keyboard shortcuts, no joystick/HOTAS.** SAVE and LOAD act on whichever browser
window has keyboard focus — trivially the right one, no plugin-side routing needed, since a
physical joystick press is invisible to browser JS (only the plugin can see it, via Rewired) and
would need SOI-style routing to pick a browser. Deferred rather than built: revisit if HOTAS
support for these two actions is wanted later.

**The key itself is configurable and shared, not hardcoded.** Two new `Keybinds.cs` rows,
`layout-save`/`layout-load`, under a new **LAYOUT** section on the `/keybinds` page — but unlike
every other row there, they carry no joystick entry and no `Drive`/`DriveFree` dispatch
(`DefKeyOnly`, a new bind shape). The row exists purely so the assigned key is set once and
shared by every connected browser via `/keybinds-config`; the actual match happens in each
shell's own `keydown` listener (`layout-keybinds.js`), which fetches the configured key names and
compares a browser `KeyboardEvent` against them using the same `KeyboardEvent.code` → Unity
`KeyCode` naming the KEY page's own capture already uses (`keybinds-keymap.js`, shared rather
than duplicated). Default unbound, same as every other bind. Adding a bind shape with a key but
no joystick meant three call sites that had assumed "every digital bind has a joystick entry"
needed an explicit null-check instead (`Keybinds.Active`, the boot-time log loop, and
`TelemetryServer.ServeKeybindsConfig`'s JSON writer) — `key`/`joyButton`/`joyNum` are now
independently optional fields, not a pair that only ever appears together.

**One shared modal primitive.** No modal/dialog existed anywhere in `src/web/` before this — the
closest precedent was WPT's inline `editRow` (an in-place text-input swap, not an overlay).
`layout-modal.js`/`.css` is a small reusable primitive (open/close, Escape/backdrop dismiss, a
name-prompt builder, a list-picker builder) that both shells load and both SAVE and LOAD build
on, rather than four one-off overlays. Styled only from `shared/theme.css` tokens so it looks and
behaves the same whether it's opened over the bezel or the glass.

**No LYT page nav item.** A LOAD entry on the LYT page was considered (unambiguous which browser,
since a click is already local) but deferred to explore separately later — this feature covers
the keyboard-shortcut path only.

## What is built

| File | Role |
|---|---|
| `src/plugin/LayoutStore.cs` | The layout library: storage, disk persistence (`com.roque.NOXMFD.layouts.json`) — opaque `data` blob |
| `src/plugin/Keybinds.cs` | `DefKeyOnly` bind shape; `layout-save`/`layout-load` rows under a new LAYOUT section |
| `src/plugin/CommandDispatcher.cs` | `layout.save` (reuses existing `wname`/`group`/`text` envelope fields — no new wire fields) |
| `src/plugin/TelemetryServer.cs` | `GET /layout-options`; `ServeKeybindsConfig` emits `joyButton`/`joyNum` only when a bind actually has one |
| `src/web/shell/layout-store.js` | Fetch-on-open client (`/layout-options`, `layout.save`) — no background poll, layouts don't change on their own |
| `src/web/shell/layout-modal.js`, `layout-modal.css` | The shared modal primitive |
| `src/web/shell/layout-keybinds.js` | Polls `/keybinds-config`, matches a `keydown` against the configured save/load keys |
| `src/web/shell/classic/mfd.js` | `captureLayoutState`/`applyLayoutState` — `{splitMode, splitVariant, pages}` |
| `src/web/shell/f35/f35.js` | `captureLayoutState`/`applyLayoutState` — `{cells, pages}`, rebuilding portals directly from a saved `F35Glass` arrangement rather than replaying merge/split actions |
| `src/web/pages/keybinds/keybinds.js` | Renders a key-only row (one wide keyboard cell, no joystick column) |
| `tools/serve_web.py` | Mocks `/layout-options` (one CLASSIC + one F-35 demo layout) and the two new `KEYBINDS` rows |

## Verification performed

- `dotnet build -c Release` clean (0 errors, same warning baseline).
- Full `*.test.js` suite unaffected.
- In the `serve_web.py` harness: LOAD's picker correctly filters to the current shell only;
  applying a saved layout reproduces both a CLASSIC split (confirmed via `panePages`/`splitVariant`
  state and each pane's `iframe.src`) and an F-35 merged-portal arrangement (confirmed via each
  portal's `flexGrow`/`page-frame.src`). Setting a key on the KEY page for `layout-save` round-trips
  through the generic `keybind.set-key` handler with no bind-specific code, and the shell's
  `LayoutKeybinds.match` correctly opens the SAVE modal for a matching `KeyboardEvent`.
- Not verified: real joystick/HOTAS is irrelevant here by design. Real in-game persistence
  (`BepInEx/config/com.roque.NOXMFD.layouts.json` surviving a restart) is unverified — the harness
  has no real plugin process to restart.

## Out of scope / deferred

- Joystick/HOTAS binding for SAVE/LOAD LAYOUT.
- A LOAD entry on the LYT page.
- Renaming or deleting a saved layout — the issue only asked for save/load; a growing
  never-pruned list is a real usability gap worth a future look, not something to build
  unasked.
- Real in-game verification (build + deploy + fly).
