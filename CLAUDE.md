# NO XMFD — Development Practices

Project-specific rules for working in this repo. These are learned conventions, not
guesses — most exist because a past session violated them.

## Git workflow

- **Bug fixes** → commit directly to `main`. No branch.
- **Features** → a dedicated feature branch (e.g. `feature/wpn-weapon-select`), merged
  into `main` when ready. **Delete the branch (local + remote) immediately after
  merging** — do this in the same sitting as the merge, before moving on to
  versioning/release work.
- **"push" and "merge to main" are separate authorizations — "push" never implies the
  other.** "push" (on a feature branch) pushes that branch only, exactly as-is; it does
  not fast-forward-merge into `main`, and does not push `main`. Merging into `main`
  needs its own explicit instruction ("merge to main", "merge it in") given
  separately, even right after a "push" in the same turn. Default assumption on any
  feature branch is "stays a branch" until that explicit merge instruction arrives.
- **Never push unless the user's current message literally contains the word "push"**
  (or "upload"/"publish to remote"). This is the single most-violated rule in this
  project (10 documented incidents). None of the following authorize a push: "yes",
  "ok", "looks good", approving a proposed plan, or a content instruction like "update
  the readme" — even if that plan's last step was a push.
  - `git push` must never appear in the same tool call as `git add`/`git commit` —
    not even separated by a heredoc terminator with no `&&`. Before submitting any git
    command, scan the whole string for "push"; if it co-occurs with add/commit, split
    it out and drop the push.
  - Default end-of-turn state for any git-touching turn is **"committed, not
    pushed"** — say so explicitly and wait for the word.
  - A "push" said earlier in the session does not carry forward to later turns, even
    minutes later in the same conversation.
- **No pull requests.** Don't open one, don't offer one, and after a push don't
  mention/paste the "Create a pull request" link GitHub prints — just report the push
  landed.

## Scope discipline

- Build only what was asked. An "open question" noted in a planning doc is a question
  to *ask* the user — not license to pick an answer and ship it.
- No abstractions, boilerplate, or features beyond the explicit request, even when
  adjacent and seemingly obvious (e.g. don't add a new toggle/config/UI control that
  wasn't asked for while implementing a related feature).

## Build & preview

- The frontend lives in real files under `src/web/` (`shell/`, `pages/<x>/`,
  `shared/`) — not embedded in C# strings. `dotnet build` only validates C# routes and
  the embedded-resource manifest; it does **not** validate JS/CSS.
- Verify any frontend edit by loading it in the `tools/serve_web.py` HTTP harness
  (default port 8782, `.claude/launch.json` config `hud-web`). `tools/build_preview.py`
  is stale/obsolete for this purpose.
- Launch serve_web with PowerShell `Start-Process` (a Bash background job dies when
  the tool call ends):
  ```
  Start-Process -FilePath "python" -ArgumentList "tools/serve_web.py","--port","8782" -WorkingDirectory "<repo-root>" -WindowStyle Hidden -PassThru
  ```
  Start it once per session and leave it running — it reads files off disk per
  request, so edits are picked up live, and `/`/`/f35` auto-reload the browser within
  ~1s of a saved edit (docs/live-reload.md) — no manual refresh needed. Never stop it
  as a "cleanup" step after verifying; restart only if `serve_web.py` itself changed
  (that one case the auto-reload can't cover). If a port check is needed,
  use `Get-NetTCPConnection -LocalPort 8782 -State Listen` (port-based) to find the
  real owner — process-name filters can miss it (e.g. Windows Store Python).
- Use the shared `src/web/shared/theme.css` `var(--no-*)` color tokens instead of
  hardcoding hex values (`--no-green`, `--no-red`, `--no-amber`, `--no-bg`,
  `--no-panel-bg`, `--no-panel-border`, `--no-ink`, etc.). Deliberate literal-color
  exceptions are fine when called out (e.g. a true-black power-off state).
- Perf A/B session reports go in `_scratch/perf-sessions/*.txt` (gitignored) — never
  commit or push them. The PerfLogging feature code and its docs are committed
  normally.
- After a significant code change, `dotnet build -c Release` and let the
  `DeployToGame` MSBuild target copy the fresh DLL into the real game's
  `BepInEx/plugins` folder without waiting to be asked. This is a reversible, local
  action (overwrites a DLL on disk) — it's a separate gate from `git push`, which
  still needs the literal word per the Git workflow rules above.

## Docs & comments

- Present tense, current-state only. Don't describe past changes or migrations ("X
  used to live here") — just state what's true now.
- No reader-directed meta-commentary ("this is the part people miss", "you'd be
  surprised how often"). State the fact plainly; justifying a design choice is fine,
  editorializing about the reader is not.
- Comments explain **why**, not what: a non-obvious choice, a runtime/engine/API
  constraint, an invariant a future edit must preserve, or a threading/lifecycle/
  ordering/caching assumption. A workaround's comment says when it can be removed.
  Don't restate what the code already says, don't excuse unclear code instead of
  naming/simplifying it, and — same present-tense rule as above — don't narrate
  history or process (migrations, prior implementations, "moved from X", agent/session
  actions); that belongs in commits/PRs/docs, not the comment. Skip exact line numbers
  likely to drift. A `TODO` needs a concrete condition or a linked issue/doc, not a bare
  marker.
- `docs/` holds feature design docs (markdown). These are kept permanently as a
  historical record of how a feature was planned, even after it ships — unlike
  user-facing docs (README, etc.) and code comments, which stay present-tense-only.

## Testing

- `tools/ci-check.ps1` runs the checks below in one command (`dotnet build -c Release`,
  every `*.test.js`, `dotnet test` on `tools/tests/`, and a `serve_web.py` route smoke)
  — run it before calling a change done instead of running the pieces by hand. It's not
  a substitute for the manual in-browser verification workflow when the change is
  visual.
- After any change under `src/web/`, run the JS self-checks (`*.test.js` files
  alongside the code, plain `node` scripts with asserts — no framework) before calling
  the change done.
- Adding a new MFD page touches several files (NAV, `layout-pages.js`, `split-slots.js`,
  and two hand-written switches in `mfd.js` that nothing else implies) — see
  `docs/src-architecture.md`'s "Shell hooks" section for the full checklist and which
  test backstops each step. A page that passes every self-check and still does nothing
  when clicked in full view is missing one of those two `mfd.js` switches — that
  specific gap is what `classic-button-wiring.test.js` exists to catch.
- `src/plugin/` (the C# side) is mostly untested by design — most of it reflects into
  live Unity/game objects or subclasses `MonoBehaviour` and needs the game running to
  exercise; failures there are loud (crash/exception) rather than silent. The exception
  is `tools/tests/` (xUnit, `docs/csharp-unit-testing.md`), a standalone project
  covering the plugin's pure-logic files (`JsonLite.cs`, `RouteStore.cs` so far) by
  compiling them directly rather than referencing `NOXMFD.csproj` — that would drag in
  the `$(GameDir)`-relative Unity/game references and require a real Nuclear Option
  install just to build tests. Extending it to another file means giving that file the
  same BepInEx/Unity-free seam RouteStore.cs has (injectable fields instead of direct
  `BepInEx.Paths`/`Plugin.Log`/etc. references) before it can compile standalone; don't
  treat the rest of `src/plugin/` still lacking tests as an oversight — the
  Harmony/`MonoBehaviour`/game-object files are correctly out of scope for this, not
  merely not-yet-done.
- **Live-game verification.** `ci-check.ps1` and the browser-preview workflow can't
  exercise real Unity/game state (live telemetry, reflection into private game fields,
  in-mission behavior). When a change touches that kind of code, track what still needs
  a manual in-game check as a bullet list — in the PR/commit description for a small
  change, or in the relevant `docs/` design doc for a larger one — instead of letting it
  go unverified silently. From the post-0.26.0 refactor pass
  (`docs/post-0.26-refactor-analysis.md`) — confirmed live 2026-08-22 against a running
  Free Flight mission:
  - [x] BDF/PAL faction data displays correctly after `BuildFactionForces` (classic
    full-view, real Boscali/Primeva funds/ships/vehicles/buildings/aircraft counts).
  - [x] Fresh-mission vehicle/ship/building icon capture after `CaptureTypeIcons<T>`
    (TGT page's vehicle-type icons load with real pixel data, not broken images).
  - [x] Classic full-view page forwarding with live `bdf`/`pal` data, and classic
    split-view forwarding with live `map` data — both confirmed via the shell's actual
    `page-frame`/`pane-*` iframes, not just the raw SSE payload.
  - [x] Live telemetry frame still matches its pre-refactor shape after the
    `TelemetryJson.cs` extraction (`docs/refactor-scan.md` step 10) — confirmed against
    the actual rebuilt/redeployed/restarted plugin (not just the pre-extraction one),
    `/stream`'s `bdf`/`pal`/`mis`/`obj`/`akf`/`tgt` blocks all shaped and populated as
    expected.
  - [x] Fullscreen icon: the shared `--icon-fullscreen` token resolves to the same
    non-empty inline-SVG value in both classic and F-35 shells (pixel-identical
    rendering itself still wants a quick human eyeball — not verifiable headlessly).
  - [ ] CM category display and keybind-driven CM category cycling after
    `CmReflection` — needs an actual keypress in-game, not verifiable from the browser
    side; still pending.
  - [ ] RDR/RWR/MW contact rendering with a live radar-equipped aircraft and nearby
    threats — the session tested against had no radar and no contacts nearby, so the
    arrays were empty but unexercised.
  - [x] `Keybinds.cs`'s `FindBind(id)` extraction (`docs/refactor-scan.md` step 11) —
    confirmed live 2026-08-22: `keybind.set-key` round-trips, `arm-joy`/`cancel-joy` and
    `arm-axis`/`cancel-axis` no-op cleanly, `set-axis-invert` round-trips, an unknown
    bind id no-ops instead of erroring. `clear-joy` confirmed on an already-unassigned
    bind; `clear-axis` not exercised against a real HOTAS axis binding (would have wiped
    it) but shares identical code with the five tested paths.

## Unity / BepInEx safety

- **Main-thread discipline**: `TelemetryServer` handles HTTP/SSE on background
  threads. Anything that touches a Unity object or API (scene state, `CombatHUD`,
  input, etc.) must run on the main thread — route it through the
  `TelemetryReader.Update` / `CommandDispatcher.Drain` pattern rather than calling
  Unity APIs directly from a server thread.
- **Floating origin**: `transform.position` is not world position in this game — the
  engine re-centers the world periodically. Always compute true world position via the
  origin-correction helper rather than reading `transform.position` directly; this bug
  is easy to reintroduce in new telemetry/position code.
- **Rewired input**: joystick/HOTAS buttons are invisible to Unity's legacy `Input` and
  BepInEx's `KeyboardShortcut` — the game captures them via Rewired, so joystick binds
  must read from Rewired directly. Rewired's button indices do not match Unity's
  `JoystickButton*` enum values — don't assume they line up.
- **BepInEx config defaults**: changing a `ConfigEntry`'s default value only affects a
  freshly-generated config file. An existing user's `.cfg` on disk keeps whatever value
  it already has — changing a default is not a migration for existing installs.
- **`BepInEx_Manager` doesn't survive boot → MainMenu**: in this game/Unity version,
  anything living directly on `Plugin` (a `BaseUnityPlugin`) gets destroyed a few
  hundred ms after chainloader startup, before any mission can start —
  `DontDestroyOnLoad` is a no-op when called from BepInEx's preloader context, and
  re-calling it from `Plugin.Awake` doesn't help. The fix already in place
  (`Plugin.cs`): subscribe to `SceneManager.sceneLoaded`, and on the *first* real scene
  load spawn a self-created `NOXMFD_Worker` GameObject and mark *that*
  `DontDestroyOnLoad` — it survives because a real scene exists by then. Anything that
  needs to outlive `Plugin` belongs on that Worker GameObject (or another
  freshly-spawned persistent object created from a `sceneLoaded` callback), never
  directly on `Plugin` itself.

## Build environment

- Building requires a local `GameDir.props` next to the `.csproj` (gitignored,
  machine-specific) pointing `$(GameDir)` at a Nuclear Option install, since the
  project references the game's own managed DLLs. A fresh clone or a scratch worktree
  won't build without one.
- To explore the game's assembly when adding new telemetry fields or investigating
  game-side APIs, decompile with `ilspycmd` (dotnet global tool) rather than guessing
  at field names from observed behavior.

## Releases

- Git/GitHub tag = **bare semver, no `v` prefix** (`0.10.0`, not `v0.10.0`). The
  manifest `version`, the tag, and the artifact `downloadUrl` path segment must all
  match exactly — NOMNOM's auto-update derives download URLs from the bare version
  string, so a `v` makes it 404.
- Always cut a **full release** — never `--prerelease`. GitHub only gives the Latest
  badge, and only `/releases/latest` resolves, to a non-prerelease. If one was cut
  wrong: `gh release edit <tag> --prerelease=false --latest`.
- Release title is `NO XMFD X.Y.Z (Alpha)`, explicitly via `--title` — never the bare
  tag/version as the title.
- Every release **must have `NOXMFD_X.Y.Z.zip` attached** (single `NOXMFD/NOXMFD.dll`
  inside). A tag + notes with no asset silently breaks NOMM/NOMNOM auto-update — verify
  with `gh release view <tag> --json assets -q '.assets[].name'` before considering the
  release done.
- **Build the zip with Python's `zipfile` module, not PowerShell's
  `Compress-Archive`.** `Compress-Archive` has previously written backslash path
  separators into the archive, which breaks extraction on Linux — `zipfile` always
  writes forward slashes. Package as `NOXMFD/NOXMFD.dll` (the DLL from
  `bin/Release/netstandard2.1/`, no `BepInEx/plugins/` prefix — NOMM adds that itself)
  and verify with `python3 -c "import zipfile; print(zipfile.ZipFile('<zip>').namelist())"`
  before uploading.
- Release notes = a tight changelog of the actual changes only — no generic
  install/how-to instructions (that belongs in the README). Only include changes
  actually merged into the release's target branch (`main`); don't pull in changelog
  content from a branch discussed recently in conversation but not merged — verify
  with `git merge-base --is-ancestor <branch> main` first.
- Version bump: feature-sized merges have historically bumped minor (0.16.0, 0.17.0),
  isolated fixes bump patch. Flag once if the user names a version that doesn't match
  this pattern, then follow their explicit choice without re-raising it.

## Folder architecture

Current split: `src/plugin/` (C# runtime; `Hud/`, `Http/`, `Stores/`, `Telemetry/`, `Input/`,
`Tgp/`, and `Akf/` already broken out as internal groupings — see below), `src/web/pages/`
(page-specific browser code), `src/web/shell/`
(shell/layout code, mixing shared shell mechanics with classic/f35 subfolders),
`src/web/services/` (shared browser services), `src/web/shared/` (shared CSS/fonts/
tokens), `tools/` (preview, capture, CI, tests).

Grow this incrementally, not as a big-bang reshuffle — create a folder only when moving
at least two related files or extracting a real new module, don't pre-create empty
folders for a single file or a "someday" grouping (e.g. no `Builders/` folder until
there's a second builder to put in it). Move pure/testable code before risky
runtime-coupled code. Keep composition roots (`Plugin.cs`, `TelemetryServer.cs`,
`mfd.js`, `f35.js`, page `<name>.js` files) easy to find even as they shrink. Update
`NOXMFD.csproj`/embedded-resource paths in the same commit as any move. One
responsibility-group per commit — large reshuffles are hard to review and wreck blame.

`src/plugin/Hud/`, `src/plugin/Http/`, `src/plugin/Stores/`, `src/plugin/Telemetry/`,
`src/plugin/Input/`, `src/plugin/Tgp/`, `src/plugin/Akf/`, and `src/web/shell/shared/` are
done — the rest of `src/plugin/` stays flat until enough files are ready to move together.
If/when it grows enough to need more internal structure, this is the target shape:

- **`src/plugin/`**: `Core/` (`Plugin.cs`, `MissionLifecycle.cs`, `HarmonyPatches.cs`) ·
  `Http/` (`TelemetryServer.cs`, `TelemetryAssets.cs`, `TelemetryHttpRouter.cs`; remaining
  candidates are stream/session and command-endpoint extraction) · `Commands/` (`CommandDispatcher.cs`) ·
  `Stores/` (`RouteStore.cs`, `LayoutStore.cs`, `HudPresetStore.cs` — JSON-backed,
  test-friendly, avoid direct Unity/BepInEx coupling) · `Assets/` (`AssetCapture.cs`, `SpriteCapture.cs`) ·
  `Immersion/` (`ImmersionConfig.cs`, `ImmersionState.cs`) · `Config/` (`RatesConfig.cs`,
  `ConfigurationManagerAttributes.cs`) · `Interop/` (`CmReflection.cs` and future narrow,
  specifically-named reflection adapters — never a generic `ReflectionUtils` bucket) ·
  `Util/` (`JsonLite.cs`).
- **`src/web/`**: a `protocol/` folder for shared message names/payload contracts is
  worth creating once contracts are actually centralized, not for one file. Keep
  page-specific policy files beside their page (as `avn-throttle-policy.js`,
  `afm-bg-policy.js`, `map-transform.js` already do) — don't invent a generic
  `components/`/`utils/` bucket for a shell/page-oriented app.
- **`tools/`**: split `serve_web.py` into `preview/` + `preview/mocks/` only when its
  route helpers or mocks are being touched for real work, not preemptively.

## Repo-wide coding rules

(From the user's global CLAUDE.md — restated here since they apply directly to this
project.)

- No abstractions, dependencies, or boilerplate that weren't explicitly requested.
- Deletion over addition; boring over clever; fewest files possible.
- Root-cause fixes: grep every caller of a function before patching it, fix the shared
  function once rather than patching only the path a bug report names.
- Non-trivial logic leaves one runnable check behind (assert-based demo or a small test
  file — no frameworks/fixtures needed). Trivial one-liners need no test.
