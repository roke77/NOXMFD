# TGP — optional high-quality mode (planning)

## Status

Planning only. No code yet. Default TGP behavior is the
native-resolution path that ships today; this document describes an
opt-in toggle for a sharper feed.

Two layers here: the **experiment menu** below is the wider option space
for the TGP feed as a whole (cost and quality both), meant to be picked
from when spinning up experiment branches. Everything after it is the
detailed design for one of those entries — the mirror camera.

## Goal

Let the player choose a higher-quality TGP feed when they have the
GPU headroom to spend on it, without compromising the default
experience for anyone who doesn't. The two modes coexist; only one
is active at a time.

| Mode               | Source resolution | GPU cost | Default?    |
|--------------------|-------------------|----------|-------------|
| **Native** (today) | game's prefab (~360×240) | already measurable — see below | yes |
| **High-quality**   | mirror cam at e.g. 720×480 | ~4× the readback bytes per tick, plus a camera pass | no — opt-in |

## Measured baseline

`docs/performance.md`'s `cfg-rates` section (issue #39) is the first
real instrumentation of this pipeline, and it constrains this feature
directly:

- TGP's GPU cost is **already user-perceptible at the shipping 15 Hz
  default**. `frame(tgpOpen)` windows logged 57–100 ms worst-case frame
  times against clean 6–8 ms `frame(tgpClosed)` windows.
  `CaptureFrame`'s own C# time was 0.03 ms, so the entire cost lives in
  the `Blit` + `AsyncGPUReadback` GPU work.
- Doubling the capture rate to 30 Hz overran the GPU: `tgpSkipped`
  (the `_readbackInFlight` guard in `TgpFeed.cs`) dropped 33–69 ticks
  per 5 s window, up to ~45% of attempted captures.

That 30 Hz result scaled readback **frequency**. High-quality mode
scales the other term in the same product — **pixels per readback**.
720×480 is 4× the pixels of the ~360×240 native source, so HQ at 15 Hz
moves readback bytes per second into roughly the same band as native at
60 Hz, well past the point already shown to saturate. (Not a strict
1:1 — fewer, larger transfers amortize per-request overhead better than
many small ones — but it is the same wall approached from the other
side.) On top of that, HQ adds a cost the 30 Hz test never included: a
second camera render.

The practical consequence is in the approach below: this mode is only
affordable if the mirror cam renders **on demand**, not every frame.

## Experiment menu

The feed does four things per capture tick: point a camera (free today —
it borrows the picture the game already draws for the cockpit screen),
pull that picture off the GPU, JPEG-compress it, and send it to the
browser as a flipbook of stills. Every entry below either makes the
pull-off-the-GPU step carry less, or skips it.

Roughly ordered by recommendation, not by ambition. One branch per entry.
Entries 12-13 were added later and belong early in the sequence despite
their numbers — see the sequencing note below the table.

| #  | Approach | What it changes | Effort | Cost | Quality | Notes |
|----|----------|-----------------|--------|------|---------|-------|
| 1  | Measure the JPEG step | Nothing yet — says whether compression is part of the stutter | XS | — | — | Do first. It runs on the main thread and sits outside the block `cfg-rates` timed, so it is a known unknown in the middle of a known-stuttery path |
| 2  | Drop colour depth | 4× (mono) or 2× (half-precision) less data through the saturated channel | S | ↓↓ | ~none | Best cost/benefit available. Verify against the IR and daylight modes before assuming colour is disposable — the page renders it untinted today |
| 3  | Mirror camera, render-on-demand | Chooses its own resolution instead of inheriting ~360×240 | M | ↑ | ↑↑ | Detailed in the rest of this doc. #2 roughly pays for its extra pixels; test the pair, not #3 alone. Run #12 first |
| 4  | Tighter visibility gating | Stops capturing for a hidden tab or off-screen pane | XS | ↓ | none | No downside. Fold into whichever branch runs first |
| 5  | Synthetic sensor view | Draws a pod-style picture in the browser from telemetry already streamed — no pixels captured at all | M | **zero** | different | Doesn't compete on the same axis. The only entry where the performance question disappears rather than shrinking |
| 6  | JPEG compression off the main thread | Same work, off the frame's critical path | M | ↓? | none | Gated on #1 finding a cost worth moving |
| 7  | Interlacing | Half the rows per capture tick | S | ↓ | ↓ shimmer | Reserve. Spend #2's budget first |
| 8  | GPU hardware video encode | Compresses on the GPU; raw pixels never come back | XL | ↓↓↓ | ↑↑↑ | The only path with real headroom. Vendor-specific, needs a native plugin or a streaming library — its own project |
| 9  | Real video transport | Sends inter-frame deltas instead of whole stills | L | ↓↓ | ↑ | Pairs with #8; little value alone |
| 10 | External window capture | A separate desktop app grabs the game window | L | zero | ↓ | Can't isolate the pod view, and is a second program to ship |
| 11 | Swap the game's camera to a larger RT | Reuses the free render at a higher resolution | S | ↑↑ | ↑↑ | Parked, not closed — see below. Run #12 first; it may make this moot |
| 12 | Ask whether the game already has a higher-res path | Triggers an existing quality path instead of building one | S | ? | ↑↑ | Upstream of #3 and #11 — if `TargetCam` renders larger in some context (zoomed/expanded targeting view), high quality costs nothing extra |
| 13 | Verify we capture the authoritative texture | Possibly fixes picture quality with no cost change | XS | — | ↑? | A quality-bug hypothesis, not an optimisation. Compare the page against the cockpit screen side by side |

**Sequencing.** First batch: **#1, #2, #12, #13** — all XS/S, all either
free information or a small change on the measured bottleneck, and #12
can invalidate the two most expensive entries before any effort goes into
them. Only then choose between #3 and #11.

### On #11

The swap approach was tried and abandoned because it repositions the
in-cockpit targeting overlay (the UI canvas snaps to the swapped RT's
dimensions instead of the prefab's) and costs framerate even with no
subscriber, since it enlarges a render the game performs every frame
regardless.

It stays on the menu because neither failure has actually been root-caused
— the overlay displacement in particular is a canvas/aspect problem that
may have a fix, and if it does, the swap path gets high resolution
*without* paying for a second render pass, which is the one thing the
mirror camera cannot offer. Worth a branch to characterise the two
failures properly rather than inheriting the earlier verdict. The mirror
camera remains the default plan unless that investigation finds something.

## Findings from a third-party integration

Menu entries #12 and #13 come from reading a community integration that
bridges the "Missile Camera" mod into a NOXMFD page (lupfine's forks of
`Mursisru/MissileCamera` and of this repo). That work solves a different
problem — making a feed exist at all, where the owning mod only renders
while its own cockpit panel or fullscreen view is up — and it reuses
`TgpFeed`'s capture pipeline unchanged, so it contributes nothing to the
cost question. Four things in it are relevant here anyway.

**Resolution negotiation (#12).** That integration didn't build a camera
to get better quality. It set a flag the owning mod already consulted, so
its existing size-resolution logic returned fullscreen-grade dimensions
instead of the small cockpit-panel size. The quality path already
existed; it just had to be asked for. The equivalent question for TGP has
never been asked: whether the game itself renders `TargetCam` larger in
some context we could trigger. If it does, high quality costs nothing
that a mirror camera or an RT swap would cost.

**Authoritative texture (#13).** That integration deliberately reads a
dedicated output texture rather than the feed camera's own
`targetTexture`, because the camera's target is swapped to an
intermediate HDR buffer mid-render and restored afterwards — so reading
it at an arbitrary moment does not reliably yield the final picture.
`TgpFeed` reads `cam.targetTexture` directly. If Nuclear Option's
`TargetCam` does anything comparable (post-processing, HDR intermediate,
temporal effects), the captured picture may not match what the cockpit
screen shows. This presents as the feed looking subtly wrong, not as a
failure, which is why it would not have surfaced as a bug report.

**Thermal modes double as the colour-depth justification.** That
integration exposes the vision filter (Color / NightVision / WhiteHot /
BlackHot / Contour) as a page-cyclable feature. WhiteHot and BlackHot are
monochrome by definition, so offering thermal modes on the TGP page makes
menu entry #2 unambiguous rather than a judgement call — in those modes
there is no colour to discard. A feature and a bandwidth optimisation
that happen to be the same change. This supersedes the IR question filed
under open questions below as "cosmetic detail, defer."

**MJPEG cold-start stall — likely a live bug here.** Their `/rc.mjpg`
handler originally sent a 1×1 placeholder JPEG immediately on connect,
because a fresh connection that receives zero bytes while the pipeline
warms up can be marked failed by the browser and never recover once real
frames arrive — presenting as "works after one page reload." That
workaround was reverted and the issue left open on their side.

`TelemetryServer.HandleMjpegAsync` has the same shape: it writes nothing
until `_tgpJpg != null && id != lastSeen`. TGP's warm-up is shorter than
a missile camera's (no missile-selection step), which may be why it has
not been reported, but the failure mode is identical. This is a
correctness bug rather than a cost or quality question, so it is not a
menu entry — it wants its own ticket, and reproducing it deliberately
(connect a client before the first frame exists) is the first step.

## Why this is a separate planning doc

We already tried the obvious implementation of high-quality (swapping
`TargetCam.cam.targetTexture` to a larger RT) and learned the hard
way that it has two costs:

- a noticeable FPS hit even when nobody is watching the feed (because
  it forces the game's cam+UICam to render at the larger size every
  frame, not just on capture ticks); and
- it **repositions the in-cockpit targeting overlay** (the white box +
  red crosshair) because the UI canvas snaps to the swapped RT's
  dimensions instead of the prefab's.

The second is a UX dealbreaker: even if the player accepts the FPS
cost, they shouldn't lose their in-cockpit targeting reticle to opt
into a sharper web feed. So if we add this mode at all, it has to be
done a different way.

## Approach: mirror camera

Instead of redirecting the game's existing TargetCam, spawn **our
own** `Camera` as a sibling on the same mount point.

- Read `TargetCam.GetCamMount()` for the active mount (forward / rear /
  landing) and parent the mirror cam there.
- Copy the private `cam.fieldOfView` from the game's `TargetCam` on each
  capture tick so the mirror tracks the same zoom-on-target behavior.
- (Optional) copy IR state so the mirror reflects `SwitchIRState`.
- Render our cam to **our own** RenderTexture at the chosen high-res
  size (configurable; start with 720×480 — `TgpFeed.MaxDim` is already
  720 and currently a no-op, since the native source is smaller, so
  the encode side needs no change to carry this).
- Keep the mirror cam's `Camera.enabled == false` and drive it with an
  explicit `Camera.Render()` on capture ticks only. See below.
- Do NOT touch the game's `TargetCam.cam`, `UICam`, viewport rects,
  aspect, or material. The in-cockpit screen stays exactly as
  vanilla — the cam and UI canvas it depends on are completely
  unmodified.
- Feed the high-res RT into the same `Graphics.Blit` → `AsyncGPUReadback`
  → `EncodeToJPG` pipeline the native path uses today.

### Render on demand, not every frame

An `enabled` camera renders every frame — 60 renders/s to serve a 15 Hz
feed, three quarters of them discarded. Disabling the component and
calling `Camera.Render()` from the capture tick cuts the render cost to
a quarter of that at the default rate, and makes the cost scale with
the TGP rate slider instead of with the player's framerate.

This is the difference between the feature being affordable and not —
but it does not make the mode free. The native path piggybacks on a
render the cockpit needs anyway: `TargetCam` renders for the in-cockpit
screen regardless, so today's cost is the `Blit` + readback alone. The
mirror cam is a genuinely additional render pass. Rendering on demand
takes it from 60/s down to the capture rate; it does not take it to
zero, and it will not be cheaper than what ships today.

The remaining honest tradeoff is the readback: 4× the bytes per tick,
on a transfer already shown to back up. If HQ at 15 Hz produces a
`tgpSkipped` count resembling native at 30 Hz, the resolution is too
high for the machine — which is what makes this opt-in rather than a
new default.

## What we deliberately don't do

- We **don't build on the swap-based path** (overriding
  `cam.targetTexture` and `UICam.targetTexture`) *as this design's
  foundation*. As it stands it breaks the in-cockpit overlay
  positioning, and mirror cam is the only path currently known to
  satisfy "high-res web feed AND vanilla cockpit display." It remains
  a live investigation in its own right — menu entry #11 — because a
  fix for the overlay displacement would make it strictly cheaper than
  this design.
- We **don't fall back automatically** based on framerate. The user
  is choosing the mode explicitly; we don't second-guess them.

## How the player toggles the mode

On the **RTS page**, next to the TGP rate slider it shares a cost
budget with. That page already owns TGP tuning, is already discoverable
in the MFD nav (CFG/KEY/LYT/RTS), and already has the whole
`rates.set` → `RatesConfig` → live-apply path this needs — a quality
toggle is one more control on an existing surface rather than a new one.

Follow `RatesConfig`'s existing shape: a `ConfigEntry` for persistence,
a setter the command dispatcher calls, and `SettingChanged` forwarding
the new value to a `SetMode(...)` on the feed that engages/releases the
mirror cam. Being a `ConfigEntry` also means Configuration Manager (F1)
picks it up for free.

One thing to carry over from `docs/performance.md`: a `ConfigEntry.Value`
write triggers a synchronous `.cfg` save that measured up to ~9 ms on the
main thread. A discrete toggle writes once per click, so this is fine as
long as it stays a toggle — the `input`-for-display / `change`-for-persist
split only matters for continuous controls like the sliders.

## State machine

Three states, one per mode plus idle:

```
Idle  ─── (subscriber appears) ──▶  Native
Idle  ─── (subscriber appears, config=HQ) ──▶  HighQuality
Native ──── (config change → HQ) ──▶  HighQuality   (engage mirror cam)
HighQuality ── (config change → Native) ──▶  Native (release mirror cam)
Native / HighQuality ── (last subscriber leaves) ──▶  Idle
```

Both Native and HighQuality use the same readback + encode +
gating + Disengage path that already exists; the only thing the mode
governs is the **source texture** the readback reads from:

- Native: `tc.cam.targetTexture` (game's own RT) — today's behavior.
- HighQuality: our mirror cam's RT.

So the mode switch is, in code: "swap the `Texture src` we Blit from,
and engage/release the mirror cam GameObject." Everything downstream
is shared.

## Implementation sketch (when we get to it)

1. **Mirror cam controller.** A small helper class that, given a
   `TargetCam`, parents a disabled mirror `Camera` to the active mount,
   matches `fieldOfView`, renders to a new high-res RT on an explicit
   `Render()` call, and can be cleanly disposed.
2. **Config entry + RTS toggle**, following `RatesConfig`. Subscribe to
   `SettingChanged`; forward the new value to the feed.
3. **Reader switches `src`.** In `CaptureTgpFrame`, after the
   subscriber gate but before the size/Blit logic:
   - If mode == HighQuality, ensure mirror cam exists (engage if not),
     update its fov/mount, `Render()` it, set `src = mirror.targetTexture`.
   - If mode == Native, ensure mirror cam is released, set
     `src = tc.cam.targetTexture` (today's path).
4. **Disengage releases the mirror cam too** — extend the existing
   `DisengageTgp()` to call into the mirror cam controller's
   teardown.
5. **Preview harness.** The `tools/serve_web.py` http harness can supply a
   mock quality field if we want to show a mock toggle, but it doesn't need
   to render anything different — the MFD pane treats both modes the same.

## Open questions to settle while implementing (not now)

- Default HQ resolution. 720×480 keeps aspect with the native source and
  fits `TgpFeed.MaxDim`; 1280×720 is sharper but roughly triples the
  readback bytes again, on the transfer the measurements already flag.
  Ship 720×480 and let the readback numbers decide whether a higher rung
  is even offerable.
- Whether HQ needs to clamp the TGP rate slider. Rate and resolution
  multiply into the same readback budget, so 30 Hz + HQ is a combination
  the measurements say cannot work. Capping the slider while HQ is
  engaged may be simpler than letting the player find the cliff.
- Does the mirror cam need its own `UICam` to draw the overlay
  (mag/dist/grid/mode)? Almost certainly no — we'd render those
  overlays on the MFD client from the SSE snapshot, which is free.
- IR mode: does the mirror cam need its own post-process to match the
  game's IR look, or can we read the same shader values off the
  TargetCam? No longer purely cosmetic — a monochrome thermal mode is
  also what makes menu entry #2 free of judgement, so this rides with
  the colour-depth experiment rather than being deferred.

## Out of scope

- Actually building any of this. This document is plan-only.
- Changing the default mode. Native stays default.
- Removing the current gating or async-readback work — both apply to
  HighQuality unchanged.
- The swap approach, *within this design* — it's tracked separately as
  menu entry #11, not folded in here.

## Pre-flight before implementing

- Read `src/plugin/TgpFeed.cs` — it maps the relevant `TargetCam`
  internals (cam / mount / fov reflection) and implements the subscriber
  gating + async readback this mode has to carry over. `TgpFeed.cs` is
  the source of truth for the pipeline.
- Read `docs/performance.md`'s `cfg-rates` section in full. It is the
  only measurement this feature has.
- **Restore `src/plugin/PerfLog.cs` and its call sites from history
  first.** It was removed once the `cfg-rates` findings were banked, but
  its two key instruments — `frame(tgpOpen)` vs `frame(tgpClosed)` split
  and `TgpFeed.ReadbackSkipCount` — are exactly what says whether a
  mirror cam is affordable. Restore it, take a native-path baseline on
  the test machine, then build against that number rather than the ones
  quoted here.
