# SOI include/exclude — [issue #58](https://github.com/roke77/NOXMFD/issues/58)

**Status:** shipped in 0.43.0, confirmed working in-game (checkboxes, exclude/include, and real SOI
focus skipping an excluded pane). An external review of 0.43.0 then found a real gap the harness's
JS model didn't cover — see "Shrink-plus-exclusion fix" below — since fixed, covered by real xUnit
tests against the production logic (not just a hand-copied JS mirror), and rebuilt/redeployed; not
yet re-confirmed live after that fix.

## What it is

A per-surface opt-out from the SOI ring ([`man/keybinds.md#sensor-of-interest-soi`](../man/keybinds.md#sensor-of-interest-soi)):
mark a full-view display, one pane of a CLASSIC split, or one F-35 portal so **SOI NEXT/PREV**
(`SoiFocus.Cycle`) skips it — it simply never appears in the ring, the same as a surface that
doesn't exist. Default is **included**, matching `SoiFocus`'s existing "opt-in" philosophy for the
ring itself (nothing focuses until a SOI key is pressed) applied one level down: nothing is
excluded until a pilot deliberately excludes it.

## UI

Controls live in the existing **LOAD LAYOUT** modal ([`man/layouts.md#saveload-layout`](../man/layouts.md#saveload-layout)),
above the list of saved layouts — reusing `layout-modal.js`'s `pickList`, not a new dialog. The
checkboxes describe the *browser window's own current* surfaces, checked by default:

- **CLASSIC, full view:** one checkbox, "Include panel in SOI".
- **CLASSIC, H_SPLIT:** "Include TOP panel in SOI" / "Include BOTTOM panel in SOI" — pane 0 is
  TOP, pane 1 is BOTTOM, the same convention `mfd.js`'s `soiKeys()` already uses.
- **CLASSIC, V_SPLIT (any width variant — `v`/`vw`/`vwr`):** "Include LEFT panel in SOI" /
  "Include RIGHT panel in SOI" — pane 0 is always LEFT regardless of which side is wider.
- **F-35:** one checkbox per live portal, "Include portal N in SOI", numbered 1-4 left to right
  (`portals[0]` is the leftmost).

Fetched fresh (`GET /soi-excluded?cid=...`) every time LOAD LAYOUT opens rather than cached, so a
change made from another browser tab sharing the same cid is never shown stale. Toggling a
checkbox fires `soi.include` immediately — there's no separate save step.

## Design decisions

- **Only exclusions are stored server-side (`SoiFocus._excluded`, a `HashSet<(cid, pane)>`), not
  an included flag per surface.** SOI's ring is opt-in for the ring itself (nothing focuses until
  a key is pressed) but opt-*out* per surface — "not in the set" already means included, the
  default every surface had before this feature existed. Cheaper than tracking every surface's
  state and needs no migration for surfaces that predate the feature.
- **`RingLocked()` filters excluded surfaces out directly** rather than filtering post-hoc in
  `Cycle()` — every ring consumer (today, only `Cycle`) automatically respects exclusions with no
  separate check.
- **Excluding the currently-focused surface moves focus off it immediately** (`SetIncluded`,
  jumping to the new ring's first member, or clearing focus if none remain) — the same reasoning
  `SetPaneCount` already uses when a merge shrinks the pane count out from under the focused pane.
  A pilot unchecking "include" shouldn't need a spare SOI press before the ring reflects it.
- **No dangling per-index state.** `SetPaneCount` purges any exclusion on a pane index that no
  longer exists whenever a merge shrinks the count (`pane >= n`), so a later split that recreates
  that same index starts back at the default (included). `ReleaseOnDisconnect` purges every
  exclusion for a cid that has genuinely disconnected (no twin tab left holding it open) for the
  same reason — an exclusion should never outlive the display it described.
- **`cid` is read from the client-generated `sessionStorage` id, same as every other SOI wire
  message** (`soi.panes`/`soi.page`) — no new identity concept.
- **`GET /soi-excluded?cid=...` is a new, minimal endpoint** rather than folding exclusion state
  into the shared telemetry frame's `SoiJson()`: that frame is broadcast identically to every
  connected display (cache-shared across clients), but exclusion state is *per requesting
  browser's own cid* — embedding it there would mean either breaking the shared-frame cache or
  shipping every OTHER display's exclusions to a browser that has no use for them. A tiny
  fetch-on-open endpoint (mirroring `hud-presets`/`tgt-presets`'s own on-demand GET) costs nothing
  extra on the normal telemetry path and only queries per-browser data on demand, matching the
  reasoning `preset-bar.js`'s own `fetchItems` already uses.
- **The wire envelope needs no new `CommandEnvelope` fields.** `soi.include` reuses `cid` (which
  instance), `n` (pane index — already `soi.page`'s meaning), and `on` (desired state) — the exact
  fields `soi.panes`/`soi.page` already established for this same "instance + surface" shape.
- **`layout-modal.js`'s `pickList` grew a generic `opts.checkboxes` slot**, not a SOI-specific one
  — same reasoning as `item.display`'s earlier addition for the HUD/TGT preset lists: one small,
  reusable extension point rather than a one-off.
- **The shell (not `layout-keydown.js`) owns the exact wording.** `layout-keydown.js`'s shared
  `openLoadLayoutModal` only knows how to fetch/set included state generically; each shell passes
  a `getSoiSurfaces()` callback (`soiSurfaces()` in `mfd.js`/`f35.js`) that knows whether it's a
  full view, an H/V split, or how many F-35 portals are live right now, and returns the exact
  labels. Same split as `captureLayoutState`/`applyLayoutState` (state shape) already living in
  each shell while the keyboard/modal plumbing around them is shared.

### Shrink-plus-exclusion fix (review follow-up)

`SetPaneCount`'s shrink clamp originally jumped straight to `n - 1` without checking whether that
surviving pane was excluded — an external review of 0.43.0 found the exact scenario: a 4-portal
F-35 layout, portal 2 excluded, focus on portal 4, merged down to 2 portals landed focus right back
on the excluded portal 2. Fixed by preferring the nearest still-included pane on the same display
first (walking down from `n - 1`), falling through to the full ring's first member only if every
surviving pane on that display is excluded too.

The ring-selection rules this touches (`RingLocked`'s build, `Cycle`'s step, and this shrink clamp)
moved into a new pure, BCL-only `SoiRing.cs`, linked directly into `NOXMFD.Tests.csproj` — the
review separately observed that `tools/soi-focus.test.js` only exercises a hand-copied JS model of
these rules, never the actual production code, which is exactly how the shrink-plus-exclusion case
went untested in the first place. `SoiFocus.cs` still owns all the locking and live state
(`SseHub.Instances()`, `TgpManualControl.ManualMode`); `SoiRing`'s functions only compute over
plain data, so `SoiRingTests.cs` can call the real logic directly instead of a second, independently
maintained copy of it.

## What is built

| File | What |
|---|---|
| [`src/plugin/Http/SoiFocus.cs`](../src/plugin/Http/SoiFocus.cs) | `_excluded` set; `IsIncluded`/`SetIncluded`/`ExcludedJson`; `RingLocked`/`Cycle`/`SetPaneCount`'s clamp delegate to `SoiRing` below; `SetPaneCount`/`ReleaseOnDisconnect` purge stale entries. |
| [`src/plugin/Http/SoiRing.cs`](../src/plugin/Http/SoiRing.cs), [`tools/tests/SoiRingTests.cs`](../tools/tests/SoiRingTests.cs) | Pure ring-build/cycle-step/shrink-clamp rules, no game touchpoint — linked directly into `NOXMFD.Tests.csproj` so the shrink-plus-exclusion case (and the rest of the ring rules) run against the real production logic. |
| [`src/plugin/Http/TelemetryServer.cs`](../src/plugin/Http/TelemetryServer.cs) | `SetSoiIncluded` — thin delegate to `SoiFocus.SetIncluded`, same shape as its other SOI wrappers. |
| [`src/plugin/Http/ConfigEndpoint.cs`](../src/plugin/Http/ConfigEndpoint.cs), [`TelemetryHttpRouter.cs`](../src/plugin/Http/TelemetryHttpRouter.cs) | `GET /soi-excluded?cid=...` → `{"excluded":[pane,...]}`. |
| [`src/plugin/CommandDispatcher.cs`](../src/plugin/CommandDispatcher.cs) | `soi.include` — `cid`/`n`/`on`, all pre-existing envelope fields. |
| [`src/web/shell/shared/layout-modal.js`](../src/web/shell/shared/layout-modal.js), [`layout-modal.css`](../src/web/shell/shared/layout-modal.css) | `pickList`'s new `opts.checkboxes` slot, rendered above the saved-layout list. |
| [`src/web/shell/shared/layout-keydown.js`](../src/web/shell/shared/layout-keydown.js) | `makeLayoutKeydownHandlers`'s new `getSoiSurfaces` parameter; fetches `/soi-excluded`, builds the checkbox specs, sends `soi.include` on change. |
| [`src/web/shell/classic/mfd.js`](../src/web/shell/classic/mfd.js), [`src/web/shell/f35/f35.js`](../src/web/shell/f35/f35.js) | `soiSurfaces()` — this shell's own full/H-split/V-split or portal-count labels. |
| [`tools/serve_web.py`](../tools/serve_web.py) | Stateful mock: `SOI_EXCLUDED` (cid → excluded-pane set), `/soi-excluded`, `soi.include`. |
| [`tools/soi-focus.test.js`](../tools/soi-focus.test.js) | Extended the existing pure-JS SOI ring model with exclude/include, focus-move-on-exclude, pane-shrink purge, and disconnect purge. |

## Verification performed

Original feature:

- `dotnet build -c Release` — 0 errors, same warning baseline.
- Full `tools/ci-check.ps1` — build, every `*.test.js` file, `dotnet test`, route smoke, all green.
- `tools/serve_web.py` harness, live browser check: CLASSIC full view, H_SPLIT, and V_SPLIT each
  show the correct checkbox set/labels in LOAD LAYOUT, opened via the real keyboard-shortcut path
  (`handleLayoutKeydown`) while the split was actually live — confirmed via `soiSurfaces()`
  returning `["Include TOP panel in SOI","Include BOTTOM panel in SOI"]` under H_SPLIT and
  `["Include LEFT panel in SOI","Include RIGHT panel in SOI"]` under V_SPLIT. The F-35 shell with
  4 live portals showed "Include portal 1..4 in SOI". Unchecking BOTTOM under H_SPLIT correctly
  set `GET /soi-excluded` to `{"excluded":[1]}`; re-checking cleared it; a value set out-of-band
  (simulating another browser tab) was correctly picked up on the next LOAD LAYOUT open rather
  than shown stale.
- Not verified in the harness: the LYT page's own SAVE/LOAD nav items always force full view before
  opening the modal (`applySplitMode()` behind the `lyt` nav action) — a pre-existing limitation of
  that path, unrelated to this feature, and irrelevant to the checkbox labels since real usage
  reaches LOAD LAYOUT via the configured keyboard shortcut while still looking at the live
  split/portal arrangement.
- **Confirmed working in-game**: checkboxes, exclude/include round-trip, and real SOI focus
  skipping an excluded pane/portal.

Shrink-plus-exclusion fix (review follow-up):

- `dotnet build -c Release` — 0 errors, same warning baseline.
- `SoiRingTests.cs` — 12 new xUnit cases against the actual production ring-build/step/clamp logic,
  including the exact review-flagged scenario (4 portals, one excluded, shrink to 2) and its
  "every surviving pane also excluded" variant. `tools/soi-focus.test.js`'s JS mirror model also
  extended with the same two cases, kept for the browser-facing checkbox/command wiring it alone
  covers.
- Full `tools/ci-check.ps1` — every `*.test.js` file, 303 `dotnet test` cases, route smoke, all
  green.
- Not yet re-confirmed live: the fix itself, and that `RingLocked`/`Cycle` still behave identically
  post-extraction (the xUnit/JS tests above cover the logic in isolation, but not an actual layout
  merge on real connected displays).

## Open questions

- None outstanding.
