# TGP Full Screen

[Issue #70](https://github.com/roke77/NOXMFD/issues/70).

## Status

First pass implemented (`TgpFullScreen.cs`), not yet tested in-game. Design revised after two
findings that changed the approach for the better: `TgpFeed.cs` already answers where the native
picture comes from, and a side-by-side look at the community **MissileCamera** mod
(`_scratch/mursisru-missile-camera/`, already in this repo from earlier research) shows a mature,
working solution to the exact resolution/quality problem this ticket raises.

What shipped in the first pass: a dedicated `TgpMirrorCam` instance capped at 1080p, a
`ScreenSpaceOverlay` canvas with the feed `RawImage` and four corner readout labels, and the two
keybinds. In-game testing found two gaps and a request, addressed in a second pass:

- **Resolution now matches the game's actual back buffer** (`Screen.width`/`Screen.height`, no
  artificial cap) instead of a fixed 1080p ceiling — "cockpit resolution," as requested.
- **COLOR/IR toggling now works in full screen.** It didn't before: `TgpMirrorCam` is a brand-new
  camera with no connection to `TargetCam`'s own IR `Volume` (a child of the *native* camera,
  toggled by `SwitchIRState`), so flipping IR natively had no visual effect on the mirror's feed.
  Fixed by giving `TgpMirrorCam` its own opt-in `SetInfrared(bool)` — a local, narrowly-scoped
  `Volume` co-located with the mirror camera (small `SphereCollider`, `isGlobal = false`), driven
  from `TargetCam.UsingIR()` each tick. Opt-in and unused by `TgpFeed`'s existing MID/HIGH web
  pipeline, which keeps its own already-working CPU-grayscale approach unchanged — see "IR in full
  screen" below for why a shared Volume wasn't used originally, and why this one is scoped
  differently.
- **Full overlay field set, matching the manual TGP camera's own overlay** — type, pilot, range,
  altitude, relative altitude, closure, heading/elevation, bearing, mag, mode, and grid, not just
  the original four-field subset. Toggled by the same HUD keybind as before.
- **Overlay styling and crosshair matched to the manual TGP camera feed's own look**: white text on
  a translucent black pill per row (the web TGP page's `.tgp-ov-stat` chip style —
  `rgba(0,0,0,0.6)` background), not this mod's usual amber HUD-cue color, since this is
  reproducing that overlay's own look rather than adding a native HUD cue. Also added the boresight
  crosshair + Point Track box (same bar layout as `TgpNativeOverlay.SyncCrosshair`'s in-cockpit
  version, built independently since that class assumes one active consumer and full screen can be
  active at the same time as the native manual overlay).

Not yet done: per-target lock boxes on the full-screen view (the projection function is already
wired for it, just unused), and the airframe `cullingMask` exclusion (still deferred pending
another in-game look, per "The hide the airframe idea" below).

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

## IR in full screen

`docs/tgp-high-quality-mode.md` already investigated exactly this problem for the web page's
MID/HIGH mirror camera and deliberately avoided a shared Volume/shader: "`TargetCam`'s IR volume
is scoped to its own camera and not straightforwardly shareable, and a custom shader/volume risked
leaking onto other cameras through URP's layer-based volume matching." That's still true — but the
mitigation that made it a real risk there (a Volume broad enough, or positioned wrongly, to affect
the player's own main view or another camera sharing a layer) is avoidable for a *dedicated*
full-screen mirror camera in a way it wasn't for the shared class's general-purpose case:

- The Volume `TgpMirrorCam.SetInfrared` creates is **local** (`isGlobal = false`) and needs a
  collider to define its influence region — a `SphereCollider` on the *same* GameObject as the
  mirror camera, radius 0.1m, always co-located with it (it moves with the camera every tick since
  they share a transform). URP only applies a local volume to a camera whose own `volumeTrigger`
  point falls inside that collider.
- The only way this could still leak is if some *other* camera's own `volumeTrigger` happens to sit
  within 10cm of wherever the TGP mount currently is. For the player's own cockpit-view camera,
  that would require the TGP pod to be mounted essentially at the pilot's eye position — implausible
  for any real mount (nose/chin/wingtip pods sit meters from the cockpit).

This is a narrower, camera-specific version of the same idea, not a reversal of the original
decision — `TgpFeed`'s MID/HIGH web pipeline still uses its own CPU grayscale pass, unaffected,
since `SetInfrared` is opt-in and nothing there calls it.

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
- **GPU/perf cost of a second full-res camera is real and currently uncapped.** Matching
  `Screen.width`/`Screen.height` exactly (per the resolution request above) means a 4K display gets
  a 4K mirror camera rendering every frame in addition to the normal scene — real cost, worth
  measuring in-game once tried at high desktop resolutions. If it turns out to matter, the fix is a
  cap matching MissileCamera's own approach (their fullscreen ceiling is 3840×2160, not literally
  "whatever the desktop is"), not a redesign.
