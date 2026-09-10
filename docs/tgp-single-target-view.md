# TGP VIEW toggle: WTV / STV, and the real-lock zoom override

[Issue #81](https://github.com/roke77/NOXMFD/issues/81) (WTV/STV) and
[issue #83](https://github.com/roke77/NOXMFD/issues/83) (Z+/Z− during a real lock) — two features
sharing one Harmony postfix on `TargetCam.SetTargetCam`, covered in one doc since #83 changed that
postfix's own shape.

## Goal

A new TGP page toggle, `WTV` (wide target view, default) / `STV` (single target view), controlling
how the TGP camera frames a lock of 2+ targets:

- **WTV (default)** — the game's own unchanged behavior: a single lock zooms/points at it, a
  multi-target lock zooms out wide enough to keep every locked target in frame.
- **STV** — with 0-1 locked targets, identical to WTV (there's nothing to reframe: the native
  single-target framing already does what STV asks for). With 2+ locked targets, the camera
  instead frames just the target currently focused by TGT's Next/Previous Target feature
  (`TargetFocus.Id`, `docs/tgt-cycle-focus.md`, issue #62) — so stepping Next/Prev while in STV
  visibly moves the TGP camera to follow, since both read the same shared focus id.

Lock boxes (`TgpOverlay`/`TelemetryJson.cs`'s `TgpBlock.boxes`) are unaffected either way — every
locked target still gets a box; only the camera's own point/zoom framing changes.

## Why a Harmony postfix, not a parameter

`TgpFeed.CaptureFrame()` (the plugin's own per-tick capture loop) already calls the game's public
`TargetCam.SetTargetCam()` once a real lock exists, exactly as it did before this feature — that
call is what produces WTV's own default framing, and STV needs to leave it untouched when it
doesn't apply (0-1 targets, or `ManualMode` on). The actual per-target aiming math lives entirely
inside `SetTargetCam()`'s own private call graph — `GetPositionAndSize`, and the two branches it
picks between, `SingleTargetPositionAndSize`/`MultipleTargetPositionAndSize`, deciding by list
count — with no public hook to steer with a plain parameter or override list.

`TgpSingleTargetView.cs` reuses this instead of reimplementing it, via `TgpLockCameraAccess.cs` —
a small reflection cache (the same "cached `FieldInfo`/`MethodInfo`, rate-limited failure logging"
shape as `TgpManualTargetCamAccess.cs`, split out separately because it covers the opposite case: a
*real* lock, not manual control) exposing `SingleTargetPositionAndSize`, `targetPosition`/
`targetFOV`, and `AimCamera()`. `ApplyIfActive(TargetCam)`, when `Stv` is on and 2+ targets are
actually locked:

1. Resolves the focused `Unit` via `TargetUnitLookup.TryResolve(TargetFocus.Id, out Unit)` (the
   same helper `WeaponSelectors.FireSingleAtFocused` and `HudFocusMark` already use for "the one
   locked target the pilot means right now"), and confirms it's still in the current lock list.
2. Calls `TgpLockCameraAccess.TryComputeSingleTargetFraming` — the private
   `SingleTargetPositionAndSize(List<Unit>, out GlobalPosition, out float)` with a **synthetic
   one-target list** containing just the focused `Unit` — the exact same private method the native
   single-lock path itself uses. Its own second out param is not a FOV, it's the target's raw
   `definition.length` in meters; `TryComputeSingleTargetFraming` applies the same
   `Clamp(size * 75f / targetDist, 0.25f, 20f)` conversion `SetTargetCam()` itself applies right
   after calling it (`_scratch/full/TargetCam.cs`), so the result is pixel-identical to "only the
   focused target was ever locked," not just position-identical with a garbage FOV. A reused
   scratch `List<Unit>` (cleared and re-added each call, `WeaponSelectors._loadout`'s own
   precedent) avoids a per-tick allocation.
3. Writes the result into `TargetCam`'s own private `targetPosition`/`targetFOV` fields (the same
   two fields `SetTargetCam()` itself just set, via `SingleTargetPositionAndSize`'s own branch).

It does **not** call `AimCamera()` itself — see [Z+/Z− during a real lock](#zz-during-a-real-lock)
below for why that now lives one level up, in `HarmonyPatches.cs`.

`ManualMode` is checked first and short-circuits the whole postfix: manual control has no real lock
to reframe at all (`TgpManualControl.Tick()` drives the camera directly every frame instead), so it
would otherwise be fighting the exact `AimCamera()` gate `TargetCam_AimCamera_ManualGate` already
exists to protect.

## Commands and telemetry

- `tgp.view.set { on }` (`CommandDispatcher.cs`) → `TgpSingleTargetView.SetStv(on)` — explicit-state,
  same shape as `tgp.ir.set` (`docs/tgp-manual-control.md`): `on:true` = STV, `on:false` = WTV.
- **Toggle View** keybind (`Keybinds.cs`'s `tgp-view-toggle`, TGP Keybinds group) → blind flip via
  `TgpSingleTargetView.ToggleStv()` — same shape as Manual Control Toggle/Toggle IR, added because a
  keybind (unlike a remote browser sending `tgp.view.set`) can't read current state to send the
  opposite of. `tgp.view-toggle` is its `CommandDispatcher.cs`/remote-keybind twin
  (`remote-keybinds.js`), same pairing Manual Control Toggle/Toggle IR already have.
- `TelemetrySnapshot.TgpStv` (set from `TgpSingleTargetView.Stv` in `TelemetryReader.cs`) rides the
  SSE frame as a **top-level** `tgpStv` field (`TelemetryJson.cs`'s `AppendFrameHeader`), the same
  "outside the `tgp` block, so it's visible even with `{cnt:0}`" shape `tgpManual` already uses —
  `TgpBlock` itself also grew a `stv` field for completeness, but the NAV highlight reads the
  top-level one so `WTV`/`STV` stay lit correctly with no feed at all, exactly like `MAN` does.
  `telemetry-source.js` forwards it to both shells as `stv` on the `'tgp'` slice
  (`{ type:'tgp', ..., manual, stv }`).

## NAV placement

`WTV`/`STV` took over the TGP page's old `LCK`/`MAN` pair slot and `MODE` decorator (renamed
`VIEW`) — see `docs/tgp-manual-control.md`'s "Layout placement" section for the full before/after
across the classic bezel (full view and split pane) and the F-35 glass, and why `MAN` moved rather
than being removed.

## Z+/Z− during a real lock

The game auto-computes `targetFOV` every tick `SetTargetCam()` runs — tight on one target, wide
enough to fit several — with no public hook to adjust it, the same "no parameter, only a private
call graph" gap WTV/STV hit above. `TgpLockZoom.cs` lets the TGP page's own Z+/Z− bezel buttons
(already on the nav row, no placement change needed) nudge it anyway, through the same fixed
magnification ladder (`TgpManualAimMath.ZoomLevelsMag`) manual control's own Z+/Z− already step
through — `MinFov`/`MaxFov` promoted from `private` to `internal` on `TgpManualControl` so both
share the exact same clamp range (the same physical camera's FOV bounds either way).

- **No override until the pilot presses Z+/Z− at least once.** A fresh lock always starts at
  whatever the native call (or WTV/STV's own reframe) computed, untouched — matching "the default
  zoom level is set by the game" from the original ask.
- **The first press seeds its own starting level from the live `targetFOV`** at that moment (via
  `TgpLockCameraAccess.GetTargetFov`) rather than some fixed starting point, so it always feels
  like "one notch from wherever the picture already is" — including a picture WTV/STV already
  reframed, if STV is on.
- **`TgpLockZoom.Tick()`** (`TelemetryReader.Update`, called every frame regardless of TGP
  camera/page state) watches `TargetFocus.Id` for a 0-to-locked transition — the same "nothing
  focused" signal `TargetFocus.cs` itself defines — and clears the override right as a new lock
  begins, so the *next* lock again starts at the game's own default.
- `tgp.zoom.step { index }` (`CommandDispatcher.cs`, the existing Z+/Z− command) now routes by
  mode: `TgpManualControl.StepZoom` while `ManualMode` is on, `TgpLockZoom.StepZoom` otherwise —
  same wire shape, no web-side change needed since the buttons already sent this command
  unconditionally (the server-side handler was the only thing that no-op'd outside manual mode).

### Why the postfix applies both overrides together

Splitting WTV/STV and the zoom override into two independent `SetTargetCam` postfixes seemed
natural at first, but both **write `targetFOV` wholesale**, not a relative nudge — and Harmony
doesn't guarantee which of two postfixes on the same method runs first. If STV's reframe ran after
the zoom postfix, it would silently overwrite the pilot's chosen zoom with its own auto-computed
value the very next tick. So `HarmonyPatches.cs` has one postfix
(`TargetCam_SetTargetCam_LockCameraOverrides`) that applies them in an explicit, deterministic
order — `TgpSingleTargetView.ApplyIfActive` first, then `TgpLockZoom.ApplyIfActive` on top of
whatever that left — and calls `TgpLockCameraAccess.InvokeAimCamera` once at the end, only if
either actually changed something, rather than each feature invoking it independently.

## Testing

Both features' own reflection/Harmony plumbing (`TgpSingleTargetView`, `TgpLockZoom`,
`TgpLockCameraAccess`) is Unity-coupled the same way `TgpManualControl`/`TgpFeed` already are, so —
like them — none of it is unit tested directly; only in game. `TgpLockZoom` reuses
`TgpManualAimMath.NextZoomLevelMag`, already covered by manual control's own tests, for its actual
step math. The pure client-side highlight rule (`tgpMarks`'s `wtv`/`stv` fields) is covered by
`tgp-marks.test.js`, and the SSE wiring (`tgpStv` → `'tgp'` slice's `stv`) by
`telemetry-source.test.js`.
