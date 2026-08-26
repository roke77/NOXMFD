# TGP — optional high-quality mode (planning)

## Status

**Shipped**, on the `tgp-hq-mirror-camera` / `tgp-hq-overlay` / `tgp-hq-lockbox` / `tgp-hq-ir`
branch stack (menu entry #3 below, plus the overlay and IR follow-ups this doc left as open
questions). Kept as a historical record of the planning, per this repo's convention for `docs/`
design docs. See "What actually shipped" for how the real implementation diverged from this plan,
and "Open questions" for which ones are now resolved.

**Follow-up:** the single HIGH tier this doc designed was later split into independent
resolution/JPEG-quality controls — see [`tgp-extended-quality.md`](tgp-extended-quality.md).

Two layers here: the **experiment menu** below is the wider option space
for the TGP feed as a whole (cost and quality both), meant to be picked
from when spinning up experiment branches. Everything after it is the
detailed design for one of those entries — the mirror camera.

## What actually shipped

- **Render-on-demand was dropped.** This doc's whole affordability argument rests on rendering the
  mirror camera only at capture ticks instead of every Unity frame. Two tiers were actually built
  and A/B tested live (`docs/performance.md`'s 2026-08-23 entry) — a render-on-demand
  "Performance" tier and an always-enabled "Full" tier — and Performance lost: it cost about the
  same as Full on `frame(tgpOpen)` (the manual `Camera.Render()` call's cost just moved from the
  render loop into the timed capture block) while giving up correct tree/grass rendering (the
  predicted risk from this doc's own reference-mod reading, confirmed live). Full was kept as the
  sole `HighQuality` tier — the mirror camera is a normal `enabled = true`, URP `Base` camera,
  rendered every frame like any other, not render-on-demand at all.
- **The overlay is drawn from telemetry, exactly as recommended** ("Does the mirror cam need its
  own `UICam`? Almost certainly no") — `TgpFeed.PopulateOverlay` mirrors `TargetScreenUI`'s own
  fields into the snapshot, `tgp.js` renders them client-side. Includes the per-target lock box
  (screen-projected via the mirror camera's own `WorldToViewportPoint`, per this doc's explicit
  warning not to use `WorldToScreenPoint`), color-coded by status (friendly/jammed/lased/outdated),
  with a friendly X and a lased target's red crosshair added afterward.
- **IR mode shipped, not via a shared post-process or a shader port.** The open question asked
  whether the mirror cam needs its own post-process to match the game's IR look, or could read the
  same shader values off `TargetCam`. Neither: `TargetCam`'s IR volume is scoped to its own camera
  and not straightforwardly shareable, and a custom shader/volume risked leaking onto other cameras
  through URP's layer-based volume matching. Shipped instead as a CPU auto-levels grayscale
  conversion on the captured bytes, after readback, gated on `TargetCam.UsingIR()` — a basic
  black-and-white look, not a simulated heat curve.
- **The toggle moved off RTS.** "How the player toggles the mode" below describes an RTS-page
  control; RTS itself was later split apart (`nav-items` branch) into a CFG page per source page.
  The quality picker (relabeled LOW/HIGH) now lives on TGP's own CFG page (`/tgpcfg`), not RTS.
- **No resolution/rate clamp was added.** The "whether HQ needs to clamp the TGP rate slider" open
  question was never resolved either way — the slider was later raised to 60 Hz with no clamp tied
  to quality mode, which is the exact "HQ + high Hz" combination this doc's cost analysis flags as
  unaffordable. Still open; see `docs/performance.md`.

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
| 14 | MSAA on the capture RT | Antialiases the feed instead of enlarging it | S | ↑ GPU, **no readback change** | ↑ | The only quality lever that spends on a different axis than the bottleneck. May buy more perceived sharpness per transmitted byte than resolution does |

**Sequencing.** First batch: **#1, #2, #12, #13** — all XS/S, all either
free information or a small change on the measured bottleneck, and #12
can invalidate the two most expensive entries before any effort goes into
them. **#14** is a natural companion to #2, since together they trade
colour for smoothness at constant bandwidth. Only then choose between #3
and #11.

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

**Still open.** Nobody has checked whether `TargetCam.cam.targetTexture` is genuinely the final
picture in this game — the shipped mirror camera reads it the same direct way `TgpFeed` reads the
native path (`OnReadbackComplete` in `TgpFeed.cs`), unchanged by anything this branch stack built.
If Nuclear Option's `TargetCam` does have an HDR-intermediate step like the reference mod's, both
Native and HighQuality would already be quietly affected, and the visual brightness/contrast issues
found and fixed while shipping the IR mode (see `docs/performance.md`) may be a symptom of exactly
this rather than something this doc's own gap analysis anticipated.

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

## Reference implementation: the MissileCamera rig

The same reading turned up something more useful than the integration
itself. The base "Missile Camera" mod (`Mursisru/MissileCamera`) is a
**working implementation of menu entry #3, in this game and this Unity
version** — a self-created camera rendering to its own RenderTexture at
player-configurable resolution. Several of this document's open questions
are answered there rather than needing to be rediscovered.

What it establishes:

- **Render-on-demand works.** Its rig creates a camera, holds it
  `enabled = false`, and drives it with explicit `Camera.Render()` at a
  config-driven rate (default 30 fps), decoupled from the game's
  framerate. It mirrors a reference camera's `cullingMask`, `allowHDR`,
  `allowMSAA` and `clearFlags`, caching so it only writes on change.
- **High resolution is viable.** Feed size is configurable, clamped
  128-2048 for its panel path and 640-3840 for fullscreen.
- **The URP requirement and the particle caveat** — both folded into the
  render-on-demand section above.
- **An HDR intermediate is real, not hypothetical.** The rig keeps a
  second `ARGBHalf` RenderTexture and blits through it for its
  thermal/NVG modes. This is the concrete basis for menu entry #13:
  a camera's `targetTexture` is not necessarily the final picture.

Practices worth copying:

- **Zoom via FOV, never by reallocating the RT.** Its quality-scale
  helper is hardcoded to 1 with the note that optical zoom must not
  recreate RT buckets. `TgpFeed` currently reallocates whenever source
  dimensions change; coupling resolution to zoom would reproduce exactly
  the stutter that rule exists to prevent.
- RT recipe: depth buffer 16, `useMipMap = false`,
  `autoGenerateMips = false`, `filterMode` chosen by context (Point when
  showing a feed 1:1, Bilinear when scaling).
- MSAA sample count matched to the pipeline — the basis for menu entry #14.

### Camera safety constraints in this game

That mod ships a `CAMERA_SAFETY.md` written after breaking these. It
forbids modifying `CameraStateManager.mainCamera` transform/FOV/nearClip,
`cameraPivot` parent/pose/scale, or `cameraMode`, and forbids Harmony
-blocking `CameraBaseState.UpdateState`.

The stated cause is the part that matters here: reparenting left
`cameraPivot` with huge local offsets **because of FloatingOrigin plus
`cockpitViewPoint`**, and on exit the stock cockpit camera flew out of
bounds.

Nuclear Option runs a floating-origin system. This document's plan parents
a new camera to `TargetCam.GetCamMount()`, which is not the same mistake
— parenting our own camera to a game mount, rather than reparenting a
game camera — but any camera created and positioned in the world has to
stay correct across origin shifts, and nothing here has accounted for
that. Treat it as a first-class risk for #3.

Also filed there, relevant the moment any overlay is drawn on a mirror
feed: project world positions with the feed camera's
`WorldToViewportPoint` scaled to screen, **not** `WorldToScreenPoint`,
which returns render-texture pixels.

### What it does not help with

Its in-game path is RenderTexture → `RawImage`. The picture never leaves
the GPU; there is no readback anywhere in the base mod. It therefore
de-risks the render half of #3 and says nothing about the transfer half,
which is the half `cfg-rates` measured. The mirror camera is a solved
problem in this game; the readback is ours alone. That is an argument for
the first batch, not against it.

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
- Keep the mirror cam's `Camera.enabled == false`, set its URP
  `renderType = Overlay`, and drive it with an explicit `Camera.Render()`
  on capture ticks only. Both halves are required — see below.
- Mirror the game camera's `cullingMask`, `allowHDR`, `allowMSAA` and
  `clearFlags`, writing only when they actually differ.
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

Two constraints on this, both from the reference implementation
documented below:

- **`enabled = false` alone is not enough under URP.** An enabled camera
  joins the pipeline's camera stack rather than merely costing a draw.
  The rig has to be `renderType = Overlay` *and* disabled, rendered
  manually. Leaving it enabled cost that project measurable framerate
  with the camera doing nothing else differently.
- **Manual rendering appears to lose particles.** That project keeps a
  pipeline-driven enabled camera specifically for its colour path, noting
  particles depend on it, and uses the manual path only for its
  thermal/NVG modes. For a targeting view — smoke, explosions,
  countermeasures — that is a real fidelity cost, not a detail. Verify
  before assuming render-on-demand is a free equivalence.

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

**As shipped:** RTS itself didn't survive — it split apart (`nav-items` branch, after this doc's
implementation landed) into a CFG page per source page, since each of its two settings only ever
mattered to one page. The quality toggle (relabeled LOW/HIGH) lives on TGP's own CFG page
(`/tgpcfg`) instead, alongside the TGP rate slider — same page, same cost budget, same
`rates.set` → `RatesConfig` path this section describes, just reached from TGP's own nav row
rather than a shared CFG/KEY/LYT/RTS group.

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
   `TargetCam`, parents a disabled mirror `Camera` (URP
   `renderType = Overlay`) to the active mount, matches `fieldOfView`
   and the reference camera's cull/HDR/MSAA/clear settings, renders to a
   new high-res RT on an explicit `Render()` call, and can be cleanly
   disposed. Verify behaviour across a floating-origin shift, and check
   whether particles survive the manual render path.
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

- ~~Default HQ resolution.~~ **Resolved, shipped as planned:** 720×480 (`TgpFeed.HqWidth`/`HqHeight`).
- **Still open.** Does the mirror cam stay correct across a floating-origin shift? Unknown, and the
  one failure mode in this area that has already burned another mod in this game. Nobody has tested
  a long flight, only spawn-and-look sessions — this is the biggest unverified risk left in the
  shipped feature.
- ~~Do particles render on the manual `Camera.Render()` path?~~ **Resolved, moot:** render-on-demand
  was dropped (see "What actually shipped") — the shipped mirror cam is always enabled/`Base`, so
  it renders through the normal pipeline, particles included. Live-tested and confirmed correct.
- **Still open, and now live.** Whether HQ needs to clamp the TGP rate slider — no clamp was ever
  added, and the slider's ceiling was later raised from 30 Hz to 60 Hz with no tie to quality mode.
  30 Hz + HQ was already flagged as unaffordable; 60 Hz + HQ is untested territory past that.
- ~~Does the mirror cam need its own `UICam`?~~ **Resolved, shipped as recommended:** no — the
  overlay renders client-side from the telemetry snapshot.
- ~~IR mode~~ **Resolved, but not as scoped here:** shipped as a CPU auto-levels grayscale
  conversion after readback, not a shared post-process or a shader-value read off `TargetCam` —
  see "What actually shipped" for why. Menu entry #2 (drop colour depth as a general bandwidth
  lever, independent of IR mode) was never built as its own toggle.

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
- Read `Mursisru/MissileCamera`'s `Camera/MissileCameraRig.cs`,
  `Camera/MissileCameraRenderPrep.cs` and `Fullscreen/CAMERA_SAFETY.md`
  before writing the mirror cam. That rig is a working version of #3 in
  this game, and its safety doc is a list of camera mistakes already made
  here and paid for.
- **Restore `src/plugin/PerfLog.cs` and its call sites from history
  first.** It was removed once the `cfg-rates` findings were banked, but
  its two key instruments — `frame(tgpOpen)` vs `frame(tgpClosed)` split
  and `TgpFeed.ReadbackSkipCount` — are exactly what says whether a
  mirror cam is affordable. Restore it, take a native-path baseline on
  the test machine, then build against that number rather than the ones
  quoted here.
