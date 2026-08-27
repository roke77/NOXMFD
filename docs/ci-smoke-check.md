# CI smoke check — automate the checks already run by hand

## Status

Implemented locally. `tools/ci-check.ps1` runs the Release build, every JavaScript self-check,
the standalone xUnit project, and a `serve_web.py` route smoke in one command. `tools/ci-check-selftest.ps1`
validates that the script fails when each class of check is deliberately broken. A hosted GitHub
Actions workflow remains an optional follow-on, but the full plugin build is blocked on ordinary
GitHub-hosted runners unless the workflow can provide `GameDir` and the Nuclear Option managed DLLs;
the manual in-browser workflow remains separate.

## Where this came from

An external code-review agent recommended "a simple CI script that runs `dotnet build`, all Node
tests, and a `serve_web.py` smoke." This repo has no hosted CI workflow today. The local
`tools/ci-check.ps1` script is the required one-command check before calling a change done.

## What the local script automates

- `dotnet build -c Release` — 0 errors is the bar (warnings tracked separately, see
  `docs/build-warning-cleanup.md`).
- `find src/web -name '*.test.js' -exec node {} \;` — the full JS self-check suite (22 files under
  `src/web/`, plus 2 more under `tools/`, as of 2026-08-20), plain `node` scripts with asserts, no
  framework.
- `dotnet test tools/tests/NOXMFD.Tests.csproj` — the pure-logic C# test project.
- A `tools/serve_web.py` HTTP route smoke on a scratch port. Visual frontend verification remains
  human-driven, as required by CLAUDE.md's "Build & preview" section.

## Why this is small and low-risk

Nothing here is new product logic. The script wires four existing checks together so they run in
one command instead of relying on every session or contributor to invoke them separately. The
`serve_web.py` portion starts the server, hits representative page routes, asserts HTTP 200, and
stops it; it is an HTTP reachability smoke distinct from the full manual visual workflow.

## Implemented shape

A single local script, `tools/ci-check.ps1`. A cross-platform entry point matters only if a hosted
Actions workflow is added later:

1. `dotnet build -c Release` — fail on any error (warnings don't fail the check; see
   `docs/build-warning-cleanup.md` for that separate effort).
2. Run every `src/web/**/*.test.js` and `tools/*.test.js` via `node`, fail on any non-zero exit or
   non-"OK"/"passed" output.
3. Start `tools/serve_web.py` on a scratch port, `curl`/fetch `/` and a small representative set of
   page routes (e.g. `/afm`, `/map-view?bare`, `/hud`), assert HTTP 200, then stop the server. Not a
   substitute for the manual in-browser verification workflow — just proves the harness itself
   still boots and routes correctly. **Use `/map-view?bare`, not `/map`** — `/map` is the captured
   map *image* endpoint (`serve_web.py`'s `_asset_ref('map')` lookup), which 404s in a clean CI
   checkout with no `preview/captures/` populated; `/map-view` is the actual MAP page route and
   always renders regardless of capture state, matching how the shell itself loads it.
4. Run `dotnet test tools/tests/NOXMFD.Tests.csproj`.

A GitHub Actions workflow (`.github/workflows/ci.yml`) running checks on every push is an optional
follow-on. A full hosted workflow cannot run the current `dotnet build -c Release` step on an
ordinary GitHub-hosted runner because `NOXMFD.csproj` references `$(GameDir)\NuclearOption_Data\Managed`
assemblies and deploys to `$(GameDir)\BepInEx\plugins`. Practical CI choices are either a partial
portable workflow (`dotnet test tools/tests/NOXMFD.Tests.csproj`, Node tests, and `serve_web.py`
smoke) or a self-hosted Windows runner with Nuclear Option installed. This document's implemented
scope remains the local one-command check plus its self-test.

## Relationship to `docs/csharp-unit-testing.md`

That doc's own scope checklist already ends with "Confirm `dotnet test` runs clean in CI (if/when
this repo gets CI) alongside the existing `dotnet build` + `node *.test.js` checks" — this doc is
what makes "if/when this repo gets CI" concrete. Locally, the xUnit step is already part of
`tools/ci-check.ps1` and covered by `tools/ci-check-selftest.ps1`; hosted CI remains a separate
decision because of the `GameDir` dependency above.

## Scope

- [x] Write `tools/ci-check.ps1` covering steps 1-3 above
- [x] Verify it catches a deliberately-broken build, a deliberately-failing JS test, a
      deliberately-failing xUnit test, and a deliberately-broken `serve_web.py` route —
      `tools/ci-check-selftest.ps1`
- [ ] (Optional, separate follow-on) Wire checks into GitHub Actions, either as a portable partial
      workflow or on a self-hosted runner that can provide `GameDir`
- [x] Add `dotnet test` as a fourth step
