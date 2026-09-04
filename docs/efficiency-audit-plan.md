# Efficiency audit execution plan

## Status

Planning doc for executing on `docs/plugin-efficiency-audit.md` (41 findings) and
`docs/web-efficiency-audit.md` (42 findings). Both audits are investigation-only; this document
decides what actually gets built, in what order, and — just as importantly — what does not get
built and why.

## Scope decision

83 findings is a menu, not a checklist. The plugin audit's own framing measured steady-state
main-thread cost at ~3 ms/sec, under 0.3% of a 60 fps frame, and called that number good; most of
both audits' Tier 2/3/4 items are GC-pressure and micro-allocation cleanup with no reported
symptom behind them. Grinding through all of it without a measured problem to point at is scope
creep dressed up as diligence.

This plan implements a small, high-confidence cut and explicitly parks the rest. Anything parked
gets revisited only if a real symptom shows up (a reported stutter, a slow cold-load complaint, a
memory-growth report) — not on a schedule.

Three items found during the audits are correctness bugs, not efficiency findings, and are staged
first regardless of any perf reasoning:

- SQD's aircraft column freezes (stale cache, not a redraw problem) — plugin audit, "Correctness
  issues found in passing."
- `HudTtiCue.Build()` failure re-runs a full scene scan every frame for the rest of the mission
  once it fails once — plugin audit, same section.
- `keybinds.js`'s `flashRejected` indexes into the wrong table for any Immersion-section bind, so
  the "rejected" flash silently no-ops or hits the wrong row — web audit, "Correctness issues
  found in passing."

A fourth correctness item — TD's PAD-cursor block being dead code (`td.js` never wires the
`cursor`/`cursor-focus` message branches `wpt.js`/`sqd.js` have) — needs a human decision before
it can be staged: delete the ~23 dead lines, or restore the missing wiring so TD actually gets a
PAD cursor. Flagging here; decide at the start of Stage 1 rather than guessing now.

## Stage 1 — correctness fixes

**Done.** Branch: `audit-correctness-fixes` (off `main`, since these are unrelated bugs bundled as
one reviewable unit rather than the usual single-fix-direct-to-`main` pattern).

- Fix SQD aircraft-column staleness: `Squad.RebuildState()` called from the 1 Hz slow tick beside
  `Squad.CheckLiveness()`, per the audit's proposed fix. SSE layer already change-gates by string
  comparison, so no extra wire traffic.
- Fix `HudTtiCue.Build()`'s unthrottled retry: a `float _nextBuildAttempt` back-off.
- Fix `keybinds.js`'s `flashRejected` table mismatch: resolve the row in whichever table
  (main vs. Immersion) actually contains the rejected bind's id.
- Decide and implement the TD PAD-cursor question (delete or restore).

Each fix gets the smallest test the existing conventions support — an xUnit test for the plugin
side where one already exists for the touched file, a `.test.js` for the web side following the
`sig`/`builtKey` precedent already in the codebase.

## Stage 2 — plugin per-frame allocation fixes (net-negative or near-zero diff, verified findings)

**Done.** Branch: `audit-plugin-frame-allocs` (off `audit-correctness-fixes`'s tip, since finding
03 touches the same file — `HudTtiCue.cs` — as Stage 1's retry-backoff fix).

- **01+02**: rewrite the TD-assign `PollTapHold` loop to stop allocating 9 display classes/frame;
  read `Time.unscaledTime` once. Net shorter than what it replaces, no behavior change.
- **03**: restore `HudTtiCue`'s intended 4 Hz throttle (drop the `_cachedTti < 0f` retrigger).
- **05**: unlocked fast-path guard in `RemoteInputState` for the no-remote-browser-connected case.

All three are Confirmed/verified findings with effort described as "net −" or a handful of lines,
and none require a measurement harness to trust — the diff itself is the evidence.

## Stage 3 — plugin asset serving (measurable, user-facing)

Branch: TBD, off `audit-plugin-frame-allocs`'s tip until the earlier stages are merged to `main`.

- **13**: cache decompressed embedded assets in a `ConcurrentDictionary<string, byte[]>`, and stop
  linear-scanning 109 manifest names per request.
- **14**: serve gzip when `Accept-Encoding` allows it, for text/JSON/SVG assets. Per the review
  annex: must send `Vary: Accept-Encoding`, use the compressed `Content-Length`, and keep ETag/304
  correct for both compressed and uncompressed clients; never gzip PNG/woff2.

This is the one pair in either audit with an actual before/after number attached without needing
new instrumentation — file size (168.5 KB → 52.3 KB on the largest JS asset) is measurable today.
Worth doing before restoring `PerfLog.cs` for that reason.

## Stage 4 — web Tier 1 render guards (batched)

Branch: TBD.

Findings 01-09 from the web audit, batched as one pass per the audit's own suggested sequencing —
every one is the same "add a comparison before the write" shape, so uniform risk, done together.
Excludes:
- **10** (AKF SSE-layer signature) — per the review annex, the naive signature can suppress a real
  update once the feed hits its 50-entry cap; needs to wait for plugin finding 26's explicit feed
  version first, so plugin and web ship the same contract together.
- **19** (`_postUp` double-wrap) — per the review annex, measurement-gated: changes an ownership
  contract to save one small allocation, not worth it without profiling showing it matters.

Each guard needs its invalidation triggers named explicitly (page entry, SSE reconnect, mode
change, etc. — per finding) and a test proving both halves: unchanged input skips the write,
changed input still renders on the next tick.

## Parked (not scheduled)

Everything else in both audits: plugin findings 06-12, 15-27 and the 28-41 deletion/dedup tier;
web findings 11-18, 20-25, 40-42 dedup tier. Rationale is the same throughout — real but small,
no reported symptom, and several (15, 16, 21) carry the review annexes' warnings about needing
bounded-concurrency or buffer-ownership work that raises the risk past what the payoff justifies
right now. Revisit if a specific symptom surfaces.

Restoring `PerfLog.cs` is a prerequisite for any future stage that claims a frame-time win from
inspection rather than a plain measurable quantity (like Stage 3's file sizes) — per both audits'
own explicit instruction not to claim a win without it.

## Sequencing notes

Stages are independently shippable; later stages are not blocked on earlier ones except where
noted (Stage 4's exclusion of finding 10 waits on a plugin-side change, not a plugin stage as
scheduled here). Version-bump and release cadence follows existing project convention — each
stage's own commits decide whether it's a patch or minor bump when it ships.
