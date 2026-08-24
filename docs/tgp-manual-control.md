# TGP — manual camera control (pan/tilt/zoom)

## Status

**Planning.** Not started. Written after pulling the full source of a third `9138noms` mod,
`TargetCamControl`, which ships exactly this feature standalone — a DCS-style manual override of
the same `TargetCam` NOXMFD's `TgpFeed` already reflects into.

Reviewed once. Agreed: targets the real `TargetCam` (not just the HQ mirror), the Harmony
requirement is justified, and auto-exit-on-real-lock is the right v1 default. Four changes made
from that review, folded into the sections below: (1) this ships as a new `TgpManualControl.cs`,
not inside `TgpFeed.cs`; (2) the command surface is designed remote-capable from the start, even
though v1 ships local-only; (3) lifecycle rules — page close, gear/landing-cam, aircraft loss — are
now explicit exit triggers, not just "real lock acquired"; (4) skipping world-hit raycasting is now
framed as a v1 validation spike to confirm/deny, not a settled scope cut.

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
- **External TGP page closes** — `!TelemetryServer.WantsTgpFrames`. This is the exact signal
  `TgpFeed.Tick()` already gates on (`src/plugin/TgpFeed.cs`'s `WantsTgpFrames` check, added for
  cockpit-hide in 0.29.1) — reuse it, don't invent a second "is anyone watching" signal. **Default:
  closing the page exits manual mode.** A "let me keep pointing the cockpit MFD manually with no
  external page open" mode is a real but separate feature (nobody's watching the external feed, so
  the only audience is the native cockpit display) — out of scope for v1, worth its own toggle if
  ever requested, not a default behavior to build speculatively now.
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

## What v1 should probably skip

`TargetCamControl` includes several pieces that read as valuable but separable. One of them —
world-hit raycasting — is a **validation spike, not a settled scope cut**; the rest are safe to
defer without re-checking:

- **World-hit raycasting + `GlobalPosition` tracking** for a stable pointed-at world spot. This is
  the single most valuable piece of the reference implementation (it's what makes "point at a place
  and have it stay there"), and the risk of skipping it isn't hypothetical: a pure aircraft-relative
  pan/tilt with no world-relock will visibly drift off whatever the player was looking at every
  time the aircraft banks or turns, which is likely to read as broken rather than as an acceptable
  v1 limitation. **Build the simpler aircraft-relative version first specifically to find out how
  bad this is in practice** (a quick spike, not a ship decision), and treat "add the raycast/
  relock system" as the probable very-next-step rather than a maybe. Don't call v1 done on the
  simple version without that check.
- **In-cockpit MFD info-panel patch** (`TargetScreenUI.UpdateTargetInfo` prefix, populating RNG/ALT/
  HDG/GRID/MODE from the manual hit point). Cosmetic polish for the native cockpit display; the
  external TGP feed is NOXMFD's primary surface and doesn't depend on this.
- **In-cockpit crosshair reticle.** Same reasoning — nice on the native MFD, not required for the
  external feed (the pointed-at spot is implicitly screen-center on the external picture too).
- **Manual IR toggle** (`ACT_FORCE_COL`, the `SwitchIRState` patch). NOXMFD's HQ overlay already has
  its own separate simulated-IR look (`docs/tgp-high-quality-mode.md`); native IR toggling from
  manual mode is a nice-to-have, not core to "point the camera and zoom."

## Open questions

- **Zoom limits.** `TargetCamControl` exposes `MinFOV`/`MaxFOV` as tunable config (defaults 0.25°/
  80°, i.e. up to ~40x). Decide whether NOXMFD exposes this as config or ships fixed limits for v1.
- **Where does the toggle status live?** Recommend a minimal read-only `MANUAL`/`AUTO` indicator on
  the TGP page itself (alongside the existing feed), not a TGP CFG button — this is a live,
  keybind-driven control (consistent with `docs/remote-keybinds.md`'s toggle-vs-set distinction),
  not a set-once preference, so it shouldn't live where the other CFG-page settings do. A reset/
  control hint (e.g. "TGP MANUAL — press RESET to recenter") is worth pairing with it so the state
  isn't silent.
- **Does `camTimeout` pinned high fight anything else?** `TgpFeed` doesn't read `camTimeout`
  directly today, but confirm no other system depends on it counting down normally while manual mode
  is engaged.

## v1 implementation shape

1. `TgpManualControl.cs` — state (`ManualMode`, pan/tilt direction, `DesiredFOV`), the reflection
   cache, engage/exit/reset, and the public `SetPan`/`SetZoom`/`Toggle`/`Reset` API (remote-ready
   per [above](#fit-with-noxmfds-existing-architecture), local-only wired for v1).
2. `HarmonyPatches.cs` — add the `TargetCam.Update`/`AimCamera`/`SwitchIRState` prefixes as three
   more nested classes, matching the file's existing one-class-per-patch convention.
3. `Keybinds.cs` — new KEY-page binds: toggle, reset, pan/tilt button pairs, pan/tilt axes,
   zoom in/out.
4. TGP page — minimal `MANUAL`/`AUTO` status indicator (see the toggle-placement open question
   above).
5. Wire every exit trigger from [Lifecycle](#lifecycle-every-exit-trigger): real lock, page close,
   aircraft loss, gear/landing-cam conflict (via the `currentMode == landingMode` check), toggle
   off.
6. Verify: Native and HQ quality, cockpit-hide ON and OFF, gear extend/retract and full landing
   while manual mode is active, a fast lock/unlock cycle (does auto-exit and auto-re-entry feel
   clean, not jittery), and the aircraft-relative-drift spike from
   [What v1 should probably skip](#what-v1-should-probably-skip).

## Out of scope

- Any weapon-lock or targeting integration — this is camera pointing only.
- In-cockpit info-panel patch and crosshair reticle (separable additions, listed above).
- The world-hit raycast/relock system is **not** flatly out of scope — see
  [What v1 should probably skip](#what-v1-should-probably-skip): build without it first, then
  decide from the spike result.
- Manual IR toggling.
- Remote-keybind wiring for TGP pointing — the command surface is designed for it (see
  [above](#fit-with-noxmfds-existing-architecture)), but only local KEY binds ship in v1.
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
