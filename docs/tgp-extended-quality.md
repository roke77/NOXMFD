# TGP — extended resolution and JPEG quality controls

## Status

Implementation is active on `tgp-extended-high-quality`.

Implemented in the first pass:

- LOW/MID/HIGH resolution tiers at native 360x240, 720x480, and 1080x720;
- independent JPEG LOW/MID/HIGH values at 30, 50, and 75;
- legacy `hq` config and command input migration to MID plus temporary telemetry compatibility;
- direct raw-array JPEG encoding on one bounded background worker;
- generation invalidation for disengage and live setting changes;
- TGP CFG controls, specific cost warnings, reset behavior, preview values, and automated tests.

Live-game performance and image-quality validation remains open. The test checklist under
"Acceptance criteria" is the handoff for that session; automated checks do not establish the
visual benefit or safe capture-rate ceiling of 1080x720.

This plan replaces the current single **TGP — FEED QUALITY** choice with two independent
settings:

1. **TGP — FEED RESOLUTION** chooses the camera/source resolution.
2. **TGP — FEED QUALITY** chooses the JPEG compression quality applied to every streamed frame.

The split matters because these settings spend performance in different places. Resolution changes
how many pixels the mirror camera renders and how much raw data crosses from GPU to CPU. JPEG
quality does not add pixels; it changes how aggressively those pixels are compressed after the
readback, primarily affecting CPU encoding work, file size, and network bandwidth.

The current defaults remain unchanged in effective behavior: native 360x240 resolution, JPEG
quality 50, and 15 Hz capture.

## Proposed player-facing controls

### TGP — FEED RESOLUTION

| UI label | Preferred value | Source | Resolution | Relative pixels | Default |
|---|---|---|---:|---:|---|
| **LOW** | `native` | Nuclear Option's own TargetCam + UICam texture | 360x240 | 1x | yes |
| **MID** | `mid` | NOXMFD mirror camera | 720x480 | 4x LOW | no |
| **HIGH** | `high` | NOXMFD mirror camera | 1080x720 | 9x LOW / 2.25x MID | no |

The current UI's **HIGH** becomes **MID** without changing its 720x480 picture. Existing users who
have the current `hq` mode saved should therefore land on MID, not silently jump to the more
expensive new HIGH tier.

All three resolutions preserve the game's native 3:2 aspect ratio. The new HIGH starts at
1080x720 rather than 1440x960: it is a meaningful 50% increase in linear resolution over MID while
limiting the jump to 2.25x its pixels. A 1440x960 source would be 4x MID's pixels and is too
aggressive as the first higher tier on the existing readback/MJPEG pipeline.

### TGP — FEED QUALITY

| UI label | Preferred value | JPEG encoder value | Default | Intent |
|---|---|---:|---|---|
| **LOW** | `low` | 30 | no | Smaller stream; more visible block/ringing loss |
| **MID** | `mid` | 50 | yes | Today's shipped behavior |
| **HIGH** | `high` | 75 | no | Retain more fine detail; larger stream |

The numeric mapping is an initial test proposal, not a claim that 30/50/75 are equally spaced in
perceived quality or file size. Unity accepts JPEG quality values from 1 through 100 and defaults
to 75. The scale is nonlinear, so the final LOW/HIGH numbers need screenshot and file-size A/B
testing on real TGP scenes before they are treated as settled.

Keeping MID at 50 is important: upgrading NOXMFD must not change the appearance, CPU cost, or
bandwidth of a user who leaves both new controls at their defaults.

## What the current feed actually produces

There are currently two modes:

| Current UI | Internal value | Actual encoded dimensions | JPEG quality |
|---|---|---:|---:|
| LOW | `native` | 360x240 | 50 |
| HIGH | `hq` | 720x480 | 50 |

The native resolution is exact, not an estimate. Inspection of the installed game's
`resources.assets` shows a `TargetWindowRender` RenderTexture at 360x240. Both `TargetCamera` and
`targetCamUICamera` target that same texture, which is why the LOW/native video already includes
the game's UI overlay in its pixels.

The current mirror camera is fixed at 720x480 in `TgpFeed.HqWidth/HqHeight`. The shared capture
path also has `MaxDim = 720`, so merely creating a larger mirror texture would not currently
produce a larger JPEG: the capture stage would downscale it back to a 720-pixel longest side.

Every frame is encoded with `JpegQuality = 50`. The default capture rate is 15 Hz and the player
can currently select up to 60 Hz, with a warning above 15 Hz but no quality-dependent hard cap.

## What the JPEG value means

The encoder receives a complete uncompressed image plus an integer quality value. JPEG broadly:

1. converts RGB into brightness and colour components;
2. splits the image into 8x8 blocks;
3. transforms each block into spatial-frequency coefficients;
4. quantizes those coefficients, discarding increasingly subtle information;
5. entropy-compresses the remaining values into the final JPEG byte stream.

The `30`, `50`, or `75` setting principally controls quantization. A lower number uses larger
quantization steps, which throws away more fine detail and produces a smaller file. A higher number
preserves more coefficients and normally produces a larger file.

It does **not** mean "30/50/75 percent of the original image," and the scale is not linear. A
quality-75 frame is not predictably 50% larger or 50% better than quality 50. Output size depends
heavily on scene content: flat sky compresses cheaply, while foliage, grass, smoke, explosions,
sensor noise, and sharp overlay edges require more data.

NOXMFD uses MJPEG, not an inter-frame video codec. At 15 Hz it produces up to 15 independent,
complete JPEG files every second. No frame can reuse information from the preceding frame the way
H.264/H.265/AV1 can.

## Cost model: resolution and JPEG quality are separate axes

The raw readback buffer is RGBA32: four bytes per pixel before JPEG compression.

| Resolution | Pixels/frame | Raw RGBA/frame | Raw readback at 15 Hz |
|---|---:|---:|---:|
| 360x240 | 86,400 | 0.35 MB | 5.2 MB/s |
| 720x480 | 345,600 | 1.38 MB | 20.7 MB/s |
| 1080x720 | 777,600 | 3.11 MB | 46.7 MB/s |
| 1440x960 (not proposed) | 1,382,400 | 5.53 MB | 82.9 MB/s |

These are internal GPU-to-CPU transfer figures, not network traffic. The JPEG sent over the network
is much smaller and varies with quality and scene content.

Resolution affects:

- mirror-camera render target size and pixel shading/post-processing cost;
- GPU-to-CPU readback volume;
- IR grayscale work (two CPU passes over all pixels in mirror/IR mode);
- JPEG encoder input size;
- raw-buffer memory and copy volume.

JPEG quality affects:

- how much detail the JPEG encoder retains;
- encoded file size and MJPEG network bandwidth;
- some encoder CPU time, although input resolution is normally the stronger CPU-cost multiplier;
- visible blockiness, ringing, and smearing around high-frequency detail.

JPEG quality does not reduce the mirror-camera render or raw readback cost. Choosing HIGH
resolution with LOW JPEG quality still renders and reads back all 777,600 pixels; it only compresses
the resulting bytes more aggressively afterward.

## Current pipeline and its main-thread cost

The current frame path is:

```text
TargetCam or mirror camera renders on GPU
  -> Graphics.Blit into the capture RenderTexture
  -> AsyncGPUReadback.Request
  -> callback receives raw RGBA pixels
  -> Texture2D.LoadRawTextureData
  -> optional two-pass IR grayscale conversion
  -> Texture2D.Apply
  -> Texture2D.EncodeToJPG
  -> TelemetryServer.PushTgpFrame
  -> MJPEG clients receive the cached JPEG
```

The GPU readback is already asynchronous. `AsyncGPUReadback` avoids blocking the Unity main thread
until the GPU finishes, at the cost of a few frames of latency. `_readbackInFlight` prevents
requests from stacking: if the previous readback has not completed, the new capture tick is
dropped.

The callback's CPU work is synchronous. `Texture2D.EncodeToJPG` does not return until it has built
the complete JPEG byte array, and the project currently performs it in the Unity/main-thread frame
path. The server push itself is only a short locked assignment; HTTP/MJPEG writing is not the
expensive part on the Unity main thread.

Measured history at 15 Hz:

- Native 360x240 JPEG encoding measured roughly 0.7-0.9 ms/frame in one session.
- Current 720x480 HQ encoding measured roughly 3.04 ms/frame.
- Current HQ had `tgpSkipped=0` at the 15 Hz default in its live A/B session.
- At higher capture rates, earlier native-path sessions reproduced substantial readback backlog:
  up to roughly 45-48% of attempted captures dropped at 30 Hz on the test hardware.

At 60 game FPS the frame budget is 16.7 ms. A 3 ms encode does not occur on every game frame at a
15 Hz feed rate; instead, approximately every fourth game frame receives that extra synchronous
work. This tends to show up in frame-time consistency and 1% lows rather than as a simple constant
average-FPS subtraction.

A 1080x720 frame has 2.25x the pixels of 720x480. A naive linear extrapolation puts its JPEG work
near 6-7 ms at the same settings, but this is an estimate, not a result. It must be measured. At
that size, keeping compression on the main thread could consume a large fraction of a 16.7 ms
frame, especially when IR's two pixel scans happen on the same capture.

### The avoidable CPU-to-GPU round trip

After readback, the current code copies raw bytes into a CPU-readable `Texture2D` and calls
`Texture2D.Apply(false, false)`. `Apply` uploads the CPU texture data back to the GPU. The pipeline
does not render that Texture2D afterward; it only feeds it to the JPEG encoder.

Moving to Unity's raw-array JPEG API can remove the intermediate Texture2D, its raw-data load, and
the `Apply` upload entirely. That should be tested as an independent change before attributing all
improvement to background threading.

## Moving JPEG encoding off the main thread

Unity 2022 exposes two suitable array encoders:

- `ImageConversion.EncodeArrayToJPG`
- `ImageConversion.EncodeNativeArrayToJPG`

Unity documents the array/native-array variants as thread-safe, and the installed Nuclear Option
`UnityEngine.ImageConversionModule.dll` contains the relevant array JPEG encoder. This is the
preferred first path: it preserves the existing JPEG/MJPEG protocol while moving the CPU encoder
away from Unity's frame loop.

### Proposed worker pipeline

```text
Unity/main thread
  -> render + Blit
  -> submit AsyncGPUReadback into an owned buffer
  -> when complete, hand that buffer to one encoder worker
  -> return to the game loop

Encoder worker
  -> optional IR grayscale/auto-levels
  -> EncodeArrayToJPG / EncodeNativeArrayToJPG
  -> place completed JPEG in a thread-safe completion slot

Unity/main thread (or the already thread-safe server cache handoff)
  -> publish newest completed JPEG
```

The readback request's returned memory cannot simply be retained by a worker indefinitely: Unity
only guarantees access to a completed request's result briefly before disposing the request. The
pixels must therefore land in an owned buffer whose lifetime extends through encoding.

### Required ownership and backpressure rules

- Use one encoder worker, not one new `Task` per frame.
- Keep the queue bounded to at most one waiting frame plus one encoding frame.
- Prefer dropping a stale frame to accumulating latency. A live sensor feed must remain current;
  delivering every old frame seconds late is worse than dropping frames.
- Use pooled or persistent raw buffers, ideally a two- or three-buffer ring. Do not allocate a new
  1.4-3.1 MB raw byte array every capture.
- Never reuse or dispose a raw buffer until both readback and encoding have released it.
- Tag work with a generation number covering mission, resolution, and quality changes. Ignore a
  completion from an old generation after disengage, aircraft change, or mode switch.
- Keep cameras, RenderTextures, Texture2D objects, scene state, and other normal Unity APIs on the
  Unity thread. Only raw-array processing and APIs explicitly documented thread-safe belong on the
  encoder worker.
- Serialize the IR auto-level state. `_irMinEma/_irMaxEma` are temporal state; two workers encoding
  frames concurrently would race and could apply frames out of order. A single worker preserves
  ordering naturally.
- Publish `_active`, `_engaged`, and teardown state on the Unity thread, or protect them explicitly.
  `TelemetryServer.PushTgpFrame` itself is a short lock-protected cache update and can accept a
  controlled thread-safe handoff.

### Suggested implementation sequence

1. **Direct-array encode, still synchronous.** Replace the Texture2D path with
   `EncodeArrayToJPG`/`EncodeNativeArrayToJPG` on the current thread. Verify orientation, RGBA
   channel order, sRGB/linear handling, IR output, and exact JPEG dimensions. Measure whether
   removing `LoadRawTextureData` + `Apply` helps before adding concurrency.
2. **One bounded encoder worker.** Move grayscale and JPEG encoding together. Add explicit buffer
   ownership, completion, cancellation/generation, and drop counters.
3. **Add the resolution/quality controls.** Once measurement is trustworthy, expose the 3x3 matrix
   and assess HIGH resolution without conflating its cost with a simultaneous pipeline rewrite.

The order can be combined into one development branch, but each step should be benchmarkable and
revertible independently.

## Configuration and compatibility plan

The word "quality" currently means resolution/source throughout the code and wire protocol. A
clean implementation must avoid silently changing what existing saved values and older clients
mean.

### Internal names

- Rename `TgpQuality` enum to `TgpResolution`.
- Values: `Native`, `Medium`, `High`.
- Rename `TgpFeed.Quality` to `TgpFeed.Resolution`.
- Rename `HqWidth/HqHeight` to tier-specific resolution constants or a small settings lookup.
- Add a separate JPEG-quality enum/value, e.g. `TgpJpegQuality.Low/Medium/High` mapping to
  30/50/75.
- IR and client-side overlay decisions must use `Resolution != Native`, not equality with one
  particular mirror tier.

### Persisted BepInEx configuration

The existing config key is `Refresh Rates / TgpQuality`, with `native` or `hq`. Renaming the key
would lose existing user intent unless migration logic is added.

Lowest-risk plan:

- Continue binding the existing on-disk `TgpQuality` key as the resolution setting, with a comment
  that the stale key name is retained for config compatibility.
- Normalize `native` to LOW and legacy `hq` to MID.
- Also accept `mid` as MID and `high` as HIGH.
- Persist `mid` after the legacy `hq` value is read successfully.
- Add a new key `TgpJpegQuality`, default `mid`.

This preserves the current mode for existing users while allowing clean terminology everywhere
outside the legacy config identity.

### Commands and HTTP configuration

Preferred new command groups:

- `rates.set`, group `tgpResolution`, value `native|mid|high`
- `rates.set`, group `tgpJpegQuality`, value `low|mid|high`

Keep accepting the old `tgpQuality` group as a resolution alias for at least one release.

`/rates-config` should expose:

```json
{
  "tgpHz": 15,
  "tgpResolution": "native",
  "tgpJpegQuality": "mid",
  "tgpSuppressNative": false
}
```

For a compatibility window it can also emit legacy `tgpQuality`, using `native` for LOW and `hq`
for either mirror tier. Old clients only need to distinguish baked-native overlay from
client-rendered mirror overlay; they do not need to distinguish MID from HIGH resolution.

### Telemetry contract

The current top-level telemetry field `tgpQuality` also means `native|hq` and is forwarded through
the shell to decide whether the browser draws the client-side overlay.

Preferred transition:

- Add `tgpResolution: native|mid|high`.
- Keep legacy `tgpQuality: native|hq` temporarily.
- Browser code reads `tgpResolution` first and falls back to legacy `tgpQuality`.
- Client overlay condition becomes `resolution != native`, covering both mirror tiers.
- Remove the legacy alias only in a deliberate protocol-breaking release.

The new JPEG quality does not need to ride every telemetry snapshot for rendering. It belongs in
`/rates-config`; include it in streamed telemetry only if a UI surface genuinely needs live status.

## Capture-path changes

`TgpMirrorCam.Engage(TargetCam, width, height)` already supports arbitrary dimensions and
reallocates its own RenderTexture when the requested size changes. No new camera architecture is
required.

The source selection becomes:

```text
LOW/native -> game's 360x240 TargetWindowRender
MID         -> mirror camera at 720x480
HIGH        -> mirror camera at 1080x720
```

The current global `MaxDim = 720` must become resolution-aware or be removed for known fixed mirror
sizes. Otherwise HIGH will render at 1080x720 and then be immediately downscaled to 720x480 before
readback/encoding.

Recommended shape: resolve a single immutable settings record per capture containing:

- whether the source is native or mirror;
- mirror width/height, if applicable;
- maximum encoded dimension;
- JPEG numeric quality;
- stable wire/config names.

This avoids scattering `if High` checks across source selection, output sizing, IR, telemetry, and
logging.

When resolution changes, reset or dimension-key the source diagnostic log. `_srcLogged` currently
logs only once per engagement, so a live LOW -> MID -> HIGH switch can otherwise hide the actual
new source/capture dimensions during testing.

## TGP CFG page

Replace the existing section with:

### TGP — FEED RESOLUTION

- LOW — 360x240, native game camera, lowest additional cost.
- MID — 720x480 mirror camera, today's HIGH behavior.
- HIGH — 1080x720 mirror camera, sharpest and most expensive.

The description must explain that MID/HIGH render an additional camera and that resolution affects
render, readback, IR, and encode cost.

### TGP — FEED QUALITY

- LOW — JPEG 30.
- MID — JPEG 50, current/default behavior.
- HIGH — JPEG 75.

The description must explain that JPEG quality changes compression/detail and network size but not
resolution or raw readback cost.

Warnings should be specific rather than one generic "HQ costs FPS" message:

- MID/HIGH resolution warning: extra mirror-camera render and larger readback.
- HIGH JPEG warning: larger MJPEG frames and more encode/network pressure.
- Combined HIGH/HIGH warning: maximum CPU/GPU/bandwidth cost.
- Existing refresh-rate warning remains visible above 15 Hz.

Reset restores 15 Hz, LOW/native resolution, MID/50 JPEG quality, and cockpit-feed hiding OFF.

## Performance instrumentation and test matrix

Restore or recreate the previous TGP performance instrumentation before judging the new tiers.
Measure at least:

- `frame(tgpOpen)` average, maximum, p95/p99, and 1% low;
- `CaptureFrame` CPU time;
- IR grayscale time;
- JPEG encode time;
- raw readback completion latency;
- readback drops (`tgpSkipped`);
- encoder-queue drops (new counter);
- delivered JPEG frames/second;
- average/p95 JPEG byte size;
- resulting MJPEG MB/s;
- GC allocations/collections;
- main-thread time before and after worker offload.

### Core 3x3 matrix

At a fixed 15 Hz, test all nine combinations:

| Resolution | JPEG LOW/30 | JPEG MID/50 | JPEG HIGH/75 |
|---|---|---|---|
| 360x240 | test | test (current default baseline) | test |
| 720x480 | test | test (current HIGH baseline) | test |
| 1080x720 | test | test | test (worst case) |

Use at least three representative scenes:

1. flat sky/sea — easy to compress;
2. terrain with dense trees/grass — difficult high-frequency detail;
3. combat scene with smoke, explosions, moving units, overlay text and lock boxes.

Then test HIGH resolution at 10, 15, and 30 Hz with JPEG MID. Do not extrapolate the current 60 Hz
slider ceiling into a claim of support: 30 Hz already produced severe readback drops on earlier
hardware in some sessions, and HIGH multiplies raw bytes per capture by another 2.25 over MID.

### Visual comparisons

- Capture the same target, FOV, bearing, time of day, and camera mode for every setting.
- Compare native cockpit feed, LOW, MID, and HIGH side by side at 1:1 pixels and at actual tablet/
  MFD display size.
- Inspect foliage, thin vehicle silhouettes, diagonal edges, text, lock-box borders, smoke, and IR
  gradients for blockiness/ringing.
- Record exact JPEG byte sizes alongside screenshots; visual benefit without its bandwidth number
  is incomplete evidence.

## Acceptance criteria

- Existing `hq` users migrate to MID/720x480, not HIGH/1080x720.
- Defaults remain native 360x240 + JPEG 50 + 15 Hz.
- Each of the nine combinations persists and restores correctly.
- LOW/native continues to capture the game's baked UICam overlay exactly once.
- MID and HIGH draw the client-side overlay, manual crosshair, Point Track box, and target lock
  boxes exactly once.
- COLOR and IR work in every resolution/quality combination.
- Resolution changes produce the advertised JPEG dimensions.
- JPEG-quality changes do not change dimensions.
- No stale frame from an earlier mission/resolution/quality is published after a switch.
- Encoder work cannot create an unbounded queue.
- Disengage and plugin teardown wait for or safely invalidate worker-owned buffers.
- A slow encoder drops frames instead of accumulating latency.
- No regression in cockpit-feed suppression/restoration.
- Full plugin build, JS suite, C# suite, and web smoke checks pass.
- Live A/B data is recorded in `docs/performance.md` before deciding whether HIGH needs a rate cap.

## Likely files involved

- `src/plugin/TgpMirrorCam.cs` — rename the resolution enum; no fundamental camera redesign.
- `src/plugin/TgpFeed.cs` — resolution lookup, quality lookup, per-tier cap, direct-array encode,
  worker/buffer lifecycle, metrics and compatibility behavior.
- `src/plugin/RatesConfig.cs` — two independent persisted settings and legacy normalization.
- `src/plugin/CommandDispatcher.cs` — new command groups plus the old alias.
- `src/plugin/TelemetrySnapshot.cs` / `TelemetryReader.cs` / `TelemetryJson.cs` — resolution field and
  temporary legacy alias if streamed compatibility is retained.
- `src/plugin/Http/TelemetryServer.cs` — `/rates-config` response and thread-safe completed-frame
  handoff.
- `src/web/pages/tgpcfg/*` — two independent three-button controls, descriptions and warnings.
- `src/web/pages/tgp/tgp.js` / `tgp.html` — mirror-overlay gating based on non-native resolution.
- `src/web/services/telemetry-source.js`, shell relay code and associated tests — carry the renamed
  resolution field.
- `tools/serve_web.py` — preview values for both settings.
- `tools/tests/TelemetryJsonTests.cs` and TGP/telemetry JS tests — new and legacy protocol cases.
- `src/plugin/README.md`, `docs/tgp-high-quality-mode.md`, and `docs/performance.md` — architecture,
  historical naming, and measured results.

## Alternatives considered

### Keep Texture2D encoding on the main thread

Smallest implementation, but the new 1080x720 tier makes its periodic frame-time cost much more
important. Acceptable only if live measurements show the estimate is badly pessimistic.

### General-purpose managed JPEG library

Threadable, but adds a dependency and may be slower or allocate more than Unity's native encoder.
There is little reason to start here while Unity already exposes a documented thread-safe array
encoder in the installed runtime.

### libjpeg-turbo native plugin

Likely faster through SIMD and can run on a worker, while retaining MJPEG. Costs native DLL
packaging, architecture-specific deployment, pinning/interop, and another update surface. Reserve
it for measurements proving Unity's array encoder insufficient.

### Hardware H.264/H.265/AV1 encoding

The real long-term headroom path: raw pixels need not return to the CPU for software JPEG, and
inter-frame compression can reuse prior frames. This replaces the MJPEG transport/browser path and
requires platform/vendor-specific native integration or a streaming library. It is a separate
project, not part of these two controls.

### 1440x960 HIGH

Preserves 3:2 and doubles MID on each axis, but produces 4x MID's pixels, approximately 82.9 MB/s
of raw readback at 15 Hz, and a likely large main-thread encode spike on the current pipeline.
Defer until 1080x720 plus worker encoding has measured headroom.

## Decisions proposed by this plan

- Separate resolution and JPEG quality into independent settings.
- Rename current HIGH/720x480 to MID without changing existing users' effective mode.
- Add HIGH at 1080x720.
- Use JPEG 30/50/75 as the initial LOW/MID/HIGH test values; retain 50 as default.
- Keep native 360x240 + JPEG 50 + 15 Hz as the default combination.
- Preserve `hq` as a legacy MID input and preserve old command/telemetry behavior during a
  compatibility window.
- Prefer Unity's thread-safe raw-array JPEG encoder and a single bounded worker over spawning tasks
  per frame.
- Measure all nine combinations before adding a quality-dependent refresh-rate clamp.

## Remaining decisions and live verification

1. Confirm JPEG 30 and 75 visually; adjust the endpoints if they are too destructive or too large.
2. Decide whether HIGH gets a hard 15 Hz ceiling, a stronger warning only, or an automatically
   suggested lower rate. Do not silently change the user's rate without explicit UI feedback.
3. Decide how long to retain legacy `tgpQuality` command/telemetry aliases.
4. Verify raw pixel channel order and colour space after the switch from Texture2D encoding to the
   array encoder on the actual Direct3D runtime.
5. Re-test the mirror camera across a long flight/floating-origin shift; this remains an open risk
   from the original HQ implementation and becomes more important before expanding its use.
6. Measure raw-buffer allocation/GC pressure. The bounded worker prevents queue growth, but the
   readback callback currently owns one exact-size managed byte array per accepted frame rather
   than a reusable native-buffer ring.

## References

- `src/plugin/TgpFeed.cs` — current readback, IR and JPEG pipeline.
- `src/plugin/TgpMirrorCam.cs` — current 720x480 mirror camera.
- `src/plugin/RatesConfig.cs` — existing rate/quality persistence.
- `src/web/pages/tgpcfg/` — current LOW/HIGH picker.
- `docs/tgp-high-quality-mode.md` — mirror-camera design and historical alternatives.
- `docs/performance.md` — live TGP readback/encode measurements.
- Unity 2022.3 `ImageConversion.EncodeToJPG` documentation — quality range and Texture2D encoder.
- Unity `ImageConversion.EncodeNativeArrayToJPG` / `EncodeArrayToJPG` documentation — thread-safe
  raw-array encoders.
- Unity 2022.3 `AsyncGPUReadback` documentation — asynchronous readback and request lifetime.
