# Plugin efficiency audit — `src/plugin/` hot paths

## Status

**Investigation only — nothing implemented.** 41 findings, none of them applied. This is a
planning document: it records where the plugin still wastes work, what the fix for each looks
like, and which ones are not worth doing. It complements `docs/performance.md`, which holds the
*measured* history and the shipped items; this file holds the unshipped candidates found by a
full read of the C# source.

Audit run against `main` at `92c8783` (version 0.39.1).

## Scope and method

All 74 files under `src/plugin/` (18,107 lines) read in full, split across six subsystem passes:
telemetry, HTTP/SSE, TGP, HUD/asset-capture, input/dispatch, and stores/squad. Every candidate
was traced to its caller to establish real cadence before being recorded — a cost only matters
once it is known whether it runs per frame, per tick, per request, or once. Game-side costs were
checked against the decompiled sources in `_scratch/full/`.

Findings marked **[verified]** were re-checked against the tree by hand after the read; the rest
rest on a single careful pass. Each finding carries a confidence:

- **Confirmed** — read directly, no assumption.
- **Likely** — reasoned, with the assumption named.

### Cadence facts the findings depend on

| Path | Rate | Source |
| --- | --- | --- |
| `TelemetryReader.Update` fast tick | 10 Hz (`FastInterval`, 1-30 Hz settable) | `TelemetryReader.cs:14`, `RatesConfig.cs:42` |
| Contact snapshot rebuild | 4 Hz (`ContactInterval`) | `TelemetryReader.cs:18` |
| `ScanWorld` slow scan | 1 Hz (`SlowInterval`) | `TelemetryReader.cs:15` |
| SSE frame push | 10 Hz per client (`FrameEveryMs`) | `SseHub.cs:19` |
| SSE loop wakeup | ~62 Hz per client (`CursorTickMs`) | `SseHub.cs:18` |
| TGP capture | 15 Hz default, slider to 60 Hz | `TgpFeed.Interval`, `RatesConfig.TgpMaxHz` |
| `MonoBehaviour.Update`/`LateUpdate` | per frame (60 Hz+) | 9 files carry a per-frame entry point |

## Framing — where the mod's cost actually is

`docs/performance.md` measured steady-state main-thread cost at **~3 ms/sec, under 0.3% of a
60 fps frame**, and its structural items are shipped (async sprite captures, serialize-once per
tick with a version cache, the 4 Hz contact split, client-side glow baking and off-screen
culling). `docs/refactor-scan.md` records a completed SRP/DRY pass over the largest files.

So most findings below are **GC pressure and frame-spike reduction, not average-FPS recovery**.
That is the axis prior work left open: `performance.md` dropped its buffer-reuse item on CPU
grounds after measuring `BuildUnits` at 0.08 ms, while noting that GC pause is a separate axis
from CPU time. Three findings are about throughput rather than garbage: **04** (the world scan),
**13** and **14** (the HTTP asset path — a cold tablet load, not the game thread).

`PerfLog.cs` and `PerfDiag.cs` are both out of the tree, so the build carries no instrumentation.
Restore `PerfLog.cs` from history before claiming a frame-time win from anything here.

## Priority table

Effort is a rough diff size, not a schedule. "Net −" means the fix deletes more than it adds.

| # | Finding | Cadence | Effort | Confidence |
| --- | --- | --- | --- | --- |
| 01 | TD-assign loop allocates 27 objects per frame | per frame | net − | Confirmed [verified] |
| 02 | `Time.unscaledTime` read 12× per frame | per frame | 3 lines | Confirmed [verified] |
| 03 | `HudTtiCue` 4 Hz throttle defeated | per frame | 4 lines | Confirmed [verified] |
| 04 | `ScanWorld` uses `FindObjectsByType` over a live registry | 1 Hz | 4 lines | Likely [verified] |
| 05 | 7 locks + 7 `UtcNow` per frame for remote input | per frame | 4 lines | Confirmed |
| 06 | TGP full-screen overlay recomputed at 60 Hz | per frame | 6 lines | Confirmed |
| 07 | Three HUD cues build strings their guards discard | per frame | ~25 lines | Confirmed |
| 08 | JSON array items box into `params object[]` | 10 Hz | ~15 lines | Confirmed [verified] |
| 09 | 1 Hz blocks re-serialized at 10 Hz | 10 Hz | ~15 lines | Confirmed |
| 10 | Blocks materialized as intermediate strings | 10 Hz | ~20 lines | Confirmed |
| 11 | `CursorJson()` formatted before its change check | ~60 Hz/client | ~20 lines | Confirmed |
| 12 | Extension events copy a dictionary per tick | ~62 Hz/client | 5 lines | Confirmed |
| 13 | Embedded assets re-read per request | per request | ~8 lines | Confirmed [verified] |
| 14 | No gzip on 1.18 MB of text assets | per request | ~15 lines | Confirmed [verified] |
| 15 | Requests handled inline on the accept thread | per request | ~8 lines | Confirmed [verified] |
| 16 | Multi-MB `byte[]` per captured TGP frame | per capture | ~15 lines | Confirmed |
| 17 | 10 Hz boxing reflection for keypress state | 10 Hz | 6 lines | Confirmed |
| 18 | Four 1 Hz scans rediscovering fixed data | 1 Hz | ~30 lines | Confirmed |
| 19 | Route save: 2 serializations + file copy per mutation | per mutation | 5 lines | Confirmed [verified] |
| 20 | Squad lock relay is O(members²) messages | per message | 6 lines | Confirmed |
| 21 | `SendToAll` re-encodes per recipient | per broadcast | ~20 lines | Confirmed |
| 22 | Squad-target lookup walks members per HUD marker | per frame | ~15 lines | Confirmed [verified] |
| 23 | Three TGP paths re-assert more often than the game | per frame | ~20 lines | Likely |
| 24 | Weapon-selector loadout built twice per snapshot | per snapshot | ~8 lines | Confirmed |
| 25 | CM category re-scanned reflectively per held frame | per held frame | 8 lines | Confirmed |
| 26 | AKF feed copied 1 Hz, re-serialized 10 Hz | 10 Hz | ~15 lines | Confirmed |
| 27 | `GetFrameBytes` copies the snapshot on cache hits | 10 Hz/client | 6 lines | Confirmed |
| 28-41 | Deletions and simplifications (see that section) | — | ~200 lines out | Confirmed |

## Tier 1 — per-frame waste

Only nine files carry a per-frame entry point (`HudDeclutter`, `HudFocusMark`, `HudPower`,
`HudSquadTargetMark`, `HudTgpCue`, `HudTtiCue`, `HudWaypointCue`, `MissionLifecycle`,
`TelemetryReader`), so this list is short.

### 01 — TD-assign loop allocates 27 objects every frame [verified]

`Input/Keybinds.cs:941-947`, `PollTapHold` at `:1061`. Per frame, unconditional.

Both lambdas capture the loop-local `s`, so each of the 9 iterations allocates a display class
plus two `Action` delegates — ~1,600 objects/sec at 60 fps, even with every TD bind unbound,
which is the shipped default. The loop sits deliberately above the
`if (!any && !anyRemoteFire) return;` early-out at `:955` so a release on an idle frame still
resets `PressStartTime`, so it never gets skipped. The other three `PollTapHold` call sites
capture nothing and are free.

**Fix:** call `KeybindTapHold.Poll` directly and branch inline. The two lambda bodies differ only
in `retain`, and `TdStore.Assign(int slot, bool retain = false)` already has the matching shape,
so the rewrite is shorter than what it replaces. No behavior change — same state machine, same
order.

### 02 — `Time.unscaledTime` fetched 12 times per frame [verified]

`Input/Keybinds.cs:1063`. Per frame, once per tap/hold bind.

Two combat-mode binds, one waypoint bind and nine TD slots each read it independently: 12
managed→native transitions per frame for a value that cannot change within a frame.

**Fix:** read once at the top of the tap/hold section and pass it through. `KeybindTapHold.Poll`
already takes `now` as a parameter, so nothing downstream changes. Folds into 01.

### 03 — `HudTtiCue`'s 4 Hz throttle is defeated whenever no missile is airborne [verified]

`Hud/HudTtiCue.cs:50` (condition), `:57-61`, `Hide()` at `:147`. Per frame.

`Hide()` resets `_cachedTti = -1f`, and the recompute condition includes `|| _cachedTti < 0f`, so
as soon as there is no TTI to show the guard inverts into a trigger and the recompute runs every
frame. That recompute is `TargetTtiEstimator.ComputeTti`, which walks the whole
`UnitRegistry.allUnits` list with a type check per element. The intended cadence is stated in the
file: "Recomputing TTI walks UnitRegistry.allUnits, so keep it on the same 4 Hz cadence as the
contact snapshots." A target locked with nothing in flight is the normal state.

**Fix:** drop `|| _cachedTti < 0f` from the condition and replace the `Hide()` at `:59` with
`SetVisible(false); return;` so the cached "no TTI" result survives to the next frame. `Hide()`
stays as-is on the genuine no-target path at `:39`, and `targetId != _cachedTargetId` still forces
a recompute on a new lock.

### 04 — `ScanWorld` uses `FindObjectsByType<Unit>` when the game keeps a live registry [verified]

`Telemetry/TelemetryReader.cs:242`. 1 Hz.

The most expensive main-thread call in the mod, and the one `performance.md` measured at 17-78 ms
spikes before the capture path was made async. The game maintains
`UnitRegistry.allUnits` — a live `List<Unit>` added on register and removed on disable — and this
mod already reads it in two other places (`Hud/TargetTtiEstimator.cs:15`,
`Tgp/TgpManualControl.cs:334`). `performance.md` files this call as "a secondary target… gated to
1 Hz"; the call is avoidable outright rather than only gateable.

**Fix:** copy rather than alias — keep a reusable `List<Unit>` and
`Clear()` / `AddRange(UnitRegistry.allUnits)`, so the game cannot mutate the collection
mid-iteration. Every downstream consumer already null-guards `definition` and `NetworkHQ`, which
is what a fresh registry entry can briefly lack. `TargetSelectionPolicy.IsDisclosed` takes
`_cachedUnits`, not `_units`, so it is unaffected.

**Assumption to check in-game:** membership parity between `FindObjectsByType` and `allUnits` for
the units this mod reports.

### 05 — Seven locks and seven `DateTime.UtcNow` calls per frame with no browser connected

`Input/Keybinds.cs:877`, `:900-901`, `:949-952`; `Input/RemoteInputState.cs:28-42`, `:79-92`.
Per frame, unconditional — all seven sit above the `:955` early-out.

Each call takes a lock, reads `DateTime.UtcNow`, and for the fire states hashes a string key into
a dictionary, to discover that no remote browser has sent input. That is the steady state for any
keyboard/HOTAS-only player.

**Fix:** an unlocked guard in `RemoteInputState` — `SetFire`/`SetCursor` bump a
`volatile int _writes`, and the getters return the empty result immediately while it is zero. TTL
and min-press semantics are untouched for anyone actually connected. A monotonic `volatile int` is
safe to read unsynchronized.

### 06 — TGP full-screen overlay fully recomputed at 60 Hz for a 10 Hz readout

`Tgp/TgpFullScreen.cs:134` → `PopulateOverlay` `:140-222`. Per frame; the only gates above `:134`
are `Active`/pause/aircraft/landing and `HudVisible` (default true).

Three costs, none gated:

- A 60 km `Physics.Raycast` plus a fresh grid-label string, via
  `TgpManualControl.ComputeOverlaySample` → `TryGetLookPoint`.
- Roughly 14 string allocations from interpolation and `UnitConverter.*Reading` (each of which
  interpolates in turn) for values like Mag, GRID and ALT that change a few times a second. Where
  TMP's `text` setter early-outs on an identical string, the interpolation has already allocated.
- A per-target lock-box projection, plus a delegate allocated at the call site each frame
  (`:149`, an instance method group, so not cached by the compiler).

The native in-cockpit overlay this mirrors runs at 10 Hz, driven by the game's own
`StartSlowUpdate`.

**Fix:** accumulate `dt` in `Tick` and call `PopulateOverlay` at ~10 Hz, hoisting the two
`SetActive` calls at `:158-159` out so crosshair visibility stays frame-exact. 6× reduction on all
three costs; the video feed itself stays per-frame.

**Note:** the lock boxes are not dead — `Telemetry/TelemetryReader.cs:894` consumes
`Overlay.Boxes` — so this is over-computation for a slower consumer, not unused work.
`TgpFullScreen` itself does not read them (see its own comment at `:146`).

### 07 — Three HUD cues build strings every frame that their own write-guards discard

`Hud/HudWaypointCue.cs:69-80`; `Hud/HudTtiCue.cs:62` with `Hud/HudTtiMath.cs:44-48`;
`Hud/CombatHudMarkerLookup.cs:23`. Per frame.

The `Text.text` / `TMP_Text.text` write-guards are correct and load-bearing — those setters dirty
Unity UI layout even on an unchanged value, which is why `performance.md` item #7 exists. The
waste is upstream of the guard:

- The waypoint readout costs ~6 allocations per frame (an `int.ToString`, a concat, a
  `params object[]`, two boxes, a conditional name concat, the result) before the comparison.
- The TTI label costs ~4 (`FormatTti`'s two concats with an `int` box, plus `"TTI " +`) for a
  value gated to 4 Hz two lines above.
- `CombatHudMarkerLookup.TryGet` does a reflection `FieldInfo.GetValue` plus a type test twice per
  frame — both `HudFocusMark` and `HudSquadTargetMark` call it — for `CombatHUD.markerLookup`, a
  field initializer that is only ever mutated in place via `Add`/`Remove`/`Clear`, never
  reassigned.

**Fix:** compare quantized inputs (tenths of a km, integer bearing, index, steer flag, name)
instead of the formatted string; move the TTI label build inside the existing recompute block; and
memoize the marker dictionary against the `CombatHUD` reference, which also covers the per-respawn
HUD rebuild both callers already detect. Keying on the reference rather than assuming a global is
the correctness requirement.

## Tier 2 — per-tick and per-request waste

### 08 — Every JSON array item boxes its fields into a `params object[]` [verified]

`Telemetry/TelemetryJson.cs:494-505` (worst), same shape at `:297`, `:341`, `:374`, `:388`,
`:404`, `:422`, `:437`, `:464`, `:517`, plus the 33-argument header at `:23-54`. 10 Hz per
snapshot version, on the SSE thread.

`UnitsArray` passes 14 arguments per unit through `AppendFormat` — one `object[14]` allocation plus
a box for every `float`, `uint` and `byte`, then a composite-format parse of the template. At 200
contacts that is 200 arrays and ~2,400 boxes per frame, ~24,000 boxes/sec, before RDR, HSD, RWR,
MW and parts add their own.

**Fix:** chained `Append` with explicit `ToString("0.0", CultureInfo.InvariantCulture)` for floats
— never bare `Append(float)`, which uses current culture. For an allocation-free version, one
private helper using `float.TryFormat(Span<char>)` (netstandard2.1 has it) into a
`stackalloc char[16]`. The existing `{n:0.0}` precision specifiers must be reproduced exactly or
clients see different precision. `TelemetryJson` is pure and already has a standalone test seam,
so this is coverable by a check.

See also the documentation correction at the end of this file: `performance.md` currently records
this boxing as already fixed.

### 09 — Blocks that only change at 1 Hz are re-serialized at 10 Hz

`Telemetry/TelemetryJson.cs:94`, `:104`, `:122-127`; sources built in `ScanWorld` at
`Telemetry/TelemetryReader.cs:312-316`. Rebuilt at 1 Hz, serialized at 10 Hz.

`bdf`, `pal`, `mis`, `obj`, `akf`, `loadout` and `pylons` are all built inside the 1 Hz slow scan,
but the frame cache keys on `_snapVersion`, which every fast tick bumps — so nine tenths of the
serialization is discarded. Per frame that is ~72 `BdfCountArray` rows across two factions, the
AKF feed arrays, the OBJ tree, and `MisBlock`'s full `MisDescription`, which the snapshot
documents as "full multi-paragraph text", re-escaped and re-copied ten times a second.

**Fix:** the pattern already exists — `soiJson` and `extSlicesJson` are passed into `Serialize` as
pre-rendered JSON strings. Cache one `_slowBlocksJson` string per second in `ScanWorld` and
`Append` it verbatim. Built on the main thread and immutable thereafter, so handing it to the SSE
thread is the same contract `soiJson` already relies on. The data is already only sampled at 1 Hz,
so no additional staleness is introduced.

### 10 — Each block and array is materialized as an intermediate string, then copied in

`Telemetry/TelemetryJson.cs:94-127`, every `private static string *Array(...)` returning
`sb.Append(']').ToString()`, `:249-275`; `Http/TelemetryServer.cs:465`. 10 Hz.

Each of ~15 blocks builds a complete string in its own `StringBuilder`, `ToString()`s it, and
copies it into the outer builder — for contacts that is tens of KB per hop. `BdfBlock` and
`PalBlock` add a `string.Format` result plus eight `+` concatenations each. The finished frame is
then copied once more only to bracket it: `GetBytes("data: " + payload + "\n\n")`. Total is
roughly 2-3× the frame's bytes in transient garbage per frame. `new StringBuilder(2048)` at `:15`
is also an order of magnitude under the real frame size, so it regrows every time.

**Fix:** convert the writers from `static string X(...)` to
`static void AppendX(StringBuilder sb, ...)` — the call sites in `AppendFramePayload` already hold
`sb`. Mechanical, ~20 signatures. Do the SSE bracketing in `TelemetryServer` (a 3-argument
`GetBytes` into a pre-sized array) rather than changing `Serialize`'s contract, which the
standalone tests and tools depend on. Raise the initial capacity while there.

### 11 — `CursorJson()` is formatted and boxed before the change check that discards it

`Http/SoiFocus.cs:286-289`, called from `Http/SseHub.cs:143`. Once per 16 ms tick, per SSE
connection, before the comparison.

A 6-argument `string.Format` — a `params object[]` plus two float and three long boxes — built
only to be discarded by the `string.Equals` on the next line whenever nothing moved, which is the
common case. With 4 displays open that is ~250 formatted strings and ~1,250 boxes per second, plus
a duplicate `Encoding.UTF8.GetBytes` per client on the ticks that do change.

**Fix:** mirror the `GetFrameBytes` version cache. Add a `_cursorVersion` bumped in
`SetCursorVector`, `CursorSelect`, `SetCursorSelectHeld` and `MapAction`, and a
`GetCursorBytes(out long version)` that caches formatted bytes shared across clients; `SseHub`
then compares a `long`.

**Ordering requirement:** bump the version *after* the value write, or a reader caches stale bytes
under the new version. Three of the four writers already hold `_lock`; `SetCursorSelectHeld` at
`Http/SoiFocus.cs:177` is a bare `Volatile.Write` and needs the same lock or an ordered
`Interlocked.Increment` after the write.

### 12 — Extension SSE events allocate a full dictionary copy per tick, per connection

`Extensions/ExtensionRegistry.cs:110-111`, consumed at `Http/SseHub.cs:238`. ~62 Hz per connected
display. Flagged independently by two of the six passes.

The zero-extension case is free (a shared `_emptyEvents`), but `_events` is never cleared, so once
any extension publishes a single event every tick allocates and copies a dictionary that exists
only for per-key comparison. Three displays is ~190 dictionary allocations/sec on HTTP worker
threads.

**Fix:** either add an `_eventsVersion` counter incremented in `PublishEvent` and skip the whole
block unless it moved, or return the `ConcurrentDictionary` directly and let `SseHub` iterate it —
its enumerator is lock-free and does not throw on concurrent mutation. The channel is documented
in `Extensions/Api.cs:28-30` as per-name last-write-wins, so a consistent multi-key snapshot is not
required; a value shipping one tick earlier is harmless, and the client-side `lastExtEvents` gate
still holds.

### 13 — Embedded assets are re-read from the assembly and copied twice on every request [verified]

`Http/TelemetryAssets.cs:46-58`. Per asset request.

A fresh decompress, a doubling-growth `MemoryStream`, and a `ToArray` copy every time, for data
that cannot change within a run. `shell/classic/mfd.js` is 168.5 KB, so that one file costs
~340 KB of transient allocation per request, on the accept thread (see 15). The `no-cache` ETag at
`:37-44` makes a warm reload mostly 304s, but a first load, a hard refresh, a new tablet, or any
client with cleared storage pays it for every asset — a classic-shell load pulls 24 `/assets/`
sub-requests from `mfd.html` plus each split pane's own page, realistically 50-70 requests.

**Fix:** one `ConcurrentDictionary<string, byte[]>` keyed on the resolved resource name, populated
with `GetOrAdd`. Assets are immutable for the process lifetime, so no invalidation. Worst case
1.18 MB resident, which the resources already occupy in the DLL image.

While in the file: `FindResourceName` at `:90-98` linear-scans all 109 manifest names with
`EndsWith(…, OrdinalIgnoreCase)` per request, before the 304 shortcut, so even a fully-cached
reload pays it 24-70 times. A lazy `Dictionary<string,string>` keyed by the `".web."`-onwards
suffix makes it one `TryGetValue`.

### 14 — 1.18 MB of text assets served uncompressed, with no gzip anywhere in the plugin [verified]

`Http/TelemetryAssets.cs:60-63`. Per asset request.

No `Accept-Encoding` check and no `Content-Encoding` header exists anywhere in `src/plugin/` (zero
grep hits for gzip, `GZipStream`, `Accept-Encoding` or `Content-Encoding`). Measured on the actual
files in the tree:

| Asset | Raw | gzip | Saved |
| --- | ---: | ---: | ---: |
| `shell/classic/mfd.js` | 168.5 KB | 52.3 KB | 69% |
| `shell/classic/mfd.css` | 63.5 KB | 34.8 KB | 45% |
| `shell/f35/f35.js` | 84.5 KB | — | — |
| `pages/map/map.js` | 64.1 KB | — | — |
| 109 embedded files, total | 1.18 MB | — | — |

**Fix:** when `Accept-Encoding` contains gzip and the content type is `text/*`,
`application/json` or SVG, serve pre-gzipped bytes with `Content-Encoding: gzip`. Compose with 13
— cache both the raw and the gzipped array in the same `GetOrAdd`, so compression happens once per
asset per process. `System.IO.Compression.GZipStream` is BCL, no dependency.

`Content-Length` must be the compressed length. Do not gzip the PNGs or woff2.

### 15 — Every non-SSE request, body write included, runs inline on the single accept thread [verified]

`Http/TelemetryServer.cs:503-510` (accept loop), `:522-545` (`TrackRequestAsync`);
`Http/TelemetryHttpRouter.cs:123`. Every request to every synchronous endpoint.

`TrackRequestAsync` is `async` but has no yield point for synchronous endpoints: `RouteAsync`
executes its handler body synchronously and returns `Task.CompletedTask`, so
`await …ConfigureAwait(false)` never yields. The blocking
`ctx.Response.OutputStream.Write(body, 0, body.Length)` therefore pushes up to 168 KB to a
possibly-slow wifi tablet before `GetContext()` is called again. A browser opens ~6 parallel
connections and has them serviced strictly one at a time. `/command` POSTs read their request body
on the same thread (`Http/CommandEndpoint.cs:124`), and `entry.Resolve(relPath)`
(`Http/ExtensionEndpoint.cs:68`) runs arbitrary extension code there.

**Fix:** keep registration synchronous — that is what makes the shutdown invariant at
`Http/TelemetryServer.cs:402-404` ("joining it seals registration") hold — then dispatch the
routing. Split into a sync `Register(id, ctx, path)` plus
`_ = Task.Run(() => RunAsync(request, ct))`.

Handlers already assume a threadpool thread and touch no Unity API, so there is no new
thread-safety exposure; each owns its own `HttpListenerContext`, and the shared state they read is
already lock- or volatile-guarded. `WaitForActiveRequests`/`AbortActiveRequests` are unaffected
because they key off `_activeRequests`.

### 16 — Every captured TGP frame allocates a fresh multi-MB `byte[]`

`Tgp/TgpFeed.cs:309`. Per captured frame (15 Hz default).

`request.GetData<byte>().ToArray()` on the main thread: ~345 KB per frame at Native (~5 MB/s),
1.38 MB at MID (~20 MB/s), 3.1 MB at HIGH (~47 MB/s) — each a large-object-heap allocation.

**Fix:** a 3-slot reused `byte[]` ring filled with `CopyTo`, reallocated only when `captureW/H`
change. Three slots is provably enough given the existing bounds: `_pendingEncode` is single-slot
and `_readbackInFlight` blocks a second concurrent readback, so slot *k−3* is always free.

The encoder worker reads a buffer the main thread owns, so that invariant is what makes this safe
and deserves a comment at the ring declaration.

### 17 — 10 Hz reflection with boxing for state that only changes on a keypress

`Telemetry/TelemetryReader.cs:544` (`gunsLinked`), `:567` (`nightVisActive`), `:585`
(`navLights.isOn`), `:607-611` (`IRSources`), `:666` → `CmReflection.cs:24-30`. 10 Hz.

Three `FieldInfo.GetValue` calls that each box a `bool`; `GetHeatLevel` enumerating the reflected
list as non-generic `IEnumerable`, boxing the enumerator; and
`CmReflection.GetFirstCountermeasure` going through `MethodInfo.Invoke`, roughly three orders of
magnitude slower than a direct call and allocating on the way in and out. Selected CM category
changes on a keypress; NVG, nav lights and gun-link change on a toggle.

**Fix:** move the group — `CmCategory`, `NightVision`, `NavLightsOn`, `GunsLinked` — into the 1 Hz
`ScanWorld` group beside `_flares`/`_loadout`, whose comment at `:293-294` gives this exact
rationale. ~6 lines moved; the fields already exist as reader state. Boxing on `GetValue` cannot be
avoided without `CreateDelegate`, so moving the cadence is the lower-risk change.

**This is a UX decision, not only a perf one:** it puts up to 1 s of lag on AVN's indicator
lights. Worth confirming rather than assuming.

### 18 — Four 1 Hz scans that rediscover fixed data, or never stop retrying

Individually small; together the bulk of what the slow scan wastes.

- **`BdfTypeCounts` is O(types × defs)** — `Telemetry/TelemetryReader.cs:473-490`, driven by the
  lambdas at `:461-463`, twice per scan for two factions. The type name is recomputed for every
  definition once per type row: with 7 ship / 44 vehicle / 20 building definitions against ~7/10/7
  type rows that is 629 `Enum.ToString()` calls per faction, ~1,258 per second, each a
  reflection-backed name lookup plus a string allocation, plus as many string comparisons.
  **Fix:** invert the loop — one pass over `defs` calling `typeNameOf` once per definition into a
  reused `Dictionary<string,int>`, then one pass over `types` reading it. Same output, same
  ordering, ~12 lines. (The lambdas themselves capture nothing, so they are not a per-call
  allocation.)
- **`CountFlares`** — `:514`, called at `:299`. `ac.GetComponentsInChildren<FlareEjector>()`
  allocates an array and walks the entire airframe hierarchy every second to rediscover an ejector
  set that is fixed per airframe; only `GetAmmo()` changes. **Fix:** cache on aircraft change using
  the `EnsureAfterburnerCache` guard shape. The loop already null-checks each ejector, so a
  battle-destroyed one degrades correctly.
- **`BuildParts` allocates a string per part per tick** — `:1510`, called at `:895`, so 10 Hz not
  1 Hz. `UnityEngine.Object.name` marshals a new managed string on every access, so ~36 per tick
  and ~360/sec, for names the comment at `:1494-1499` describes as fixed for the airframe.
  **Fix:** cache a `string[] _partNames` behind an aircraft-identity guard; the per-tick loop then
  only writes `Hp`/`Detached`. Key on `parts.Count` as well if a part can detach mid-flight.
- **`TryCaptureFrontalSilhouette`** — `Assets/AssetCapture.cs:281`, called from `ScanWorld` at
  `:304`. `Resources.FindObjectsOfTypeAll<Image>()` returns every loaded `Image` including prefab
  instances, allocating the whole array, and the loop then filters scene objects by hand
  (`img.gameObject.scene.name == null` at `:286`). On a miss it deliberately does not mark
  `_capturedFrontal`, so it repeats every second for the whole mission. **Fix:**
  `FindObjectsByType<Image>(FindObjectsInactive.Include, FindObjectsSortMode.None)` returns exactly
  the set the loop already narrows to, which also makes the filter redundant.
- **`TryLogWeaponInfo` has no top-level guard** — `Assets/AssetCapture.cs:380-412`, 1 Hz. Unlike
  its sibling `TryLogPartLayout` at `:351-354`, it iterates all stations and computes the dedupe
  key before discovering there is nothing to log, marshalling `info.name` twice per station.
  **Fix:** mirror `TryLogPartLayout` — add the aircraft's `definition.unitName` to a
  `_loggedWeaponSets` set and return early on a hit.

## Tier 3 — per-mutation, per-message, and squad paths

### 19 — Every route mutation does two full serializations plus a whole-file copy and a synchronous main-thread write [verified]

`Stores/RouteStore.cs:209-218`, with `RefreshServedJsonOnly` at `:222` and `BuildFileJson` at
`:257`. Per mutation; 28 `Save()` call sites.

`Save()` builds the entire library twice — the served view then the file view, which differ only by
a handful of session-only flags — then backs up and rewrites the whole file. One click on a route
is 2 full JSON builds plus `File.Exists`, `File.Copy` of the old file and `File.WriteAllText`, all
synchronous on the Unity main thread. Several mutators produce byte-identical file content and
still write: `SetActiveRoute` (`:389`) to the already-active id, `ResetWaypoint`/`StepWaypoint`
(`:918`, `:956`) clamped at an end so holding W- at index 0 writes on every press, and
`ResetRoute` (`:408`) on an already-zeroed route.

Callers are discrete: `CommandDispatcher.cs:232-253` (one browser command per click or keypress),
`Squad/Squad.cs:637-641` (per received share message), and `TelemetryReader.cs:214`
(`AdvanceIfNear`, 1 Hz, but only when a waypoint actually advances). The W+/W- keybinds are
edge/tap-hold, so this is per press, not per frame. `performance.md` already flags this `Save()` as
a watch-item with the same shape as the ~9 ms `ConfigEntry.Value` stall it measured once.

**Fix:** keep the last-written file JSON in a static string and skip the backup and write when the
newly built one is equal — ~5 lines, no data-loss risk, since identical bytes are identical bytes.
The double *build* can also go by having `BuildRoutesJson` emit both variants in one pass, but the
disk half is the expensive half.

**Prefer that over a debounce/dirty-flag timer**, which would risk losing the last mutation on a
crash or alt-F4 — for a hand-built route library that is the data users would most miss. If a timer
is used anyway, flush on `OnSquadEnded` and mission end.

Related, and worth fixing once in the shared helper from finding 31: **no store writes atomically**
— there is no temp-file-plus-rename anywhere in `Stores/`, so a crash mid-`WriteAllText` truncates
the live file and `.bak` is the only recovery.

### 20 — Leader re-broadcasts the full lock aggregate on every member's change, O(members²) messages

`Squad/Squad.cs:587-595`, relay at `:594`. Per inbound `sqd.locks` message; each member sends one
whenever its own lock set changes, evaluated at 1 Hz from `Squad/SquadTargets.cs:32-35`.

With M members, each member's change causes one relay to all M. In a dogfight where everyone's
lock list churns each second, an 8-member squad produces ~64 Steam sends/sec carrying
near-identical payloads — and each send re-serializes and re-marshals independently (finding 21),
so it multiplies.

**Fix:** set a `_locksDirty` flag in `HandleLocks` instead of relaying, and do the single relay
from `SquadTargets.Tick()`, which already runs at 1 Hz and already owns the leader-side relay call.
~6 lines across the two files; it also collapses the leader's own change and members' changes into
one send per tick. Costs up to 1 s extra latency on the HUD squad-target glyphs, which are already
a 1 Hz change-driven cue.

### 21 — `SendToAll` serializes, encodes and marshals the identical payload once per recipient

`Squad/Squadron.cs:194-199` calling `:156-188`. Per broadcast.

Per recipient the same bytes are rebuilt from scratch: an `Envelope(type, payload)` string concat
(`:162`), `Encoding.UTF8.GetBytes` (`:163`), then `Marshal.AllocHGlobal` + `Marshal.Copy` +
`FreeHGlobal` (`:171-187`). Hit by the 5 s presence beat to the whole faction roster
(`Squad/Presence.cs:44-50`, up to ~30 peers), every `BroadcastRoster` (`Squad/Squad.cs:650`), every
disband, every `SendData`, and every locks-aggregate relay.

**Fix:** split `SendTo`'s body so the envelope, encode and alloc happen once in `SendToAll` and
only `SteamNetworkingMessages.SendMessageToUser` runs per peer, with `SendTo` delegating to the
same private core to keep its signature. The payload is provably identical per recipient — it is a
single `string` parameter. Keep the per-peer `EResult` logging so the "did every send succeed"
contract at `:192-193` survives.

### 22 — Squad-target lookup walks the member dictionary once per HUD marker, every frame [verified]

`Stores/SquadTargetsStore.cs:115-120` and `:126-135`; caller `Hud/HudSquadTargetMark.cs:78` inside
`LateUpdate`, looping every entry of the native HUD marker lookup at `:70`.

For a leader, each marker costs a full dictionary walk with a `HashSet.Contains` per member plus an
enumerator allocation — O(markers × members) per frame — for a set that only changes when an
`sqd.locks` message arrives. `HasAnyRemoteTargets` does the same walk once per frame, though it is
at least a correct up-front gate and is documented as such.

**Fix:** keep a `_memberUnion` HashSet rebuilt inside the only three mutation points —
`SetMemberIds` (`:53`), `RemoveMember` (`:61`), `OnSquadEnded` (`:143`) — so both queries become
one hash lookup or a count check. The cached set belongs in this file rather than the HUD
component, precisely so all three sites stay covered. Also: `SetMemberIds` allocates
`new HashSet<uint>(ids)` at `:55` before its equality check and discards it on the common
no-change path — compare first, allocate second.

Separately, `Hud/HudSquadTargetMark.cs:136-143`: `HideAll()` never clears `_marks`, so after
leaving a squad the `!inSquad` path at `:64` walks a stale dictionary every frame doing a native
`activeSelf` read per glyph, and the glyph GameObjects are never destroyed, lingering under
`iconLayer` until the next HUD rebuild. Destroying and clearing on the squad-exit transition is
behavior-preserving — the marks are already hidden. The "hide, don't destroy" reasoning at `:86-88`
is about markers crossing the screen edge, not about leaving a squad.

Inverting the marker loop to iterate targeted ids instead would be the larger win, but
`SquadTargetsStore` today only answers per-id queries, so it needs a new store method — worth it
only if the marker count proves large.

### 23 — Three TGP paths re-assert or recompute more often than the game does

- **Cockpit suppression re-fires `onCamToggle` every frame** — `Tgp/TgpFeed.cs:99` →
  `UpdateNativeSuppressionGate` (`:498-512`) → `SuppressNativeScreen` (`:514-520`) →
  `InvokeTargetCamToggle` (`:554-572`). This path is in `Tick` *before* the `Interval` accumulator
  at `:102`, so it is per frame, not per capture. Per frame it costs
  `GameManager.GetLocalAircraft`, `GetTargetList()`, a `FieldInfo.GetValue`, and a delegate invoke
  into `TacScreen_OnCamToggle`, which calls `SetActive` on two or three GameObjects. The game
  raises this event only on a transition (`_scratch/full/TargetCam.cs:315-331`), so 59 of every 60
  invocations are no-ops crossing the managed/native boundary. **Fix:** cache `cam.enabled` and
  re-invoke on its `false→true` transition, otherwise return early when
  `_cockpitDisplaySuppressed`. **Likely, not confirmed** — the code carries no comment explaining
  why it re-fires, so confirm in-game that the re-assert is defensive rather than load-bearing; a
  missed re-assert lets the cockpit TGP overlay reappear.
- **`UpdateExposure` invoked reflectively every frame** — `Tgp/TgpManualControl.cs:444` via
  `Tgp/TgpManualTargetCamAccess.cs:72-75`, while manual mode is on. A `MethodInfo.Invoke` plus the
  callee's own `GetAmbientLight()`, two `Mathf.Lerp`s and two URP volume writes. The game's own
  `Update` gates this behind a 1-second timer (`_scratch/full/TargetCam.cs:545`). **Fix:** an
  accumulator — call on engage, then once per second. ~4 lines. The comment at `:435-443` only
  needs the first engage to run it, which "call immediately, then throttle" preserves.
- **`ComputeOverlaySample` has up to three independent callers per frame** —
  `Tgp/TgpManualControl.cs:717-738`, called from `Tgp/TgpFullScreen.cs:151` (60 Hz),
  `Tgp/TgpOverlay.cs:129` via `TgpFeed` (15 Hz), and `Tgp/TgpNativeOverlay.cs:158` (10 Hz). Up to
  ~85 raycasts and ~85 grid-label strings per second, some duplicated within a single frame.
  **Fix:** cache the sample on `Time.frameCount` inside the one shared function all three route
  through. ~6 lines. `Tick` writes `_panDir` before any consumer runs, so no consumer needs a
  sub-frame-fresh sample. Complements finding 06 rather than replacing it.

### 24 — Weapon-selector aggregation allocates per call and runs twice per snapshot

`Input/WeaponSelectorLogic.cs:130`; `Input/WeaponSelectors.cs:155-160`;
`Telemetry/TelemetryReader.cs:784-785`.

`WeaponSelectors` already reuses a scratch `_loadout` list (`:44`), and the aggregation stage one
layer down discards that discipline with `new List<Entry>(...)` per call — on a path that runs
every frame while a fire key is held. Separately, `EffectiveGun` and `EffectiveRelease` are called
back to back from `PushSnapshot`, and each starts with `Follow(ac); BuildLoadout(ac);`, so every
weapon station is walked twice per snapshot to produce a byte-identical loadout.

**Fix:** a static scratch `List<Entry>` cleared at the top of `BuildEntries` — every consumer
(`Cycle`, `Effective`, `FirstAvailable`) uses the result and discards it before returning, so
there is no aliasing — plus a `Time.frameCount` and aircraft stamp on `BuildLoadout`.

Both make the class non-reentrant, which the existing `_loadout` scratch already assumes; worth a
comment naming the main-thread assumption. The frame stamp assumes nothing mutates `weaponStations`
and re-reads the loadout within one frame; `SelectFirstAvailable` changes only the current station,
not the station list, so this holds today.

### 25 — Countermeasure category index re-scanned reflectively on every held frame

`Input/Keybinds.cs:1263-1282` (`IndexOfCategory`), reached from `Drive` at `:1249`;
`CmReflection.cs:23-29`. Per frame while Flares or Jammer is held — both are registered
`edge: false` at `:162-167`.

The `FieldInfo`/`MethodInfo` are cached, but the scan is not: every held frame re-reads the private
station list and calls `GetFirstCountermeasure` through `MethodInfo.Invoke` once per station until
it matches. Re-firing the jammer every held frame is deliberate (it self-disables after ~0.1 s) —
that is the intended cadence for the fire, not for the lookup.

**Fix:** memoize the two indices against the `CountermeasureManager` instance, resetting when the
reference changes. ~8 lines, contained in `IndexOfCategory`.

**Verify first:** the cache assumes an aircraft's `countermeasureStations` composition is fixed for
the life of the manager. If a mid-flight rearm mutates stations in place without replacing the
manager, a stale index deploys the wrong countermeasure. Keying on `(mgr, list.Count)` covers the
add/remove case cheaply.

### 26 — AKF kill feed copied at 1 Hz and re-serialized at 10 Hz for kill-only data

`Akf/AkfTrackerLogic.cs:22-23`, `:37-38`, `:136-140`; consumers at
`Telemetry/TelemetryReader.cs:320-340`; serialized by `Telemetry/TelemetryJson.cs:136-167`.

`AkfKillEntry` is a struct (`TelemetrySnapshot.cs:342-351`), so `ToArray` copies up to 2×50 structs
every second whether or not a kill occurred. Each 10 Hz frame then rebuilds the whole feed JSON —
up to 100 entries with 3-4 `EscapeJson` calls each — for data whose only mutation point is
`RecordKill` (`Akf/AkfTrackerLogic.cs:47`). Rank and funds do change per tick, but they are seven
numbers at the tail of the block.

**Fix:** an `int FeedVersion` on `AkfTrackerLogic` incremented in `RecordKill`; `BuildAkf` skips
the `ToArray` when it has not moved, and `TelemetryJson` caches the two array strings against it,
concatenating the live numeric tail each frame. A forgotten increment freezes the feed, so key
strictly off the single mutator. Sizing assumes a real match fills the 50-line cap; early in a
mission this is negligible.

### 27 — `GetFrameBytes` copies the whole snapshot struct on every cache hit, for a dead out-param

`Http/TelemetryServer.cs:441-470`; sole caller `Http/SseHub.cs:137`. 10 Hz per connected display,
and most calls are cache hits.

The `_lock` acquisition and the snapshot copy both happen before the version check, so a hit pays
for both. `TelemetrySnapshot` declares ~128 field declarations, several multi-field (e.g.
`public float WorldX, WorldY, WorldZ;`), so that is on the order of 150 fields and high-hundreds of
bytes memcpy'd per client per 100 ms to read one bool. And `valid` is discarded by its only caller
(`GetFrameBytes(out _)`) — the comment's justification that it "drives the 10 Hz vs 1 Hz ping
cadence" no longer holds, since `SseHub` has one cadence (`FrameEveryMs`).

**Fix:** drop the `out bool valid` parameter, read `_snapVersion` alone for the hit check
(`Interlocked.Read`, no lock), and take `lock (_lock) { v = _snapVersion; snap = _latest; }` only
on the miss path, re-reading both together there so the cached bytes and `_frameVersion` stay
consistent. Deciding a hit against a version that is already newer is benign — the next `Push`
bumps it and forces the rebuild.

## Tier 4 — deletions and simplifications

No meaningful perf claim; these shrink the code. Roughly 200 lines net removal.

| # | What | Where | Δ |
| --- | --- | --- | ---: |
| 28 | Two dead reflection reads — `cam`/`screenRenderer` are both reassigned at `:166-167` before either is read; the intervening block only touches `targets` **[verified]** | `Tgp/TgpFeed.cs:143-144` | −2 |
| 29 | 22 mechanically identical page routes as an if/else-if chain → one `HashSet` membership test plus `"pages/" + name + "/" + name + ".html"`; the five genuine exceptions (`/map-view`, `/f35`, `/mfd`, `/`, `/index.html`) keep explicit branches. An exact-set test cannot admit a path the chain rejects, so no traversal exposure | `Http/TelemetryHttpRouter.cs:66-111` | −38 |
| 30 | 11 handlers wrap `WriteJson`/`WriteBinary` in a `try/catch/finally-Close` that those methods already do at `TelemetryServer.cs:629-655`, and `TrackRequestAsync` closes again in its own `finally` — so each site double-closes and swallows twice. Leave `CapturedAssetEndpoint.ServeIconTypes` (`:194-215`), which writes the response itself; better still, route it through `WriteJson` **[verified]** | `ConfigEndpoint`, `SseHub`, `ExtensionEndpoint`, `CapturedAssetEndpoint` | −33 |
| 31 | Three of five stores repeat the same ~14-line shape: a `Path.Combine(configDir, "com.roque.NOXMFD.<x>.json")` property, `File.Exists` → `ReadAllText` → `JsonLite.Parse` → catch → "file unreadable, starting empty", and `ConfigBackup.BackupIfExists` → `WriteAllText` → catch → "failed to persist". Two static helpers next to `ConfigBackup.cs` (`ReadJsonFile`, `WriteJsonFile`) — which must stay BepInEx-free so `RouteStore` keeps compiling standalone for `tools/tests`. Also the natural home for finding 19's equality guard and an atomic temp+rename. `TdStore`/`SquadTargetsStore` do not persist and are correctly excluded | `Stores/RouteStore.cs`, `LayoutStore.cs`, `HudPresetStore.cs` | −25 |
| 32 | `UniqueRouteName` and `UniqueName` are line-for-line identical apart from element type, both allocating a fresh `HashSet` per call → `UniqueName(string, IEnumerable<string>)` | `Stores/RouteStore.cs:323`, `LayoutStore.cs:112` | −10 |
| 33 | `ResolveFont()` duplicated verbatim in two cue files, with two separate `static Font? _font` caches and therefore two full `FindObjectsByType<Text>` scene scans plus two builtin-resource fallbacks. The amber literal is copy-pasted four times (`HudWaypointCue.cs:27`, `HudTgpCue.cs:24`, `HudTtiCue.cs:14`, `HudFocusMark.cs:23`). Same de-duplication `CombatHudMarkerLookup` already applies to the marker reflection | `Hud/HudWaypointCue.cs:196-207`, `HudTgpCue.cs:170-184` | −10 |
| 34 | The four combined-fire binds are hand-enumerated in three separate places, and `IsCombinedFireBind` does four `ReferenceEquals` per active bind per frame to re-derive membership the registry already knows. One static `(bind, remoteGroup, fire)` table plus a flag on the four `BindDef`s. Preserve dispatch order: gun → release → release-single → jammer-pod | `Input/Keybinds.cs:949-976` | −6 |
| 35 | `CommandDispatcher` re-implements `TargetUnitLookup.TryResolve` twice — `:741` is character-for-character the helper's predicate, `:366` is it minus an already-performed `id != 0` check. The helper's header says it exists because callers "independently grew the same check". Leave `TargetDeselect` (`:456`) alone: it omits the `disabled` check on purpose so a dying unit can still be deselected | `CommandDispatcher.cs:366`, `:741` | −2 |
| 36 | `ClampUnit` duplicated verbatim → `Mathf.Clamp(value, -1f, 1f)`, which `TgpManualControl.SetPan` already uses for the same thing. Differs only on NaN, which neither path can produce | `Input/Keybinds.cs:978`, `CommandDispatcher.cs:595` | −6 |
| 37 | Two dead members, zero callers in `src/plugin` or `tools`. `CloseSessions` appears only inside comments explaining why it is deliberately not called (`Squad.cs:292`, `:387`), which still read correctly without it | `Squad/Squadron.cs:137-140`, `:277` | −5 |
| 38 | `string.Join(",", set.Select(id => id.ToString(...)))` allocates an enumerable, a closure, an intermediate string per id and an array, then copies into the `StringBuilder` that is already in hand — where every other builder in the same files uses a plain `foreach`. Drops `using System.Linq` from both | `Stores/TdStore.cs:186`, `:194`; `SquadTargetsStore.cs:66`, `:74` | −4 |
| 39 | `SynthesizeAlphaIfOpaque`'s probe loop only short-circuits on an exact zero, so any icon with partial transparency is scanned in full before the same early return fires. `if (px[i] < 250) return;` collapses the loop and `minA` into one test — logically identical, since `minA >= 250` iff no byte is below 250. Background thread, so a simplification rather than a perf fix | `Assets/SpriteCapture.cs:150-153` | −2 |
| 40 | IR auto-levels computes each pixel's luma twice over up to 3.1 MB — pass 1 can write its luma byte into `px[i]` for pass 2 to read, halving the arithmetic. `tools/tests` already links this file, so assert the stretch still maps min→0 / max→255 | `Tgp/TgpFeedSettings.cs:125-146` | −4 |
| 41 | `UpdateMinimap` re-asserts the box background and every ornament while the minimap is already hidden — `_minimapHidden` is set at `:198` but never consulted on the hide path — costing a `Graphic.enabled` setter plus one `SetActive` icall per ornament per frame. Only `DynamicMap.EnableCanvas(false)` genuinely needs re-asserting (the game re-enables that canvas itself on Maximize). Separately `ResolveMinimapExtras` has no "already tried" flag, so if the parent carries no `Image` it re-walks the hierarchy every frame, marshalling a fresh `Transform.name` string per child. **Test the M-key maximize/minimize cycle** — the assumption is that nothing native re-enables the box or ornaments | `Hud/HudDeclutter.cs:196-197`, `:212-225` | ~0 |

Also recorded, not recommended: `ModifiersHeld` (`Input/Keybinds.cs:1175`) allocates a LINQ
enumerator on every held frame and is vacuously true today — `SetKeyBind` (`:684`) only ever
constructs `new KeyboardShortcut(key)` with no modifiers, so no bind in this codebase can carry
one. Its comment says it is kept so a future modifier-capable capture UI does not silently
regress. Deleting it trades that forward-compatibility for three lines; that is a call for the
maintainer, not a defect.

## Correctness issues found in passing

Not performance. Recorded because they surfaced during the read.

- **The SQD roster's aircraft column freezes.** `Squad.BuildStateJson` bakes live game values into
  a string rebuilt only on a *protocol* mutation — `SelfAircraftUnitName()` (`Squad/Squad.cs:730`,
  `:838`) and `PlayerRoster.AircraftFor` for the leader (`:845`) and per member via
  `MembersJsonServed` (`:757-771`, `:849`). `RebuildState` is never called on a timer;
  `Http/SquadEndpoint.cs:13-14` and `Http/SseHub.cs:163` both just read the cached string. So the
  column shows whatever was true when the last squad message arrived, and `MembersJsonServed`'s own
  comment ("recomputed fresh from `PlayerRoster.AircraftFor` on every `/squad` poll") describes
  behavior the code does not have. **Fix:** one `Squad.RebuildState()` from the 1 Hz slow tick
  beside `Squad.CheckLiveness()` (`TelemetryReader.cs:222`) — one rebuild per second including
  three or four cheap interop calls, and the SSE layer already change-gates the push by string
  comparison, so nothing extra goes on the wire.
- **`HudTtiCue.Build()` failure re-runs a scene-wide scan every frame.** `Hud/HudTtiCue.cs:43`,
  `:85`. The one-shot log guards (`_loggedNoAltitude`, `_loggedBadField`) say the cue is disabled,
  but nothing is disabled: `FindFirstObjectByType<Altitude>(FindObjectsInactive.Include)` repeats
  every frame for the rest of the mission whenever a target is locked and `_label` is null.
  **Fix:** a `float _nextBuildAttempt` back-off, ~4 lines.
- **`EnsureAfterburnerCache` never retries.** `Telemetry/TelemetryReader.cs:1076-1112`. If
  `CombatHUD` is not up on the first push, `_abAircraft` is still assigned, so the guard at `:1078`
  passes forever and the cache is never rebuilt for that aircraft.
- **No store writes atomically.** No temp-file-plus-rename anywhere in `Stores/`, so a crash
  mid-`WriteAllText` truncates the live file; `.bak` is the only recovery. Finding 31's shared write
  helper is the place to fix it once.
- **The `DamageEffects.BlastFrag` Harmony patch is probably redundant and actively harmful.**
  `HarmonyPatches.cs:129-136`, versus `:150-157` and `:159-166`. The file's own comment at
  `:138-149` explains that `BlastFrag`'s `missileID` is unreliable — deferred a physics tick, so a
  salvo misattributes — and that recording at `Missile.PenetrateObject`/`Missile.Detonate` "instead
  avoids this". All three patches are still applied, and `RecordWeaponHit` is last-write-wins
  (`Akf/AkfTrackerLogic.cs:99`), so the known-wrong variant overwrites the good record. It also
  fires on every explosion in the match, not just the player's. **Verify with `ilspycmd` that every
  kill-dealing munition derives from `Missile` before deleting** — if some ordnance only reaches
  `BlastFrag`, deleting this loses weapon names for it. Nothing crashes either way; the field is
  already nullable and optional.

## Documentation correction

`docs/performance.md` records "`string.Format` boxed every float/int/bool" under a heading marked
**HISTORICAL, fixed by item #2**. Item #2 fixed the per-client re-serialization; the boxing is
still in the code, once per frame (finding 08). Worth correcting so a later reader does not skip it
as solved.

## Not worth chasing

Recorded so a future pass does not re-propose them.

- **TGP render-on-demand instead of a permanently-enabled mirror camera.** Built, live A/B tested
  and rejected: `docs/tgp-high-quality-mode.md` records that the "Performance" tier cost about the
  same on `frame(tgpOpen)` (the manual `Camera.Render()` cost moved from the pipeline to the call
  site) while losing terrain detail, and that `enabled = false` is not sufficient under URP.
- **Pooling the per-tick `.ToArray()` buffers** (units, RWR, MW, parts). The arrays are the handoff
  to a background SSE thread that serializes them; a reused buffer would tear mid-serialize.
  `performance.md:168-178` records the exact race this shape replaced. Finding 08 attacks the
  boxing, not the arrays. The fresh `PartHp[]` per tick is deliberate for the same reason
  (`TelemetryReader.cs:1494-1499`).
- **`GetFrameBytes` holding `_frameLock` across `Serialize`.** That is the point of shipped item #2
  — concurrent clients block briefly and then get a cache hit rather than each re-serializing. No
  network I/O happens under it.
- **Missing cache headers on `/map` and the icon PNGs.** The clients cache-bust deliberately
  (`/map?t=Date.now()`, `/icon?type=…&v=tries`, `/airframe?…&v=`) for the retry-until-captured
  flow. Headers on a URL that changes every request buy nothing, and `<img src>` requests are
  already deduped by the browser for the page's lifetime.
- **MJPEG re-encoding unchanged frames.** Both `Http/TgpMjpegHandler.cs:88` and
  `Http/ExtensionEndpoint.cs:102` already gate on frame id. The per-frame `head` string cannot be
  fully precomputed because `Content-Length` varies.
- **`AsyncGPUReadback` and the `_readbackInFlight` skip.** Already async, and already drops a tick
  rather than stacking readbacks (`Tgp/TgpFeed.cs:263`). No synchronous `ReadPixels`/`GetPixels`
  anywhere in the subsystem, and capture is gated on `WantsTgpFrames`, a `Volatile.Read` of an int.
- **`CommandDispatcher`'s dispatch.** Already a dictionary built once at type init with
  `StringComparer.Ordinal` and a single `TryGetValue` in `Drain` (`:343`). The command lock is
  already scoped to the dequeue alone (`Http/CommandEndpoint.cs:69-77`), and the queue is drained
  in place, never copied.
- **"Iterating all 70 binds every frame."** `JoyBtn`/`ReadAxis` early-out before touching Rewired
  when unbound (`Input/Keybinds.cs:1222`, `:1193`), and the `Active()` pass is a struct-enumerator
  walk with a `KeyCode.None` compare per row. A fresh install does zero Rewired work per frame.
  (A HOTAS user with many bound buttons does re-fetch `ReInput.controllers.Joysticks` once per bound
  bind per frame; caching it for the duration of one `Poll()` is ~6 lines if that proves to matter.)
- **`KeyboardShortcut.IsDown()`/`IsPressed()` being avoided.** Deliberate and documented at
  `Input/Keybinds.cs:1149-1158` — BepInEx disqualifies the shortcut when any unrelated key is held.
  Reading `Input.GetKey` directly is both correct and cheaper.
- **`JsonLite.EscapeJson`.** Allocation-free when nothing needs escaping — it only creates the
  `StringBuilder` on the first character that must be escaped, which is the common case for unit and
  player names.
- **`PlayerRoster.Refresh` rebuilding all three collections at 1 Hz.** Deliberate and documented at
  `:111-113`: a full rebuild means squad-end and aircraft-swap need no invalidation path. `_scratch`
  is reused and the two per-tick allocations are bounded by faction size.
- **`ConfigEntry<T>.Value` reads per frame.** A cached typed field access, not a `.cfg` re-parse.
  The ~9 ms stall `performance.md` measured was on a *write*.
- **`SceneSingleton<T>.i` and `GameManager.GetLocalAircraft` read repeatedly.** Both plain static
  field reads (`_scratch/full/SceneSingleton.cs:5`, `_scratch/full/GameManager.cs:114`), not `Find`
  calls. The only cost is a Unity fake-null comparison.
- **`weaponManager.GetTargetList()` called four times per contact tick.** `WeaponManager.GetTargetList()`
  is `return targetList;` (`_scratch/full/WeaponManager.cs:207-210`).
- **`targets.Contains(u)` inside the per-unit loops.** O(n·m), but `m` is the player's lock count —
  single digits. A `HashSet` would cost more to build than it saves.
- **The shutdown machinery's `Thread.Sleep(25)` poll** (`Http/TelemetryServer.cs:593`).
  Shutdown-only, bounded at 500 ms, and recorded as a deliberate hardening requirement in
  `docs/server-hardening.md`.
- **`SseHub`'s `wrote` flag and `OutputStream.Flush()`** (`:130`, `:246`). Mono's
  `HttpResponseStream.Flush()` is likely a no-op, and this is a synchronous call in an otherwise
  async loop — but the flag looks deliberately added, and removing it could add a tick of cursor
  latency. Needs a live latency comparison before touching; not worth the risk on inspection alone.
- **`SseHub`'s ~62 Hz per-connection timer** (`:248`). Do findings 11 and 12 first and re-measure:
  once the per-tick body is a few `Interlocked.Read`s and reference compares, the wakeup costs
  almost nothing. A wait-handle rewrite couples `SoiFocus` to `SseHub` and risks a stalled display
  on a missed wakeup.
- **`_lastWeaponByAttacker` never pruned** (`Akf/AkfTrackerLogic.cs:24`). Bounded by distinct
  attackers per mission, and the tracker is mission-scoped, so it cannot accumulate across sessions.
- **`RouteStore.UpdateSharedRoute`'s O(n²) `FindWaypointMatch`** (`:614-660`). Only on receipt of a
  re-share of an already-accepted route, over at most a few dozen waypoints, and the `used[]`
  bookkeeping it buys is the correct duplicate-handling semantics.
- **Harmony's `___field` injection in the `TargetCam`/`TargetScreenUI` gates**
  (`HarmonyPatches.cs:176-188`, `:219-250`). The per-frame ones are a single static bool read, and
  the heavy overlay one rides the game's existing 0.1 s `StartSlowUpdate`.

## Suggested sequencing

Grouped so each step is independently shippable and live-testable.

1. **01 + 02** — one rewrite of the TD-assign loop. Net shorter, no behavior change, removes the
   largest per-frame allocation source in the plugin.
2. **03** — restore `HudTtiCue`'s intended throttle. Four lines.
3. **13 + 14** — asset cache and gzip together, since they share the same `GetOrAdd`. Largest
   user-visible effect (cold tablet load).
4. **05** — the remote-input guard.
5. **08 + 09** — the two serialization wastes. Largest per-frame CPU and GC item;
   `TelemetryJson` is pure, so cover it with a standalone check.
6. **28-41** as one deletion pass, running `tools/tests` and `HudPresetStore.SelfCheck` afterwards
   (31 and 32 touch persistence).
7. Everything else by the priority table, measuring with a restored `PerfLog.cs` before and after
   anything that claims a frame-time win.

Items needing an in-game answer before implementation: **04** (registry membership parity),
**17** (is 1 s indicator lag acceptable), **23** (is the cockpit-suppression re-assert
load-bearing), **25** (can a rearm mutate stations in place), **41** (does anything native
re-enable the minimap ornaments), and the `BlastFrag` question under correctness.

## Review annex — implementation safeguards and plan refinements

Reviewed against `main` at `d26e48c` (version 0.40.0). The intervening remote-keybind diagnostic
work touches `RemoteInputState` and `TelemetryServer`, but does not implement the efficiency
findings above. The findings therefore remain candidates, subject to the corrections and added
acceptance criteria in this annex.

### Corrections to proposed implementations

- **Finding 16 needs explicit buffer ownership, not a simple cyclic ring.** Three buffers are
  sufficient only when a slot is not reused until the GPU readback, pending queue, or encoder has
  released it. A naive `index = (index + 1) % 3` can wrap onto the buffer still owned by a slow
  encoder after newer captures replace the pending frame. Use a small free-buffer pool or explicit
  slot states, and return the encoder's slot in a `finally` block. Test a deliberately stalled
  encoder at HIGH resolution before considering this safe.
- **Finding 15 must bound request concurrency.** Moving synchronous handlers off the accept thread
  is sound, but unrestricted `Task.Run` permits a burst of requests to create unbounded thread-pool
  work. Preserve synchronous active-request registration, add a bounded concurrency gate or other
  backpressure, and keep shutdown able to abort and await every admitted request. Exercise the
  active SSE/MJPEG shutdown case as well as a burst of short asset and command requests.
- **Findings 13 and 14 need complete HTTP representation semantics.** A gzip implementation must
  send `Vary: Accept-Encoding`, use the compressed length, and keep ETag/304 behavior correct for
  both compressed and uncompressed clients. Verify cold and warm loads, clients without gzip,
  extension assets, and multiple LAN browsers. Retain raw bytes for PNG and woff2 resources.
- **Finding 17 should not move every reflected indicator to 1 Hz as one group without a UX check.**
  Prefer invalidation/versioning at the command or keypress that changes discrete mod-owned state.
  For game-owned state that still requires observation, choose cadence per field and measure the
  visible delay rather than accepting a blanket one-second lag.
- **Finding 05's one-way `_writes` counter has limited lifetime value.** It removes work only until
  the first remote input in the process, after which every getter takes the original path forever.
  Use separate cursor/fire activity state, or document that this deliberately optimizes only
  sessions that never use remote input. Any resettable idle fast path must preserve the TTL and
  minimum-press guarantees under concurrent reads and writes.
- **Finding 04's parity check is broader than collection membership.** Compare spawning,
  despawning, disabled, network-replicated, and temporarily incomplete units between
  `FindObjectsByType<Unit>` and `UnitRegistry.allUnits`. Preserve a stable iteration snapshot if the
  registry can mutate during the scan.

### Priority and scope refinements

Correctness work should be triaged ahead of micro-allocation cleanup. In particular, track the SQD
aircraft-column refresh, `EnsureAfterburnerCache` retry behavior, failed `HudTtiCue.Build()` retry
rate, atomic store writes, and the `BlastFrag` attribution question as explicit work items rather
than leaving them buried in an efficiency document. The `BlastFrag` change remains blocked on an
assembly check proving which kill-capable ordnance paths do or do not derive from `Missile`.

Do not implement findings 28–41 as one deletion pass. They span routing, persistence, HUD,
input, squad transport, image processing, and TGP behavior. Finding 31 in particular changes the
durability contract and needs focused persistence tests; it is not cleanup-only. Group commits by
one responsibility and run the relevant standalone tests after each group.

A safer initial order is:

1. Resolve or ticket the correctness findings and required game/assembly decisions.
2. Apply **01 + 02**, then **03**, as separate low-risk per-frame changes.
3. Apply **13 + 14** together with HTTP cache/encoding tests.
4. Measure **05**, **08–12**, and **27** under representative client counts before choosing their
   final designs.
5. Attempt **15** and **16** only with bounded-concurrency and buffer-ownership stress checks.
6. Handle deletion/refactor findings in small subsystem groups, not one repository-wide pass.

### Measurement and acceptance matrix

Add a status to every finding: `ready`, `needs measurement`, `needs browser test`, `needs
live-game test`, `blocked on decision`, or `implemented`. Each implemented item should record its
validation command/scenario and whether it targets CPU, allocation rate, frame spikes, response
latency, or transfer size.

Use at least these workloads for before/after evidence where relevant:

- no browser clients, one local client, and several simultaneous LAN clients;
- a busy mission with approximately 200 reported units/contacts;
- TGP at its default 15 Hz and maximum 60 Hz, including HIGH resolution and a slowed encoder;
- active `/stream`, `/tgp.mjpg`, and extension requests during `TelemetryServer.Stop()`;
- route/store mutation followed by forced interruption and recovery from the persisted file.

Do not claim a frame-time improvement from inspection alone. Restore the existing performance
instrumentation for plugin hot paths, and retain output under `_scratch/perf-sessions/` as required
by the repository workflow. Correct the historical wording in `docs/performance.md` at the same
time finding 08 is implemented: serialization is shared between clients, but `AppendFormat`
boxing inside the one serialization remains.
