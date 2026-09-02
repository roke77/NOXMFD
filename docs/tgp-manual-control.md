# TGP — manual camera control (pan/tilt/zoom)

## Status

**Built and confirmed working in-game.** Manual control targets the real `TargetCam`, not only the
HQ mirror. Harmony gates the native drive methods while manual mode owns the camera, and a real
target lock returns ownership to the game immediately. `TgpManualControl.cs` owns the feature
instead of `TgpFeed.cs`; its command surface remains remote-capable even though only local controls
are wired. Gear or landing-camera activation and aircraft loss are explicit exit triggers. Point
Track stores its world hit through `GlobalPosition` because a direction alone drifts as the aircraft
moves and the floating origin rebases.

Shipped beyond the original scope after live testing surfaced real needs: **Point Track**
(lock the aim to a fixed world point instead of a free direction), a **calibrated Zoom Axis**
(a physical slider whose moved position *is* the zoom level, not rate-based), a **boresight crosshair**
on the TGP page, **Area Track following the airframe** (a centered/reset camera now turns
with the aircraft instead of holding a frozen world bearing), **manual COLOR/IR toggling**, and
**Snap To Head Tracker** (`tgp-manual-snap-headtracker` — points the aim at wherever the pilot's own
view currently looks, reading `CameraStateManager`'s `mainCamera.transform.forward` so TrackIR, VR
head tracking, and plain mouse-look all work through the one path; releases Point Track like Reset
does). All six were explicitly requested
during testing, not scope creep — see [What actually shipped](#what-actually-shipped-v1-revised)
and [Debugging findings](#debugging-findings-worth-keeping) below for what each one is and the
real bugs found getting there.

A later development pass — originally its own `tgp-hud-tracker` branch, since merged into this one
— added the full-screen pilot-HUD line-of-sight cue and extended PAD Cursor Select's nearby-unit
handoff to work from both Area Track and Point Track.

**Reversed during testing:** the "external TGP page closes" exit trigger described below never
shipped as designed. First in-game test toggled the bind with no `/tgp` browser page open
anywhere, and manual mode engaged then auto-exited on the very next tick — reading as "nothing
happened." That trigger's own reasoning (reuse `TgpFeed`'s "is anyone watching" signal) was sound
for what it protected against, but it wrongly coupled manual *pointing* to whether anything was
*watching the external feed* — the in-cockpit MFD is a legitimate audience on its own, and manual
control drives `TargetCam` directly, the same component the native screen renders from, so it
never needed `TgpFeed` or a page to work. The "cockpit-only manual mode" this section originally
called a separate, out-of-scope feature is now just how it always behaves — not a mode switch, no
added flag.

## Goal

Let the player take manual control of the TGP camera's pointing direction and zoom, independent of
whatever the game's own auto-lock logic is doing — pan/tilt to look at any point in the world, zoom
in/out within a clamped range — driven by keybinds (KEY page, HOTAS-capable), off by default.

This is explicitly scoped as **pointing control**, not a targeting feature: it does not create a
weapon lock, doesn't feed `WeaponManager`, and isn't meant to replace the existing lock-driven TGP
feed — it's an additional mode the player switches into and out of.

## Control invariants

- Cache private `TargetCam` state in `TgpManualTargetCamAccess`; call public `SetTargetCam()` and
  `CancelTarget()` directly.
- Gate native `Update()` and `AimCamera()` behavior while manual mode owns the camera. Otherwise the
  game changes FOV and mounts, counts down `camTimeout`, and steers back toward its target position.
- Engage only outside landing mode, enable the camera through `SetTargetCam()`, and keep
  `camTimeout` alive while manual control is active.
- Store Area Track aim aircraft-locally so it follows the airframe. Store Point Track destinations
  as `GlobalPosition` so floating-origin rebases do not move the designated point.
- Exit immediately when a real lock appears and call `CancelTarget()` so the normal camera-toggle
  event restores cockpit UI ownership.

## Reflection surface (decompiled)

Confirmed directly against `_scratch/full/TargetCam.cs` (already in this repo, used for the TGP
suppression work):

- `SetTargetCam()` and `CancelTarget()` are **public** — no reflection is needed to call either from
  `TgpFeed` or the manual-control class.
- `currentMount`, `camMountForward`, `targetFOV`, `camTimeout`, `currentMode`, `canvasObjectLanding`
  are all `private` fields and require cached reflection access.
- `Update()`'s mount-switch/FOV-lerp/`camTimeout` countdown and `AimCamera()`'s `LookRotation`
  toward `targetPosition` are exactly what the `Update`/`AimCamera` Harmony prefixes above must
  suppress — confirmed directly against the decompiled game implementation.
- `SetTargetCam()` calling `AimCamera()` at its own tail matters here too: the one-time engage call
  (`ForceEnableManualCam`'s `tc.SetTargetCam()`) would itself trigger a real `AimCamera()` call
  unless `ManualMode` is already `true` *before* that call — ordering matters when porting this.

## Fit with NOXMFD's existing architecture

**New file: `TgpManualControl.cs`, not inside `TgpFeed.cs`.** `TgpFeed` is capture/overlay/
cockpit-hide plumbing — reflecting `cam`/`targetScreenRenderer`, driving the JPEG capture pipeline,
and (as of 0.29.1) the cockpit-hide toggle. Manual pointing is a different responsibility: it owns
its own state (`ManualMode`, `PanDir`/mount rotation, `DesiredFOV`) and its own per-tick drive
logic. Private `TargetCam` access is centralized in `TgpManualTargetCamAccess.cs` (`currentMount`,
`cam`, `camTimeout`, `currentMode`, `canvasObjectLanding`, IR/exposure methods) so the state
machine does not also own reflection details. `TgpFeed` only needs to *ask* this new class one thing
each capture tick — is manual mode active? — to decide whether to skip its own `tc.SetTargetCam()`
call (see below). Keeping them separate means `TgpFeed` doesn't grow a second unrelated state
machine, and manual control doesn't need to know anything about JPEG capture, readback, or the
cockpit-hide overlay event.

**Harmony is not a new dependency.** `src/plugin/HarmonyPatches.cs` already exists, with an
established convention this feature should follow exactly: one nested `static class` per patch,
`[HarmonyPatch(typeof(...), "MethodName")]`, patched individually via `CreateClassProcessor(...)`
in a per-class `try/catch` inside `HarmonyPatches.Init()` — so a failure to apply one of the three
`TargetCam` patches (e.g. a future game update renaming `AimCamera`) degrades to a logged warning,
not a crash of every other patch in the file.

**Keybinds reuse the existing axis-capable bind pattern**, not a new input-registration path.
NOXMFD already has exactly this shape of control for MAP's cursor (`docs/map-cursor.md`,
`Keybinds.cs`'s `AddAxis` + the two
`cursor-axis-h`/`cursor-axis-v` binds, "a deflected axis overrides its two keys"). TGP pan/tilt
should be new KEY-page binds following that same pattern — button pairs (`tgp-pan-left/right`,
`tgp-tilt-up/down`) plus optional axis binds (`tgp-pan-axis`, `tgp-tilt-axis`) for a HOTAS mini-stick
or hat, and plain button binds for `tgp-zoom-in`/`tgp-zoom-out`/`tgp-manual-toggle`/
`tgp-manual-reset` — no Rewired action registration, no new capture UI.

**Remote control: v1 ships local KEY binds only, but the command surface is designed remote-ready
from the start.** The feature request that led to this doc came from external-MFD usage, so this
isn't an afterthought — it's a scoping decision about *when* to build it, not *whether* the design
allows for it. `src/web/services/remote-keybinds.js` already has the exact shape TGP pointing
needs, proven twice over:

- **Pan/tilt** is a continuous, held, two-axis input — the same shape as MAP's remote cursor
  (`cursorRoleForBind`/`cursorStateFromActive`/`cursor.set`, with a server-side TTL in
  `RemoteInputState` behind `TelemetryServer.GetRemoteCursorState`/`SetRemoteCursorState`). A `tgp-pan`/`tgp-tilt`
  pair of roles and a `tgp-pan.set { x, y }` command, with its own TTL state alongside the existing
  cursor/fire TTL blocks in `RemoteInputState`, is the same pattern, not a new one.
  **(Built, but not this way — see below.)** The TGP page's own on-screen joystick (`#tgp-joystick`,
  `tgp.js`) sends `cursor.set { x, y }` directly — the *existing* command, not a new `tgp-pan.set` —
  since `Keybinds.Poll()` already merges `GetRemoteCursorState` into the PAD cursor vector it feeds
  `SetPan` whenever TGP holds SOI (the "PAD Cursor consolidation plan" above). No plugin change was
  needed for this. What's still deferred is the *remote-keybinds.js role* wiring this bullet
  describes — a separate physical/remote device mapping its own keys to `tgp-pan`/`tgp-tilt` roles
  the way it already does for MAP's cursor — which is a distinct feature from a mouse/touch drag
  control on this same page.
- **Zoom in/out** is a continuous, held, single-direction input — the same shape as the remote fire
  actions (`fireRoleForBind`/`fireGroupsFromActive`/`fire.set`, `RemoteFireMinPressTicks` guaranteeing
  a fast tap survives at least one `Poll()` frame). `tgp-zoom-in`/`tgp-zoom-out` roles and a
  `tgp-zoom.set { group, on }` command reuse that shape directly.
- **Toggle/reset** are one-shot actions — the same shape as every existing `commandForBind` switch
  case (`tgp-manual-toggle` → `{ cmd: 'tgp-manual.toggle' }`, `tgp-manual-reset` → `{ cmd:
  'tgp-manual.reset' }`).

Because all three shapes already exist in `remote-keybinds.js`/`TelemetryServer.cs` for other
features, adding TGP pointing to remote control later is extending three existing switch
statements plus one new TTL state block — not a redesign. **Decision: build `TgpManualControl.cs`'s
public API (`SetPan(x, y)`, `SetZoom(dir, on)`, `Toggle()`, `Reset()`) so `CommandDispatcher` can
call it identically whether the command came from a local keybind or `/command` over the network —
then wire only the local KEY-page binds for v1.** Remote wiring becomes a follow-up doc/PR, not a
rearchitecture.

**`TgpFeed.CaptureFrame()`'s existing gate needs one more condition.** Today it calls
`tc.SetTargetCam()` every capture tick only when `hasTargets` is true
(`src/plugin/Tgp/TgpFeed.cs`). Manual mode auto-exits on a real lock (see
[Lifecycle: every exit trigger](#lifecycle-every-exit-trigger) below), so this call site needs no
change: manual mode is never active at the same time `hasTargets` is true.

## Lifecycle: every exit trigger

Manual mode has more ways to end than "the player toggled it off," and the review's "haunted
feature" risk is exactly what happens if any of them are missed. `TgpManualControl` should check
all of these every tick (cheap — it's already ticking every frame to drive pan/tilt) and exit
cleanly (zero the mount/camera local rotation, then call `tc.CancelTarget()`) the instant any of
them is true:

- **Player toggle off** — the obvious one, `tgp-manual-toggle` pressed again.
- **Real target lock acquired** — `WeaponManager.GetTargetList().Count > 0` (the auto-exit already
  discussed).
- ~~External TGP page closes~~ — **removed** (see Status). Manual control never depended on
  `TelemetryServer.WantsTgpFrames`/`TgpFeed` for the pointing itself; it drives `TargetCam` directly,
  so it works with no browser page open, visible on the native in-cockpit MFD alone. If a page *is*
  open, `TgpFeed.CaptureFrame()` picks up the same already-enabled camera automatically — no
  coordination needed either way.
- **Aircraft changes or is destroyed** — same guard `TgpFeed.CaptureFrame()` already has
  (`GameManager.GetLocalAircraft(out Aircraft ac); if (ac == null) ...`); manual mode must not hold
  a reference to a `TargetCam` belonging to a previous aircraft.
- **Landing gear extends/retracts, or touchdown fires.** This is a real gap the three `Update`/
  `AimCamera`/`SwitchIRState` Harmony prefixes do **not** cover. `_scratch/full/TargetCam.cs` shows
  `TargetCam_OnSetGear` and `TargetCam_OnTouchdown` are wired to `aircraft.onSetGear`/
  `aircraft.OnTouchdown` directly (not called from `Update`/`AimCamera`), and `TargetCam_OnSetGear`
  unconditionally calls `SetLandingCam()` on gear extension (when `PlayerSettings.landingCam`
  allows it) — which reassigns `currentMode = CamMode.landingMode`, re-parents `cam` to
  `camMountLanding`, and changes `cam.fieldOfView` to `landingCamFoV`, none of which any patch here
  intercepts. Patching two more methods is one option; simpler and cheaper: **each tick, if
  `currentMode` reads back as `landingMode` while `ManualMode` is true, treat that as an external
  signal that gear/touchdown fired and exit manual mode** — reactive, one-frame lag at worst,
  no new patches, and it can't miss a case the two owning methods might grow in a future game
  update (a name-matched Harmony patch could silently stop firing after a decompile-breaking
  update; a state check on `currentMode` degrades gracefully instead).
- **`TargetCam_OnUnitDisable`/`TargetCam_OnDetach` destroy the component or its renderer outright**
  (aircraft destroyed, part detached) — same defensive null-check `TgpFeed` already applies to
  `cam`/`tc` before touching them covers this; manual mode's per-tick check should bail the same
  way if `tc == null` or the reflected `cam` field returns `null`.

**`TgpMirrorCam.SyncFromSource(cam)` (HQ mode) needs no changes.** It reads whatever `cam`'s current
mount/FOV/orientation is, every tick, regardless of *why* that state is what it is — manual control
writes to the same `cam`/`currentMount` fields the mirror already syncs from, so HQ mode should
track a manually-pointed camera exactly like it tracks a lock-driven one, with no separate
integration work.

**Composes with the cockpit-hide toggle** (`docs/tgp-suppress-native-render.md`, shipped in
0.29.1). That feature only touches `TacScreen`'s display overlay via `onCamToggle`; this feature
only touches where the camera points and its FOV. Both can be on simultaneously with no interaction
expected — worth confirming in the verification pass, not assumed risk-free without a check.

## What actually shipped (v1, revised)

Six pieces beyond the original scope, all added in response to real problems hit during live
testing, not spec'd up front:

- **Point Track** (`tgp-point-track` bind) — an internal toggle, not a hold. Raycasts along the
  current aim against a world-geometry-only layer mask (see [Debugging
  findings](#debugging-findings-worth-keeping) — this mask matters a lot) and locks onto whatever
  it hits. While locked, the aim tracks that fixed world point every tick, immune to the aircraft's
  own translation/rotation — the exact "world-hit raycasting" this doc originally floated as a
  validation spike. Pan/tilt input while locked does **not** release the lock; it nudges the aim
  off the tracked point as an independent offset (see below), and releasing the stick commits one
  fresh raycast to redesignate a new locked point at the new aim. Pressing the bind again, or any
  lifecycle exit trigger, releases back to free Area Track.
- **Manual track to unit-lock handoff** — pressing the existing **PAD Cursor Select** bind in
  either Point Track or Area Track searches for the closest selectable live unit to the current
  ground look point. Point Track reuses its retained world point; Area Track raycasts its live aim
  using the same world-geometry query that supplies the manual TGP overlay.
  The normal acquisition radius is 50 m, expanded to one full unit length for unusually large
  units. A match goes through the same select-only path used by MAP/RDR commands (TGT filters,
  no neutral/scenery/self targets, HUD marker/audio when available, and multiplayer target-list propagation), then
  manual mode exits and the game's normal locked `TargetCam` takes over. With no nearby match the
  press is still delivered to the focused web display and the current manual tracking mode remains
  unchanged.
- **Calibrated Zoom Axis** (`cursor-zoom-axis` bind — one of the shared PAD Cursor binds, see
  [PAD Cursor consolidation](#pad-cursor-consolidation-built) — an `AddAxis` bind like Cursor
  Horizontal/Vertical) — a physical analog control (e.g. a HOTAS slider) whose raw position
  directly *is* the zoom level, log-linear from `MaxFov` (widest) to `MinFov` (tightest, see
  Debugging findings' log-linear curve note), not a rate like the Cursor Zoom In/Out buttons. When
  the axis moves, it jumps zoom to that position; while it is stationary, Zoom In/Out can still
  adjust the camera. Deliberately **not** rate-limited or smoothed — a calibrated control's whole
  point is instant, 1:1 response.
- **Boresight crosshair** — a small white reticle with a gap at center, shown on the TGP page only
  while manual control is on via the `.tgp-manual` class. Screen-centered, not synced to the
  letterbox-corrected overlay rect the HQ stat overlay uses:
  `object-fit: contain` already keeps the picture's own center at the panel's center regardless of
  letterboxing, so plain 50/50% CSS positioning lines up correctly on its own.
- **Area Track follows the airframe.** A centered/reset camera now turns *with* the aircraft as it
  banks/turns, instead of holding whatever world bearing was forward at reset time. Implemented as
  an offset from the aircraft's own forward, expressed in the aircraft's local space
  (`_localPanDir`, `Vector3.forward` = boresight) — the world-space direction actually sent to the
  mount is re-derived from the aircraft's *current* attitude every tick, not just when there's
  pan/tilt input.
- **Snap To Head Tracker** (`tgp-manual-snap-headtracker` bind) — points Area Track's aim at
  wherever the pilot's own view currently looks. Reads `SceneSingleton<CameraStateManager>.i
  .mainCamera.transform.forward`, the same final rendered-camera direction `CameraCockpitState
  .UpdateState` already applies TrackIR/VR head tracking/plain mouse-look to, so no separate
  TrackIR-specific path is needed. Converted to aircraft-local the same way Point Track's baseline
  capture is, so a subsequent bank/turn still carries the aim with the airframe instead of pinning
  it to a world bearing. Releases Point Track like Reset does; leaves zoom untouched.

Two related pieces stayed out of scope — see [Out of scope](#out-of-scope).

## In-cockpit overlay (v2)

The web TGP page's overlay (`TgpOverlay.cs`/`tgp.js`) and the native in-cockpit MFD's overlay
(`TargetScreenUI`) are two separate rendering paths reading the same `TargetCam`/telemetry state —
this section is the second one, which the original [Out of scope](#out-of-scope) list deliberately
deferred as "cosmetic polish." Revisited because the in-cockpit screen is a legitimate, self-
contained audience for manual pointing (see [Status](#status) — this was already established when
the page-close exit trigger was removed), and a data-blank screen while manually pointing is a real
usability gap, not just missing polish.

**Why this works with no target lock.** `TargetScreenUI.UpdateTargetInfo` already runs on its own
`StartSlowUpdate(0.1f, ...)` timer regardless of manual mode — it isn't one of the methods
`TgpManualControl`'s Harmony patches gate. It just early-returns to "NO LOCK" the instant
`targetList.Count == 0` (`_scratch/full/TargetScreenUI.cs`), which is always true while `ManualMode`
is on: `Tick()` already auto-exits manual mode the instant a real lock exists, so there's never a
"which one wins" race between a real lock's data and manual mode's synthetic data.

**What's available without a lock:**
- Own-aircraft state — heading/altitude/speed, no target needed.
- Aim direction — azimuth (already computed for the native bearing readout) and elevation (not
  shown anywhere natively — the native overlay only ever needed a target's bearing, never the
  camera's own tilt, because a locked target only needs one number to point back at it).
- Magnification — `10f / cam.fieldOfView`, identical math to `TargetCam.GetMag()`.
- A "look point" — one raycast along the current aim (`WorldGeometryLayerMask = 64`, the same mask
  Point Track already uses to avoid self-hitting the fuselage), or the already-tracked point
  directly while Point Track is locked (no extra raycast — `Tick()` is already recomputing toward
  it every frame). Gives range, world altitude, and map grid of whatever's actually in the
  crosshair — not stale the way `tc.GetDist()`/`tc.GetGrid()` are in manual mode (they're written by
  `AimCamera()`, which the manual-mode Harmony gate skips entirely).
- **Not available:** target type, pilot name, faction/jam/lase/outdated status — all require a real
  `Unit`, which manual mode by definition doesn't have.

**Field mapping.** Reuses `TargetScreenUI`'s own TextMeshProUGUI/Image elements — no new Unity UI added. A
Harmony prefix on `UpdateTargetInfo`, gated on `ManualMode`, skips the original method entirely
(which would otherwise show "NO LOCK") and drives the same fields from `TgpManualControl`'s state
via Harmony's `___field` injection — reaches the private serialized fields directly, no separate
reflection cache needed the way the `TargetCam` fields require.

| Native field                | Native meaning        | Manual-mode content                                                |
|------------------------------|------------------------|----------------------------------------------------------------------|
| `typeText`                   | target type/count      | `MANUAL` / `POINT TRACK`                                             |
| `pilotText`                  | pilot name              | hidden                                                                |
| `magText`                    | magnification           | unchanged math, sourced from `_desiredFov`                           |
| `modeText`                   | COLOR/IR                | unchanged (`tc.UsingIR()` still valid — untouched by manual mode)    |
| `bearingText` / `bearingImg` | target bearing          | aim azimuth (same `camMount`-derived value the native code already used) |
| `heading`                    | target heading          | **repurposed:** aim elevation (`EL {el}°`) — fills a gap the native display never had |
| `distance`                   | target range            | look-point range (raycast / Point Track hit)                         |
| `altitude`                   | target altitude         | look-point world altitude                                            |
| `rel_altitude`               | target rel. altitude    | look-point altitude relative to own aircraft                         |
| `speed`                      | target speed            | **hidden** — own aircraft speed duplicated the flight HUD, removed after testing |
| `rel_speed`                  | target closing speed    | **repurposed:** closure rate toward the look point (`CLO`, positive = closing) |
| `gridText`                   | target grid             | look-point grid                                                      |

Turret-aiming reticle (`aimingBoxBgd`/`aimingDotImg`) is untouched — it's already gated behind the
same `targetList.Count == 0` early return today (no lock, no turret-aim box), so skipping the whole
method while `ManualMode` is on preserves that existing behavior exactly, not a new gap.

**Boresight crosshair + Point Track marker.** The in-cockpit feed gets a crosshair — 4 arms reaching
nearly to the frame edges, built from plain `Image` bars since there's no matching native element to
reuse for a full-frame pointing reticle (a departure from the web TGP page's much smaller gapped
cross, `.tgp-crosshair`, sized for a different context). Synced every tick from the same Harmony
prefix regardless of `ManualMode`, so it's hidden the instant manual mode ends rather than sticking
around. All sizing is anchor-stretched (0..1 of the canvas's own rect) rather than computed from
`RectTransform.rect.width/height` as absolute units — the first pass did the latter and rendered as
essentially invisible, because that canvas's rect isn't guaranteed to be in screen pixels (could be
world-space units for a Screen Space - Camera or World Space canvas). While Point Track is locked, a
small square box appears at center, its side exactly matching where the crosshair arms start — hand-
built from the same bars as the crosshair, not `TargetScreenUI`'s own `targetLockBox` prefab. That
prefab was tried first (reusing the same art real target locks use elsewhere on this screen) but its
border is a fixed-width sliced sprite that stayed visually thin no matter how big the box's
`RectTransform` was resized, so matching border thickness to the crosshair required building it by
hand instead. Each crosshair arm's length is derived as `4 * gap` (`gap` = the box's own half-size)
so it stays exactly 2x the box's side length by construction, not by hand-tuned numbers that could
drift off that ratio during later tuning passes.

**Debugging finding: a decompile can be wrong about a UI field's type, and Harmony won't tell you.**
The first build declared the injected `___field` parameters as `UnityEngine.UI.Text`, matching
`_scratch/full/TargetScreenUI.cs`. That decompile is stale — the live 0.34+ game build switched
`TargetScreenUI`'s text fields to `TMPro.TextMeshProUGUI` (`NOXMFD.csproj` already carries a
`Unity.TextMeshPro` reference with a comment saying as much, added for an earlier feature). Harmony
patched cleanly with the wrong type — no patch-apply failure, no caught exception at the call site —
it just produced a type-confused field access that displayed plausible-looking values for a few
ticks and then crashed the whole game process, deep inside `TextMeshProUGUI`'s own internals
(`SetArraySizes`), nowhere near the actual bug. Root-caused from a Unity crash dump stack trace
(`%AppData%/LocalLow/Shockfront/NuclearOption/Player.log`), not from BepInEx's own log, which showed
nothing wrong. Fixed by declaring every text field as `TextMeshProUGUI` instead. Worth checking a
field's real type against a crash or a live inspector, not just an older decompile, whenever a
Harmony `___field` injection targets game UI.

## Pilot-HUD line-of-sight cue (v4, `tgp-hud-tracker` branch)

Issue #59 adds a second in-game surface beyond the native TGP screen: an amber `TGP` cue on the
pilot's full-screen combat HUD while manual mode is active. It is deliberately parented to
`CombatHUD.iconLayer`, not the aircraft-fixed `FlightHud.HUDCenter` or heading tape. The game's
current `mainCamera` projection therefore carries normal free-look, TrackIR, padlock and cockpit-FOV
changes automatically, including views well outside the forward combiner area.

`TgpManualControl.TryGetAimDirection` exposes the final normalized world-space `_panDir` through a
read-only seam. `HudTgpCue.LateUpdate` projects that direction from the pilot camera, after the
controller's normal `Update` tick has settled it. The cue shows four separated corner brackets and
a 2x2 precision dot while in view, leaving the rest of the interior readable around native HUD/unit
symbology. An independent caret clamps to an inset screen edge while the direction is out of view;
it never borrows `CombatHUD.SetTargetArrow`, which the native target-list state machine owns.

`HudDirectionCueMath` contains the BCL-only screen rectangle math: behind-camera inversion, inset
edge intersection, and a retained last direction around the otherwise ambiguous exact-rear point.
It is linked into `tools/tests`, so those boundaries run without Unity. The renderer performs one
camera projection and UI position update per active frame, with no target scan, raycast, reflection,
network work or recurring allocation. It is cockpit-only and hides immediately when manual mode
ends; a successful Area Track or Point Track unit-lock handoff therefore replaces it with native
target state.

Implementation and automated checks are complete. The core cockpit cue, off-screen caret, revised
half-scale geometry, centre dot, and unit-lock handoff have been exercised in game. Full TrackIR,
respawn, unusual resolution/UI-scale, dense-overlap, and external-camera validation remains in
`docs/tgp-hud-tracker.md`'s extended matrix.

## Web overlay parity (v3, `tgp-manual-web-overlay` branch)

The in-cockpit overlay above (v2) only ever reached the native screen — the external web TGP page
(the MFD's own `/tgp` iframe, used from a phone/tablet/second monitor) still showed nothing but a
small, fixed-size crosshair while pointing manually: `TgpFeed.CaptureFrame()`'s `Overlay.Populate()`
call unconditionally cleared the whole stat block whenever the real target list was empty, which is
always true in manual mode (`Tick()` auto-exits the instant a real lock exists). This branch gives
the web page the same RNG/ALT/REL/CLO/GRID/MODE/MAG/EL data the in-cockpit screen already has, and
replaces the crosshair with the same final proportions the in-cockpit one settled on.

**Shared computation, not two implementations.** `TgpManualControl.ComputeOverlaySample(tc, mount,
ac)` — a new method returning a small `ManualOverlaySample` struct (az/el/mag/IR/point-track/hit/
range/altitude/relAltitude/closure/grid) — is the single source of truth both surfaces read from.
`TgpNativeOverlay.Populate` (in-cockpit TextMeshPro fields) was refactored to call it instead of
re-deriving the same raycast/trig math inline; `TgpOverlay.PopulateManual` (new, mirrors `TgpOverlay
.Populate`'s existing locked-target path) calls it too, from `TgpFeed.CaptureFrame()` whenever
`!hasTargets && TgpManualControl.ManualMode`. Neither surface can drift from the other on the
underlying numbers now — only on how each one renders them.

**Wire format.** `TelemetrySnapshot` gained two fields (`TgpManualPointTrack`, `TgpElevationDeg`) —
everything else reuses the existing locked-target `Tgp*` fields (`TgpMag`, `TgpRangeM`, `TgpGrid`,
`TgpIR`, `TgpBearingDeg`, `TgpAltitudeM`, `TgpRelAltitudeM`, `TgpRelSpeedMps`-as-closure), the same
way the in-cockpit TextMeshPro fields were repurposed. `TelemetryJson.TgpBlock()`'s `"cnt":0`
short-circuit — the client's original cue to hide the overlay entirely — had to learn a second
condition: `TgpTargetCount <= 0 && !TgpManualActive`, since manual mode legitimately has zero real
targets but still has real data to show. Three new JSON fields inside the `tgp` block: `manual`,
`pointTrack`, `el`. No change needed in `telemetry-source.js` — the whole `tgp` block already flows
through to the page's `data` object unchanged; only `tgp.js` needed to read the new fields.

**Client-side gating stays "HQ quality," for manual mode too.** (The `tgp-extended-quality`
follow-up later renamed this axis and its gate to `resolution !== 'native'`, and `hq`/`native` to
mirror-vs-native — the `quality === 'hq'` shown below is the exact expression at the time this bug
was found, not what's in `tgp.js` today.) The original `applyOverlay` only
ever drew for `quality === 'hq' && data.cnt > 0` — Native mode's locked-target case relies on the
game's own baked-in video overlay instead, since Native captures the stacked-camera UICam output
directly. This branch's first pass assumed manual mode had no such baked-in overlay in *either*
quality (there's no lock for the native UICam to draw) and drew client-side regardless of quality —
wrong: `TgpNativeOverlay` populates those exact same `TargetScreenUI` fields (and its own crosshair)
for manual mode too, so Native's captured video already shows manual data, the same as it always
did for a lock. Drawing it again as HTML on top double-showed everything in Native quality — caught
in live testing, not before. Fixed by keeping the original "HQ only" gate and just adding `manual`
as an alternative to `locked` *within* it: `show = quality === 'hq' && (manual || locked)`. The
crosshair CSS needed the identical fix — `.tgp-crosshair` now also requires `.show-overlay` (which
is only ever set for HQ), not just `.tgp-manual`, so Native doesn't draw a second crosshair on top
of the one already baked into the frame. A new `applyManualOverlay(data)` handles the manual field
mapping into the same corner-group elements `applyOverlay`'s locked-target path already uses — no
pilot, no per-target boxes, own-aircraft SPD hidden via a new `.tgp-ov-hidden` utility class (not
dashed — matches the in-cockpit overlay's `SetActive(false)`), HDG's slot carrying elevation instead
(`EL {n}°`) the same way the in-cockpit TextMeshPro `heading` field was repurposed.

**Crosshair rewritten to match the in-cockpit design's final proportions**, not the smaller gapped
cross this page shipped with in v1 (sized for a corner-badge context, not as the primary aiming
reference the page now needs). CSS percentages (not fixed px) so it scales with the panel:
`.tgp-panel` is already the containing block (`position: absolute`) these resolve against, so no JS
sizing code was needed the way the in-cockpit version needed anchor-stretched `RectTransform`s to
work around Unity's canvas-unit ambiguity. Same constants as `TgpNativeOverlay.SyncCrosshair` (`gap
= 0.028125`, arm length `= 4 * gap`, box side `= 2 * gap`) converted straight to percentages — keep
the two in sync if either changes. A new Point Track box (four bars, not the CSS-art `.tgp-ov-box`
lock-box style already used for real target locks) shown via a `.tgp-point-track` class tgp.js
toggles from inside `applyOverlay`, since `pointTrack` only arrives with `data`, not the top-level
flags the shell forwards before `data` exists.

**Verified without the live game** — a rare case for this feature, since the web page is pure
HTML/CSS/JS driven by a JSON contract that's easy to fake. Confirmed via `tools/serve_web.py` +
injected `postMessage` calls in a real browser: the manual overlay renders correctly in native
quality (impossible before this branch), the Point Track box's geometry lands exactly on the
designed percentages (measured via `getBoundingClientRect()`, not just eyeballed), own-aircraft SPD
hides via the class rather than showing a stale dash, and the locked-target HQ path is provably
unaffected (same test session, same page, switched payloads).

**Debugging finding: CLO shipped in raw m/s, while the in-cockpit reading is player-unit-aware.**
`UnitConverter.SpeedReading()` — what `TgpNativeOverlay` already used for the in-cockpit CLO line —
respects `PlayerSettings.unitSystem` (km/h metric, kt imperial); the first web version sent the raw
`ClosureMps` float and formatted it client-side as a hardcoded `m/s`, so the two surfaces disagreed
by a factor of 3.6x for a metric player (and used the wrong unit outright for an imperial one).
tgp.js's own `fmtDash` comment already flags RNG/ALT/etc. as raw/unconverted — a known, accepted
simplification predating this branch — but CLO didn't exist on the web page before this branch, so
there was nothing worth staying "consistent" with by repeating that simplification for a brand-new
field. Fixed by having the server format it via the same `UnitConverter.SpeedReading()` call and
send the ready string (`TgpOverlay.ClosureReading`, JSON `clo`) — the same approach `GRID` already
used — rather than teaching the client its own copy of the unit-conversion math.

**Debugging finding: reusing one percentage number on both axes assumes a square container, and
`.tgp-panel` isn't one.** The crosshair/box CSS's first pass used identical percentage values for
lengths on both the X and Y axes (e.g. `thickness: 0.5%` for both a bar's width and another bar's
height). `.tgp-panel` has a fixed `aspect-ratio: 3/2` — width is always 1.5x height — so `0.5%` of
width is a 1.5x bigger physical length than `0.5%` of height. The visible symptoms were exactly
what CSS math predicts: vertical and horizontal crosshair arms rendered at visibly different
thicknesses, and the Point Track box came out as a 1.5x-wide rectangle whose four independently-
placed edge bars didn't quite reach each other at the corners. Fixed by treating height as the
master reference axis and dividing every width-relative counterpart by 1.5 (multiplying by 2/3),
verified by measuring actual rendered `getBoundingClientRect()` pixel sizes in a properly-3:2-shaped
container — a bare/unshelled test page doesn't reliably reproduce that shape on its own (the harness
session used to confirm the fix had to fake a 900×600 body to see `.tgp-panel` actually resolve to
3:2; a wider bare viewport left it stuck at whatever aspect the raw window happened to be, since
`aspect-ratio` plus `max-height: 100%` doesn't shrink width back down to compensate once height gets
clamped). Worth remembering whenever a CSS value is meant to represent one physical length but has
to be expressed twice, once per axis, against a non-square container.

**Debugging finding: this branch shipped a double overlay in Native quality, found in live testing,
not before.** The premise the whole client-side gating change rested on — "manual mode has no
baked-in overlay in either quality" — was simply false for Native. Native's captured video is the
game's own stacked-camera UICam output directly, and `TgpNativeOverlay` (the v2 in-cockpit feature)
populates the exact same `TargetScreenUI` fields *and draws its own crosshair* on that same canvas
for manual mode, exactly like it always did for a real lock. So switching a low-quality TGP feed to
manual pointing showed the RNG/ALT/etc. text and crosshair twice: once baked into the MJPEG pixels
(from `TgpNativeOverlay`, already working correctly), once more drawn as HTML on top (from this
branch's `applyOverlay`/`.tgp-crosshair`, based on the false premise). The fix was mechanical once
diagnosed — `show = quality === 'hq' && (manual || locked)` instead of `manual || (quality ===
'hq' && locked)`, and the crosshair's CSS selector gaining `.show-overlay` alongside `.tgp-manual` —
but the *lesson* is that "does X already exist somewhere" needs checking against every rendering
path a feature touches, not just the one being worked on directly: this branch's own author had
built the very code (`TgpNativeOverlay`) that made its central assumption wrong, and didn't
cross-check against it before writing the opposite claim in a comment.

## Debugging findings worth keeping

Live testing surfaced several real bugs, each with a root cause worth remembering rather than
re-discovering:

- **Raycasting from inside your own aircraft self-hits the fuselage.** `Physics.Raycast` with no
  layer mask checks every collider, including the aircraft the camera is mounted on/in — a ray
  fired from the mount would immediately hit the airframe at point-blank range, producing "Point
  Track snaps to face backward into the aircraft" on the very first lock attempt. Fixed by passing
  `layerMask = 64`, the exact literal the game's own spectator camera states already use for
  terrain-only collision checks (`_scratch/full/CameraOrbitState.cs`, `CameraChaseState.cs`,
  `CameraControlledState.cs` — all `Physics.Linecast(..., layerMask)` against scenery, never
  aircraft). Worth checking the decompile for prior art like this before inventing a filter.
- **Re-raycasting every tick while nudging trembles near any surface edge.** An early version
  redesignated Point Track's locked point on every tick a nudge was active. Near any
  discontinuity — two adjacent surfaces a meter or two apart, e.g. a raised platform next to open
  ground — a ray sweeping a fraction of a degree can alternately hit one surface then the other on
  consecutive ticks, each becoming a new "locked" point and fighting the drift-correction into a
  visible back-and-forth. Fixed by raycasting **once**, only when the nudge input drops back below
  threshold (i.e. on release), not every tick during the drag.
- **Coupling aircraft-motion correction and pilot nudge into one variable breaks in two different
  ways depending on which one "wins."** Two sequential bugs came from this: (1) running the
  correction-toward-the-locked-point unconditionally every tick, then applying the nudge on top,
  let the correction snap most of the way back to the old point before the nudge landed — "stuck,
  can't pan." (2) The opposite version — skipping the correction entirely while nudging — left
  nothing anchoring the aim to the locked point during a drag, so it just carried the aircraft's
  own motion for as long as the stick was held — "drifts with the plane." The fix was to stop
  treating them as one value two different writers fight over: `_pointTrackBaseline` (the
  aircraft-motion correction, a direct `LookRotation`-style recompute toward the locked point
  every tick, unconditionally, never touched by input) and `(_pointTrackOffsetAz,
  _pointTrackOffsetEl)` (the nudge, only ever changed by input) are independent state, combined
  additively (`aim = baseline rotated by offset`). Neither can fight the other because neither
  writes to what the other reads.
- **World-angle-per-second rates are blind to zoom, and that reads as "the more zoom, the more
  jumpy."** At `MinFov` (~40x magnification, 0.25° wide), even a fraction of a degree of motion
  crosses a huge share of the visible picture — a rate tuned to feel right at wide FOV is violent
  shake once zoomed in. This wasn't a logic bug (a dense per-tick diagnostic log showed azimuth/
  elevation changing smoothly, no oscillation, at every zoom level) — it was the *visual
  amplification* of normal, correct motion. Fixed by scaling every user-driven angular rate
  (`PanSpeedDegPerSec`, `TiltSpeedDegPerSec`) by `currentFOV / MaxFov`, so the on-screen rate of
  motion stays roughly constant across the zoom range instead of the world-angular rate staying
  fixed while its visual effect balloons — the same reason real gimbal/TGP slew rate drops as
  magnification rises. Deliberately **not** applied to Point Track's baseline correction itself
  (see the coupling bug above) — only to genuinely user-driven slew.
- **A calibrated axis wants instant response, not smoothing.** The Zoom Axis was briefly
  rate-limited (`Mathf.MoveTowards`) to tame an erratic-looking raw signal, which made zoom feel
  sluggish — wrong fix for wrong problem. A rate cap can't distinguish "genuine fast intentional
  slider movement" from "noise"; both look identical to a limiter. The actual evidence (the axis
  value dwelling at each extreme for a full second or more, not single-frame spikes) didn't match
  ordinary pot noise anyway — that pattern points at a real signal from the wrong physical control,
  not something software smoothing fixes. Reverted to direct, instant response; if a zoom axis
  still reads as erratic, the fix is checking its binding on `/keybinds`, not adding lag.
- **A held key or a mis-centered axis can silently undo a `Reset()` one frame later.** If pan/tilt
  input is still non-zero at the exact moment `Reset()` runs, the very next `Tick()` immediately
  pans away from the just-centered direction — the reset looks like it "sometimes doesn't work."
  Fixed by zeroing pan input inside `Reset()` (matching what `Engage()` already did), plus a
  `LogWarning` specifically when this condition is detected, so a genuinely stuck/noisy axis is
  visible in the log instead of just "reset is flaky."
- **`src/web/**/*` is embedded into the plugin DLL** (`NOXMFD.csproj`'s
  `<EmbeddedResource Include="src\web\**\*">`) — editing an `.html`/`.css`/`.js` file has zero
  effect in the running game until `dotnet build -c Release` runs, exactly like a C# change. Easy
  to forget when a change (like the crosshair) is CSS/HTML-only and feels like "just a web asset."
- **First manual engage of a fresh mission looked darker/lower-contrast than the normal feed.**
  Root cause: gating all of `TargetCam.Update()` also gated its cosmetic per-second exposure ramp
  (`UpdateExposure` — ambient-light-driven `postExposure`/`contrast` on the screen's post-process
  volume), which this doc's own `TargetCam_Update_ManualGate` comment had already flagged as a
  possible future issue and named the fix for. On a real lock this is invisible, since a prior real
  lock already ran `Update()` at least once and left the volume adapted — but on the very first
  manual engage of a mission, before any real lock has ever run it, the volume is still sitting at
  `Awake()`'s cold-start values. Fixed by having `Tick()` call the same private `UpdateExposure()`
  directly every tick (reflection, not reimplementing its ambient-light formula) — cheap (a couple
  of field writes) and stays correct if the game's formula changes later.

## Open questions

- **Zoom limits** — resolved: fixed at `MinFov = 0.25°` / `MaxFov = 20°`, matching
  `TargetCam.SetTargetCam()`'s native clamp range. No new config is needed because the native
  camera never goes tighter or wider. The Zoom Axis bind (see
  [What actually shipped](#what-actually-shipped-v1-revised)) maps
  its full physical travel across exactly this range.
- **Where does the toggle status live?** — resolved: manual mode is indicated by the TGP page
  crosshair and the native in-cockpit `MANUAL` / `POINT TRACK` overlay state. Not on TGP CFG, per
  the original reasoning (a live control, not a set-once preference).
- **Does `camTimeout` pinned high fight anything else?** Not hit in testing — no other system reads
  `camTimeout` while manual mode is active. Not exhaustively audited, but no longer a live concern.
- **Near-overhead "keyhole" swings during Point Track.** Point Track's baseline correction is a
  direct, unlimited recompute toward the locked point every tick (see [Debugging
  findings](#debugging-findings-worth-keeping) — a rate cap here previously fought the nudge
  offset and had to be removed). Flying near-overhead of a fixed ground point can still make the
  required look angle swing through large angles quickly, same as a real gimbal's keyhole. Not
  reported as a problem in testing so far; if it does become one, the fix is a dedicated guard
  (e.g. auto-releasing the lock past some rate-of-change threshold), not slowing down normal
  tracking for everyone.

## v1 implementation shape

Delivered, including the five additions in [What actually shipped](#what-actually-shipped-v1-revised):

1. `TgpManualControl.cs` — state, engage/exit/reset, Point Track (raycast lock + decoupled
   baseline/offset), and the public `SetPan`/`SetZoom`/`SetZoomAxis`/`Toggle`/`Reset`/
   `TogglePointTrack`/`TryLockTrackedUnit` API (remote-ready per [above](#fit-with-noxmfds-existing-architecture),
   local-only wired so far).
2. `TgpManualAimMath.cs` — pure pan/tilt/zoom geometry (`az/el`, FOV-scaled nudge, Zoom Axis
   mapping), covered by `tools/tests/TgpManualAimMathTests.cs`.
3. `TgpManualTargetCamAccess.cs` — cached private `TargetCam` field/method access for the game
   camera, current mount/mode, landing canvas, timeout, IR toggle, and exposure update.
4. `HarmonyPatches.cs` — the `TargetCam.Update`/`AimCamera` prefixes and the
   `TargetScreenUI.UpdateTargetInfo` prefix, matching the file's existing one-class-per-patch
   convention. `SwitchIRState` is called through `TgpManualTargetCamAccess`, not patched.
5. `TgpNativeOverlay.cs` — native in-cockpit `TargetScreenUI` text/crosshair population, separated
   from manual-control lifecycle/state.
6. `Keybinds.cs` — KEY-page binds: toggle, reset, Point Track, and manual COLOR/IR toggle.
   Pan/tilt/zoom are the shared PAD Cursor binds instead of dedicated TGP ones (see
   [PAD Cursor consolidation](#pad-cursor-consolidation-built)); the existing PAD Cursor Select
   bind also promotes either manual tracking mode near a unit into the normal game lock.
7. TGP page — boresight crosshair gated on the `tgp-manual` class.
8. Every exit trigger from [Lifecycle](#lifecycle-every-exit-trigger) except the removed page-close
   one: real lock, aircraft loss, gear/landing-cam conflict.
9. Verified in-game: Native quality, a fast lock/unlock cycle, Point Track lock/nudge/release/
   redesignate, the calibrated zoom axis, and the airframe-follow behavior. HQ quality and
   cockpit-hide interaction not specifically re-verified after the debugging pass — worth a look if
   either one is touched again.

## PAD Cursor consolidation (built)

**Status: built.** Manual TGP used to own its own pan/tilt/zoom binds (`tgp-pan-left/right`,
`tgp-tilt-up/down`, `tgp-pan-axis`, `tgp-tilt-axis`, `tgp-zoom-in/out`, `tgp-zoom-axis`),
independent of the PAD Cursor binds (`cursor-up/down/left/right`, `cursor-axis-h/v`,
`cursor-select`) that every MFD page's cursor already shares. Two separate input systems meant a
HOTAS with limited axes/buttons couldn't avoid binding the same physical control to both — using
the TGP camera and having any other cursor-driven MFD page as SOI at the same time collided. The
fix folds TGP pointing into the same generic PAD Cursor tool, and makes the manual camera itself a
first-class, cyclable SOI target instead of a parallel always-on input path.

**Bind changes:**
- Remove `tgp-pan-left/right`, `tgp-tilt-up/down`, `tgp-pan-axis`, `tgp-tilt-axis`,
  `tgp-zoom-in/out`, `tgp-zoom-axis` from the KEY page's TGP section. It keeps only the four
  lifecycle binds: `tgp-manual-toggle`, `tgp-manual-reset`, `tgp-point-track`,
  `tgp-manual-ir-toggle` (camera mode / COLOR-IR).
- Add `cursor-zoom-in`/`cursor-zoom-out` (digital) and `cursor-zoom-axis` (calibrated, reusing the
  log-linear FOV curve from [Debugging findings](#debugging-findings-worth-keeping)) to the PAD
  Cursor Keybinds section. These also absorb MAP's old dedicated `map-zoom-in`/`map-zoom-out`
  binds (removed): `Keybinds.Poll()` routes Cursor Zoom In/Out to the manual camera (held, every
  frame) while it holds SOI, or edge-triggered into `TelemetryServer.MapAction("zoom-in"/"zoom-out")`
  — the same action MAP's old binds sent, which TGT/RDR/WPT/HUD already repurpose as scroll/step on
  their own pages (docs/page-cursor.md) — for any other focused display. Cursor Zoom Axis stays
  camera-only; nothing else has an absolute-zoom concept to drive.

**SOI ring membership:** `TelemetryServer`'s SOI ring is currently built purely from connected
browser (cid, pane) clients (`SoiRingLocked()`) — it has no concept of anything that isn't a web
document. A new synthetic ring entry represents the manual TGP camera, but it exists in the ring
**only while `TgpManualControl.ManualMode` is true** — not merely while an aircraft/`TargetCam`
exists. Turning manual mode off (the toggle bind, or any existing lifecycle exit trigger) removes
the entry from the ring; if it was SOI when that happens, SOI reassigns to the next ring member,
the same way a disconnecting client is handled today.

**Auto-steal on engage:** pressing `tgp-manual-toggle` to turn manual mode **on** both inserts the
synthetic entry into the ring and immediately steals SOI onto it — the pilot doesn't have to Tab
to it manually the first time. From then on it's an ordinary ring member: `SOI Next`/`SOI Prev`
cycles through it alongside every MFD pane, and it can be tabbed away from and back to.

**Headless while un-focused:** tabbing SOI away from the camera to a different pane does **not**
exit manual mode — it keeps running exactly as it does today (still pointing, still capturing),
just deaf to PAD input, until one of the existing [Lifecycle](#lifecycle-every-exit-trigger) exit
triggers fires (real lock, aircraft loss, gear/landing-cam conflict, or the toggle bind again).

**Input routing:** `Keybinds.Poll()` already computes one cursor vector + select/zoom state per
frame regardless of what's SOI. It gains a check for `TelemetryServer.IsTgpSoi` — true when the
current SOI is either the synthetic camera entry itself, OR an ordinary pane/portal that's showing
the TGP page. That same per-frame vector/zoom goes to
`TgpManualControl.SetPan`/`SetZoom`/`SetZoomAxis` whenever `IsTgpSoi` is true, instead of only when
the camera's own ring entry is literally focused: the TGP page IS this camera's display, so a
pilot who Tabs directly onto an already-open TGP pane (without ever Tabbing onto the camera's own
entry, or even with the in-cockpit view hidden entirely) still expects pointing control to work.
`cursor-select` keeps doing exactly what it does today (`TryLockTrackedUnit`, self-gated on
`ManualMode` — see [Manual track to unit-lock handoff](#manual-track-to-unit-lock-handoff)).
`TelemetryServer.CursorSelect()`'s own broadcast is a harmless no-op when nothing is listening, so
it doesn't need special-casing here. When SOI is an ordinary pane not showing TGP, none of this
changes.

Since the plugin has no way to know what page a pane's content shows on its own, the SOI-focused
shell reports it: a new `soi.page` command (`cid`, `n` = pane index, `wname` = page name), sent by
`mfd.js`/`f35.js` whenever their own SOI focus changes or their focused pane's page changes
(`reportSoiPage()` in each). `TelemetryServer.ReportSoiPage` only accepts a report that matches the
CURRENT `(cid, pane)` target — a stale report arriving after focus already moved elsewhere is
ignored — and `SetSoiTargetLocked` clears the remembered page on every target change, so a leftover
"tgp" reading can't survive a Tab to a different real pane that hasn't reported in yet.
`TelemetryServer.IsTgpSoi = IsNativeTgpSoi || <focused page is "tgp">`.

**UI feedback — the ring.** `mfd.js`'s `#soi-ring` (and its F-35 shell twin, `f35.js`'s
`renderSoiRing`) is positioned/toggled purely by matching this shell's own pane/portal identity to
the server's `(cid, pane)` SOI target, completely unchanged from before this feature — the camera's
synthetic cid never equals any real client's own cid, so every display's `soiFocused` naturally
reads false and no ring shows anywhere while the camera itself holds focus, with no special case
needed. An earlier version of this content-matched instead — ringing whichever pane happened to be
*showing* the TGP page while the camera was SOI — which was wrong the moment a real TGP pane also
existed as its own separate, independently-focusable ring member: SOI could be tabbed onto that
literal pane directly, and lighting it up for the camera's unrelated focus was a real reported bug.
The ring is strictly per-target identity now: a real TGP pane rings when IT is genuinely SOI (plain
pane matching, nothing camera-specific), and never rings for the camera's own focus.

**UI feedback — in-cockpit.** `TgpNativeOverlay.SyncCrosshair` creates a small "SOI" tag — a tight
translucent-black chip (`rgba(0,0,0,0.6)`, matching the web TGP page's own data-field chips in
`tgp.css`) behind centered TextMeshPro text — the first time the crosshair itself is built,
parented under the same crosshair root so it shares its 0–1 canvas-normalized coordinate space.
Toggled with `IsNativeTgpSoi` (**not** the broader `IsTgpSoi` the input-routing check above uses):
the in-cockpit tag lights up only when the camera's own synthetic ring entry is literally focused,
not merely whenever a real TGP pane holds SOI — the web ring already covers that case, and showing
both at once as "focused" read as confusing. Positioned horizontally centered, vertically centered
between the bottom of the camera feed (y=0) and the bottom edge of the crosshair's own Bottom arm
(y = `1 - armEnd`, the same constant the crosshair bars are built from) — reads as attached to the
crosshair without overlapping it. Auto-sized to its box rather than a fixed point size, since the
canvas's real pixel scale isn't known here; uses TMP's default font rather than copying the game's
own `TargetScreenUI` style, a known simplification worth revisiting if it looks visually mismatched
next to the real fields.

**Implementation notes:**
- The synthetic ring cid is `TelemetryServer.NativeTgpCid` (`" tgp-camera"`, a leading space) —
  `SseHub`'s cid sanitizer only lets `[a-zA-Z0-9-]` through a real client's reported cid, so this
  can never collide with one, even by coincidence.
- `TgpManualControl.Engage()`/`ExitManual()` are the sole choke points for every entry/exit path
  (`Toggle()`, the unit-lock handoff, and every [lifecycle exit trigger](#lifecycle-every-exit-trigger)),
  so hooking `TelemetryServer.ClaimNativeTgpSoi()`/`ReleaseNativeTgpSoi()` there covers all of them
  without touching each call site individually.
- `Keybinds.Poll()`'s cursor vector feeds `TgpManualControl.SetPan` with Y negated: screen-space
  cursor Y grows downward (Cursor Down is `+1`), but `SetPan`'s Y is elevation-positive-up.
- `TelemetryServer.CursorSelect()`'s broadcast (a sequence counter bump) is a harmless no-op when
  nothing is listening, so `Keybinds.cs`'s `cursor-select` handler needed no special-casing for the
  synthetic SOI target — it already just calls `TryLockTrackedUnit()` unconditionally, self-gated
  on `ManualMode`; that method resolves the current look point for either Area or Point Track.

## TGP page NAV additions — LCK/MAN, CLR/IR (built)

The TGP page's bezel/glass nav gained four buttons alongside MAIN/CFG: `LCK`, `MAN`, `CLR`, `IR`
(the button's own `data-action`/command name stayed `tgp-manual-off` — only the on-screen label
changed, from `TGT` to `LCK`). `LCK`/`MAN` is a mutually-exclusive pair choosing which camera feeds
the page — a real (native) unit lock, or the manual camera — and `CLR`/`IR` is a second pair
choosing that active feed's color mode. Each pair gets a small decorative label between its two
buttons (`MODE` between LCK/MAN, `IMG` between CLR/IR), same word-plus-triangle treatment as WPN's
MASTER/MODE (`docs/radar-master-arms.md`).

**Highlight state**, not a page selection: unlike every other NAV entry, all four buttons reflect
live game state rather than "which page is this." `LCK` lights when a real unit is locked
(`!manual && data.cnt > 0`); `MAN` lights when `TgpManualControl.ManualMode` is on; `CLR`/`IR`
mirror whichever feed is actually active's `data.ir` flag. All four go dark with no feed up at all
(`data` is only ever `{cnt:0}` in that case — `TelemetryJson.cs`'s `TgpBlock`). The rule itself is
one shared `tgpMarks(cnt, manual, ir)` (`src/web/shell/shared/tgp-marks.js`), called by both `mfd.js` and
`f35.js` rather than each shell computing it independently, so the two can't drift — its own
`{tgt, man}` return shape kept the pre-rename property name since it's internal, read only by
`markTgpMode`/`placeTgpNavLabels`, not shown anywhere. Because this needs live data no static
table has, `LCK`/`MAN`/`CLR`/`IR` are **not** NAV.tgp entries — `NAV.tgp` stays exactly
`[MAIN, CFG]`, and the four are hand-placed by each layout's own renderer, the same "NAV stays
empty, the layout hand-rolls it" shape WPN's ARM/SAFE/A-A/A-G already use for the same reason
(`combatMode`/`masterArmsOn` are live state too).

**Commands.** Each pair is an explicit-state "set", not a toggle — pressing an already-lit button
is a no-op, matching `master-arms.set`/`combat-mode.set`'s own shape rather than replaying
`tgp-manual-toggle`/`tgp-manual-ir-toggle`'s blind flip:
- `tgp.manual.set { on }` → `TgpManualControl.SetManual(on)` — idempotent twin of `Toggle()`;
  `on:true` engages (same aircraft/TargetCam guards as the keybind), `on:false` exits.
- `tgp.ir.set { on }` → `TgpManualControl.SetIR(on)` — idempotent twin of `ToggleIR()`, and unlike
  `tgp.manual.set` it acts identically whether `ManualMode` is on or a real target is locked. See
  [Native-lock CLR/IR override](#native-lock-clrir-override-built) below for the real-lock half —
  the automatic behavior it overrides, and why it needs its own Harmony patch to stick.

**Layout placement:**
- Classic bezel, full view (`mfd.js`'s `placeTgpNavLabels`): `MAIN, LCK, MAN, CLR, IR, CFG` fill
  the left column top to bottom (`left0`-`left5`) — `CFG` keeps its existing bottom-of-column slot.
  Recomputed on page entry and on every `tgp` telemetry tick, the same re-render-in-place shape
  `placeWpnNavLabels` uses for ARM/SAFE.
- Classic bezel, split pane (`renderSplitLabels`' own `tgp` branch): `MAIN/LCK/MAN` fill the left
  bank (slots 0-2), `CLR/IR/CFG` fill the right bank (slots 0-2) — each decorator's pair stays
  adjacent on one bank, the same requirement `placeMapPaneDecorator`/`placeWpnPaneDecorator`
  enforce for theirs. Only re-renders when a pane is actually showing TGP.
- F-35 glass (`f35.js`): `TGP_MODE_NAV`/`TGP_IR_NAV` place `LCK/MAN/CLR/IR` at column 1, rows 2-5
  (`tgpNavItems`'s `MAIN`/`CFG` already own rows 1 and `ROWS`), same "unconditional command pair"
  shape as `MASTER_ARMS_NAV`/`COMBAT_MODE_NAV`. `markTgpMode`/`markTgpImg` re-apply the highlight
  off the cached `tgp` slice on every tick (`onSlice`) and on nav rebuild, mirroring
  `markMasterArms`/`markCombatMode`.

**Z+/Z- (built) — the manual camera's own on-page zoom buttons**, distinct from the general
remote-keybind role wiring still deferred above: two plain bezel buttons jumping between fixed
magnification LEVELS via `TgpManualControl.StepZoom(dir)`, through a `tgp.zoom.step { index }`
command (`CommandDispatcher.cs`) — not the physical Cursor Zoom In/Out keybind's own continuous
rate (`SetZoom`/`tgp.zoom.set`, unchanged and still used by the keybind). Dialing through every
intermediate magnification decimal-by-decimal at the keybind's rate was slow to land on a specific
number, so the bezel buttons instead jump straight to the next of a fixed list of magnifications
(`TgpManualAimMath.ZoomLevelsMag`: 0.5, 1, 2, 4, 8, 16, 32, 40x — roughly doubling each step,
covering the same 0.5x-40x range the continuous zoom already does). `NextZoomLevelMag(currentMag,
dir)` picks the next level up/down from wherever the FOV currently sits (not necessarily itself a
level, e.g. left over from the keybind's continuous zoom), clamping at the ends rather than
wrapping. A no-op while `ManualMode` is off (LCK mode): `TgpManualControl.Tick()` never reads
`_desiredFov` outside manual mode, so a step taken while off just sits there unseen until the next
`Engage()` resets it to `MaxFov` anyway — no extra gating needed in the command handler.
- Classic bezel, full view only (`placeTgpNavLabels`): `Z+`/`Z-` fill `right0`/`right1`, the
  otherwise-empty right bank — TGP's split-pane branch has no room (all 6 of its slots are already
  MAIN/LCK/MAN/CLR/IR/CFG), so split-pane TGP has no zoom buttons.
- F-35 glass: `TGP_ZOOM_NAV` places `Z+`/`Z-` at column 2, rows 2-3 — column 2 is entirely free for
  TGP (only column 1 is spoken for), so there's no split-pane-style capacity problem here.
- Both shells wire a plain pointerdown/pointerup pair per button: pointerdown sends one step
  immediately, then repeats it at a fixed interval (typematic — press once, then repeat after an
  initial delay, same feel as a held keyboard key: `TGP_ZOOM_STEP_INITIAL_DELAY_MS` = 350,
  `TGP_ZOOM_STEP_REPEAT_MS` = 150) until pointerup/pointercancel/pointerleave clears the timer.
  Classic shell tracks the held pointer by its `pointerId` (not by re-checking which key is under
  the pointer at release), so dragging off the key before lifting still stops the repeat.

## On-screen joystick (built)

A mouse/touch-only pan/tilt control on the TGP page itself (`#tgp-joystick`, `tgp.js`), bottom-right
of the panel — HOTAS pan/tilt and the KEY-page's own axis binds had no on-screen equivalent, so a
mouse/touch pilot had no way to point the manual camera at all short of a bound keyboard key.

- **No plugin change.** Sends `cursor.set { x, y }` — the same command `remote-keybinds.js` already
  sends for a remote-mapped physical key — which `RemoteInputState.SetCursor` already TTLs (250ms)
  and `Keybinds.Poll()` already merges into the PAD cursor vector it feeds `TgpManualControl.SetPan`
  whenever TGP holds SOI (see [PAD Cursor consolidation](#pad-cursor-consolidation-built) above).
  This is the "Pan/tilt" bullet under [Remote control](#fit-with-noxmfds-existing-architecture)
  above, reusing the *existing* command a page-local drag control already qualifies for, rather than
  the new `tgp-pan.set` that bullet originally proposed for a remote *keybind role*.
- **Math**: knob offset from the pad's center, divided by the pad's radius, clamped to the unit
  CIRCLE (`Math.hypot(dx, dy) > 1` rescales both axes) rather than a unit square — a real joystick's
  travel is circular, and clamping per-axis independently would let a diagonal drag report a
  deflection magnitude greater than a straight one. Right/down are positive on both axes, matching
  the screen-space convention `Keybinds.cs`'s own Cursor Left/Right/Up/Down already use before
  their Y gets negated for elevation — no sign-flip needed between this control and that one.
- **Continuous while held**: a `setInterval` re-sends the last computed vector every 50ms while
  dragging (comfortably under the 250ms TTL), the same keepalive cadence `remote-keybinds.js`
  already uses for its own `cursor.set` sends — a `pointermove`-only send would let the camera stop
  responding the moment the pointer stops moving while still held. Release sends an explicit
  `{x:0,y:0}` immediately (`pointerup`/`pointercancel`) rather than waiting out the TTL, for a crisp
  stop instead of up to 250ms of drift.
- **Gating**: dimmed and `pointer-events: none` outside `.tgp-manual` (a real unit locked instead of
  the manual camera) — functionally a no-op regardless, since `TgpManualControl.Tick()` returns
  immediately whenever `ManualMode` is off, so the held vector this control sends is never read into
  an actual pan — but a live-looking control that silently does nothing reads as broken.
- **Layout**: fixed pixel size, not the panel-percentage scaling the stat overlay uses — a drag
  target needs a stable minimum touch size regardless of how small the panel gets, unlike text that
  can just shrink. Positioned clear of `.tgp-ov-br`'s own bottom-right stat stack rather than the
  same corner, so the two don't overlap when both show at once (HQ quality, manual mode).
- A CSS gotcha worth remembering: a `transition` declared on a lower-specificity rule for a property
  a higher-specificity rule also sets can end up never resolving to the override's value at all, in
  at least one tested environment — dropping the transition (an instant opacity snap on the
  `.tgp-manual` gate above) sidesteps it entirely rather than chasing the exact conditions that
  trigger it.
- **Colors**: white ring/knob at rest (`--no-white-dark` border, `--no-white-dim` knob fill,
  `--no-white` knob border), amber (`--no-amber`) while actively dragging (`.dragging` class) — same
  family/shade mapping the rest of the page's white-on-black look uses, so the control doesn't stand
  out as its own color scheme. The pad's own translucent fill (`rgba(0, 0, 0, 0.6)`) matches the stat
  overlay's own pill background (`.tgp-ov-stat`, `.tgp-ov-compass`) exactly, rather than an
  independently-chosen alpha.

## Native-lock CLR/IR override (built)

A real (native) unit lock picks COLOR vs. IR on its own — `TargetCam.SetTargetCam`
(`_scratch/full/TargetCam.cs`) switches to IR when it's night, the target is beyond 10km, **or**
the player has the game's own "always IR" setting on (`PlayerSettings.tacScreenIR`), and back to
COLOR otherwise — every single frame a target stays locked, with no toggle of its own. That switch
lives inline in `SetTargetCam` itself, not in `AimCamera()` (an old comment on `ToggleIR` misnamed
it; `AimCamera()` only ever handles FOV/mount slewing).

Manual mode gets to just flip `IRMode` directly and leave it alone, because
`TargetCam_AimCamera_ManualGate` (`HarmonyPatches.cs`) already skips the whole call chain that
would otherwise fight it (see `ToggleIR`'s own comment). A real lock has no such gate — the
automatic switch keeps running every frame regardless — so a one-time flip from `SetIR` would be
silently overwritten on the very next frame.

Fixed with a second Harmony patch **and** a piece of persisted state:
- `TgpManualControl._nativeIrOverride` (`bool?`) — `null` until the player uses CLR/IR outside
  manual mode; sticky afterward, since there's no "revert to automatic" control, only CLR/IR, so it
  persists across a new lock too (deliberately — the whole point is "this mod's choice wins").
- `TargetCam_SetTargetCam_IrOverride` (`HarmonyPatches.cs`) — a **Postfix** on `SetTargetCam`
  (not a Prefix/skip: the rest of that method's camera-positioning/zoom/timeout work must still run
  normally for a real lock). Once the native method — and its own auto-switch — has already run for
  this frame, the postfix re-asserts `_nativeIrOverride` if it's set and doesn't already match,
  calling the same `TgpManualTargetCamAccess.SwitchIR` helper `SetIR` uses. No-ops instantly while
  `ManualMode` is on, since that path owns IR directly and never needs re-asserting.

`SetIR`/`ToggleIR` themselves dropped their `if (!ManualMode) return;` guard — they now read
`tc.UsingIR()` and flip it the same way regardless of mode, only additionally recording
`_nativeIrOverride` when `!ManualMode` so the postfix has something to re-assert. This means the
`tgp-manual-ir-toggle` keybind and the TGP page's CLR/IR buttons already work identically whether
manual control is on or a real target is locked — no separate bind or button was needed.

## Out of scope

- Additional weapon-lock/targeting controls beyond the PAD Cursor Select manual-track handoff.
- ~~In-cockpit `TargetScreenUI` info-panel patch (RNG/ALT/HDG/GRID/MODE from the manual hit
  point)~~ — **built**, see [In-cockpit overlay](#in-cockpit-overlay-v2).
- ~~Manual IR toggling (`SwitchIRState`)~~ — **built**: a `tgp-manual-ir-toggle` bind flips
  `IRMode` through `TgpManualTargetCamAccess` (`SwitchIRState` is private but self-contained, and
  unpatched by anything else). Safe because `AimCamera()` — the only thing that would otherwise
  fight a manual flip with its own automatic time-of-day/distance-based switching — is already
  skipped entirely while `ManualMode` is on (`TargetCam_AimCamera_ManualGate`), so `IRMode` just
  holds whatever it's set to. NOXMFD's HQ overlay's separate simulated-IR look
  (`docs/tgp-high-quality-mode.md`) is unrelated — this is the Native-mode camera's own IR, baked
  into the video like the rest of Native mode's picture. Since extended to also override a real
  (native) unit lock's own automatic switching — see
  [Native-lock CLR/IR override](#native-lock-clrir-override-built) below.
- Remote-keybind wiring for TGP pointing — the command surface is designed for it (see
  [above](#fit-with-noxmfds-existing-architecture)), but only local KEY binds are wired so far.
- Changes to `tgp-suppress-native-render.md`'s cockpit-hide feature or `tgp-high-quality-mode.md`'s
  HQ mirror pipeline — related, unmodified by this feature.

## Related

- [`tgp-hud-tracker.md`](tgp-hud-tracker.md) — the full-screen pilot-HUD line-of-sight cue (see
  [Pilot-HUD line-of-sight cue](#pilot-hud-line-of-sight-cue-v4-tgp-hud-tracker-branch) above),
  its own extended validation matrix.
- [`tgp-suppress-native-render.md`](tgp-suppress-native-render.md) — same `TargetCam`, same
  `onCamToggle`/`CancelTarget` surface, shipped 0.29.1, composes with this feature.
- [`internal-mfd.md`](internal-mfd.md) — unrelated native cockpit-MFD content planning with a
  different target-camera problem.
- `_scratch/full/TargetCam.cs` — the decompiled source this doc's claims about `Update`/
  `AimCamera`/`SetTargetCam`/`CancelTarget` are verified against.

## Implementation notes after live testing

NOXMFD separates manual pointing into two explicit pilot concepts:

- Area Track: free manual aim is stored aircraft-local, so a centered camera turns with the
  aircraft. This matched the live in-game feel requested during testing.
- Point Track: a deliberate toggle locks the aim to a fixed world point, then allows nudge and
  redesignate on release.

The explicit split keeps mode ownership legible. Free scanning does not silently re-designate a
ground point, and Point Track is available when stabilization is wanted. TGP inputs remain in the
existing KEY-page bind registry so joystick, axis, and remote-command behavior share one input
model. Live testing confirms the current game build uses `TextMeshProUGUI` for the relevant
`TargetScreenUI` fields, so the Harmony patch injects TMP fields and delegates their population to
`TgpNativeOverlay`.

The implementation preserves these invariants:

- `TargetCam.Update` and `AimCamera` Harmony gates while manual mode is active.
- `camTimeout` pinning while manual mode owns the camera.
- Landing-camera conflict handling: force out of landing mode on engage and exit if landing mode
  appears while manual mode is active.
- Manual COLOR/IR toggle through the private `SwitchIRState` method.
- Native in-cockpit overlay population for manual state.
- Point/ground tracking with `GlobalPosition`.
- Zoom-aware pan/tilt scaling.

Responsibilities remain split as follows:

- `TgpManualControl.cs`: state machine, lifecycle, Point Track, input API.
- `TgpManualAimMath.cs`: pure pan/tilt/zoom math, covered by unit tests.
- `TgpManualTargetCamAccess.cs`: private `TargetCam` reflection.
- `TgpNativeOverlay.cs`: in-cockpit text/crosshair population.
- `HarmonyPatches.cs`: the actual game method gates/prefixes.
- TGP web page files: external-MFD display state and overlay.

### Remaining hardening: deterministic forward mount

The game's TGP camera can internally sit on different mounts, including the forward target camera,
rear target camera, and landing camera. Manual mode needs a deterministic starting point: the normal
forward TGP mount with leftover local position and rotation cleared before pan, tilt, and zoom take
over.

Vanilla `SetTargetCam()` already does this when the target camera is disabled. The decompiled game
code sets `currentMount = camMountForward`, reparents `cam.transform` to `camMountForward`, zeros
local rotation/position, resets the forward mount, sets FOV/clip planes, marks
`currentMode = targetForward`, and enables the camera.

The edge case is entering manual mode while the target camera is already enabled from previous game
state. In that case, vanilla `SetTargetCam()` may not run its full "camera was disabled" reset path,
so manual mode could inherit a stale mount or transform.

What that would feel like in-game:

- Manual mode starts looking backward, sideways, or offset from boresight.
- Pan left/right feels inverted or strangely rotated.
- Reset does not feel perfectly centered.
- The first manual view after a real lock, landing-cam transition, or fast lock/unlock sequence
  starts from a surprising angle.
- The external HQ feed still works technically, but the view direction feels wrong.

The hardening is small and defensive: cache `camMountForward` in
`TgpManualTargetCamAccess`, and on manual engage explicitly set `currentMount` to it, reparent the
camera to it, zero the camera's local position/rotation, zero the forward mount, and mark
`currentMode = targetForward`. This makes the starting camera state deterministic before giving
control to the pilot.
