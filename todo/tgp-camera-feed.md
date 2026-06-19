# TGP camera feed — feasibility investigation

## Goal

Stream the player's in-game TGP (Targeting Pod) feed — the one that
follows the currently locked target — to the MFD's TGP page over the
existing HTTP server.

## TL;DR: feasible, and easier than first expected

The game already ships a fully-built target-tracking camera (`TargetCam`
on each aircraft). We don't need to spin up our own camera and aim it —
we mirror or read back the existing one. Performance impact is small;
the most likely path is one extra camera pass at a low resolution +
JPEG encoding on a background thread, served as MJPEG so any browser
renders it with a plain `<img>` tag.

## Findings from static analysis

(Game DLL decompiled via `ilspycmd`; relevant types saved to
`_scratch/full/` while exploring.)

### `TargetCam` — `_scratch/full/TargetCam.cs`

A per-aircraft `MonoBehaviour` that already implements a TGP-style
camera. It is attached to a fixed mount on the airframe and is
auto-aimed at the currently selected target from
`aircraft.weaponManager.GetTargetList()`.

Relevant members:

- `aircraft.targetCam` — public, set during `TargetCam.Initialize()`.
- Internally instantiates `GameAssets.i.targetCam` (a prefab) and pulls
  two `Camera`s out of its children: `cam` (the scene render) and
  `UICam` (HUD overlay). Both private.
- Three mount points: `camMountForward`, `camMountRear`,
  `camMountLanding` (CamMode enum: `targetForward`, `targetRear`,
  `landingMode`).
- IR mode toggle via `SwitchIRState(bool)` — overrides saturation +
  exposure (greyscale TGP look).
- Public getters useful for the MFD overlay: `GetMag()`, `GetDist()`,
  `GetGrid()`, `UsingIR()`, `GetCamMount()`.
- The output texture is rendered to a `targetScreenRenderer` (an
  in-cockpit display). The RenderTexture itself is wired up at the
  prefab level; we don't see the assignment in code.

The cam is `enabled = false` until the player activates the screen in
cockpit. We can keep it on for our purposes.

### `TargetScreenUI` — the existing in-cockpit display

Reads from `aircraft.targetCam` and surfaces mag/distance/grid/mode
strings. Useful reference if we want to mirror the same overlay on the
MFD instead of building one from scratch.

### `CameraStateManager` — the global camera switcher

Manages the player's main view camera and several states
(`cockpitState`, `chaseState`, `freeState`, the post-kill cinematic
`TVState`, etc.). Has `mainCamera`, `cockpitCamRender`, and
`selectionCam` as public Cameras. None of these are the TGP — they are
the world view, the cockpit-only render, and the unit-selection
thumbnail camera respectively. They're useful as evidence that the game
already runs multiple cameras + render targets per frame at no
noticeable cost, so adding one more is in budget.

## Implementation paths (in order of preference)

### Path A — read back the existing `TargetCam`'s render texture

1. Locate `Aircraft.targetCam` on the local player aircraft (we already
   resolve the local aircraft elsewhere in the mod).
2. Reflect into the private `cam` field (Camera) and either:
   - read its `targetTexture` directly (if the prefab assigns one), or
   - assign our own `RenderTexture` to `cam.targetTexture` — but only
     if doing so doesn't break the in-cockpit screen renderer (the
     screen material is bound to whatever texture is already there).
3. Force `cam.enabled = true` so it keeps rendering when the player is
   not actively looking at the in-cockpit TGP screen.
4. Each tick on a background-friendly cadence (10–15 Hz):
   - `Graphics.Blit` the render texture into a small `Texture2D`
     (256×256 or 320×240).
   - `Texture2D.EncodeToJPG(quality: 60)`.
   - Push bytes into a per-client MJPEG stream.
5. Serve `/tgp.mjpg` as `multipart/x-mixed-replace; boundary=…`. The
   MFD just renders `<img src="/tgp.mjpg">` and we're done.

Pros: no extra camera pass; the game already does the rendering work.
Cons: depends on the prefab having a `targetTexture` assigned (likely
but unverified), and risk of interfering with the cockpit display.

### Path B — mirror with our own camera

If Path A's read-back is awkward (no targetTexture on the prefab,
breaks the screen, etc.):

1. Spawn a child `Camera` on the same mount as `TargetCam.GetCamMount()`.
2. Copy fieldOfView from the private `cam` each frame (the game already
   computes zoom-on-target distance).
3. Render to our own small `RenderTexture`. Same encode/serve pipeline
   as Path A from there.

Pros: fully isolated, can't break anything. Cons: a second camera pass.

### Picking between them at runtime

Try A first. If we see the in-cockpit screen go black after we assign a
texture, drop to B. Decide once during development; ship whichever
works without compromising the game's own UI.

## Open questions to settle while implementing

- Does `aircraft.targetCam` exist on every aircraft type, or only on
  ones with a real TGP? If only some, the MFD's TGP page needs a
  fallback ("NO TGP" placeholder).
- Does the camera render valid frames when the in-cockpit screen is
  *not* the player's current view? If Unity culls disabled-volume
  cameras aggressively we may need to keep the screenVolume enabled too.
- IR mode: do we expose a toggle on the MFD (call into `SwitchIRState`)
  or just track whatever state the player set in cockpit? Probably the
  latter for v1.
- What's the texture's actual resolution? If the game already uses a
  small render target, even Path A's encode cost is trivial.

## Decisions to make before merging

- Resolution + frame rate. Start 256×256 @ 10 fps. Tune by perf.
- Single-frame JPEG vs MJPEG. MJPEG is easier and the browser handles
  it; commit to MJPEG.
- JPEG encoding off the Unity main thread: use a small thread pool. The
  bytes from `EncodeToJPG` are CPU work that doesn't need the main
  thread.
- MFD wiring: TGP page reuses `<iframe>`-style layout, but the body is
  just `<img src="/tgp.mjpg">` with an overlay for mag/dist/grid/mode
  read from the existing telemetry SSE (no new endpoint needed for the
  overlay strings — push them through the snapshot like flares/jammer).

## Risks (still mostly the same)

- Performance: an extra camera + 10 Hz JPEG encode is small but real.
  Verify with the in-game FPS counter on a worst-case scene.
- LAN bandwidth: a 256×256 JPEG at quality 60 is ~5–10 KB. At 10 fps
  that's ~80 kbps per viewer. Trivial on local Wi-Fi.
- Game updates may move private fields. Path A uses reflection on the
  private `cam` field, which is the riskiest hookpoint. If a game patch
  breaks it, Path B (our own camera) is the durable fallback — keep the
  Path B code path around as a safety net even after Path A ships.

## No further web research needed

Static analysis answered all the "is this even possible" questions.
MJPEG over `multipart/x-mixed-replace` is well-trodden ground in C#
HttpListener land — the existing `HandleSseAsync` is a near template
for a `HandleMjpegAsync`. We can build straight off what we already have.
