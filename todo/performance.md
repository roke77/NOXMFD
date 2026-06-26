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

## Hot paths identified (code-anchored)

### Game main thread → FPS hit

Everything in `TelemetryReader.Update` (`src/TelemetryReader.cs:123`)
runs on Unity's main thread.

- **10 Hz allocation churn (GC stutter).** Every 100 ms,
  `PushSnapshot` (`src/TelemetryReader.cs:599`) allocates fresh arrays:
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

Redraw happens in `drawOverlay` (`src/ClientPage.cs:487`), invoked on
every SSE message (~10 Hz).

- **`shadowBlur` on every draw call — the prime suspect.** Each icon
  sets `shadowBlur=8` (`src/ClientPage.cs:393`), each RWR line
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
  (`src/TelemetryServer.cs:526`, called from `HandleSseAsync` `:505`),
  and `string.Format` boxes every float/int/bool. Open the combined MFD
  + a separate RWR tab + a tablet = the entire contact list serialized
  3× independently, 10×/sec. Should be serialized **once per tick** and
  shared by all SSE clients.

### Latent data race (fix opportunistically with #2/#3)

`BuildParts` hands the shared `_partsBuf` reference into the snapshot
(`src/TelemetryReader.cs:854`); the background SSE thread serializes it
while the main thread overwrites it in place next tick. Units avoid this
today only because `BuildUnits` does `.ToArray()`. Serializing
once-per-tick (item #2) is the clean fix for both the duplicate work and
the race.

## Plan, in priority order

| # | Change | Layer | Effort | Payoff |
|---|--------|-------|--------|--------|
| 0 | Measure: A/B mod, instrument hot paths | — | XS | Confirms targets |
| 1 | Pre-bake icon/line glow; kill live `shadowBlur` | client | S | **Biggest MAP/RWR win** |
| 2 | Serialize once per tick, cache by version, all SSE clients write the same bytes | server | M | Kills N×-per-client cost + boxing; also fixes the data race |
| 3 | Reuse buffers / eliminate 10 Hz `.ToArray()` churn | main thread | M | Kills GC stutter |
| 4 | Split rates: contacts ~3–4 Hz; RWR/MW + own-ship 10 Hz | both | M | Cuts main-thread, serialize, AND redraw cost together |
| 5 | rAF-coalesce client redraw + off-screen contact cull | client | S | Smoother when zoomed in |

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

## Recommended sequencing

1. **#0 measurement** — ~1 hour, produces the numbers that justify
   everything else.
2. **#1 and #2** — highest payoff-to-risk, roughly independent, safe to
   ship and live-test incrementally.
3. **#3 / #4** — structural follow-up, only if the numbers still show the
   main thread as the bottleneck after #1/#2.

## Out of scope

- Reducing what the game itself spends on a busy scene — only our
  marginal cost is in scope.
- Auto-degrading quality based on framerate. If we add rate/quality
  knobs, the player chooses them.
- Rewriting the transport (SSE → WebSocket) — not needed for these wins.

## Pre-flight before implementing

- Run Step 0 and record the numbers in this doc before touching #1+.
- After editing the embedded frontend (`ClientPage.cs` / `MfdPage.cs`),
  run `python tools/build_preview.py`.
- Live-test each shipped item in a busy match; the symptom is only
  reproducible under load.
