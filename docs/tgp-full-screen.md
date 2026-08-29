# TGP Full Screen

[Issue #70](https://github.com/roke77/NOXMFD/issues/70).

## Status

First pass implemented (`TgpFullScreen.cs`), not yet tested in-game. Design revised after two
findings that changed the approach for the better: `TgpFeed.cs` already answers where the native
picture comes from, and a side-by-side look at the community **MissileCamera** mod
(`_scratch/mursisru-missile-camera/`, already in this repo from earlier research) shows a mature,
working solution to the exact resolution/quality problem this ticket raises.

What shipped in the first pass: a dedicated `TgpMirrorCam` instance capped at 1080p (see "ponytail"
note in the source for the upgrade path), a `ScreenSpaceOverlay` canvas with the feed `RawImage`
and four corner readout labels (type/range/altitude/heading-or-elevation/mode) driven by a private
`TgpOverlay`, and the two keybinds. Not yet done: per-target lock boxes on the full-screen view
(the projection function is already wired for it, just unused), and the airframe `cullingMask`
exclusion (still deferred pending an in-game look, per "The hide the airframe idea" below).

## Goal

A cinematic full-screen view of the TGP camera feed — not a stretched picture-in-picture, a real
high-quality view a pilot would want to just watch. A second keybind toggles a readout overlay
(range/altitude/heading/mode/etc.) on or off while full screen is active.

## Why not just show the native picture full screen

The first pass of this doc proposed grabbing `TargetCam`'s own `cam.targetTexture` and stretching
it into a full-screen `RawImage`. That texture is a fixed-size `RenderTexture` the game allocates
once for an in-cockpit instrument — sized for a small physical screen (`TargetCam` projects onto a
3D screen mesh in the cockpit, `targetScreenRenderer`, per `TgpFeed.cs`), not a monitor. Stretched
full screen it would be soft/blocky — directly the "resolution and image quality" problem raised.
The exact pixel size isn't in any script (Unity asset setting), but `TgpFeed.CaptureFrame()` already
logs it every time the web `/tgp` page runs at Native resolution (`"TGP Native source WxH"`) — a
known-knowable number, just not one worth designing around now that a better option exists.

## The better approach: an independent high-resolution mirror camera

NOXMFD already solves this exact problem for the web page's MID/HIGH quality settings:
**[`TgpMirrorCam.cs`](../src/plugin/Tgp/TgpMirrorCam.cs)** creates its own `Camera`, parents it to
`TargetCam`'s active mount (`tc.GetCamMount()`), copies FOV/cullingMask/HDR/MSAA/clearFlags from
the real `TargetCam.cam` each tick, and renders into a `RenderTexture` sized however the caller
asks — completely independent of the native instrument's own fixed resolution. `TgpFeed.cs` already
proves this looks right: it's the same technique already shipping today for the web page's HIGH
setting.

**MissileCamera does the same thing for exactly this reason**, and its numbers confirm the
approach: `MissileCameraRig` builds its own camera + `RenderTexture`, and
`MissileCameraFeedConfig.ResolveActiveFeedSize` requests a **small** texture (128–2048px) normally,
but jumps to **640×360 up to 3840×2160** specifically once
`MissileCameraFullscreenController.IsActive` — i.e., the same "tiny instrument feed vs. cinematic
full screen" problem, solved by re-sizing an independent camera's render target on entering full
screen, not by stretching the small one.

**Plan: extend `TgpMirrorCam` (or a full-screen-specific sibling built the same way) to render at a
resolution matched to the screen** (e.g. `Screen.width`/`Screen.height`, or a config-driven cap
like MissileCamera's 3840×2160) only while full screen is active, and display *that* texture in the
full-screen `RawImage` — not `cam.targetTexture`. Bilinear filtering + MSAA (`TgpMirrorCam` already
sets `FilterMode.Bilinear`; MSAA sample count would need adding, mirroring
`MissileCameraRig.ApplyConfig`'s `antiAliasing = msaa`) gets the rest of the way to "cinematic"
instead of "instrument video."

## The "hide the airframe" idea

Worth offering, but as a **camera `cullingMask` exclusion on the mirror camera**, not by disabling
the aircraft's actual renderers:

- **Cheaper and safer.** A `cullingMask` change is a per-camera setting with no state to restore
  elsewhere, no risk of the aircraft staying invisible to the pilot's own main view or to other
  players in multiplayer if a restore path is ever missed (the exact class of bug this mod's own
  `HudDeclutter`/`ImmersionState` code goes out of its way to avoid — always restoring native state
  on every exit path, not just the happy one).
  Disabling renderers directly would need that same discipline for comparatively little benefit.
  MissileCamera doesn't need this trick at all (a missile in flight is already away from the
  launching aircraft), so there's no existing reference implementation for it in that codebase —
  it'd be new, TGP-specific work.
- **Whether it's needed at all is unconfirmed.** A pod's mount FOV is normally narrow and zoomed on
  a distant target, unlikely to catch the aircraft's own nose/wing — this may simply never come up
  in practice. Worth checking once the mirror-camera full-screen view exists and a wide/cinematic
  FOV is tried in-game, rather than building an exclusion for a problem not yet observed.

## What already exists to reuse

- **The feed itself needs no new plumbing.** `CombatHUD.LateUpdate` already calls
  `aircraft.targetCam.SetTargetCam()` continuously whenever `targetList.Count > 0`
  (`_scratch/full/CombatHUD.cs`), independent of which weapon is selected.
- **The mirror-camera technique is already proven code**, not a new risk — `TgpMirrorCam.cs` is
  live today for the web page's HIGH setting.
- **The readout data already exists as a plain object.** [`TgpOverlay.cs`](../src/plugin/Tgp/TgpOverlay.cs)
  computes range/altitude/heading/speed/mode/grid/etc. into public properties, already shared
  between the web TGP page and the native manual-mode in-cockpit overlay
  (`TgpManualControl.ComputeOverlaySample`). The HUD toggle's text can populate from the same
  object, called directly rather than through `TgpFeed`'s own web-subscriber gating (today
  `Overlay.Populate` only runs while `TelemetryServer.WantsTgpFrames` is true; this feature needs
  its own always-on tick regardless of whether a browser is watching).
- **A mature full-screen-overlay lifecycle to copy, not reinvent.**
  `MissileCameraFullscreenController.cs` is a solid reference for the parts that are easy to get
  subtly wrong: a scene-local (not `DontDestroyOnLoad`) overlay canvas so it can't survive into the
  wrong mission; an `IsActive` getter that self-heals if the overlay object died out from under the
  state flag; deferring exit until it's actually safe (their case: don't cut away while a missile
  the pilot fired is still worth watching — our equivalent would be finishing the current
  auto-exit condition cleanly rather than mid-transition); and yielding to the game's own pause/map
  UI automatically. Worth structuring `TgpFullScreen.cs` the same way rather than the thinner
  version this doc originally sketched.
- **Auto-exit precedent.** [Manual TGP control](tgp-manual-control.md) already establishes the
  pattern for a mod-added camera mode that must give way instantly (real lock forming, aircraft
  lost, landing camera taking over) — same discipline needed here, reinforced by MissileCamera's
  own more elaborate version of the same idea.

## Design

- **`TgpFullScreen.cs`** (new, `src/plugin/Tgp/`) — static class, `Active`/`HudVisible` state,
  `Toggle()`/`ToggleHud()`, auto-exit checks (aircraft lost, landing cam engaging, gear down, target
  list empty and manual mode isn't active) run from the same per-frame poll `TgpManualControl` rides
  on. Structured after `MissileCameraFullscreenController`'s lifecycle shape (see above) rather than
  a bare bool flag.
- **A dedicated high-resolution mirror camera** — either `TgpMirrorCam` extended with a
  full-screen-sized `Engage` call, or a sibling built the same way — parented to `tc.GetCamMount()`,
  sized to the screen (or a config cap) only while full screen is active, released on exit the same
  way `TgpMirrorCam.Disengage()` already does for the web pipeline.
- **A full-screen `ScreenSpaceOverlay` canvas + `RawImage`**, built once on first engage (lazy
  construction, matching `TgpNativeOverlay.SyncCrosshair`'s own idiom), `texture` set to the mirror
  camera's `RenderTexture`, `SetActive(false)` when inactive.
- **Two new dedicated keybinds** in `Keybinds.cs`'s TGP section: **TGP Full Screen Toggle** and
  **TGP Full Screen HUD Toggle** — single toggle-on-press binds (`edge: true`), matching Manual
  Control Toggle's own shape.
- **HUD toggle** shows/hides our own `TextMeshProUGUI` elements over the full-screen image,
  populated from `TgpOverlay`'s existing fields.

## Non-goals / risks for this pass

- **No change to the web `/tgp` page, `TgpFeed`, or the native physical cockpit screen.** The new
  mirror camera is a second, independent instance of the same technique `TgpMirrorCam` already
  uses — nothing about the existing pipeline needs to move.
- **Auto-exit is not optional.** A full-screen video feed with no exit path would leave the pilot
  unable to see flight instruments or the outside world.
- **Interaction with [Power](power-toggle.md) is untested** but expected to be a non-issue — the
  new canvas is independent of `FlightHud`'s canvas.
- **Airframe exclusion is deferred**, not dropped — see "The hide the airframe idea" above; add it
  only if an in-game look shows it's actually needed.
- **GPU/perf cost of a second full-res camera** is a new consideration `TgpMirrorCam`'s existing
  MID/HIGH sizes didn't have to worry about as much — a 4K mirror camera rendering every frame is
  real cost. Worth capping the resolution (matching MissileCamera's own clamp, not necessarily its
  exact 3840×2160 ceiling) rather than always matching the user's full desktop resolution.
