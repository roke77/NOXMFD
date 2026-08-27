# C# unit testing — isolating pure logic, xUnit for coverage

## Status

In progress. `tools/tests/` is stood up with `JsonLite.cs` + `RouteStore.cs` covered (step 1 below),
and `TelemetryServer.cs`'s JSON-writer layer is extracted into `TelemetryJson.cs` and covered (step
2). Later feature work also links the pure `TgpManualAimMath.cs` and
`Hud/HudDirectionCueMath.cs` helpers directly into the standalone project; the latter covers the
TGP HUD cue's edge placement, behind-camera behavior, and exact-rear stabilization. Steps 3-6 are
otherwise not started — see the Scope checklist.

## The problem

The standalone `tools/tests/` xUnit project covers the plugin's pure, Unity-free seams by compiling
those source files directly. Most `src/plugin/` files still rely on `dotnet build` plus a manual
in-game checklist because they reflect into live Unity/game objects. That's a reasonable default
for code that genuinely can't run outside Unity, while the remaining checklist below identifies
business logic that can still be separated and tested safely.

## Why a blanket refactor is the wrong shape

"Refactor all C# files for testability" was the original framing, and it's worth saying plainly
why that's not the plan: several files — `HarmonyPatches.cs`, `AssetCapture.cs`, `TgpFeed.cs`,
`SpriteCapture.cs`, `HudDeclutter.cs`, `CommandDispatcher.cs`, `Plugin.cs`, `MissionLifecycle.cs` —
*are* the Unity/game integration seam by design. There's no business rule hiding inside a Harmony
prefix that just returns `ImmersionState.MasterArmsOn`, or inside a GPU capture pipeline
(Blit → AsyncGPUReadback → JPEG). Refactoring those for testability would move code around for
zero testing benefit and add risk without expanding meaningful coverage.

The other files split differently: some already have zero Unity coupling and just need tests
written against them today; others have real, extractable business logic sitting mixed in with a
thin layer of live game reads.

## Survey

Coupling measured as a rough signal: hits for `SceneSingleton<`, `GameManager.`, `Aircraft`,
`CombatHUD`, `FactionHQ`, `UnityEngine.`, `MonoBehaviour`, `Vector3`, `Transform` per file.

### Already pure, real logic, zero refactor needed

| File | Lines | Notes |
|---|---|---|
| `RouteStore.cs` | 364 | Route/waypoint mutators, name-dedup, proximity-advance — already ported from `wpt-route.js`'s pure functions, and those specific methods are genuinely pure. **Correction (2026-08-21): the file as a whole is not "zero touchpoints."** `Load()`/`Save()` reference `BepInEx.Paths.ConfigPath` (`:50`) and `Plugin.Log` (`:117`), and `BuildJson()` calls `TelemetryServer.EscapeJson` (`:124`) — none of those are Unity/game-object coupling (the survey's grep signal below doesn't catch them, which is why it missed this), but a standalone test project still can't compile against this file without either those three symbols available or a small extraction of the pure mutators into their own seam. Still the single biggest win here, just not a zero-touchpoint one — budget a small storage/log/escape seam alongside step 1 below, not a bigger effort than that. |
| `JsonLite.cs` | 184 | The JSON parser. Already the obvious first target. |
| `ExtensionRegistry.cs` | 188 | Registration table, bounded command queue, manifest sort — mostly state bookkeeping, some real logic. |
| `HudDirectionCueMath.cs` | 108 | Pure screen-rectangle placement for the manual-TGP HUD cue; already linked into `tools/tests` with edge, rear, invalid-input, and stabilization coverage. |
| `TgpManualAimMath.cs` | 76 | Pure azimuth/elevation/zoom-axis math for manual TGP pointing; linked into `tools/tests`. |
| `TgpFeedSettings.cs` | ~140 | TGP resolution/JPEG-quality name normalization and dimension resolution, plus the aspect-preserving resize and IR auto-levels math extracted from `TgpFeed.cs`/`SpriteCapture.cs`; linked into `tools/tests`. |

### Real logic worth partially extracting

| File | Lines | Touchpoints | The extractable core |
|---|---|---|---|
| `TelemetryServer.cs` | 2019 | 6 | The standout: only 6 real game touchpoints across 2000 lines. Nearly the whole file is JSON-string-building (`BdfBlock`, `MisBlock`, `ObjBlock`, `EscapeJson`, …) over already-extracted snapshot data, not live game state. Pulling that serialization layer into its own class is the highest-value single move here. |
| `AkfTracker.cs` | 157 | 6 | Weapon-attribution TTL bookkeeping, funds delta, kill categorization. Needs `PersistentID` and `Time.unscaledTime` swapped for a plain id/float parameter to go fully pure. |
| `Keybinds.cs` | 936 | 11 | Low density for its size. The tap-vs-hold arbitration (`PollTapHold`) is clean, separable logic buried in a large bind-table file. |
| `WeaponSelectors.cs` | 336 | 21 | Real cycle-selection algorithm (recall/advance/skip-depleted), tightly interleaved with live loadout reads — needs a plain loadout-entry DTO before it separates cleanly. |
| `HudWaypointCue.cs` | 220 | 25 | Small pure geometry kernel (bearing → tape position, edge-clamp math) inside an otherwise Unity-heavy `MonoBehaviour`. |

### Pure Unity/game glue — not a testability-refactor target

`AssetCapture.cs`, `TgpFeed.cs`, `SpriteCapture.cs`, `HudDeclutter.cs`, `HarmonyPatches.cs`,
`CommandDispatcher.cs`, `Plugin.cs`, `MissionLifecycle.cs`, `TelemetrySnapshot.cs` (plain DTO, no
logic), plus the trivial config wrappers (`ImmersionConfig.cs`, `HudDeclutterConfig.cs`,
`RatesConfig.cs`, `ConfigurationManagerAttributes.cs`, `ImmersionState.cs`).

## Test framework: xUnit

Two real options were on the table:

1. **A plain console app with manual asserts**, run via `dotnet run` — mirrors `src/web/`'s own
   `node file.test.js` convention exactly, zero new dependency.
2. **xUnit**, via a normal `dotnet test` project.

The JS side's "no framework" choice makes sense in its own context: `src/web/` ships with no
build tooling at all — no bundler, no `package.json` — so avoiding a JS test framework keeps that
true. The C# side doesn't have an equivalent purity to protect: `NOXMFD.csproj` already pulls NuGet
packages routinely (`BepInEx.Core`, `UnityEngine.Modules`, `BepInEx.PluginInfoProps`). Against that
backdrop, xUnit is the boring, standard choice, not an added dependency to justify — `dotnet test`
discovery, real IDE test-runner integration, readable assertion diffs, and it's what any C#
contributor already expects. **Recommended: xUnit.**

## Project shape

A new sibling project, not a `ProjectReference` to `NOXMFD.csproj` itself — that would drag in the
`$(GameDir)`-relative `Assembly-CSharp.dll`/`UnityEngine.Modules`/Mirage/Rewired references and
require a real Nuclear Option install just to build the test project. This repo already has the
precedent for a standalone sibling tool project: `tools/apicheck/` — its own `.csproj`, own TFM,
excluded from the main plugin build (`<Compile Remove="tools\**\*.cs" />` in `NOXMFD.csproj`).

- New folder: `tools/tests/` (or `NOXMFD.Tests/` at repo root — naming TBD), own `.csproj`,
  `net8.0` (no Unity-Mono constraint needed — the files under test have zero Unity references).
- References the specific pure `.cs` files directly (`<Compile Include="..\..\src\plugin\JsonLite.cs" />`,
  etc.), not the whole plugin project — keeps the test project's dependency graph limited to exactly
  what it tests.
- `PackageReference`: `xunit`, `xunit.runner.visualstudio`, `Microsoft.NET.Test.Sdk`.
- Already-excluded from `NOXMFD.csproj`'s own build the same way `tools/apicheck/` is, once the
  folder lands under `tools/`.

## Proposed incremental order

Smallest safe step first, each one a self-contained PR:

1. **Stand up the project** against `JsonLite.cs` (100% pure, zero extraction work) and
   `RouteStore.cs`'s mutators (need a small seam first — see the corrected survey note above:
   `BepInEx.Paths`/`Plugin.Log`/`TelemetryServer.EscapeJson` aren't available to a standalone test
   project as-is). Immediate real coverage either way, just not equally zero-effort.
2. **Extract `TelemetryServer.cs`'s JSON-writer layer** into its own class (`TelemetryJson.cs`?) —
   the single highest-value move, given how much of that 2000-line file it is. `docs/server-hardening.md`
   picks up from here — further splitting the rest of `TelemetryServer.cs` (asset serving, the
   command queue, SSE/MJPEG) once this piece is out, plus unrelated request-hygiene hardening on the
   command endpoints.
3. **Extract `AkfTracker.cs`'s attribution/bookkeeping logic** — swap `PersistentID`/
   `Time.unscaledTime` for plain parameters at the boundary.
4. **Extract `Keybinds.cs`'s tap/hold arbitration** into a pure function. — done in
   `KeybindTapHold.cs`, covered by `KeybindTapHoldTests.cs`.
5. **Extract `HudWaypointCue.cs`'s geometry kernel**.
6. **`WeaponSelectors.cs`** — lowest priority of the five; needs a loadout DTO layer first, more
   design work than the others before it's separable.

Each step should land with its own tests in the same PR — no extraction without the test that was
the point of doing it.

## Scope

- [x] Decide `tools/tests/` vs a root-level `NOXMFD.Tests/` project name/location
- [x] Stand up the xUnit project, wire `JsonLite.cs` + `RouteStore.cs` in as the first real tests
- [x] Extract and test `TelemetryServer.cs`'s JSON-writer layer — `TelemetryJson.cs`,
      `TelemetryJsonTests.cs`
- [ ] Extract and test `AkfTracker.cs`'s attribution/bookkeeping logic
- [x] Extract and test `Keybinds.cs`'s tap/hold arbitration — `KeybindTapHold.cs`,
      `KeybindTapHoldTests.cs`
- [ ] Extract and test `HudWaypointCue.cs`'s geometry kernel
- [ ] Design a loadout DTO, then extract and test `WeaponSelectors.cs`'s cycle-selection algorithm
- [ ] Confirm `dotnet test` runs clean in CI (if/when this repo gets CI — see
      `docs/ci-smoke-check.md`) alongside the existing `dotnet build` + `node *.test.js` checks
