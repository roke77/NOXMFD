# Build warning cleanup

## Status

Complete for compiler warning cleanup. A fresh `dotnet build -c Release --no-incremental` on
2026-08-27 emits no C# nullable or unassigned-field warnings. The only remaining build warning is
the deliberately deferred `MSB3277` `System.IO.Compression` assembly-version conflict described
below; the build succeeds and MSBuild resolves it consistently.

## Where this came from

An external code-review agent noted the build passes but emits ~111 warnings and recommended
chipping them down "until warnings become meaningful again." Re-measured directly rather than taking
that count on faith — see below for what it's actually made of.

## Historical survey (measured 2026-08-20, `dotnet build -c Release --no-incremental`)

MSBuild reports **111 Warning(s)** total. Deduplicating by (file, line, code) across `src/plugin/`
gives **104 distinct nullable/unassigned-field warnings**, plus a separate, unrelated
**`System.IO.Compression` version-conflict warning (`MSB3277`)**. The 104 break down:

| Code | Count | Meaning |
|---|---|---|
| CS8600 | 35 | Converting a possibly-null value to a non-nullable type |
| CS8618 | 19 | Non-nullable field/property not set by the end of the constructor |
| CS0649 | 15 | Field never assigned (always its default value) |
| CS8602 | 14 | Dereference of a possibly-null reference |
| CS8603 | 10 | Possible null reference return |
| CS8604 | 9 | Possible null reference argument |
| CS8625 | 2 | Null literal converted to a non-nullable reference type |

By file:

| File | Warnings |
|---|---|
| `TelemetryReader.cs` | 29 |
| `CommandDispatcher.cs` | 28 |
| `WeaponSelectors.cs` | 16 |
| `AssetCapture.cs` | 13 |
| `Keybinds.cs` | 7 |
| `TelemetryServer.cs` | 7 |
| `AkfTracker.cs` | 2 |
| `TgpFeed.cs` | 2 |

## Two different problems hiding in one number

**Most of `CommandDispatcher.cs`'s 28 warnings are false positives, not debt.** 14 of the repo's 15
`CS0649` warnings live in `CommandDispatcher.cs`'s `CommandEnvelope` class (`cmd`, `group`, `on`,
`hz`, `bind`, `n`, `index`, `text`, `wz`, `key`, `wname`, `id`, `cid`, `wx`) — every field the
compiler thinks is "never assigned" is actually populated by `UnityEngine.JsonUtility.FromJson`
via reflection, which the compiler can't see through. A single `#pragma warning disable CS0649` (or
`[System.Diagnostics.CodeAnalysis.SuppressMessage]`) scoped to that one class, with a comment
explaining why, correctly resolves 14 of the 15 in one deliberate move — not per-field annotation,
and not "fixing" anything that's actually broken.

**The 15th `CS0649`, `Keybinds.cs:87`'s `BindDef.AxisValueNow`, is a different, more interesting
case — not a false positive, and not warning-cleanup at all.** Its own comment claims "per-frame
scratch (analog), valid only inside `Poll()`," and two other comments in the same file (`:259`,
`:354`) say `Poll()` reads it via a stored `BindDef` reference — but grepping the whole file finds
no assignment to it anywhere. The actual live analog-axis read path is `ReadAxis(BindDef bind)`
(`:843`), which returns the value as a plain function result and never touches the field. Either
the field is dead code left behind when `ReadAxis()` replaced whatever used to write it (and the
three comments describing it are now wrong), or something that was meant to consume this field
never got wired up. Flagged separately rather than folded into the pragma above.

**Resolved:** it's dead code, confirmed without needing an in-game trace — a full-repo grep found
only the field declaration and the three (now-corrected) comments, no read or write anywhere,
and unlike `CommandEnvelope`, `BindDef` is built with plain object initializers (`AddAxis`), never
deserialized via `JsonUtility` reflection, so there's no hidden writer to account for. The live
analog-cursor path (`Poll()` → `ReadAxis(bind)` → used inline in the cursor-vector calculation)
never touched this field to begin with. Removed the field and corrected the three comments.

**Current result:** later nullability cleanup removed the remaining 54 C# warnings from
`TelemetryReader.cs`, `WeaponSelectors.cs`, and `AssetCapture.cs`. The planned `WeaponSelectors.cs`
loadout DTO / cycle-selection extraction has since landed in `WeaponSelectorLogic.cs`; the other
pure-logic extractions remain separate tasks in `docs/csharp-unit-testing.md`. Compiler cleanup no
longer depends on those refactors.

## Execution record

1. **`CommandDispatcher.cs`**: one scoped pragma with an explanatory comment removed 14 warnings
   (~13% of the historical total). Its 15th `CS0649` sibling (`Keybinds.cs:87`) was excluded from
   suppression and resolved as dead code, as recorded above.
2. **`TelemetryReader.cs`** (29) and **`AssetCapture.cs`** (13): resolved by later nullable
   annotations and guards. Their Unity-heavy behavior remains covered by build/live verification.
3. **`WeaponSelectors.cs`** (16): nullable warnings resolved. Its loadout DTO and cycle-selection
   testability extraction later landed in `WeaponSelectorLogic.cs`.
4. **`Keybinds.cs`**, **`TelemetryServer.cs`**, **`CommandDispatcher.cs`**, **`AkfTracker.cs`**, and
   **`TgpFeed.cs`**: done in the item-1 pass with nullable annotations/guards only, no behavior
   refactor.
5. **`MSB3277` (`System.IO.Compression` conflict)**: separate from the nullable debt above — an
   assembly-version conflict between `netstandard.library.ref`'s `System.IO.Compression.dll` and
   `Mirage`'s dependency graph, resolved in MSBuild's favor already (build succeeds), so this is
   cosmetic today. Worth a `<Compile>`/binding-redirect investigation only if it ever starts causing
   an actual runtime `MissingMethodException` — not before.
6. **Follow-up no-incremental pass**: a later branch-local audit found warnings that an incremental
   build could hide. The reopened nullable sites in `AssetCapture.cs`, `TelemetryReader.cs`, and
   `TgpManualControl.cs` were resolved with null-aware signatures, local guards, and safe fallbacks.
   Use `--no-incremental` for future warning audits.

## Scope

- [x] Scope a `CS0649` suppression to `CommandDispatcher.CommandEnvelope` (14 of the 15) with an
      explanatory comment
- [x] Investigate `Keybinds.cs:87`'s `AxisValueNow` separately (not a suppression candidate) —
      confirm whether it's dead code or an unwired analog-cursor path (resolved: dead code, removed)
- [x] Suppress test-project-only `CS0649` noise for linked DTO/support files in
      `tools/tests/NOXMFD.Tests.csproj`
- [x] Sweep nullable annotations/guards in the small files (`CommandDispatcher.cs`, `Keybinds.cs`,
      `TelemetryServer.cs`, `AkfTracker.cs`, `TgpFeed.cs`)
- [x] Fix nullable warnings in `TelemetryReader.cs`/`AssetCapture.cs` opportunistically (no dedicated
      sweep on untested code)
- [x] Resolve `WeaponSelectors.cs`'s nullable warnings; its DTO/test extraction later landed in
      `WeaponSelectorLogic.cs`
- [x] Leave `MSB3277` alone unless it starts causing a real runtime failure
