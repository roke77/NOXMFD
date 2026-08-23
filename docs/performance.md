# Performance — RWR/MAP lag & FPS hit in busy matches (planning)

## Status

**Mixed — historical investigation with shipped fixes, plus open follow-ups tracked below.**
Items #A/#1/#2 shipped (see "Status (as of the #A/#1/#2 work)" further down); items #4/#5 remain
deliberately deferred; items #6/#7/#8 (added 2026-08-20) are open. Triggered originally by observed
symptoms in a high-activity match: noticeable lag in the RWR and MAP displays, plus a noticeable
in-game FPS hit. Both scale with unit count, which is why a busy furball hits them at the same time.

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
  sets `shadowBlur=8` (in `src/web/pages/map/map.js`), each RWR line
  `shadowBlur=6` (`:451`), each missile likewise. Canvas `shadowBlur`
  is one of the most expensive 2D ops — a per-draw-call blur pass. With
  40+ contacts that's 40+ blur passes 10×/sec. Almost certainly the
  single biggest client-side lag source, and the cheapest to fix:
  pre-bake the glow into the cached tinted-icon canvas
  (`tintedIcon`, `:368`) once, instead of blurring live every frame.
- **Two spots added after the #1 fix still use live `shadowBlur`,
  missed by it (found 2026-08-20):** `drawTargetBox`
  (`src/web/pages/map/map.js:265`, the locked-target corner brackets)
  and the route waypoint markers (`:497`) both set `shadowBlur` fresh
  on every draw, the exact pattern #1 eliminated for icons and RWR
  lines. Lower severity than the original finding — locked targets and
  route waypoints are usually a handful, not 40+ — but the fix is the
  same two-stroke technique `drawRwrLines` already uses two functions
  above `drawTargetBox` in the same file (see item #8 below).
- **Redraw driven directly by data arrival**, not `requestAnimationFrame`,
  so bursts aren't coalesced and redraw doesn't align to refresh.
- **No off-screen cull** — every contact is transformed and drawn even
  when off the visible canvas (matters most zoomed in).

### GPU (TGP feed) → confirmed real, still unfixed

See the 2026-08-16 section below for the measurements. The finding stands and
is **still fully reachable by the player, with no warning**: the RTS page's
TGP slider (`src/web/pages/rates/rates.html:54`) still exposes the full
`5-30 Hz` range, and 30 Hz is the exact setting the measurements showed
dropping 33-69 of every 5-second window's readback ticks (up to ~45%) with
worst-case frame times up to 100 ms. No cap, no in-UI cost warning, no
render-on-demand path exists yet. This is the single highest-value open item
— the investigation is already done, only the fix (item #6 below) is
missing.

### Game main thread → per-frame UI churn (new, found 2026-08-20)

`HudWaypointCue.LateUpdate` (`src/plugin/Hud/HudWaypointCue.cs:75`) rebuilds a
`string.Format` and sets Unity `Text.text` **every rendered frame** (60 Hz+,
not gated to any interval) whenever a waypoint route is active — even when
the displayed distance/bearing round to the same value as last frame.
`Text.text`'s setter dirties Unity UI layout on every set regardless of
whether the content actually changed. Same class of cost the "10 Hz
allocation churn" finding above already flagged, just at render-rate instead
of tick-rate, and narrower in scope (nothing else in this mod writes to a
Unity UI Text component outside a gated interval). See item #7 below.

### Server → wasted CPU (indirect FPS pressure) — HISTORICAL, fixed by item #2

This section describes the pre-fix state, kept for context on why item #2 (below) exists — it is
**not** current behavior. `Serialize` no longer re-runs per client; verify at
`src/plugin/Http/TelemetryServer.cs`'s frame-version cache.

- **`Serialize` re-ran in full, per client, every 100 ms**
  (`src/plugin/Http/TelemetryServer.cs`, called from `HandleSseAsync` —
  line numbers as they stood at the time this was written), and `string.Format` boxed every
  float/int/bool. Open the combined MFD + a separate RWR tab + a tablet = the entire contact list
  serialized 3× independently, 10×/sec.

### Latent data race — HISTORICAL, fixed by item #2

Also pre-fix state, kept for context.

`BuildParts` hands the shared `_partsBuf` reference into the snapshot
(`src/plugin/TelemetryReader.cs:854`); the background SSE thread serializes it
while the main thread overwrites it in place next tick. Units avoid this
today only because `BuildUnits` does `.ToArray()`. Serializing
once-per-tick (item #2) is the clean fix for both the duplicate work and
the race — and is shipped, per the "Status" section below.

### Watch-item, not currently a problem (found 2026-08-20)

`RouteStore.Save()` (`src/plugin/Stores/RouteStore.cs`) does a synchronous
`File.WriteAllText` on the main thread on every route/waypoint mutation —
the same shape as the ~9 ms `ConfigEntry.Value` write stall the 2026-08-16
section below found and fixed (by moving the RTS sliders from `input` to
`change`). Checked every caller: nothing fires it continuously today —
route/waypoint edits are discrete button clicks, map waypoint placement is a
single long-press, and the 1 Hz proximity-advance tick only calls `Save()`
when a waypoint is actually reached. No action needed now; flagged so a
future continuous-fire control (e.g. a drag-to-reorder UI) doesn't
reintroduce the same stall by calling a `RouteStore` mutator per input event
instead of on release — follow the `input`-for-display / `change`-for-persist
split noted in Finding 2 below.

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
| 6 | Cap the TGP slider below 30 Hz (or add an in-UI cost warning near it), or move toward render-on-demand | client+plugin | S–M | **Closes the one confirmed-and-measured, still-unfixed cost** — see the GPU/TGP section above | open |
| 7 | Throttle `HudWaypointCue`'s readout rebuild (skip the `Text.text` write when rounded values haven't changed, or gate to ~5–10 Hz) | main thread | XS | Removes 60 Hz string alloc + Unity UI layout-dirty churn | open |
| 8 | Extend #1's fix to `drawTargetBox` and the waypoint markers (two-stroke glow like `drawRwrLines`, or bake into a cached canvas) | client | XS | Removes the two live-`shadowBlur` spots #1 missed | open |

### Item #A — RESULT (done, commit eb2ecc7)

Replaced synchronous `SpriteToPng` with `SpriteCapture.Request` (`src/plugin/SpriteCapture.cs`):
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
- **#6 (TGP slider risk).** The measurements already exist (2026-08-16
  section below) — this item is closing the gap between "measured" and
  "fixed." Cheapest version: lower `RatesConfig.MaxHz` for the TGP group
  specifically (it currently shares the same 1-30 Hz bound as the telemetry
  tick, `src/plugin/RatesConfig.cs:23`) or add cost-warning text on the RTS
  page near the slider. The fuller version is render-on-demand or the
  mirror-cam approach `docs/tgp-high-quality-mode.md` scoped for an
  unrelated feature but with the same GPU-cost tradeoff shape.
- **#7 (waypoint HUD readout).** Simplest fix: compare the new formatted
  string against the last one written and skip the `Text.text` set when
  unchanged (cheap since the string is already built for the compare) —
  keeps the exact same visual update cadence a player would perceive as
  live, while eliminating the churn on frames where nothing moved enough to
  change the displayed digits.
- **#8 (target box / waypoint marker glow).** Same fix shape as #1: either
  bake into a small cached canvas per color (few distinct colors: target
  lock, next/reached/pending waypoint), or switch to `drawRwrLines`'s
  two-stroke technique (wide faint underlay + bright core, no `shadowBlur`
  at all) since both draw simple strokes/arcs, not a raster icon.

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
(pre-baked glow), **#2** (serialize-once). Per Step 0, the mod's
steady-state main-thread cost is ~3 ms/sec — under 0.3% of a 60 fps
frame — so there is **no remaining 10×-type win in the mod's CPU path**;
the sustained FPS hit in a busy match is the game rendering N units, not
us. The items below are what's left to *evaluate* before declaring the
floor, plus the marginal polish we deliberately deferred.

> **The instrumentation is no longer in the build.** `PerfDiag` and its two
> `Diagnostics` toggles (`PerfLogging`, `FeaturesActive`) were removed once the
> measurements above were banked and the CPU path was shown to be at its floor —
> they had no value during normal play. Everything below records what those
> measurements found and what remains unmeasured; to re-measure, restore
> `src/plugin/PerfDiag.cs` and its call sites from the history (they were dropped
> in one commit, so `git revert` brings back the whole apparatus).

## Next steps to evaluate (blind spots our instrumentation can't see)

By the end of its life the `PerfDiag` rollup also logged **avg / 1%-low / min
FPS** (`Time.deltaTime` sampled every frame; 1%-low = reciprocal of the
99th-percentile frame time) and **GC collection-count deltas** (`d0/d1/d2`
per gen, per window), folding the frame-time readout and blind-spot #3 into
the log itself — a comparative session needed no external FPS overlay. FPS was
only logged while a mission ran (the reader drove the per-frame `Tick`).
Caveat worth keeping in mind for any future session: FPS is *capped by VSync /
target framerate* — if the game is VSync-locked at 60, an A/B "win" shows up as
a higher 1%-low / fewer dips, not a higher avg.

`PerfDiag` measured **main-thread CPU time**, plus FPS/GC. Things it never
captured, ordered by value:

1. **A/B the mod's marginal cost — do this first.** With the in-mission toggle
   gone, the remaining method is the gold-standard one, which never depended on
   the mod's own instrumentation:
   - **DLL fully removed:** same busy mission, FPS (external overlay —
     Steam/RTSS) with `NOXMFD.dll` in `plugins/` vs. pulled out. This measures
     the mod's **total** cost — active per-frame work *and* the static cost of
     being loaded (idle HTTP listener thread, persistent MissionLifecycle
     Update, JIT/assemblies). Small delta → we're at the floor, stop.
     Surprising gap → something the CPU timers missed (GPU, render thread, the
     game reacting to our HUD-hiding) is at play.
   - The old `FeaturesActive` toggle split that delta in two by idling the
     reader while keeping the DLL loaded, isolating *active* from *static*
     cost. If a future investigation needs that split again, restore it from
     history rather than re-deriving it.

2. **GPU cost, especially the TGP feed.** `PerfDiag` was CPU-only. The TGP feed
   does a `Blit` + `AsyncGPUReadback` every frame *while a TGP pane is
   open* — real GPU work that was invisible to those timers. Method: FPS with a TGP
   pane open vs. closed in the same scene. If there's a gap: drop the TGP
   capture rate, or render-on-demand, or cap resolution further. (The
   capture is already gated on subscribers and async — see
   `TgpFeed.CaptureFrame` / `OnReadbackComplete`.)
   **Confirmed 2026-08-16 — see the dated section below.** This blind spot is
   no longer blind: it's a real, measured cost. Follow-up fix work is tracked
   as its own ticket, separate from the investigation that found it.

3. **GC allocation rate.** No GC spikes showed in `PushSnapshot`, but the
   10 Hz `.ToArray()` churn (units/rwr/mw) + the now-owned parts array do
   allocate. **Now logged** as `gc d0/d1/d2` deltas per 5 s window. Read it:
   high `d0` with `d1`/`d2` ≈ 0 is cheap gen-0 churn (mostly harmless); rising
   `d1`/`d2` means objects surviving to older gens → longer pauses → worth
   pooling/double-buffering those per-tick arrays (the old #3 idea, dropped
   on *CPU* grounds — BuildUnits was 0.08 ms — but GC pause is a separate
   axis). Low across the board → ignore.

The frame-time 1%-low and GC-count readouts this section asked for are now
in the rollup, so #1's FPS delta and #3's allocation pressure read straight
off the log. #2 (GPU/TGP) remains the one axis the rollup can't see — for
that, A/B a TGP pane open vs. closed and compare the logged FPS.

## 2026-08-16 — cfg-rates branch: TGP GPU cost confirmed, plus a config-write stall

The `cfg-rates` branch (issue #39) made the main telemetry tick and the TGP
feed's rate live-adjustable from a new RTS page, to gather real cost/benefit
data instead of reasoning about refresh rates abstractly. In-game testing
surfaced two findings — one closes blind-spot #2 above, the other is
unrelated but real.

### Method: `PerfLog.cs`

A small purpose-built rollup timer (`src/plugin/PerfLog.cs`), narrower than
the removed `PerfDiag` apparatus but covering the same idea — `using
(PerfLog.Time("name")) { ... }` around a hot-path block, logged every 5s via
the normal BepInEx log. Wrapped `ScanWorld`, `PushSnapshot`,
`TgpFeed.CaptureFrame`, and `RatesConfig`'s `ConfigEntry.Value` writes.
Extended with:

- **Frame time split by TGP subscriber state** (`PerfLog.Frame`, gated on
  `TelemetryServer.WantsTgpFrames`) — logs `frame(tgpOpen)` vs
  `frame(tgpClosed)` avg/max/spike-count separately, specifically to catch
  GPU cost a CPU `Stopwatch` around `CaptureFrame()` can't see.
- **`TgpFeed.ReadbackSkipCount`** — counts capture ticks dropped because the
  previous `AsyncGPUReadback` hadn't completed (`TgpFeed.cs`'s existing
  `if (_readbackInFlight) return;` guard), read-and-reset each rollup.

Removed from the tree once this investigation's findings were banked (same
lifecycle as the earlier `PerfDiag` apparatus) — the numbers above are what
it found. To re-measure or extend it for the follow-up TGP ticket, restore
`src/plugin/PerfLog.cs` and its call sites (`TelemetryReader.Update`,
`TgpFeed.Tick`/`CaptureFrame`, `RatesConfig.SetFastHz`/`SetTgpHz`) from
history rather than re-deriving it.

### Finding 1 — TGP's GPU cost is real, and pre-existing (not new to this branch)

`TgpFeed.CaptureFrame`'s own C# execution time stayed sub-millisecond even at
30 Hz (0.03ms avg) — the cost isn't in the code a CPU timer can see. But
`frame(tgpOpen)` windows showed markedly worse worst-case frame times than
`frame(tgpClosed)` windows throughout a session, **even at the old
hardcoded-15Hz default**:

```
frame(tgpOpen) avg=6,609ms max=76,737ms spikes=3/757   (tgpHz=15 — the OLD default)
frame(tgpOpen) avg=6,745ms max=57,827ms spikes=1/742   (tgpHz=15)
frame(tgpOpen) avg=6,135ms max=100,000ms spikes=1/815  (tgpHz=15; 100ms = Unity's deltaTime clamp — the real stall was longer)
```

versus clean `frame(tgpClosed)` windows before the TGP pane opened (avg
6-8ms, no big spikes). This means the `Blit` + `AsyncGPUReadback` GPU work
`TgpFeed.CaptureFrame` kicks off was already causing real, user-perceptible
stutter at the rate this mod has always shipped at — the `cfg-rates`
experiment didn't introduce this cost, it just built the first instrument
that could see it, and gave the player a dial that can push it further into
the red.

**Pushing TGP to 30 Hz makes it measurably worse.** `tgpSkipped` (readback
backlog) climbed to **33-69 dropped ticks per 5s window at 30Hz** — up to
~45% of attempted captures — versus the code's own assumption ("we'll only
skip one or two ticks per second under load," calibrated for 15Hz). The GPU
genuinely cannot keep up with 30Hz readback requests on the test machine.

**Decision:** leave the `cfg-rates` feature (CFG/KEY/LYT/RTS nav, the two
sliders) as-is — it's the tool that found this, not the bug. TGP GPU
performance work is tracked as its own follow-up ticket, separate from this
investigation. Candidates for that ticket to evaluate: capping the TGP
slider's usable range below 30Hz, render-on-demand instead of every capture
tick, or the mirror-cam approach `docs/tgp-high-quality-mode.md` already
scoped (unrelated feature, same GPU-cost tradeoff shape).

### Finding 2 — `ConfigEntry<T>.Value` can stall the main thread for ~9ms

Separately: `RatesConfig.FastHz.Value-write avg=8,973ms max=8,973ms n=1` — a
single BepInEx `ConfigEntry.Value` write (which triggers a synchronous `.cfg`
file save) blocked the main thread for ~9ms, more than half a 60fps frame
budget. Write costs varied wildly (0.001ms-8.97ms across different
sessions), consistent with OS/disk I/O jitter rather than CPU cost.

**Fixed**: the RTS page's sliders now fire `rates.set` on `change` (drag
release / arrow-key commit) instead of every `input` tick during a drag —
same live label update, far fewer writes, same end-user behavior. Any future
page adding a continuous-drag control backed by a `ConfigEntry` should use
the same `input`-for-display / `change`-for-persist split; every other
config-backed control in this codebase today is a discrete toggle/click, so
this is the first place the distinction mattered.

## 2026-08-20 — post-release code scan: TGP slider risk still live, three new spots found

A full read-through of `src/plugin/` and `src/web/` against this doc, prompted by wanting a
general "what's possible" performance pass rather than a specific reported symptom. No new
instrumentation — this was code inspection plus re-checking what the 2026-08-16 measurements
already established, applied to the files that didn't exist yet when this doc was last updated
(`RouteStore.cs`, `HudWaypointCue.cs`, `AkfTracker.cs`, `WeaponSelectors.cs`).

**Confirmed unchanged / still correct:** the three shipped fixes (#A, #1, #2) are still in place
in current code. `HudOptionsJson`'s 1 Hz unconditional refresh is deliberately cheap and already
reasoned about, not an issue. `AkfTracker.cs`/`WeaponSelectors.cs` — no LINQ or array allocation
in their hot paths. Items #4/#5 remain not implemented, still genuinely optional per the existing
"data doesn't justify it yet" call — nothing found that changes that.

**New, in priority order:**

1. **TGP slider risk (item #6) is the highest-value open item.** The 2026-08-16 measurements
   below already proved 30 Hz drops up to 45% of readback ticks with 100 ms worst-case frame
   times — that risk is still fully exposed to the player via the RTS page with no cap or
   warning. The investigation is done; only the fix is missing.
2. **`HudWaypointCue`'s per-frame readout rebuild (item #7)** — found while reading the newest
   HUD-drawing code, not present in the 2026-08-16 pass since the file didn't exist yet.
3. **Two live-`shadowBlur` spots in `map.js` missed the #1 fix (item #8)** — `drawTargetBox` and
   the waypoint markers, both added after #1 shipped. Same class of cost, smaller magnitude
   (few contacts vs. 40+).
4. **`RouteStore.Save()`'s synchronous write is a watch-item, not a current bug** — no caller
   fires it continuously today, but it's the same shape as the `ConfigEntry.Value` stall Finding
   2 (below) already found once. Recorded so a future continuous-fire caller doesn't reintroduce
   it silently.

## 2026-08-23 — tgp-safety-baseline branch: PerfLog recreated, cold-start bug confirmed

First of the three `docs/tgp-high-quality-mode.md` follow-up branches. `src/plugin/PerfLog.cs` had
to be **recreated**, not restored — despite this doc's own instruction to restore it from history,
it turns out the original was never committed; only its description here survived. Extended with
`TgpFeed.EncodeToJPG` timing (closing the "measure the JPEG step" blind spot from the HQ doc's
experiment menu) and a diagnostic warning for `HandleMjpegAsync`'s cold-start-stall theory. Item #6
above (TGP slider risk, no cap or warning) is also fixed on this branch — the RTS page now shows an
amber warning above 15 Hz, no hard cap.

Live-tested at the default 15 Hz and manually raised to 30 Hz via the RTS page.

**TGP GPU cost at 15 Hz, this session:** `frame(tgpOpen)` avg 7-9ms, max mostly 8-20ms, 0 spikes
(>20ms threshold) across ~20 rollup windows, `tgpSkipped=0` throughout. This doesn't match the
2026-08-16 numbers (avg 6.6ms but max 57-100ms with 1-3 spikes/window) — flagged as a discrepancy,
not a contradiction, since test conditions (target type, scene load, hardware) weren't controlled
to match between sessions. Don't treat either session's numbers as the definitive floor without a
controlled re-run.

**`EncodeToJPG` measured for the first time — not the bottleneck.** Consistently ~0.7-0.9ms avg
regardless of capture rate (15 or 30 Hz), a small fixed slice of the 7-9ms open-frame cost. The
JPEG step is not worth optimizing; the cost lives in the Blit + AsyncGPUReadback path, as already
suspected.

**30 Hz reproduces the 2026-08-16 skip-rate finding almost exactly.** `tgpSkipped` per 5s window:
36, 72, 19, 1, 11, 14, 6, 5, 30, 43, 5 (avg ~22/window, worst 72 of ~150 attempted ≈ 48%) — matches
the original "33-69 dropped ticks per 5s window, up to ~45%" on a different session. Confirms the
30 Hz risk is real and repeatable, not a one-off. Frame time itself did **not** degrade further at
30 Hz (still avg 6-8ms, close to the 15 Hz numbers) — the cost shows up as dropped/skipped captures
(a choppier delivered feed), not as worse overall frame stutter. One large spike (max≈46,700ms,
2/4397 samples in-window) appeared in the rollup immediately after switching to 30 Hz, but coincided
with an in-game map-capture burst — an unrelated scene-load artifact, excluded from the numbers
above.

**MJPEG cold-start bug: confirmed, twice, not just a theory anymore.**
```
TGP MJPEG cold start: client waited 3255ms with zero bytes before the first frame.
TGP MJPEG cold start: client waited 4297ms with zero bytes before the first frame.
```
Two separate cold connects, both multi-second silent stalls before any byte reached the client —
squarely in the range that can cause a browser to give up on a `multipart/x-mixed-replace` stream.
Follow-up ticket: fix it, but don't blindly port the source integration's placeholder-frame
workaround (`docs/tgp-high-quality-mode.md`) — that fix was tried and reverted there, for reasons
not documented on their side. Understand why before choosing an approach.

**Decision:** this branch's scope is done — the instrumentation answered the JPEG-cost and
cold-start questions, and reproduced the 30 Hz skip-rate risk live. Remove `PerfLog.cs` and its
call sites once the next two follow-up branches (MJPEG cold-start fix, HQ mirror camera) are done,
same lifecycle as `PerfDiag` before it.

## 2026-08-23 — tgp-mjpeg-cold-start branch: cold-start fix shipped, live-verified

Second of the three `docs/tgp-high-quality-mode.md` follow-up branches, off `tgp-safety-baseline`.
`HandleMjpegAsync` now writes a precomputed 4x4 dark-gray placeholder JPEG immediately on connect
if no real `_tgpJpg` exists yet, so a fresh client never sits on literal zero bytes. Deliberately
**not** the source integration's approach verbatim (its own placeholder fix was tried and reverted
there, undocumented why) — this one is a static, compile-time byte array rather than anything
generated at request time, so there's no runtime Unity-API-off-main-thread risk and no per-connect
allocation to reason about.

Live-verified end to end: Network tab showed the connection holding at ~0.3KB (the placeholder)
while idle with the TGP page open and nothing locked — previously this window was 0 bytes and the
theorized failure state. On locking a target the feed transitioned to a growing real stream (17.9MB
over a 4.3-minute mixed lock/unlock session), and correctly stopped growing on unlock. No errors,
no dropped readbacks (15 Hz session).

The cold-start diagnostic's logged duration is measuring wall-clock time from connect to the first
*real* frame, which includes however long the player takes to lock a target — not pipeline latency
alone. A 105-second reading during this test was the player's own delay before locking, not a
regression; the diagnostic doesn't distinguish the two. Fine to leave as-is since the number it
logs was never load-bearing for a pass/fail signal, only a "did it ever say near-zero" check for
the pre-fix behavior.

**Decision:** shipped. Third branch (HQ mirror camera) is next; `PerfLog.cs` stays until that one
also lands.

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

- Step 0 is done and its numbers are recorded above. If perf work reopens,
  restore the `PerfDiag` instrumentation from history first and re-measure —
  don't optimize against the old figures, the code has moved on since.
- After editing the embedded frontend (`src/web/shell/*` / `src/web/pages/*`),
  run `python tools/serve_web.py --open` and verify over HTTP.
- Live-test each shipped item in a busy match; the symptom is only
  reproducible under load.
