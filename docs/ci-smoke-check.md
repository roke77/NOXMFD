# CI smoke check — automate the checks already run by hand

## Status

Implemented locally. `tools/ci-check.ps1` runs the Release build, every JavaScript self-check,
the standalone xUnit project, and a `serve_web.py` route smoke in one command. A hosted GitHub
Actions workflow remains an optional follow-on; the manual in-browser workflow remains separate.

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

A GitHub Actions workflow (`.github/workflows/ci.yml`) running the same checks on every push is an
optional follow-on. This document's implemented scope is the local one-command check.

## Relationship to `docs/csharp-unit-testing.md`

That doc's own scope checklist already ends with "Confirm `dotnet test` runs clean in CI (if/when
this repo gets CI) alongside the existing `dotnet build` + `node *.test.js` checks" — this doc is
what makes "if/when this repo gets CI" concrete, and gives that future `dotnet test` step something
to plug into rather than starting CI from scratch at that point.

## Scope

- [x] Write `tools/ci-check.ps1` covering steps 1-3 above
- [ ] Verify it catches a deliberately-broken build, a deliberately-failing JS test, and a
      deliberately-broken `serve_web.py` route as a sanity check on the check itself
- [ ] (Optional, separate follow-on) Wire the script into a GitHub Actions workflow
- [x] Add `dotnet test` as a fourth step
