# TGP - suppress native cockpit screen while external feed is active

## Status

**Implemented on `main`, with one known visual race still open.** Config,
TGP CFG UI, `/rates-config`, `rates.set`, and `TgpFeed` integration are built and have been
live-tested in LOW/native and HIGH/HQ modes (the two tiers that existed at the time — see
[`tgp-extended-quality.md`](tgp-extended-quality.md) for the later MID/HIGH split). The suppress
mechanism itself (`TargetCam.onCamToggle` -> `TacScreen`) doesn't branch on resolution at all, so
MID is expected to behave identically, but hasn't been separately spot-checked live.

Live testing rejected the first camera-toggle approach. Disabling `TargetCam.cam`/`UICam` made the
external HQ feed lose the game's normal target zoom/FOV tracking until another game UI path, such
as opening the in-game map, woke that state back up. Current implementation therefore leaves
`TargetCam.cam` and `UICam` enabled.

Live testing also rejected renderer/material approaches:

- `targetScreenRenderer.enabled = false` removed a front cockpit/airframe section on the T/A-30,
  because that renderer owns more than the display glass.
- A narrow `material.mainTexture` swap was not enough; the cockpit video stayed visible.
- Blanking every reported material texture slot overreached and blacked other cockpit display
  content.

Current implementation keeps the renderer, materials, and cameras enabled. It uses the game's own
`TargetCam.onCamToggle` event to tell `TacScreen` to hide only its `targetCamDisplay` overlay,
letting the cockpit MFD fall back to its normal radar/time content while the external feed still
syncs from the live TargetCam state. The hide is reasserted while an external TGP page is
subscribed, not only while a target is locked.

This should be treated as primarily cosmetic. It removes the duplicate moving cockpit TGP picture
while the external TGP feed is active, but it should not be sold as a meaningful GPU saving unless
future profiling proves this display-overlay suppression is measurable.

Known remaining issue: rapid target deselect/reselect can still flash the cockpit TGP overlay for a
brief instant. Reasserting suppression every frame, preserving suppression through capture cleanup,
and switching from a post-target-loss grace period to indefinite suppression while the external TGP
page is open did **not** fully eliminate the flash. This points to a game-side `TacScreen`/target
transition repaint or toggle happening after NOXMFD's per-frame suppression call. A complete fix
likely needs a deeper Harmony hook around the game's own show/toggle path rather than another
polling timer.

## Goal

When the external TGP feed is active, the game also shows the native in-cockpit `TargetCam` picture.
The player may want the external MFD to be the only visible TGP picture, whether the external feed
is LOW/native or HIGH/HQ.

Add an opt-in toggle on the TGP CFG page:

- Off by default.
- Available in both LOW/native and HIGH/HQ quality modes.
- Persisted through `RatesConfig`.
- Restored cleanly when the toggle is disabled, the TGP page closes, the aircraft changes, or the
  plugin shuts down.

## Why Not Disable The Camera?

`TgpFeed.CaptureFrame()` already drives the game's `TargetCam` each capture tick:

```csharp
if (targets != null && targets.Count > 0)
{
    tc.SetTargetCam();
}

Camera? cam = _camField.GetValue(tc) as Camera;
if (cam == null || !cam.enabled) { ClearFeed(); return; }
```

That is required even in HQ mode. `TgpMirrorCam.SyncFromSource(cam)` copies the native camera's
mount, FOV, clip planes, and orientation so the external feed tracks the same target state as the
game.

The decompiled `TargetCam.SetTargetCam()` resets mount/FOV/position/mode whenever `cam.enabled`
is false:

```csharp
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
}
camTimeout = 3f;
```

Live testing confirmed the practical consequence: toggling camera `enabled` interfered with the
game's own zoom/FOV behavior. That design is ruled out for this feature.

## Current Implementation Shape

`TgpFeed` now suppresses only the cockpit TargetCam overlay:

1. Reflect `TargetCam.cam` as before.
2. Reflect `TargetCam.targetScreenRenderer`.
3. Call `SetTargetCam()` while a target is locked, as before.
4. Sync `TgpMirrorCam` from the native camera, as before.
5. Populate the HQ overlay, as before.
6. If suppression is enabled and an external TGP page is subscribed, invoke
   `TargetCam.onCamToggle` with `{ enabled = false, camMode = targetForward }`.
7. Run that hide check every frame, separate from the configured TGP capture Hz, and keep
   reasserting it even when there is no locked target.
8. Capture cleanup may clear the external frame and overlay data, but does not restore the
   cockpit overlay while suppression is enabled; the suppression gate owns restore timing.
9. When suppression ends, invoke the same event with `enabled = true` so the cockpit TargetCam
   overlay can return if the target camera is still active.

The feature intentionally does **not** touch:

- `TargetCam.cam.enabled`
- `UICam.enabled`
- `Renderer.enabled`
- material textures
- camera render targets
- `TargetCam` component state
- target lock state
- zoom/FOV state
- mount selection
- IR mode
- `TgpMirrorCam`

## Toggle Wiring

`RatesConfig` owns the persisted option:

```csharp
private static ConfigEntry<bool>? _tgpSuppressNative;
public static bool TgpSuppressNative => _tgpSuppressNative?.Value ?? false;

public static void SetTgpSuppressNative(bool on)
{
    if (_tgpSuppressNative != null) _tgpSuppressNative.Value = on;
    TgpFeed.SuppressNativeDisplay = on;
}
```

`CommandDispatcher` handles:

```csharp
rates.set { group: "tgpSuppressNative", wname: "on" | "off", on: bool }
```

`/rates-config` includes:

```json
{
  "tgpSuppressNative": false
}
```

`tgpcfg` shows the control under the quality picker. It remains available for both LOW/native and
HIGH/HQ quality modes.

UI details captured during testing:

- `TGP - CAMERA FEED` was renamed to `TGP - REFRESH RATE`.
- `TGP - COCKPIT FEED` was renamed to `TGP - HIDE COCKPIT FEED`.
- The toggle row follows the KEY page's row shape: label/description on the left, toggle pushed
  right on the same line.
- The ON hover state keeps text readable on the green background.
- The toggle must not be hidden or disabled in LOW/native mode; suppression applies in both LOW and
  HIGH whenever it is ON and a TGP page is subscribed.

## Live Findings

### Rejected approaches

- **Disable `TargetCam.cam`/`UICam`: rejected.** It broke or desynced normal TGP zoom/FOV tracking
  in the external HQ feed. Opening the in-game map could make zoom start working again, which is a
  strong sign this path interferes with the game's own camera/UI state ordering.
- **Disable `targetScreenRenderer`: rejected.** On the T/A-30 it removed a whole visible forward
  cockpit/airframe section, not only the MFD glass.
- **Swap only `material.mainTexture`: rejected.** The cockpit TGP picture remained visible.
- **Blank all material texture slots: rejected.** It overreached and blacked unrelated cockpit
  display content.

### Confirmed working pieces

- Invoking `TargetCam.onCamToggle(false)` hides only the cockpit TargetCam overlay and lets the
  cockpit display show its normal radar/time content.
- Leaving `TargetCam.cam`, `UICam`, render targets, renderers, and materials alone preserves the
  external feed's target tracking and HQ mirror sync.
- The feature works in both LOW/native and HIGH/HQ modes.
- The external TGP feed continues to work while the cockpit overlay is hidden.
- The default radar/content fallback appears correctly when the cockpit TGP overlay is hidden.
  The radar sweep animation may be absent in that fallback during suppression, but that was judged
  acceptable for now.
- Build/deploy and browser-side page tests pass with the current implementation.

### Debugging lessons

- The first anti-flicker attempt used a `1.25s` post-target-loss hold. Logs showed the timer was
  being initialized, but `ClearFeed()` restored the cockpit overlay almost immediately with nearly
  the full hold still remaining.
- `ClearFeed()` was changed so capture cleanup can clear NOXMFD's published external frame and
  overlay data without also restoring the cockpit overlay while suppression is enabled.
- After that fix, logs confirmed the hold timer counted down correctly, but live testing still
  showed a brief flash on fast deselect/reselect.
- The current implementation removed the hold and instead keeps suppression active indefinitely
  while a TGP page is subscribed and the toggle is ON. Live testing still showed a flash, which
  strongly suggests the remaining issue is not ordinary NOXMFD cleanup timing.

### Remaining problem

Rapidly deselecting/reselecting targets can still produce a brief in-cockpit TGP flash. Since the
flash survives continuous per-frame suppression, the likely cause is that the game re-shows or
repaints `TacScreen.targetCamDisplay` during its own target transition after NOXMFD has already
sent the hide event for that frame.

Potential next investigation:

- Decompile the relevant `TacScreen` target-camera display update/toggle path.
- Identify the exact method that reacts to target selection/loss and re-enables the target-camera
  display.
- Consider a narrow Harmony postfix/prefix that prevents `targetCamDisplay` from being enabled
  while `TgpFeed.SuppressNativeDisplay && TelemetryServer.WantsTgpFrames`.
- Keep avoiding camera/renderer/material mutation unless a deeper inspection proves a safer,
  display-only target exists.

## Verification Checklist

- LOW/HIGH + suppression OFF: native cockpit screen and external feed behave as before.
- LOW/HIGH + suppression ON: cockpit display shows its normal radar/time content, external feed
  keeps normal target zoom/FOV.
- Toggle OFF while locked: cockpit screen returns cleanly.
- Switch quality while suppression is ON: cockpit suppression remains active.
- Close the external TGP page while suppression is ON: cockpit screen returns cleanly.
- Lose target while suppression is ON: cockpit TargetCam overlay remains hidden while the external
  TGP page is subscribed; native `camTimeout` behavior remains game-owned.
- Rapidly deselect/reselect targets while suppression is ON: known remaining problem. Brief cockpit
  TargetCam flash may still occur.
- No exceptions or noisy log spam in `Player.log`.

## Open Questions

- Does invoking `onCamToggle(false)` have any side effect beyond `TacScreen.targetCamDisplay` and
  the hidden time widget? Decompile says no for current `TacScreen`, but test across aircraft.
- Is there any measurable performance change? Because the cameras and renderer remain enabled,
  expect little or no GPU render-pass saving from this safe implementation.
- Can a narrow `TacScreen` hook eliminate the remaining fast deselect/reselect flash without
  touching camera state or cockpit renderers?

## Out Of Scope

- Native/LOW capture behavior beyond hiding the cockpit TargetCam overlay.
- Auto-enabling based on framerate or client count.
- Suppressing `TgpOverlay` data.
- Any new HQ capture pipeline changes.
- Camera-state interventions warned against by `docs/tgp-high-quality-mode.md`.
