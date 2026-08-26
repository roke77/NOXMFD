# TGP feed — performance architecture alternatives

## Status

Architecture investigation. No alternative in this document is selected for implementation.

The current extended-quality implementation remains the baseline: three capture resolutions,
three JPEG quality values, asynchronous GPU readback, and one bounded background JPEG worker. This
document identifies other designs that could improve frame-time consistency, capture rate,
bandwidth, allocation pressure, or high-resolution headroom.

## Current architecture

The browser receives the TGP as an MJPEG stream:

```text
TargetCam or MID/HIGH mirror camera renders on the GPU
  -> Graphics.Blit into a capture RenderTexture
  -> AsyncGPUReadback returns raw RGBA pixels
  -> one managed byte array owns the completed readback
  -> one bounded background worker optionally converts IR to grayscale
  -> ImageConversion.EncodeArrayToJPG
  -> TelemetryServer stores the latest JPEG
  -> /tgp.mjpg sends independent JPEG frames to each browser
```

The design already avoids a synchronous GPU readback and keeps JPEG compression off Unity's main
thread. It also bounds latency: only one pending encode is retained, so a slow encoder drops stale
work instead of building an unbounded queue.

Its remaining performance costs are structurally different:

- MID and HIGH use an additional camera that renders every Unity frame while engaged, even when the
  stream captures at a lower rate such as 15 Hz.
- Every accepted frame crosses from GPU memory to CPU-accessible memory in full RGBA form.
- The readback callback creates an exact-size managed byte array for every accepted frame.
- JPEG compresses every frame independently, so successive frames cannot reuse unchanged image
  information.
- Higher JPEG quality increases encode work and bandwidth without changing resolution.
- Every MJPEG client receives a complete JPEG for every delivered frame.

The correct optimization depends on which of these costs dominates in the live game. Average and
p95 camera-render time, readback completion time, encode time, dropped captures, JPEG size, managed
allocations, delivered frame rate, and browser latency should be measured separately.

## Alternative 1 — adaptive MJPEG policy

Keep the current renderer, worker, endpoint, and browser `<img>` consumer, but adapt capture work to
observed backlog.

Possible policy inputs:

- readback already in flight;
- encoder pending-slot replacement rate;
- recent encode duration;
- recent JPEG byte size;
- active resolution and JPEG quality;
- number of TGP subscribers.

The controller can lower capture rate first, then JPEG quality or resolution only if the player has
explicitly enabled automatic adaptation. A simpler initial version can leave settings untouched and
only skip scheduled captures while either stage is busy.

**Expected benefit:** protects latency and game frame consistency during expensive HIGH/HIGH scenes.

**Cost and risk:** low to medium. The main risk is unstable quality or frame-rate oscillation, so
changes need hysteresis and minimum dwell times. Automatic resolution or quality changes also need
clear UI feedback and must not silently override explicit player choices.

**What it does not solve:** full RGBA readback, independent-frame JPEG bandwidth, and continuous
mirror-camera rendering.

## Alternative 2 — reusable readback buffers

Replace the managed `ToArray()` allocation per accepted frame with a small owned buffer ring. Each
slot remains unavailable until the encoder finishes with it; a full ring drops the incoming frame.
Unity native containers or pinned/reusable managed buffers are possible implementations, depending
on which encoder overload and lifetime rules work reliably in the installed Unity runtime.

**Expected benefit:** lower allocation rate and fewer garbage-collection disturbances, especially
at HIGH resolution or high capture rates.

**Cost and risk:** medium. Ownership must remain explicit across the asynchronous readback callback,
encoder thread, disengage, setting changes, and mission teardown. Reusing a slot before encoding
finishes would corrupt a frame.

**Decision gate:** implement only if live allocation and GC measurements show that the current
per-frame byte array is material. Pooling adds lifecycle complexity but cannot reduce camera-render,
readback, JPEG computation, or network cost.

## Alternative 3 — render the mirror camera at capture cadence

MID and HIGH currently keep the mirror camera enabled so URP renders it every game frame. The feed
usually consumes only 15 of those renders per second. Rendering the mirror only when a capture is
scheduled could remove unused camera passes.

A direct `Camera.Render()` implementation is not suitable as-is: live investigation found that it
loses tree and grass detail in this game. A viable prototype therefore needs a render-pipeline-aware
trigger, such as enabling the camera for a selected normal URP frame and requesting readback after
that render completes.

**Expected benefit:** potentially the largest incremental GPU saving when game FPS is much higher
than TGP capture rate.

**Cost and risk:** medium to high. Render ordering, one-frame state transitions, overlay projection,
manual camera movement, foliage, post-processing, and camera-stack behavior all need live testing.
Incorrect scheduling can capture an old frame or a partially synchronized camera.

## Alternative 4 — faster software JPEG encoder

Keep MJPEG and the browser contract, but replace Unity's array JPEG encoder with a native optimized
encoder such as libjpeg-turbo.

```text
GPU readback -> owned RGBA buffer -> native JPEG worker -> existing MJPEG endpoint
```

**Expected benefit:** lower encode time and possibly better high-resolution throughput while
retaining the existing transport and UI.

**Cost and risk:** medium. It adds a native DLL, architecture-specific packaging, interop, buffer
pinning, colour-format conversion, error handling, and another dependency to update. Actual gains
must be benchmarked against Unity's encoder on the same hardware; a native dependency is not
justified by theoretical throughput alone.

**What it does not solve:** mirror-camera cost, GPU readback, independent-frame bandwidth, or
per-client full-frame delivery.

## Alternative 5 — sidecar video encoder

Move compression and video transport out of the Unity plugin. NOXMFD hands raw frames to a local
helper process through shared memory or another bounded local IPC channel. The helper uses a mature
video stack such as FFmpeg or GStreamer to encode and serve the browser stream.

```text
Unity camera -> GPU readback -> bounded local IPC
  -> helper process -> H.264/other video encoder -> browser transport
```

**Expected benefit:** isolates encoder CPU work and failures from the game process, enables mature
software and hardware encoders, and makes codec experiments possible without repeatedly changing
Unity-side code.

**Cost and risk:** high. Distribution is no longer a single plugin DLL. Process startup, executable
trust, version matching, IPC backpressure, crash recovery, firewall behavior, installation through
mod managers, and cleanup all become product concerns. Raw frames still leave the GPU unless the
sidecar can receive a shared GPU surface, which is substantially more platform-specific.

## Alternative 6 — hardware video encoding and browser video transport

Replace MJPEG with an inter-frame video pipeline, preferably without copying full raw frames back to
managed memory:

```text
TGP GPU texture
  -> platform/native GPU-surface interop
  -> hardware H.264 encoder
  -> WebRTC or another low-latency browser video transport
  -> browser video decoder
```

This is the strongest architectural alternative. H.264 is the conservative first codec candidate
because browser and hardware support are broad; other codecs can be evaluated only after the
transport boundary is proven.

**Expected benefit:**

- removes software JPEG work from the game process;
- can avoid or reduce GPU-to-CPU raw-pixel transfers;
- uses dedicated GPU video-encode hardware where available;
- uses temporal compression between frames, substantially reducing bandwidth compared with MJPEG;
- offers more headroom for 1080x720 at 30 FPS or higher.

**Cost and risk:** very high. Unity texture interop, graphics API differences, GPU-vendor encoder
APIs, hardware availability, codec negotiation, keyframe policy, packet loss, latency buffering,
browser lifecycle, and fallback behavior all need design and testing. WebRTC adds connection and
signalling machinery even on a local network. A fallback is required for unsupported hardware or
drivers.

This option changes the feature from an image endpoint into a real-time media subsystem. It should
start as a disposable proof of concept rather than a production rewrite.

## Comparison

| Alternative | Implementation cost | Main-thread/GPU benefit | Encode benefit | Bandwidth benefit | Keeps current browser/MJPEG contract |
|---|---|---|---|---|---|
| Adaptive MJPEG | low-medium | indirect | indirect | moderate when throttled | yes |
| Reusable buffers | medium | GC consistency only | none | none | yes |
| Capture-cadence mirror render | medium-high | potentially high GPU saving | none | none | yes |
| Native JPEG encoder | medium | none | potentially high | encoder-dependent | yes |
| Encoder sidecar | high | isolates CPU work | potentially high | high with video codec | no |
| Hardware video + WebRTC | very high | potentially highest | potentially highest | highest | no |

These estimates are hypotheses, not benchmark results. In particular, a faster encoder does not
help if mirror rendering or GPU readback is the dominant cost, and buffer pooling does not help if
garbage collection is already negligible.

## Recommended investigation order

### Phase 1 — establish the bottleneck

Measure LOW/MID/HIGH at 15 and 30 Hz with JPEG MID and HIGH. Record:

- mirror-camera render cost;
- readback request and completion rate;
- readback-in-flight skips;
- encoder time and pending-frame drops;
- raw-buffer allocation rate and GC collections;
- JPEG average and p95 size;
- delivered FPS and end-to-end browser latency;
- game average FPS, frame-time p95/p99, and 1% lows.

### Phase 2 — test low-disruption changes

1. Add diagnostic-only adaptive/backlog calculations without changing player settings.
2. Prototype capture-cadence mirror rendering and verify foliage, post-processing, COLOR/IR, manual
   control, Point Track, overlays, and floating-origin behavior.
3. Prototype reusable buffers only if Phase 1 identifies meaningful allocation or GC pressure.

### Phase 3 — compare encoders

Benchmark Unity JPEG against a native JPEG encoder using identical captured buffers. Compare encode
time, output size, visual quality, allocations, packaging cost, and shutdown behavior. Retain MJPEG
if it satisfies the desired resolution and frame-rate targets.

### Phase 4 — prove the architecture replacement

Build a narrow hardware-video prototype for one supported Windows graphics path and one browser. It
only needs to answer:

- Can the TGP GPU texture reach the encoder without a managed RGBA copy?
- Does encode/decode latency remain acceptable for manual camera control?
- What bandwidth and frame-time improvement does it provide over HIGH/HIGH MJPEG?
- Can the system fall back cleanly when hardware encoding is unavailable?
- Can it be packaged without making installation or support unreasonable?

Only a successful prototype and measured need justify replacing the current MJPEG endpoint.

## Decision guidance

- If HIGH/HIGH is visually useful and meets frame-time targets, keep the current architecture.
- If latency rises only under encoder backlog, prefer adaptive capture scheduling.
- If GC spikes correlate with frame allocations, add a bounded reusable-buffer ring.
- If mirror rendering dominates, prioritize capture-cadence rendering before changing codecs.
- If JPEG encoding dominates but bandwidth is acceptable, benchmark a native JPEG encoder.
- If both encoding and bandwidth prevent the desired resolution/frame rate, prototype hardware
  video transport.
- If native codec integration is valuable but unsafe inside Unity, evaluate the sidecar boundary.

## Related documents

- `docs/tgp-extended-quality.md` — current resolution, JPEG-quality, and background-worker design.
- `docs/tgp-high-quality-mode.md` — mirror-camera design and earlier capture alternatives.
- `docs/performance.md` — measured TGP capture and mirror-camera results.
- `src/plugin/TgpFeed.cs` — active readback, queue, grayscale, and JPEG pipeline.
- `src/plugin/TgpMirrorCam.cs` — active MID/HIGH mirror camera.
