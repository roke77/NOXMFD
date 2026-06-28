# Performance — RWR/MAP lag & FPS hit in busy matches (planning)

## Status

Planning only. No code yet. Triggered by observed symptoms in a
high-activity match: noticeable lag in the RWR and MAP displays, plus a
noticeable in-game FPS hit. Both scale with unit count, which is why a
busy furball hits them at the same time.

## The key insight

There are **two distinct problems in two different places**, not one:

- **FPS hit** lives on the Unity main thread, inside
  `TelemetryReader.Update` — per-tick work and allocations that scale
  with unit count.
- **RWR/MAP lag** lives in the browser, inside the canvas redraw — per
  contact drawing cost that scales with unit count.

A third, quieter cost (full re-serialization per client on a background
thread) competes for CPU cores and indirectly starves the game.

Because all three scale with unit count, they surface together — but
they need different fixes, so we treat them separately.

## Step 0 — measure before optimizing (do this first)

A 50-unit furball tanks FPS even with no mod loaded. We must isolate our
**marginal** cost before writing fixes, or we'll chase the wrong thing.

1. **A/B the mod.** Same busy mission, mod enabled vs. plugin DLL
   removed/renamed. Record the FPS delta. That's our real budget.
2. **Instrument the hot paths.** Temporary `Stopwatch` logging of average
   ms/call for:
   - `ScanWorld` (1 Hz scan + `FindObjectsByType`),
   - `PushSnapshot` → `BuildUnits` (10 Hz),
   - `Serialize` (length in bytes + time, per client).
   Cheap to add; turns "noticeable" into numbers we can target and
   verify against.

Do not start the structural fixes (#3/#4 below) until these numbers say
the main thread is actually the bottleneck.

## Measured — Step 0 results (2026-06-26)

First instrumented run: ~10 min busy match, **228 units**, 70–138 visible
contacts, **2 web clients** (MAP + RWR open). Decimal comma in the log is
locale formatting (`16,870ms` = 16.87 ms).

**Steady state (103 of 119 rollup windows) — our mod is nearly free:**

| Path | avg | per-second |
|------|-----|-----------|
| `ScanWorld` (1 Hz) | ~1.5 ms | ~1.5 ms/s |
| `PushSnapshot` (10 Hz) | ~0.16 ms | ~1.6 ms/s |
| `BuildUnits` (10 Hz) | **~0.08 ms** | negligible |
| `Serialize` (~20/s, 2 clients) | ~0.18 ms | background thread, ~8–13 KB payload |

Total steady main-thread cost ≈ **3 ms/sec** — under 0.3% of a 60 fps
frame. **Our steady-state cost does NOT explain a sustained FPS drop;**
the sustained part of the in-game FPS hit in a 228-unit match is
overwhelmingly the game itself rendering that many units, not us.

**The spikes (16 of 119 windows) — this IS our cost:**

- **Mission load: a single 673 ms freeze** = encoding the **16 MB**
  in-game map PNG on the main thread (`SpriteToPng`: `Graphics.Blit` →
  `ReadPixels` → `EncodeToPNG`, all synchronous).
- **Recurring mid-combat: 17–78 ms scan spikes, ~1–2×/min**, tracking
  rising contact counts. Cause: when a *new unit/aircraft type* first
  appears, `ScanWorld` synchronously extracts its icon (and, for new
  airframes, the 32-part silhouette). A steady drip of single-frame
  stutters in an evolving battle — this is the "FPS hit" feel.

### What this changes about the plan

1. **RWR/MAP lag is client-side.** Server cost is tiny, payloads are
   8–13 KB → the lag is in the browser canvas redraw. **#1 (shadowBlur)
   is confirmed as the right lead** for the lag symptom.
2. **The FPS hitches are the synchronous capture path, not per-tick
   work.** Original **#3 (BuildUnits / 10 Hz buffer churn) is
   effectively dead** — BuildUnits is 0.08 ms. The real FPS target is a
   new item (**#A** below): move icon/map/airframe captures off the
   synchronous main thread (we already solved this for TGP with
   `AsyncGPUReadback`), and shrink the 16 MB map.

Still worth doing: the A/B (DLL pulled) to put a number on the sustained
game-vs-mod split, but the instrumentation already makes the case.

## Hot paths identified (code-anchored)

### Game main thread → FPS hit

Everything in `TelemetryReader.Update` (`src/plugin/TelemetryReader.cs:123`)
runs on Unity's main thread.

- **10 Hz allocation churn (GC stutter).** Every 100 ms,
  `PushSnapshot` (`src/plugin/TelemetryReader.cs:599`) allocates fresh arrays:
  `BuildUnits` does `_unitBuf.ToArray()` (`:960`), plus `BuildRwr`,
  `BuildMw`, `BuildFailures` each allocate. In a busy match that's KBs
  of garbage 10×/sec → GC spikes → the "stutter" feel. `BuildParts`
  (`:844`) already reuses a buffer — that's the pattern to extend.
- **`BuildUnits` does per-unit work at 10 Hz.** `TryGetKnownPosition`
  for every visible unit, 10×/sec (`:935`). Units don't move far in
  100 ms at map scale, so this rate is overkill.
- **`ScanWorld`'s `FindObjectsByType<Unit>`** (`:150`) — the classic
  expensive call, but gated to 1 Hz, so it's a secondary target.

### Browser → RWR & MAP lag

Redraw happens in `drawOverlay` (`src/web/pages/map/map.js`), invoked on
every SSE message (~10 Hz).

- **`shadowBlur` on every draw call — the prime suspect.** Each icon
  sets `shadowBlur=8` (historically `ClientPage.cs`; now `src/web/pages/map/map.js`), each RWR line
  `shadowBlur=6` (`:451`), each missile likewise. Canvas `shadowBlur`
  is one of the most expensive 2D ops — a per-draw-call blur pass. With
  40+ contacts that's 40+ blur passes 10×/sec. Almost certainly the
  single biggest client-side lag source, and the cheapest to fix:
  pre-bake the glow into the cached tinted-icon canvas
  (`tintedIcon`, `:368`) once, instead of blurring live every frame.
- **Redraw driven directly by data arrival**, not `requestAnimationFrame`,
  so bursts aren't coalesced and redraw doesn't align to refresh.
- **No off-screen cull** — every contact is transformed and drawn even
  when off the visible canvas (matters most zoomed in).

### Server → wasted CPU (indirect FPS pressure)

- **`Serialize` re-runs in full, per client, every 100 ms**
  (`src/plugin/TelemetryServer.cs:526`, called from `HandleSseAsync` `:505`),
  and `string.Format` boxes every float/int/bool. Open the combined MFD
  + a separate RWR tab + a tablet = the entire contact list serialized
  3× independently, 10×/sec. Should be serialized **once per tick** and
  shared by all SSE clients.

### Latent data race (fix opportunistically with #2/#3)

`BuildParts` hands the shared `_partsBuf` reference into the snapshot
(`src/plugin/TelemetryReader.cs:854`); the background SSE thread serializes it
while the main thread overwrites it in place next tick. Units avoid this
today only because `BuildUnits` does `.ToArray()`. Serializing
once-per-tick (item #2) is the clean fix for both the duplicate work and
the race.

## Plan, in priority order (revised after Step 0)

| # | Change | Layer | Effort | Payoff | Status |
|---|--------|-------|--------|--------|--------|
| 0 | Measure: A/B mod, instrument hot paths | — | XS | Confirms targets | **done** (instrumented; A/B pending) |
| A | Async-ify captures (`AsyncGPUReadback`) + shrink the 16 MB map | main thread | M | **Kills the 673 ms load freeze + the mid-combat hitches — the real FPS cost** | **DONE** (commit eb2ecc7) |
| 1 | Pre-bake icon/line glow; kill live `shadowBlur` | client | S | **Biggest MAP/RWR lag win** (confirmed client-side) | **DONE** — glow baked into the tinted-icon cache; RWR lines use a 2-stroke glow. Verified in browser + in-game |
| 2 | Serialize once per tick, cache by version, all SSE clients write the same bytes | server | M | Kills N×-per-client cost + boxing; fixes the data race | **DONE** — GetFrameBytes version cache; BuildParts now owned. Verified in-game: 3 clients → Serialize ≈47/5 s window (≈10/s), not 3×. |
| 4 | Split rates: contacts ~3–4 Hz; RWR/MW + own-ship 10 Hz | both | M | Cuts redraw cost; modest server win | optional |
| 5 | rAF-coalesce client redraw + off-screen contact cull | client | S | Smoother when zoomed in | optional |
| ~~3~~ | ~~Reuse buffers / eliminate 10 Hz `.ToArray()` churn~~ | — | — | **Dropped** — BuildUnits measured at 0.08 ms; not a bottleneck | dropped |

### Item #A — RESULT (done, commit eb2ecc7)

Replaced synchronous `SpriteToPng` with `Capture.Request` (`src/plugin/Capture.cs`):
atlas-safe Blit → `AsyncGPUReadback` → background-thread `EncodeArray*`.
Map also downscales to 4096 + JPEG (16 MB → 3.3 MB served). Measured at
228 units:

- Map-load freeze (first `ScanWorld` max): **673 ms → 5.8 ms**.
- Mid-combat `ScanWorld` spikes: **17–78 ms (16/119 windows) → ≤12 ms
  (2/63)**. No capture errors; icons/map/airframe verified correct in-game.

Steady-state was already ~1.4 ms and is unchanged. Note: a later run had
**4 web clients** (≈40 `Serialize`/s) — still background-thread, but it
raises the value of #2.

### Item #A — async/shrink the capture path (implementation notes, for reference)

The spikes all come from `SpriteToPng` (`TelemetryReader.cs:968`) doing a
synchronous `Graphics.Blit` → `ReadPixels` → `EncodeToPNG` on the main
thread, called from `ScanWorld` for icons (`TryCaptureIcon`), the map
(`TryCaptureMap`), and airframes (`TryCaptureAirframe`).

- **Map (the 673 ms freeze):** 16 MB PNG is absurd for a map sprite.
  Downscale to a sane max dimension and/or encode JPEG; this also cuts
  the tablet's first map-load from 16 MB to ~hundreds of KB. Optionally
  move the encode off-thread.
- **Icons/airframes (the mid-combat hitches):** reuse the TGP path's
  `AsyncGPUReadback` pattern (`CaptureTgpFrame` / `OnTgpReadbackComplete`,
  `TelemetryReader.cs:1040`+) so the GPU readback doesn't stall the main
  thread, and encode PNG/JPEG on a background thread. Keep the existing
  per-scan budget (`IconsPerScan`) as a backstop.

### Notes per item

- **#1 (shadowBlur).** Lowest risk, immediately visible. Glow goes into
  the tinted-icon cache once per (type,color); for RWR lines, either drop
  the blur or pre-render a glowing line sprite. No behavior change the
  player should notice except smoothness.
- **#2 (serialize once).** Architecture: on `Push` (main thread), bump a
  version counter and store a snapshot that *owns* its arrays. A single
  serializer produces the UTF-8 bytes once per version (lazily, on a
  background thread — NOT on the main thread, or we'd add serialize cost
  to FPS) and caches them; every SSE client writes the same cached
  bytes. Resolves the data race as a side effect.
- **#3 (buffer reuse).** Extend the `_partsBuf` pattern to units/rwr/mw.
  Must coordinate with #2 so a reused buffer isn't read by the SSE thread
  mid-mutation (double-buffer/swap, or serialize-on-push-version).
- **#4 (split rates).** Contacts at map scale don't need 10 Hz; own-ship
  motion and threat cues do. Cuts cost on all three layers at once. Bigger
  change — sequence it after #1/#2 prove insufficient.
- **#5 (rAF + cull).** Coalesce: set `lastData` on message, request a
  single rAF redraw. Add a visible-bounds check before drawing each
  contact.

## Recommended sequencing (revised after Step 0)

1. **#0 measurement** — done. Numbers say: lag is client-side, FPS cost
   is the synchronous capture spikes (not per-tick work).
2. **#A + #1** — the two real targets, independent and both
   high-payoff/low-risk. #A kills the load freeze and combat hitches;
   #1 kills the RWR/MAP lag. Ship and live-test each separately.
   - Quickest single win inside #A: shrink the 16 MB map (downscale/JPEG)
     — removes the 673 ms load freeze with a small, contained change.
3. **#2 / #4 / #5** — only if still warranted after #A/#1, or if the user
   routinely opens many web clients (which multiplies #2).

## Status (as of the #A/#1/#2 work)

The three measured targets are shipped: **#A** (async captures), **#1**
(pre-baked glow), **#2** (serialize-once). PerfLogging now defaults OFF
(toggle on in the F1 menu to re-measure). Per Step 0, the mod's
steady-state main-thread cost is ~3 ms/sec — under 0.3% of a 60 fps
frame — so there is **no remaining 10×-type win in the mod's CPU path**;
the sustained FPS hit in a busy match is the game rendering N units, not
us. The items below are what's left to *evaluate* before declaring the
floor, plus the marginal polish we deliberately deferred.

## Next steps to evaluate (blind spots our instrumentation can't see)

`Diag` only measures **main-thread CPU time**. Three things it doesn't
capture, ordered by value:

1. **True A/B (mod fully removed) — do this first.** We *inferred* ~3 ms/s
   from per-path timers but never measured the real FPS delta with the DLL
   gone. Method: same busy mission, FPS with `NOXMFD.dll` in `plugins/`
   vs. pulled out. Small delta → we're at the floor, stop. Surprising gap
   → something the CPU timers miss (GPU, render thread, the game reacting
   to our HUD-hiding) is at play and becomes the next target.

2. **GPU cost, especially the TGP feed.** `Diag` is CPU-only. The TGP feed
   does a `Blit` + `AsyncGPUReadback` every frame *while a TGP pane is
   open* — real GPU work invisible to our timers. Method: FPS with a TGP
   pane open vs. closed in the same scene. If there's a gap: drop the TGP
   capture rate, or render-on-demand, or cap resolution further. (The
   capture is already gated on subscribers and async — see
   `CaptureTgpFrame` / `OnTgpReadbackComplete`.)

3. **GC allocation rate.** No GC spikes showed in `PushSnapshot`, but the
   10 Hz `.ToArray()` churn (units/rwr/mw) + the now-owned parts array do
   allocate. Method: log a `GC.CollectionCount(0/1/2)` delta over a match
   (cheap to add to the Diag rollup). If high → pool/double-buffer those
   per-tick arrays (the old #3 idea, dropped because BuildUnits *CPU* was
   0.08 ms — but GC pause cost is a separate axis we didn't measure). If
   low → ignore.

If #1 shows a gap, a quick **frame-time 1%-low / GC-count readout** added
to the Diag rollup would quantify #2/#3 directly. Not worth adding
speculatively.

## Marginal polish (deferred — data doesn't justify it yet)

- **#4 — split rates.** Contacts at map scale don't need 10 Hz; own-ship
  motion and threat cues do. Would cut redraw + serialize cost, but with
  #1/#2 done the remaining cost is already low. Revisit only if a future
  measurement shows client redraw still hurting.
- **#5 — rAF-coalesce + off-screen cull.** Coalesce redraws to one per
  frame (set `lastData` on message, request a single rAF) and skip
  contacts outside the visible canvas. Smoother when zoomed in with many
  contacts; small win post-#1.

## Out of scope

- Reducing what the game itself spends on a busy scene — only our
  marginal cost is in scope.
- Auto-degrading quality based on framerate. If we add rate/quality
  knobs, the player chooses them.
- Rewriting the transport (SSE → WebSocket) — not needed for these wins.

## Pre-flight before implementing

- Run Step 0 and record the numbers in this doc before touching #1+.
- After editing the embedded frontend (`src/web/shell/*` / `src/web/pages/*`),
  run `python tools/serve_web.py --open` and verify over HTTP.
- Live-test each shipped item in a busy match; the symptom is only
  reproducible under load.
