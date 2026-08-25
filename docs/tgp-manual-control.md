# TGP — manual camera control (pan/tilt/zoom)

## Status

**Built and confirmed working in-game.** Written after pulling the full source of a third
`9138noms` mod, `TargetCamControl`, which ships exactly this feature standalone — a DCS-style
manual override of the same `TargetCam` NOXMFD's `TgpFeed` already reflects into.

Reviewed once before implementation. Agreed: targets the real `TargetCam` (not just the HQ
mirror), the Harmony requirement is justified, and auto-exit-on-real-lock is the right default.
Four changes made from that review, folded into the sections below: (1) ships as a new
`TgpManualControl.cs`, not inside `TgpFeed.cs`; (2) the command surface is designed remote-capable
from the start, even though it ships local-only; (3) lifecycle rules — gear/landing-cam, aircraft
loss — are explicit exit triggers, not just "real lock acquired"; (4) world-hit raycasting, floated
as a "validation spike," was built out fully as **Point Track** (see below) rather than deferred —
the spike confirmed the drift was bad enough to need it.

Shipped beyond the original scope after live testing surfaced real needs: **Point Track**
(lock the aim to a fixed world point instead of a free direction), a **calibrated Zoom Axis**
(a physical slider whose position *is* the zoom level, not rate-based), a **boresight crosshair**
on the TGP page, and **Area Track following the airframe** (a centered/reset camera now turns
with the aircraft instead of holding a frozen world bearing). All four were explicitly requested
during testing, not scope creep — see [What actually shipped](#what-actually-shipped-v1-revised)
and [Debugging findings](#debugging-findings-worth-keeping) below for what each one is and the
real bugs found getting there.

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

## Precedent — `TargetCamControl` (full source reviewed)

`github.com/9138noms/TargetCamControl` (`Plugin.cs` + `Runner.cs`, both pulled in full) ships this
today as a standalone mod. Its shape, in order:

**Reflection cache** (`Plugin.Awake()`), all against the same `TargetCam` type `TgpFeed` already
reflects into: `cam`, `targetFOV`, `IRMode`, `camTimeout`, `currentMount`, `camMountForward`,
`canvasObjectTarget`, `currentMode`, `canvasObjectLanding`, plus a `MethodInfo` for
`SetTargetCam`. (`CancelTarget` and `SetTargetCam` are actually `public` on `TargetCam` — see
[Reflection surface](#reflection-surface-decompiled) below; this mod reflects `SetTargetCam` too,
but doesn't need to.)

**Three Harmony prefixes**, all gated on a `ManualMode` bool flag:

```csharp
[HarmonyPatch(typeof(TargetCam), "Update")]
static class Patch_TargetCam_Update
{
    [HarmonyPrefix]
    static bool Prefix(TargetCam __instance)
    {
        if (!Plugin.ManualMode) return true;
        // ...preserve the cosmetic exposure update...
        return false; // skip the rest of vanilla Update
    }
}

[HarmonyPatch(typeof(TargetCam), "AimCamera")]
static class Patch_TargetCam_AimCamera
{
    [HarmonyPrefix]
    static bool Prefix() => !Plugin.ManualMode;
}

[HarmonyPatch(typeof(TargetCam), "SwitchIRState")]
static class Patch_TargetCam_SwitchIRState
{
    public static bool AllowNext = false; // set true just before an intentional manual IR call
    [HarmonyPrefix]
    static bool Prefix()
    {
        if (!Plugin.ManualMode) return true;
        if (AllowNext) { AllowNext = false; return true; }
        return false;
    }
}
```

The `Update` patch's own comment explains why all three are needed, not just `AimCamera`: without
it, the game's own `Update()` would keep lerping `cam.fieldOfView` toward its own `targetFOV`
(fighting manual zoom), switching `currentMount` between forward/rear based on angle-to-target
(unstable with no real target — `targetPosition` sits near the origin), and counting down
`camTimeout` toward the auto-disable path.

**Engaging manual mode without a real target lock** (`Runner.ForceEnableManualCam`): force
`currentMode` out of `landingMode` (or `SetTargetCam()` early-returns), deactivate the landing
canvas if active, call `tc.SetTargetCam()` once if `cam.enabled` is false (this is what actually
allocates `canvasObjectTarget`/`screenVolume` and flips `cam.enabled = true` — the same call
`TgpFeed.CaptureFrame()` already makes every capture tick while a real target is locked), then pin
`camTimeout` to a large value (`99f`) and set an initial `targetFOV`.

**Driving pan/tilt/zoom every frame** (`Runner.LateUpdate`, only while `ManualMode`): zoom writes
`cam.fieldOfView` directly (lerped toward a `DesiredFOV` clamped to `[MinFOV, MaxFOV]`), pointing is
tracked as a **world-space direction** (`PanDir`), not a raw mount rotation — user pan/tilt input
rotates `PanDir` around world-up (yaw) and the current right vector (pitch), then `mount.rotation =
LookRotation(PanDir, Vector3.up)` is applied directly every frame. A periodic raycast in `PanDir`
re-derives a world hit point (`LastHitGP`, stored as the game's own `GlobalPosition` so it survives
floating-origin rebasing) and `PanDir` is re-computed from that hit point each frame before applying
new input deltas — this is what keeps the pointed-at spot stable as the aircraft translates, instead
of the view slowly drifting off target the way naive constant-rate rotation would.

**Auto-exit on a real lock** (`Runner.LateUpdate`): if the game's own `WeaponManager.GetTargetList()`
becomes non-empty while manual mode is active, manual mode exits automatically, handing control back
to vanilla logic. This is the load-bearing simplification that lets the whole feature avoid patching
whatever vanilla call site normally invokes `SetTargetCam()` on a real lock (not found in the
decompile reviewed for this doc — TargetCamControl doesn't need to know where it is, because it
just cedes control the moment a lock exists instead of fighting it).

**Exiting manual mode** (`Runner.ExitManual`): reset `cam.transform.localRotation` and
`mount.localRotation` to identity, then call `tc.CancelTarget()` — the same public method
`docs/tgp-suppress-native-render.md`'s design space is built around — which flips `cam.enabled =
false` and fires `onCamToggle(enabled:false)`, letting `TacScreen` revert the cockpit overlay
cleanly.

## Reflection surface (decompiled)

Confirmed directly against `_scratch/full/TargetCam.cs` (already in this repo, used for the TGP
suppression work):

- `SetTargetCam()` and `CancelTarget()` are **public** — no reflection needed to call either from
  `TgpFeed`/a new manual-control class, unlike `TargetCamControl` which reflects `SetTargetCam` as
  a `MethodInfo` anyway (harmless, just unnecessary for NOXMFD's case).
- `currentMount`, `camMountForward`, `targetFOV`, `camTimeout`, `currentMode`, `canvasObjectLanding`
  are all `private` fields — reflection required, same as `TargetCamControl`'s cache.
- `Update()`'s mount-switch/FOV-lerp/`camTimeout` countdown and `AimCamera()`'s `LookRotation`
  toward `targetPosition` are exactly what the `Update`/`AimCamera` Harmony prefixes above must
  suppress — confirmed line-for-line against the decompile, not just inferred from
  `TargetCamControl`'s comments.
- `SetTargetCam()` calling `AimCamera()` at its own tail matters here too: the one-time engage call
  (`ForceEnableManualCam`'s `tc.SetTargetCam()`) would itself trigger a real `AimCamera()` call
  unless `ManualMode` is already `true` *before* that call — ordering matters when porting this.

## Fit with NOXMFD's existing architecture

**New file: `TgpManualControl.cs`, not inside `TgpFeed.cs`.** `TgpFeed` is capture/overlay/
cockpit-hide plumbing — reflecting `cam`/`targetScreenRenderer`, driving the JPEG capture pipeline,
and (as of 0.29.1) the cockpit-hide toggle. Manual pointing is a different responsibility: it owns
its own state (`ManualMode`, `PanDir`/mount rotation, `DesiredFOV`), its own reflection cache
(`currentMount`, `camMountForward`, `targetFOV`, `camTimeout`, `currentMode`,
`canvasObjectLanding`), and its own per-tick drive logic. `TgpFeed` only needs to *ask* this new
class one thing each capture tick — is manual mode active? — to decide whether to skip its own
`tc.SetTargetCam()` call (see below). Keeping them separate means `TgpFeed` doesn't grow a second
unrelated state machine, and manual control doesn't need to know anything about JPEG capture,
readback, or the cockpit-hide overlay event.

**Harmony is not a new dependency.** `src/plugin/HarmonyPatches.cs` already exists, with an
established convention this feature should follow exactly: one nested `static class` per patch,
`[HarmonyPatch(typeof(...), "MethodName")]`, patched individually via `CreateClassProcessor(...)`
in a per-class `try/catch` inside `HarmonyPatches.Init()` — so a failure to apply one of the three
`TargetCam` patches (e.g. a future game update renaming `AimCamera`) degrades to a logged warning,
not a crash of every other patch in the file.

**Keybinds reuse the existing axis-capable bind pattern**, not a new input-registration path.
`TargetCamControl` had to hand-register brand-new Rewired actions into the game's Debug category
because it's a standalone mod with no existing bind UI. NOXMFD already has exactly this shape of
control for MAP's cursor (`docs/map-cursor.md`, `Keybinds.cs`'s `AddAxis` + the two
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
  `TelemetryServer.cs`'s `GetRemoteCursorState`/`SetRemoteCursorState`). A `tgp-pan`/`tgp-tilt`
  pair of roles and a `tgp-pan.set { x, y }` command, with its own TTL state alongside the existing
  cursor/fire TTL blocks in `TelemetryServer.cs`, is the same pattern, not a new one.
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
(`src/plugin/TgpFeed.cs`). Manual mode auto-exits on a real lock (see
[Lifecycle: every exit trigger](#lifecycle-every-exit-trigger) below), so this call site needs no
change: manual mode is never active at the same time `hasTargets` is true.

## Lifecycle: every exit trigger

Manual mode has more ways to end than "the player toggled it off," and the review's "haunted
feature" risk is exactly what happens if any of them are missed. `TgpManualControl` should check
all of these every tick (cheap — it's already ticking every frame to drive pan/tilt) and exit
cleanly (mirroring `TargetCamControl.ExitManual`: zero the mount/cam local rotation, call
`tc.CancelTarget()`) the instant any of them is true:

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

Four pieces beyond the original scope, all added in response to real problems hit during live
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
- **Calibrated Zoom Axis** (`tgp-zoom-axis` bind, an `AddAxis` bind like Pan/Tilt Axis) — a
  physical analog control (e.g. a HOTAS slider) whose raw position directly *is* the zoom level
  (linear map, `-1` = `MaxFov`/widest, `+1` = `MinFov`/tightest), not a rate like the Zoom In/Out
  buttons. Once bound it's authoritative outright — the buttons stop mattering. Deliberately
  **not** rate-limited or smoothed (see Debugging findings) — a calibrated control's whole point is
  instant, 1:1 response.
- **Boresight crosshair** — a small white reticle with a gap at center, shown on the TGP page only
  while manual control is on (`.tgp-manual` class already used for the status badge). Screen-
  centered, not synced to the letterbox-corrected overlay rect the HQ stat overlay uses:
  `object-fit: contain` already keeps the picture's own center at the panel's center regardless of
  letterboxing, so plain 50/50% CSS positioning lines up correctly on its own.
- **Area Track follows the airframe.** A centered/reset camera now turns *with* the aircraft as it
  banks/turns, instead of holding whatever world bearing was forward at reset time. Implemented as
  an offset from the aircraft's own forward, expressed in the aircraft's local space
  (`_localPanDir`, `Vector3.forward` = boresight) — the world-space direction actually sent to the
  mount is re-derived from the aircraft's *current* attitude every tick, not just when there's
  pan/tilt input.

Two pieces from `TargetCamControl`'s reference shape stayed genuinely out of scope — see
[Out of scope](#out-of-scope).

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

## Open questions

- **Zoom limits** — resolved: fixed at `MinFov = 0.25°` / `MaxFov = 20°`, matching
  `TargetCam.SetTargetCam()`'s own native clamp range rather than `TargetCamControl`'s
  0.25°/80° — no new config needed since the native camera never goes tighter/wider than this
  either. The Zoom Axis bind (see [What actually shipped](#what-actually-shipped-v1-revised)) maps
  its full physical travel across exactly this range.
- **Where does the toggle status live?** — resolved: a `MANUAL — RESET to recenter` badge on the
  TGP page itself, screen-top-center, shown via the same `tgp-manual` class the crosshair uses.
  Not on TGP CFG, per the original reasoning (a live control, not a set-once preference).
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

Delivered, including the four additions in [What actually shipped](#what-actually-shipped-v1-revised):

1. `TgpManualControl.cs` — state, reflection cache, engage/exit/reset, Point Track (raycast lock +
   decoupled baseline/offset), zoom-scaled pan/tilt sensitivity, and the public `SetPan`/`SetZoom`/
   `SetZoomAxis`/`Toggle`/`Reset`/`TogglePointTrack` API (remote-ready per
   [above](#fit-with-noxmfds-existing-architecture), local-only wired so far).
2. `HarmonyPatches.cs` — the `TargetCam.Update`/`AimCamera` prefixes, matching the file's existing
   one-class-per-patch convention. `SwitchIRState` was not patched — manual IR toggling stayed out
   of scope (see below).
3. `Keybinds.cs` — KEY-page binds: toggle, reset, Point Track, pan/tilt button pairs, pan/tilt
   axes, zoom in/out, and the calibrated zoom axis.
4. TGP page — `MANUAL`/reset-hint badge and the boresight crosshair, both gated on the same
   `tgp-manual` class.
5. Every exit trigger from [Lifecycle](#lifecycle-every-exit-trigger) except the removed page-close
   one: real lock, aircraft loss, gear/landing-cam conflict.
6. Verified in-game: Native quality, a fast lock/unlock cycle, Point Track lock/nudge/release/
   redesignate, the calibrated zoom axis, and the airframe-follow behavior. HQ quality and
   cockpit-hide interaction not specifically re-verified after the debugging pass — worth a look if
   either one is touched again.

## Out of scope

- Any weapon-lock or targeting integration — this is camera pointing only.
- ~~In-cockpit `TargetScreenUI` info-panel patch (RNG/ALT/HDG/GRID/MODE from the manual hit
  point)~~ — **built**, see [In-cockpit overlay](#in-cockpit-overlay-v2).
- ~~Manual IR toggling (`SwitchIRState`)~~ — **built**: a `tgp-manual-ir-toggle` bind flips
  `IRMode` via reflection (`SwitchIRState` is private but self-contained, and unpatched by anything
  else). Safe because `AimCamera()` — the only thing that would otherwise fight a manual flip with
  its own automatic time-of-day/distance-based switching — is already skipped entirely while
  `ManualMode` is on (`TargetCam_AimCamera_ManualGate`), so `IRMode` just holds whatever it's set
  to. NOXMFD's HQ overlay's separate simulated-IR look (`docs/tgp-high-quality-mode.md`) is
  unrelated — this is the Native-mode camera's own IR, baked into the video like the rest of Native
  mode's picture.
- Remote-keybind wiring for TGP pointing — the command surface is designed for it (see
  [above](#fit-with-noxmfds-existing-architecture)), but only local KEY binds are wired so far.
- Changes to `tgp-suppress-native-render.md`'s cockpit-hide feature or `tgp-high-quality-mode.md`'s
  HQ mirror pipeline — related, unmodified by this feature.

## Related

- [`tgp-suppress-native-render.md`](tgp-suppress-native-render.md) — same `TargetCam`, same
  `onCamToggle`/`CancelTarget` surface, shipped 0.29.1, composes with this feature.
- [`internal-mfd.md`](internal-mfd.md) — same precedent-gathering session; unrelated feature
  (native cockpit MFD content), different target camera problem.
- `_scratch/full/TargetCam.cs` — the decompiled source this doc's claims about `Update`/
  `AimCamera`/`SetTargetCam`/`CancelTarget` are verified against.
- `github.com/9138noms/TargetCamControl` — the reference implementation this doc is built from
  (`Plugin.cs`, `Runner.cs`, both pulled and read in full, not summarized).
