# CI smoke check — automate the checks already run by hand

## Status

Planning — not started.

## Where this came from

An external code-review agent recommended "a simple CI script that runs `dotnet build`, all Node
tests, and a `serve_web.py` smoke." This repo has no CI today — every check below is run manually,
by convention, before calling a change done.

## What already exists, unautomated

- `dotnet build -c Release` — 0 errors is the bar (warnings tracked separately, see
  `docs/build-warning-cleanup.md`).
- `find src/web -name '*.test.js' -exec node {} \;` — the full JS self-check suite (21 files as of
  2026-08-20), plain `node` scripts with asserts, no framework.
- Manual load of `tools/serve_web.py` in a browser after any frontend edit (CLAUDE.md's own "Build &
  preview" section) — currently a human-driven step, not scripted.

## Why this is small and low-risk

Nothing here is new logic — it's wiring three already-trusted, already-run checks into one script
(and optionally a GitHub Actions workflow) so they run automatically instead of relying on every
session/contributor remembering to run them by hand. Two are already trivially scriptable
(`dotnet build`, the `node *.test.js` loop); the third (`serve_web.py` smoke) needs a thin addition —
start the server, hit `/` and a couple of representative page routes, assert 200s, shut it down —
not a browser-driven check, just an HTTP reachability smoke test distinct from the full manual
verification workflow.

## Proposed shape

A single script (`tools/ci-check.ps1` or `.sh`, or both — this repo's dev environment is Windows but
GitHub Actions runners default to Linux, so a cross-platform version matters if this becomes an
Actions workflow rather than a local-only script):

1. `dotnet build -c Release` — fail on any error (warnings don't fail the check; see
   `docs/build-warning-cleanup.md` for that separate effort).
2. Run every `src/web/**/*.test.js` and `tools/*.test.js` via `node`, fail on any non-zero exit or
   non-"OK"/"passed" output.
3. Start `tools/serve_web.py` on a scratch port, `curl`/fetch `/` and a small representative set of
   page routes (e.g. `/afm`, `/map`, `/hud`), assert HTTP 200, then stop the server. Not a substitute
   for the manual in-browser verification workflow — just proves the harness itself still boots and
   routes correctly.

Local script first; a GitHub Actions workflow (`.github/workflows/ci.yml`) running the same script on
every push/PR is the natural follow-on once the script exists and is trusted, but is a separate,
optional step — this doc's scope is just having the check exist and be runnable in one command.

## Relationship to `docs/csharp-unit-testing.md`

That doc's own scope checklist already ends with "Confirm `dotnet test` runs clean in CI (if/when
this repo gets CI) alongside the existing `dotnet build` + `node *.test.js` checks" — this doc is
what makes "if/when this repo gets CI" concrete, and gives that future `dotnet test` step something
to plug into rather than starting CI from scratch at that point.

## Scope

- [ ] Write `tools/ci-check.ps1`/`.sh` covering steps 1-3 above
- [ ] Verify it catches a deliberately-broken build, a deliberately-failing JS test, and a
      deliberately-broken `serve_web.py` route as a sanity check on the check itself
- [ ] (Optional, separate follow-on) Wire the script into a GitHub Actions workflow
- [ ] Once `docs/csharp-unit-testing.md`'s xUnit project exists, add `dotnet test` as a fourth step
