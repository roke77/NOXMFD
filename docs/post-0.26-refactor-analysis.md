# Post-0.26.0 Refactor Execution Analysis

Status: analysis only. This document reviews the work merged after release `0.26.0` and compares it with the SRP/DRY/frontend/backend refactor proposals captured in `docs/refactor-scan.md` and the earlier discussion.

## Baseline

Baseline tag: `0.26.0` (`6e58a74`).

Current reviewed head: `ad3b1f2` on `main`.

Post-release scope:

- 34 commits after `0.26.0`.
- 118 files changed.
- 4698 insertions, 3783 deletions.
- Main touched areas: `src/plugin/`, `src/web/shell/`, `tools/`, `tools/tests/`, and `docs/`.

Verification run during this review:

- `powershell -ExecutionPolicy Bypass -File tools/ci-check.ps1`
- Result: passed.
- Coverage from that script: Release build, 25 JS test files, 30 xUnit tests, and route smoke for `/`, `/afm`, `/map-view?bare`, `/hud`.
- Caveat: `dotnet build` still emits warnings. The smoke check treats warnings as tracked debt, not failure.

## Overall Verdict

The post-`0.26.0` work strongly matches the high-confidence parts of the refactor proposals. It mostly executed the right kind of refactor: small, verified, behavior-preserving extractions around proven duplication.

The best architectural improvement is not just fewer lines. The better win is that several previously implicit contracts are now explicit:

- Shared shell boot/typewriter and layout-keydown mechanics.
- Shared C# JSON escaping through `JsonLite`.
- Shared response-writing helpers in `TelemetryServer`.
- Shared countermeasure reflection access.
- A CI script that makes build/test/route validation repeatable.

The work also showed good restraint. The proposals included some broad textbook extractions, such as splitting all of `map.js`, all of `Keybinds.cs`, and much of `TelemetryReader.cs` into many modules. The executed plan largely avoided those because the second scan found limited concrete duplication there. That was the right call.

## Proposal Coverage

| Proposal area | Landed work | Assessment |
| --- | --- | --- |
| Add repeatable validation | `tools/ci-check.ps1`, `tools/tests/NOXMFD.Tests.csproj`, `JsonLiteTests`, `RouteStoreTests` | Strongly aligned. This is one of the highest leverage changes. |
| Request hygiene | `/command` and `/ext/<id>/command` method/body checks | Completed. Matches server-hardening plan. |
| Classic MFD relay DRY | `forward*` boilerplate consolidated, later message dispatcher partly table-driven | Mostly completed. Good reduction, but see the dispatcher wording caveat below. |
| Shared shell code | `src/web/shell/boot-reveal.js`, `src/web/shell/layout-keydown.js` | Completed and well scoped. |
| `TelemetryServer` response helpers | `WriteJson`, `WriteBinary` | Completed. Reduces repeated response mechanics. |
| `TelemetryReader` BDF/PAL duplication | `FactionForcesBlock`, `BuildFactionForces` | Completed. Good DRY extraction with clear domain boundary. |
| Dev server captured-asset routes | `_serve_captured` in `tools/serve_web.py` | Completed. Mechanical, readable improvement. |
| `AssetCapture` cleanup | `JsonLite.EscapeJson`, generic `CaptureTypeIcons<T>` | Completed. Good dedupe with low behavior risk. |
| Shared fullscreen icon | `--icon-fullscreen` in `theme.css` | Completed. Small but clean frontend DRY win. |
| Shared CM reflection | `CmReflection.cs` used by reader/keybinds | Completed. Correctly centralizes only reflection, not caller policy. |
| Layout docs split | `docs/layouts-f35-build-log.md`, trimmed `docs/layouts.md` | Completed. Big documentation readability win. |

## What Improved Most

### 1. Testability and CI

The test project turns part of the earlier "pure logic" plan into reality. `JsonLite` and `RouteStore` are now covered outside Unity/BepInEx, and `RouteStore` received the injection/reset seams needed to make that possible.

This is deeper than a test-count improvement. It establishes a pattern for future C# refactors: extract pure seams first, then cover them with xUnit before moving riskier runtime code.

### 2. Shell Architecture

The shell work matches the frontend proposal well:

- `boot-reveal.js` isolates a browser/UI effect shared by classic and F-35.
- `layout-keydown.js` isolates same-origin iframe keydown wiring and save/load modal entry points.
- Classic's relay code is less repetitive.
- F-35 keeps its portal-specific logic local rather than being forced into a fake shared shell abstraction.

This is a good SRP/DRY balance. Shared mechanics moved out; shell-specific state and geometry stayed in place.

### 3. Backend Server Hygiene

`TelemetryServer.cs` is still large, but it is better:

- `WriteJson`/`WriteBinary` remove repeated HTTP response mechanics.
- Request body hygiene landed.
- JSON escaping moved to `JsonLite`.
- RouteStore test seams removed some direct plugin coupling.

The remaining large responsibility is the one already identified before: `TelemetryServer` still mixes routing, asset serving, command endpoints, SSE/MJPEG, config endpoints, and telemetry serialization.

### 4. Telemetry/Asset Duplication

`BuildBdf`/`BuildPal` becoming `BuildFactionForces` is exactly the kind of duplication removal the scan asked for: duplicated policy, same structure, same reason to change.

`AssetCapture` also improved in the right way. `CaptureTypeIcons<T>` centralizes the repeated "walk type list, guard captured set, request sprite, publish bytes" pattern without forcing unrelated capture modes together.

### 5. Documentation Shape

Splitting `docs/layouts.md` was a big improvement. It separates current architecture reference from build-log/history, matching the comment/documentation guidance from the session.

## Mismatches and Caveats

### 1. `docs/refactor-scan.md` Is Now Partly Stale

The doc marks all nine execution steps done, which is broadly true, but several measurements and details are now stale after the refactor itself.

Examples:

- The table still says `src/web/shell/classic/mfd.js` is 2467 lines; it is currently 2248.
- It says `docs/layouts.md` is 791 lines; it is currently 338, with 329 lines moved to `docs/layouts-f35-build-log.md`.
- It says `TelemetryServer.cs` is 2092 lines; it is currently 1860.

Recommendation: add a short "post-execution note" instead of keeping the pre-execution line table as if it were current.

### 2. Dispatcher Step Was Implemented More Conservatively Than Planned

The execution plan text says the `mfd.js` dispatcher work should cover the broader set including `rwr`, `mw`, `rdr`, and `targets`.

The landed code table-drives only the seven raw store-and-forward messages:

- `tgt`
- `bdf`
- `pal`
- `mis`
- `obj`
- `akf`
- `mapinfo`

It leaves `targets`, `rwr`, `mw`, and `rdr` as explicit branches because they reshape/validate payloads. That is a better engineering decision than forcing them into the table, but the doc/status wording overstates the implementation.

Recommendation: update the doc to say "seven verbatim raw-store messages" and explicitly record that the reshaping branches were intentionally left out.

### 3. Some New Comments Reintroduce Historical/Process Context

The earlier session produced a comment best-practices rule saying agents should avoid historical narratives and development-process context in generated code.

Several new comments reference `docs/refactor-scan.md step N` directly in production files, for example:

- `src/web/shell/boot-reveal.js`
- `src/web/shell/layout-keydown.js`
- `src/web/shell/classic/mfd.js`
- `src/web/shell/f35/f35.js`
- `src/plugin/CmReflection.cs`
- `src/plugin/AssetCapture.cs`

The technical parts of those comments are useful. The "step N" provenance is less useful long term and conflicts with the newly added guidance.

Recommendation: in a future cleanup, keep the current-behavior explanation and remove process references like "docs/refactor-scan.md step 1".

### 4. Validation Is Strong But Not Complete

`ci-check.ps1` passing is excellent. It does not cover the Unity/live-game behaviors called out in the execution plan:

- BDF/PAL faction data after `BuildFactionForces`.
- CM category display and keybind-driven CM cycling after `CmReflection`.
- Fresh-mission captured vehicle/ship/building icons after `CaptureTypeIcons<T>`.
- Classic split/full page forwarding with live `tgt`/`bdf`/`pal`/`mis`/`obj`/`akf`/`wpt` data.
- Visual confirmation of the fullscreen icon in both shells.

Recommendation: keep these as manual release-check bullets unless/until the harness can simulate them.

#### Pre-Merge Live-Game Checklist For The Refactor Package

Run this after `tools/ci-check.ps1` passes and before merging the `refactor-package-19-20-08`
branch. These checks cover the areas CI cannot prove because they depend on Unity objects, browser
cache behavior, or a live `HttpListener` instance inside the game.

1. **KEY command path and config snapshot**
   - Open `/keybinds` and confirm `/keybinds-config` renders all sections, notes, capture state, and
     immersion toggles.
   - Change one keyboard bind, confirm it round-trips in the page, then restore it.
   - Arm/cancel one joystick capture and one axis capture; confirm the page leaves capture mode cleanly.
   - Toggle `Input when unfocused`, `Radar on start`, `Engine on start`, `Master Arms on start`, and
     `HUD filters on combat mode`; confirm each persists visually after refreshing `/keybinds`.

2. **Telemetry stream shape**
   - Open `/stream` or a browser devtools Network preview while a mission is running.
   - Confirm top-level fields still parse after the `TelemetryJson` cleanup: `masterArmsOn`,
     `combatMode`, `soiTarget`, `soiPane`, `loadout`, `contacts`, `tgt`, `bdf`, `pal`, `mis`, `obj`,
     `akf`, and `ext`.
   - Switch combat mode A/A, A/G, and ALL; confirm `combatMode` and weapon soft-selection behavior
     still update on the WPN/KEY-facing UI.

3. **Embedded page routes and asset caching**
   - In the live plugin server, load `/`, `/map-view?bare`, `/hud`, `/afm`, and `/keybinds`; confirm
     each page renders with CSS/JS loaded.
   - Hard refresh one page, then refresh normally; confirm embedded assets return `ETag` and
     `Cache-Control: no-cache`, and repeated asset requests can return `304` without breaking page
     rendering.
   - Load at least one direct `/assets/...` URL used by a page, such as a page CSS or JS file, and
     confirm it returns the expected content type.

4. **Extension/static content MIME behavior**
   - If an extension is installed, open its `/ext/<id>/` route and at least one static asset under
     that extension; confirm the page loads and CSS/JS content types are correct.
   - If no extension is installed, confirm `/ext` still loads the built-in placeholder page and
     `/ext-manifest` returns valid JSON.

5. **HUD/MAP/AFM visual smoke**
   - Load `/map-view?bare` during a mission and confirm the map grid, ownship, contacts, cursor, and
     waypoint overlays still render.
   - Load `/hud` and confirm HUD options, preset state, declutter toggles, and mode tabs reflect the
     in-game HUD.
   - Load `/afm` and confirm the aircraft silhouette/failure state still appears after selecting an
     aircraft.

6. **Existing Unity-only regression checks**
   - Re-run the older release bullets above: BDF/PAL faction data, CM category display and cycling,
     fresh-mission captured icons, split/full forwarding for live data pages, and fullscreen icon
     visuals in both shells.

### 5. Backend Extraction Has Started

The broader proposal recommended extracting telemetry JSON serialization and/or route handling from `TelemetryServer.cs`.

That work has now started. `TelemetryJson.cs` owns telemetry-frame serialization,
`TelemetryAssets.cs` owns embedded web asset serving plus MIME detection, and `TelemetryHttpRouter.cs`
owns URL dispatch. The remaining backend seams are still meaningful, but they are narrower than the
original "split `TelemetryServer`" proposal:

- `TelemetryStreamHub`
- `CommandEndpoint`

Recommendation: if continuing backend architecture work, isolate SSE stream/session handling or the
command endpoint next. Avoid mixing both in one branch.

## Current Line-Count Impact

The biggest files got smaller, except `map.js`, which changed only incidentally.

| File | At `0.26.0` | Current | Net |
| --- | ---: | ---: | ---: |
| `src/web/shell/classic/mfd.js` | 2487 | 2214 | -273 |
| `src/plugin/Http/TelemetryServer.cs` | 1905 | 1417 | -488 |
| `src/plugin/TelemetryReader.cs` | 1231 | 1191 | -40 |
| `src/web/shell/f35/f35.js` | 1156 | 1091 | -65 |
| `src/web/pages/map/map.js` | 1005 | 1013 | +8 |
| `src/plugin/Keybinds.cs` | 932 | 917 | -15 |
| `tools/serve_web.py` | 787 | 761 | -26 |
| `docs/layouts.md` | 652 | 338 | -314 |
| `src/plugin/AssetCapture.cs` | 631 | 573 | -58 |
| `src/web/shell/classic/mfd.css` | 625 | 625 | 0 |
| `src/web/shell/f35/f35.css` | 621 | 619 | -2 |
| `src/plugin/TelemetryJson.cs` | 0 | 385 | +385 |
| `src/plugin/Http/TelemetryAssets.cs` | 0 | 90 | +90 |

Line count is not the main quality metric here, but it does show the refactor mostly reduced large-file pressure without large behavioral churn.

## Deferred or Rejected Proposals

These were discussed but not executed, and the current choice still looks reasonable:

- Split `map.js` into many modules: deferred/rejected for now. Existing `MapTransform`, `WptRoute`, and `TelemetrySource` already cover major separations.
- Split `Keybinds.cs` into many files: deferred. The registry is large but relatively coherent.
- Split `TelemetryReader.cs` into broad snapshot-builder classes: deferred. Only BDF/PAL had obvious duplication.
- Route table/mock-module split in `serve_web.py`: deferred as dev-tool polish.
- `AssetCapture` diagnostic logging seam: deferred. Real SRP seam, but low priority.
- F-35 CSS extraction: not done, and not urgent.

These were discussed and remain good future work:

- Consider an SSE/session hub extraction after the route table extraction has settled.
- Consider moving command queue/body handling into a focused command endpoint.
- Clean process/history comments from production code.
- Update stale planning docs after execution.

## Folder Architecture Suggestions

The current repo already has a useful first-level split:

- `src/plugin/` for C# plugin/runtime code.
- `src/web/pages/` for page-specific browser code.
- `src/web/shell/` for shell/layout code.
- `src/web/services/` for shared browser services.
- `src/web/shared/` for shared CSS/fonts/tokens.
- `tools/` for preview, capture, CI, and tests.

The next architecture improvement would be to keep making responsibilities visible inside the largest folders, especially `src/plugin/` and `src/web/shell/`. This should be incremental. Moving every file at once would create noisy history without changing behavior.

### Suggested C# Plugin Shape

Recommended direction:

| Folder | Responsibility | Candidate files |
| --- | --- | --- |
| `src/plugin/Core/` | Plugin bootstrap and lifecycle coordination | `Plugin.cs`, `MissionLifecycle.cs`, `HarmonyPatches.cs` |
| `src/plugin/Telemetry/` | Snapshot DTOs, telemetry reads, serialization | `TelemetrySnapshot.cs`, `TelemetryReader.cs`, `TelemetryJson.cs` |
| `src/plugin/Http/` | HTTP server, route handling, response helpers, streaming | `TelemetryServer.cs`, `TelemetryAssets.cs`, `TelemetryHttpRouter.cs`, future `TelemetryStreamHub.cs` |
| `src/plugin/Commands/` | Browser command envelope and command handlers | `CommandDispatcher.cs`, future command handler classes |
| `src/plugin/Stores/` | Persistent/in-memory JSON-backed stores | `RouteStore.cs`, `LayoutStore.cs`, `HudPresetStore.cs`; `ExtensionRegistry.cs` could join later if it becomes more store-like than extension API surface |
| `src/plugin/Input/` | Keybind registry, polling, joystick capture, selection helpers | `Keybinds.cs`, `WeaponSelectors.cs`, future `TapHoldDetector.cs` |
| `src/plugin/Assets/` | Sprite/image capture and asset reflection helpers | `AssetCapture.cs`, `SpriteCapture.cs` |
| `src/plugin/Hud/` | HUD-specific runtime behavior and config | `HudDeclutter.cs`, `HudDeclutterConfig.cs`, `HudCombatModeFilters.cs`, `HudWaypointCue.cs` |
| `src/plugin/Immersion/` | Immersion-mode settings/state | `ImmersionConfig.cs`, `ImmersionState.cs` |
| `src/plugin/Config/` | Config declarations and BepInEx UI metadata | `RatesConfig.cs`, `ConfigurationManagerAttributes.cs` |
| `src/plugin/Interop/` | Reflection/private-game-field adapters | `CmReflection.cs`, future focused reflection helpers |
| `src/plugin/Util/` | Pure utilities with little/no game coupling | `JsonLite.cs` |

Notes:

- `TelemetryServer.cs` has already moved under `Http/`, with asset serving and route dispatch split out. The real remaining wins are stream/session handling and command endpoint extraction.
- `TelemetryReader.cs` can stay intact until concrete seams are extracted. Do not create empty `Builders/` folders just because "snapshot builders" sound clean.
- Store classes are good candidates for tests. Anything moved into `Stores/` should avoid direct Unity/BepInEx dependencies where possible.
- Reflection helpers should live behind narrow names such as `CmReflection`, not a generic "ReflectionUtils" bucket.

### Suggested Web Frontend Shape

Recommended direction:

| Folder | Responsibility | Current or candidate files |
| --- | --- | --- |
| `src/web/shared/` | Global visual tokens, fonts, tiny browser-agnostic helpers | `theme.css`, `font.css` |
| `src/web/services/` | Runtime/browser services shared by pages and shells | `telemetry-source.js`, `send-command.js`, `pad-cursor.js` |
| `src/web/protocol/` | Shared message names, payload contracts, route constants | future `page-protocol.js`, future command constants |
| `src/web/shell/shared/` | Shell-agnostic shell mechanics | `boot-reveal.js`, `layout-keydown.js`, `layout-modal.js`, `layout-store.js`, `layout-pages.js`, `nav-model.js` |
| `src/web/shell/classic/` | Classic shell composition and classic-only policies | `mfd.html`, `mfd.css`, `mfd.js`, `classic-paging.js`, `split-*` |
| `src/web/shell/f35/` | F-35 shell composition and F-35-only policies | `f35.html`, `f35.css`, `f35.js`, `f35-glass.js`, `f35-wpn-paging.js` |
| `src/web/pages/<page>/` | Page composition, rendering, page-specific policies/tests | current page folders |

Notes:

- `src/web/shell/` currently mixes shared shell modules and shell folders. A future `src/web/shell/shared/` would make that boundary clearer.
- `src/web/protocol/` would be useful once message contracts are centralized. Until then, avoid creating it for one file.
- Page folders are already mostly healthy. Keep page-specific policies beside their page, as with `avn-throttle-policy.js`, `afm-bg-policy.js`, and `map-transform.js`.
- Avoid broad `components/` or `utils/` folders unless there are genuinely shared browser components. This app is shell/page oriented, not a generic component library.
- If `map.js` is split later, prefer page-local modules such as `map-renderer.js`, `map-icons.js`, and `map-interactions.js` under `src/web/pages/map/`, not global services.

### Suggested Tools Shape

Recommended direction:

| Folder | Responsibility | Candidate files |
| --- | --- | --- |
| `tools/preview/` | Local preview server and preview-only route helpers | `serve_web.py`, preview route helpers |
| `tools/preview/mocks/` | Stateful mock APIs for layouts, presets, keybinds, telemetry | extracted parts of `serve_web.py`, `preview-mock.js` if kept server-related |
| `tools/capture/` | Capture/screenshot utilities | `capture_assets.py`, `capture_screenshots.py` |
| `tools/tests/` | C# xUnit project | current `tools/tests/*` |
| `tools/js-tests/` or colocated tests | JS tests | current colocated `.test.js` files can stay as-is |
| `tools/ci/` | CI entry points | `ci-check.ps1` if this grows beyond one script |

Notes:

- Keeping JS tests colocated with the page/service they cover is useful. Do not move them into a central folder unless test discovery becomes painful.
- `serve_web.py` is the only tool that currently wants internal folders. Split it only when route helpers or mocks are being touched for real work.

### Migration Strategy

1. Create folders only when moving at least two related files or extracting a real new module.
2. Move pure/testable code first, especially `TelemetryJson`, protocol constants, and store-like logic.
3. Keep composition roots easy to find: `Plugin.cs`, `TelemetryServer.cs`, `mfd.js`, `f35.js`, and page `<name>.js` files should remain obvious entry points even if they shrink.
4. Update `NOXMFD.csproj` and embedded resource assumptions in the same commit as any move.
5. Prefer one responsibility-group per commit. Large folder reshuffles are hard to review and easy to misattribute in blame/history.

## Recommended Next Actions

1. Update `docs/refactor-scan.md` with a post-execution note.
   Correct the stale line counts and the dispatcher wording.

2. Clean comment provenance references.
   Remove "docs/refactor-scan.md step N" from production comments while keeping the useful live-behavior explanation.

3. Do the pre-merge live-game checklist before the next release.
   CI passed, but Unity-only behavior, embedded-asset caching, and browser/server interactions still
   need manual confirmation.

4. Choose one deeper backend refactor next.
   The best candidate is `TelemetryJson.cs`, with xUnit coverage for stable snapshot fixtures.

5. Keep broad frontend splits opportunistic.
   `map.js`, `f35.js`, and `Keybinds.cs` are still large, but the executed work correctly showed that size alone is not proof of bad architecture.
