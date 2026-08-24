# TGP — suppress native render while HQ is active (planning)

## Status

**Planning.** Not started. Requested after `docs/tgp-high-quality-mode.md` shipped, as a follow-up
that only makes sense once HQ mode exists.

## Goal

When TGP quality is **HQ**, the game keeps rendering the native in-cockpit `TargetCam` picture at
the same time NOXMFD's `TgpMirrorCam` renders the external feed — two cameras, two pictures, of
the same target. Let the player suppress the native one while HQ is active:

- **Primarily cosmetic.** A player who only looks at the external HQ feed (a second monitor, a
  tablet MFD) doesn't want the in-cockpit screen showing a different, lower-quality picture at the
  same time. This is the main ask — ship it even if the perf win turns out to be negligible.
- **Secondarily, a small perf win, if it's real.** Skipping a render pass should cost less GPU
  time. "If possible" — this document's job is to find out how much, and whether it can be done
  without side effects, before promising it.

Opt-in, TGP CFG page, next to the existing quality picker. Off by default — suppressing the
cockpit screen is a bigger behavioral change than picking HQ itself, and shouldn't surprise anyone
who didn't ask for it.

## The complication: NOXMFD already drives the thing it would suppress

This isn't a fresh camera to gate — `TgpFeed.CaptureFrame()` (`src/plugin/TgpFeed.cs:113-133`)
already touches `TargetCam` every tick, for reasons unrelated to this feature:

```csharp
if (targets != null && targets.Count > 0)
{
    try { tc.SetTargetCam(); }   // keeps the native cam's camTimeout alive
    ...
}
Camera? cam = _camField.GetValue(tc) as Camera;
if (cam == null || !cam.enabled) { ClearFeed(); return; }
```

`SetTargetCam()` is what keeps `cam.enabled` true while a target is locked — NOXMFD calls it
every capture tick specifically so the mirror cam (in HQ mode) still gets a live FOV/mount to
copy from `SyncFromSource(cam)`. **Confirmed against the decompiled source
(`_scratch/full/TargetCam.cs:289-332`):** `SetTargetCam()` gates its whole mount/FOV/position/mode
reset block on `if (!cam.enabled)`:

```csharp
// TargetCam.SetTargetCam(), decompiled
if (!cam.enabled)
{
    currentMount = camMountForward;
    cam.transform.parent = camMountForward;
    cam.transform.localEulerAngles = Vector3.zero;
    cam.transform.localPosition = Vector3.zero;
    cam.fieldOfView = 10f;
    cam.nearClipPlane = 2f;
    cam.farClipPlane = 60000f;
    currentMode = CamMode.targetForward;
    cam.enabled = true;
    ...
}
camTimeout = 3f;
...
```

So this isn't a race that might lose — it's a guaranteed hit. If `TgpFeed` leaves `cam.enabled ==
false` from the previous tick's suppression and then calls `SetTargetCam()`, the game snaps the
mount back to `camMountForward`, resets FOV to `10f`, and re-enables the camera — every tick,
whether or not the mount/FOV actually needs resetting. `TgpMirrorCam.SyncFromSource(cam)` then
copies that clobbered FOV, so the HQ mirror's zoom-on-target behavior would visibly break (FOV
pinned near 10° instead of tracking the target) the moment suppression is turned on. This is not
an unverified risk anymore — it is the implementation shape the feature must use:

**Restore-before-drive, every tick:**

1. If native render was suppressed last tick, set `cam.enabled = true` again *before* calling
   `SetTargetCam()` — so the `!cam.enabled` branch doesn't fire and nothing gets reset.
2. Call `SetTargetCam()` as today; target state, FOV, mount, IR, and `camTimeout` update normally.
3. `SyncFromSource(cam)` for the mirror cam, as today — now reading the real, undisturbed FOV.
4. Re-suppress: `cam.enabled = false`, after the game's own render pass for this frame (see the
   ordering note below), so the frozen frame stays current with the state that was just computed.
5. Restore `cam.enabled = true` permanently on every exit path: quality switches back to Native,
   the toggle is switched off, no subscribers, no aircraft, `cam`/`TargetCam` gone, or plugin
   `OnDestroy` — same discipline `TgpMirrorCam.Disengage()` already follows for the mirror rig.

Step 4's "after the game's own render pass" is itself unverified — `TgpFeed.CaptureFrame()` runs
on `Update()`, and whether toggling `enabled` there lands before or after Unity's render for that
frame determines whether *this* frame or *next* frame is the one suppressed. Off-by-one-frame
either way is harmless for a cosmetic freeze; it only matters if it causes flicker (enabled for a
half-frame, visible as a flash) — verify visually once built.

## `UICam`: the overlay camera, not just `cam`

The native feed the pilot sees in-cockpit is not one camera — `TargetCam` creates **two** on
`Initialize()` (`TargetCam.cs:127-129`):

```csharp
Camera[] componentsInChildren = UnityEngine.Object.Instantiate(GameAssets.i.targetCam, currentMount).GetComponentsInChildren<Camera>();
cam   = componentsInChildren[0];   // scene camera — what TgpFeed reflects into today
UICam = componentsInChildren[1];   // overlay camera — the targeting reticle/box drawn on screen
```

`TgpFeed` only ever reflects into `cam` (`_camField`) — it has no `UICam` field at all, so today's
Native capture path only ever reads the scene picture, never the overlay. That's fine for
capture (the overlay is redrawn client-side from `TgpOverlay` data instead, per
`docs/tgp-high-quality-mode.md`'s "What actually shipped"), but it means this feature's own
`_camField`-only view of the world is incomplete for *suppression*: disabling `cam` alone may
still leave `UICam` rendering the in-cockpit reticle over a frozen/stale scene, or may not — URP
camera stacking usually drives overlay cameras through their base camera's own render call, which
would mean disabling `cam` silently stops `UICam` too, with no separate action needed. This is
plausible from the stacking pattern but **not confirmed** by anything read so far, and is exactly
the kind of assumption that caused the swap-based approach's own overlay-positioning bug
(`docs/tgp-high-quality-mode.md`'s "Why this is a separate planning doc"). Add a
`_uiCamField` reflection lookup alongside `_camField` during implementation, and verify in-cockpit
whether `UICam` needs its own suppress/restore step or genuinely rides along with `cam`'s.

## Camera safety

`docs/tgp-high-quality-mode.md` already surfaces the reference mod's `CAMERA_SAFETY.md`: don't
touch `CameraStateManager.mainCamera`, `cameraPivot`, or `cameraMode`, and don't Harmony-block
`CameraBaseState.UpdateState`. `TargetCam.cam` is not in that forbidden list — it's the same
camera `TgpFeed` already reflects into for the Native capture path — so toggling its `enabled`
flag is a smaller intervention than anything that doc warns against. Still: **don't disable the
`TargetCam` component itself**, only its `cam`'s render. `TargetCam` also owns target-lock state,
zoom, mount selection, and IR mode, all of which the overlay and the mirror cam depend on and must
keep working exactly as today.

## What "suppressed" should mean

The camera's last-rendered frame stays in its `RenderTexture` until something renders into it
again — `cam.enabled = false` freezes the cockpit screen on whatever it last showed, it doesn't
blank it. That's likely fine (arguably better — a frozen "last real picture" reads less like a
bug than a blank panel), but confirm it looks acceptable in-cockpit before treating it as
final; a static placeholder texture is a fallback if the frozen frame looks broken (e.g. mid-pan
motion blur baked in).

## Toggle wiring

Follows `RatesConfig`'s existing shape exactly (`src/plugin/RatesConfig.cs`) — a `ConfigEntry`,
a setter, live-apply on bind:

```csharp
private static ConfigEntry<bool>? _tgpSuppressNative;
public static bool TgpSuppressNative => _tgpSuppressNative?.Value ?? false;

public static void SetTgpSuppressNative(bool on)
{
    if (_tgpSuppressNative != null) _tgpSuppressNative.Value = on;
    TgpFeed.SuppressNativeInHq = on;
}
```

Bound in the same `"Refresh Rates"` section, hidden from F1 like its siblings. `rates.set` gets
one more `group`, matching the `"tgpQuality"` pattern in `CommandDispatcher.cs:74-81`:

```csharp
else if (e.group == "tgpSuppressNative") RatesConfig.SetTgpSuppressNative(e.wname == "on");
```

`/rates-config`'s response payload gains one more field (`tgpSuppressNative: bool`) alongside
`tgpQuality`, read by `tgpcfg.js` on load like the others.

### TGP CFG page

A third control under the existing HQ quality row in `src/web/pages/tgpcfg/tgpcfg.html` — a
checkbox or small toggle button, **disabled/hidden while quality is NATIVE** (it has no effect
there, so don't offer it — mirrors how the HQ warning banner already only shows for `quality !==
'native'` in `tgpcfg.js:44`). Label something like "HIDE COCKPIT FEED WHILE HQ" with a one-line
description: freezes the in-cockpit TGP screen while HQ is active, so you're not watching two
different pictures at once.

## Implementation sketch

1. `TgpFeed` gets `internal static bool SuppressNativeInHq;` (same pattern as `Quality`), plus a
   private `bool _nativeSuppressed;` to track whether *this instance* currently has `cam`/`UICam`
   disabled — needed because step 2 below must know whether to restore before driving.
2. In `CaptureFrame()`, **before** the `targets.Count > 0` block that calls `SetTargetCam()`: if
   `_nativeSuppressed`, set `cam.enabled = true` (and `UICam.enabled = true`, pending the `UICam`
   open question above) so `SetTargetCam()`'s `!cam.enabled` branch doesn't fire and clobber
   mount/FOV. Then call `SetTargetCam()` as today.
3. After `SyncFromSource(cam)` (mirror cam) and the overlay `Populate()` call, if
   `Quality == TgpQuality.HighQuality && SuppressNativeInHq`: set `cam.enabled = false` (+`UICam`
   if needed), `_nativeSuppressed = true`. Otherwise `_nativeSuppressed = false` and leave both
   enabled.
4. Restore on every exit path — `ClearFeed()`, `Disengage()`, and the toggle/quality setters
   themselves (`RatesConfig.SetTgpSuppressNative`/`SetTgpQuality` forwarding into `TgpFeed`) — set
   `cam.enabled = true` (+`UICam`) and `_nativeSuppressed = false` unconditionally. This must not
   depend on another `CaptureFrame()` tick running afterward (e.g. mission exit, plugin destroy,
   or the player un-locking a target all stop ticking `SetTargetCam()`'s reset branch, so an
   exit-path restore is the only thing that un-freezes the screen in those cases).
5. Verify: does the toggle-off restore (step 4) itself trigger a visible mount/FOV snap, since
   `cam.enabled` was false and the next real `SetTargetCam()` call will hit the same `!cam.enabled`
   reset branch? Likely yes and likely fine (it's the same reset that already happens on a fresh
   lock today), but confirm it doesn't look like a glitch rather than a expected re-init.

## Open questions to settle before implementing

- **Does `UICam` need its own suppress/restore, or does disabling `cam` take it with it?**
  Resolved by inspection whether it's a genuine open question — check how `GameAssets.i.targetCam`
  wires its URP camera stack (does `cam` list `UICam` as a stacked overlay?) before assuming
  either answer; confirm live in-cockpit either way.
- **Frame-timing of the suppress/restore toggle** (implementation sketch step 3/5's ordering) —
  does flipping `cam.enabled` inside `Update()` suppress *this* frame or *next* frame, and does
  the toggle-off restore cause a visible mount/FOV snap? Cosmetic-only concerns, but worth an
  in-cockpit look before calling this done.
- **Is the perf win measurable at all?** Restore `PerfLog` (per `docs/tgp-high-quality-mode.md`'s
  own pre-flight note — it's the only instrument that answers this) and compare `frame(tgpOpen)`
  HQ-with-suppress vs HQ-without, both with a target locked. If the saving is in the noise, ship
  the toggle purely as the cosmetic feature it was asked for and say so plainly in the CFG page
  copy — don't oversell a perf benefit that isn't real.

## Fallback if the restore-before-drive pattern doesn't hold up

The design above (restore → drive → re-suppress, every tick) is the intended implementation, not
a maybe — the decompiled source confirms the naive "just set `cam.enabled = false` after HQ
capture" shape would fight `SetTargetCam()`'s own reset every tick, so that naive shape is already
ruled out, not a fallback to fall into accidentally. If the restore-before-drive pattern *itself*
still produces visible glitches once built (frame-timing flicker, a mount/FOV snap on toggle,
`UICam` not cooperating), the feature does not ship silently degraded to a no-op — **no feature
ships until a cosmetic-only alternative is found**, such as swapping the cockpit screen's
material/texture to a static placeholder instead of toggling the camera at all (parallel to the
"IR mode" section of `docs/tgp-high-quality-mode.md`, which chose a CPU post-process over fighting
the game's own camera-bound IR volume for the same kind of reason). Don't ship a version of this
toggle that quietly does nothing when switched on.

## Out of scope

- Native mode. This toggle has no meaning and no effect unless `Quality == HighQuality`.
- Suppressing the overlay data (`TgpOverlay`), target-lock state, zoom, or mount selection — none
  of that changes; only the native camera's own render output is affected.
- Any change to `TgpMirrorCam` or the HQ capture pipeline itself.
- Auto-enabling this based on framerate or any other heuristic — same reasoning
  `tgp-high-quality-mode.md` already gives for not auto-falling-back on quality: the player chooses
  explicitly.

## Pre-flight before implementing

- Read `docs/tgp-high-quality-mode.md` in full, especially "Camera safety constraints in this
  game" and "What actually shipped" — this feature only exists because HQ mode does, and inherits
  its constraints.
- Re-read `_scratch/full/TargetCam.cs`'s `Initialize()` and `SetTargetCam()` — already read once
  for this doc (see "The complication" and "`UICam`" above), but re-confirm against whatever
  version is on disk if time has passed, and check `GameAssets.i.targetCam`'s stack setup for the
  `UICam` question.
- Restore `src/plugin/PerfLog.cs` from history (same note `tgp-high-quality-mode.md` ends on) if
  the perf claim is going to be measured rather than assumed.
