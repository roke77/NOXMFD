# NO XMFD — Development Practices

## Git workflow

- **Bug fixes** → commit directly to `main`. No branch.
- **Features** → a dedicated feature branch (e.g. `feature/wpn-weapon-select`).

## Scope discipline

- An "open question" noted in a planning doc is a question to *ask* the user — not
  license to pick an answer and ship it.

## Build & preview

- The frontend lives in real files under `src/web/` (`shell/`, `pages/<x>/`,
  `shared/`). `dotnet build` only validates C# routes and the embedded-resource
  manifest; it does **not** validate JS/CSS.
- Verify any frontend edit in the `tools/serve_web.py` harness: `preview_start` with the
  `.claude/launch.json` config `hud-web` (port 8782). Start it once per session and
  leave it running (don't stop it as cleanup) — it serves files off disk and `/`/`/f35`
  auto-reload on save (docs/live-reload.md). Restart only if `serve_web.py` itself
  changed.
- Perf A/B session reports go in `_scratch/perf-sessions/` (gitignored).

## Docs & comments

- Present tense, current-state only. Don't describe past changes or migrations ("X
  used to live here") — just state what's true now.
- No reader-directed meta-commentary ("this is the part people miss"). State the fact
  plainly; justifying a design choice is fine.
- Comments explain **why**, not what: a non-obvious choice, an engine/API constraint,
  an invariant, or a threading/lifecycle/ordering/caching assumption. A workaround's
  comment says when it can be removed. No history or process narration ("moved from
  X"), no exact line numbers, and a `TODO` needs a concrete condition or a linked
  issue/doc.
- `docs/` holds feature design docs, kept permanently as a record of how a feature was
  planned — the present-tense rule doesn't apply to them.

## Testing

- Run `tools/ci-check.ps1` before calling a change done (`dotnet build -c Release`,
  every `*.test.js`, `dotnet test` on `tools/tests/`, a `serve_web.py` route smoke).
  Visual changes still need the in-browser check.
- JS self-checks are `*.test.js` files beside the code: plain `node` scripts with
  asserts, no framework.
- Adding a new MFD page: follow the checklist in `docs/src-architecture.md` → "Shell
  hooks".
- `src/plugin/` is untested by design except its Unity-free pieces, which
  `tools/tests/` (xUnit, `docs/csharp-unit-testing.md`) compiles directly. Bringing
  another file under test means first removing its direct BepInEx/Unity references
  (the `RouteStore.cs` seam); Harmony/`MonoBehaviour`/game-object code stays out of
  scope.
- **Live-game verification.** When a change touches code only a running game can
  exercise (live telemetry, reflection into game fields, in-mission behavior), list
  what still needs a manual in-game check — in the commit message for a small change,
  in the relevant `docs/` design doc for a larger one.

## Unity / BepInEx safety

- **Main-thread discipline**: `TelemetryServer` handles HTTP/SSE on background
  threads. Anything that touches a Unity object or API must run on the main thread —
  route it through the `TelemetryReader.Update` / `CommandDispatcher.Drain` pattern.
- **Floating origin**: `transform.position` is not world position — the engine
  re-centers the world periodically. Use the game's `GlobalPosition()` /
  `ToGlobalPosition()` instead.
- **Rewired input**: joystick/HOTAS buttons are invisible to Unity's legacy `Input` and
  BepInEx's `KeyboardShortcut`; read them from Rewired directly. Rewired button indices
  don't match Unity's `JoystickButton*` values.
- **BepInEx config defaults**: changing a `ConfigEntry` default only affects freshly
  generated configs; existing `.cfg` files keep their value.
- **`Plugin` doesn't survive boot → MainMenu**: anything that must outlive it lives on
  the `NOXMFD_Worker` GameObject spawned on the first `sceneLoaded` (see `Plugin.cs`),
  never directly on `Plugin`.

## Build environment

- Building requires a local `GameDir.props` next to the `.csproj` (gitignored,
  machine-specific) pointing `$(GameDir)` at a Nuclear Option install. A fresh clone or
  a scratch worktree won't build without one.
- To explore the game's assembly, decompile with `ilspycmd` (dotnet global tool)
  rather than guessing at field names from observed behavior.

## Releases

- NOMM distribution comes from the NOMNOM registry (`KopterBuzz/NOMNOM`,
  `modManifests/NOXMFD.json`) with `autoUpdateArtifacts` on: each GitHub release is
  picked up automatically, with its `downloadUrl` built from the tag and zip name. No
  registry PR is needed per release.
- Package `NOXMFD_X.Y.Z.zip` with Python's `zipfile`: the DLL from
  `bin/Release/netstandard2.1/` as `NOXMFD/NOXMFD.dll`, nothing else.
- Release notes = a tight changelog of changes actually merged into `main` — no
  install/how-to boilerplate. Check `git merge-base --is-ancestor <branch> main` before
  including a branch's changes.
- Version bump: features bump minor, isolated fixes bump patch. If the user names a
  version that doesn't fit, flag it once, then follow their choice.

### Pre-release quality pass

Before a release version bump, release build, tag, or `gh release create`, run this over
everything changed since the last release tag (`git log <last-tag>..HEAD`). Ordinary
development builds aren't gated.

1. Refactor/extract where the diff introduced duplication or a file doing more than
   one job (SRP, DRY).
2. Check comment quality per Docs & comments.
3. Add unit tests for new pure-logic code (`tools/tests/`, xUnit).
4. Update any planning doc (`docs/`) the changed feature touches.
5. Update any user-facing manual (`man/`) the changed feature touches.
6. Update any source-level README (`src/plugin/README.md`, `src/web/README.md`, this
   file) the changed files touch.
7. Look for folder moves per Folder architecture.
8. Check error handling on important logic nodes — reflection lookups, file/network
   I/O, parsing, anything that can throw on unexpected input or a missing/renamed game
   API. The repo pattern is fail-safe with a logged reason (`Plugin.Log?.LogWarning`/
   `LogInfo`, e.g. `SpriteCapture`'s try/catch around its GPU readback), not a crash or
   a silent no-op.

Only after this pass is done (or confirmed to have nothing to change) should the
release proceed.

## Folder architecture

- Grow incrementally: create a folder only when moving at least two related files or
  extracting a real new module — no empty or single-file "someday" folders.
- Move pure/testable code before runtime-coupled code. Keep composition roots
  (`Plugin.cs`, `TelemetryServer.cs`, `mfd.js`, `f35.js`, page `<name>.js`) easy to
  find.
- Update `NOXMFD.csproj`/embedded-resource paths in the same commit as any move. One
  responsibility-group per commit.
- Keep page-specific policy files beside their page; no generic `components/`/`utils/`
  bucket on the web side, no generic `ReflectionUtils` on the C# side.
- A new internal MFD page is a new `src/plugin/InternalMFD/InternalMfd<Name>Page.cs`
  implementing `IInternalMfdPage` — don't grow an existing page's file or the
  controller.
