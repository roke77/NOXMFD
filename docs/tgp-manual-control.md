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
(a physical slider whose moved position *is* the zoom level, not rate-based), a **boresight crosshair**
on the TGP page, **Area Track following the airframe** (a centered/reset camera now turns
with the aircraft instead of holding a frozen world bearing), and **manual COLOR/IR toggling**.
All five were explicitly requested
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

Five pieces beyond the original scope, all added in response to real problems hit during live
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
- **Point Track to unit-lock handoff** — pressing the existing **PAD Cursor Select** bind while
  Point Track is active searches for the closest selectable live unit to the tracked world point.
  The normal acquisition radius is 50 m, expanded to one full unit length for unusually large
  units. A match goes through the same select-only path used by MAP/RDR commands (TGT filters,
  no neutral/scenery/self targets, HUD marker/audio when available, and multiplayer target-list propagation), then
  manual mode exits and the game's normal locked `TargetCam` takes over. With no nearby match the
  press is still delivered to the focused web display and manual Point Track remains unchanged.
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

**Client-side gating stays "HQ quality," for manual mode too.** The original `applyOverlay` only
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
  `TargetCam.SetTargetCam()`'s own native clamp range rather than `TargetCamControl`'s
  0.25°/80° — no new config needed since the native camera never goes tighter/wider than this
  either. The Zoom Axis bind (see [What actually shipped](#what-actually-shipped-v1-revised)) maps
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
   bind also promotes a Point Track near a unit into the normal game lock.
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
`cursor-select` keeps doing exactly what it does today (`TryLockTrackedUnit`, already self-gated on
`ManualMode` and Point Track — see [Point Track to unit-lock handoff](#point-track-to-unit-lock-handoff)).
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

**UI feedback — in-cockpit.** `TgpNativeOverlay.SyncCrosshair` creates a small "SOI" TextMeshPro
label the first time the crosshair itself is built, parented under the same crosshair root so it
shares its 0–1 canvas-normalized coordinate space, and toggles it with `IsTgpSoi` (so it also lights
up when a real TGP pane — not just the camera's own ring entry — has focus). Positioned horizontally
centered, vertically centered between the bottom of the camera feed (y=0) and the bottom edge of
the crosshair's own Bottom arm (y = `1 - armEnd`, the same constant the crosshair bars are built
from) — reads as attached to the crosshair without overlapping it. Auto-sized to its box rather
than a fixed point size, since the canvas's real pixel scale isn't known here; uses TMP's default
font rather than copying the game's own `TargetScreenUI` style, a known simplification worth
revisiting if it looks visually mismatched next to the real fields.

**Implementation notes:**
- The synthetic ring cid is `TelemetryServer.NativeTgpCid` (`" tgp-camera"`, a leading space) —
  `SanitizeCid` only lets `[a-zA-Z0-9-]` through a real client's reported cid, so this can never
  collide with one, even by coincidence.
- `TgpManualControl.Engage()`/`ExitManual()` are the sole choke points for every entry/exit path
  (`Toggle()`, the unit-lock handoff, and every [lifecycle exit trigger](#lifecycle-every-exit-trigger)),
  so hooking `TelemetryServer.ClaimNativeTgpSoi()`/`ReleaseNativeTgpSoi()` there covers all of them
  without touching each call site individually.
- `Keybinds.Poll()`'s cursor vector feeds `TgpManualControl.SetPan` with Y negated: screen-space
  cursor Y grows downward (Cursor Down is `+1`), but `SetPan`'s Y is elevation-positive-up.
- `TelemetryServer.CursorSelect()`'s broadcast (a sequence counter bump) is a harmless no-op when
  nothing is listening, so `Keybinds.cs`'s `cursor-select` handler needed no special-casing for the
  synthetic SOI target — it already just calls `TryLockTrackedUnit()` unconditionally, self-gated
  on `ManualMode` and Point Track.

## Out of scope

- Additional weapon-lock/targeting controls beyond the PAD Cursor Select Point Track handoff.
- ~~In-cockpit `TargetScreenUI` info-panel patch (RNG/ALT/HDG/GRID/MODE from the manual hit
  point)~~ — **built**, see [In-cockpit overlay](#in-cockpit-overlay-v2).
- ~~Manual IR toggling (`SwitchIRState`)~~ — **built**: a `tgp-manual-ir-toggle` bind flips
  `IRMode` through `TgpManualTargetCamAccess` (`SwitchIRState` is private but self-contained, and
  unpatched by anything else). Safe because `AimCamera()` — the only thing that would otherwise
  fight a manual flip with its own automatic time-of-day/distance-based switching — is already
  skipped entirely while `ManualMode` is on (`TargetCam_AimCamera_ManualGate`), so `IRMode` just
  holds whatever it's set to. NOXMFD's HQ overlay's separate simulated-IR look
  (`docs/tgp-high-quality-mode.md`) is unrelated — this is the Native-mode camera's own IR, baked
  into the video like the rest of Native mode's picture.
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

## Annex: TargetCamControl comparison after implementation

Reviewed again after the NOXMFD manual-TGP work was already implemented and refactored. Source:
`https://github.com/9138noms/TargetCamControl`, latest reviewed commit
`405c8d78c17aac6212aca0028d8f44585eefcd30`.

TargetCamControl is a small standalone BepInEx plugin for the same underlying Nuclear Option
`TargetCam`. It is highly relevant as prior art, but not as a project-architecture model for
NOXMFD. It has three commits, all from 2026-05-04, and roughly 815 lines across two code files
(`Plugin.cs` and `Runner.cs`). That shape is reasonable for a focused standalone utility mod, but
NOXMFD has more surrounding responsibilities: external web MFDs, native/HQ video capture modes,
telemetry JSON, browser-rendered overlays, a KEY-page binding UI, remote-command concerns, tests,
and planning/manual docs.

### What it validates

The external project independently validates the main technical direction used here:

- Manual TGP control should drive the real game `TargetCam`, not only NOXMFD's HQ mirror camera.
- Vanilla `TargetCam.Update()` must be gated while manual mode owns the camera, because it would
  otherwise keep changing FOV, switching mounts, and counting down `camTimeout`.
- Vanilla `TargetCam.AimCamera()` must also be gated, or it will keep steering toward the game's
  own target position instead of the pilot's manual aim.
- Manual mode should exit when the game gets a real target lock, so vanilla targeting remains the
  owner of lock-driven behavior.
- Ground/point tracking needs world-position stability. TargetCamControl stores the hit point as a
  `GlobalPosition`, which matches NOXMFD's Point Track design and avoids floating-origin drift.
- Pan/tilt rate needs to be zoom-aware. Both projects reached the same user-facing lesson: a fixed
  world-angle rate feels increasingly jumpy as FOV narrows.
- Exiting through `TargetCam.CancelTarget()` is the correct clean handoff because it lets the game
  fire the normal camera-toggle event that cockpit UI systems already listen to.

### Important differences

TargetCamControl's default behavior is closer to "always auto ground-lock": it raycasts along the
camera direction, stores the last hit, and re-aims at that hit point as the aircraft moves. NOXMFD
now separates that into two explicit pilot concepts:

- Area Track: free manual aim is stored aircraft-local, so a centered camera turns with the
  aircraft. This matched the live in-game feel requested during testing.
- Point Track: a deliberate toggle locks the aim to a fixed world point, then allows nudge and
  redesignate on release.

That explicit split is preferable for NOXMFD because it makes mode ownership legible. Free scanning
does not silently keep re-designating a ground point, and Point Track is available when stabilization
is actually wanted.

TargetCamControl also registers its own Rewired actions in the game's controls UI. NOXMFD should
not copy that. The repo already has a KEY page, its own bind registry, joystick/axis capture, and
remote-command conventions. Keeping TGP binds inside that system avoids a second input model.

Its overlay path uses `UnityEngine.UI.Text`/Harmony Traverse-style field access. NOXMFD's live
testing found the current game build uses `TextMeshProUGUI` for the relevant `TargetScreenUI`
fields, so the safer NOXMFD design is the current one: the Harmony patch injects TMP fields and
delegates population to `TgpNativeOverlay`.

### Useful implementation details already absorbed

Several TargetCamControl ideas are already present in NOXMFD:

- `TargetCam.Update` and `AimCamera` Harmony gates while manual mode is active.
- `camTimeout` pinning while manual mode owns the camera.
- Landing-camera conflict handling: force out of landing mode on engage and exit if landing mode
  appears while manual mode is active.
- Manual COLOR/IR toggle through the private `SwitchIRState` method.
- Native in-cockpit overlay population for manual state.
- Point/ground tracking with `GlobalPosition`.
- Zoom-aware pan/tilt scaling.

NOXMFD's implementation is intentionally more split by responsibility:

- `TgpManualControl.cs`: state machine, lifecycle, Point Track, input API.
- `TgpManualAimMath.cs`: pure pan/tilt/zoom math, covered by unit tests.
- `TgpManualTargetCamAccess.cs`: private `TargetCam` reflection.
- `TgpNativeOverlay.cs`: in-cockpit text/crosshair population.
- `HarmonyPatches.cs`: the actual game method gates/prefixes.
- TGP web page files: external-MFD display state and overlay.

### Shortlist improvement: explicit forward-mount hardening

The one implementation detail still worth considering is TargetCamControl's explicit forward-mount
reset on manual engage. In simpler terms: the game's TGP camera can internally sit on different
"mounts" or camera positions, such as forward target cam, rear target cam, and landing cam. Manual
mode wants a clean starting point: use the normal forward TGP mount, put the camera exactly on that
mount, clear leftover local position/rotation, then let manual pan/tilt/zoom take over.

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

The hardening would be small and defensive: cache `camMountForward` in
`TgpManualTargetCamAccess`, and on manual engage explicitly set `currentMount` to it, reparent the
camera to it, zero the camera's local position/rotation, zero the forward mount, and mark
`currentMode = targetForward`. This is not a new feature; it is just making the starting camera
state deterministic before giving control to the pilot.

Do not copy TargetCamControl's full two-file structure, `PatchAll()` initialization, or standalone
Rewired action registration into NOXMFD. The useful part is the game-specific TargetCam behavior,
not its architecture.
