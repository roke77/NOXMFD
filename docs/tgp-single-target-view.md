# TGP VIEW toggle: WTV / STV

[Issue #81](https://github.com/roke77/NOXMFD/issues/81).

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

`TgpSingleTargetView.cs` reuses this instead of reimplementing it: a
`[HarmonyPatch(typeof(TargetCam), "SetTargetCam")]` **postfix**
(`TargetCam_SetTargetCam_SingleTargetViewOverride`, `HarmonyPatches.cs`, alongside its existing
`TargetCam_SetTargetCam_IrOverride` neighbor — same shape, same target method) lets the native call
run first (producing WTV's wide framing), then — only when `Stv` is on and 2+ targets are actually
locked — reflectively:

1. Resolves the focused `Unit` via `TargetUnitLookup.TryResolve(TargetFocus.Id, out Unit)` (the
   same helper `WeaponSelectors.FireSingleAtFocused` and `HudFocusMark` already use for "the one
   locked target the pilot means right now"), and confirms it's still in the current lock list.
2. Calls the private `SingleTargetPositionAndSize(List<Unit>, out GlobalPosition, out float)` with
   a **synthetic one-target list** containing just the focused `Unit` — the exact same private
   method the native single-lock path itself uses, so the result is pixel-identical to "only the
   focused target was ever locked." A reused scratch `List<Unit>` (cleared and re-added each call,
   `WeaponSelectors._loadout`'s own precedent) avoids a per-tick allocation.
3. Writes the result into `TargetCam`'s own private `targetPosition`/`targetFOV` fields (the same
   two fields `SetTargetCam()` itself just set, via `SingleTargetPositionAndSize`'s own branch).
4. Immediately re-invokes the private `AimCamera()` so the reframe applies this same tick, instead
   of lagging a frame behind whatever next calls it (`Update()`'s own tick, gated off entirely
   during `ManualMode` by the neighboring `TargetCam_AimCamera_ManualGate` patch).

The one-time reflection lookup (`Ensure()`) is cached the same way `TgpManualTargetCamAccess`
caches its own; a failed lookup logs once and leaves STV a permanent no-op rather than throwing
every tick.

`ManualMode` is checked first and short-circuits the whole thing: manual control has no real lock
to reframe at all (`TgpManualControl.Tick()` drives the camera directly every frame instead), so
this postfix would otherwise be fighting the exact `AimCamera()` gate `TargetCam_AimCamera_ManualGate`
already exists to protect.

## Commands and telemetry

- `tgp.view.set { on }` (`CommandDispatcher.cs`) → `TgpSingleTargetView.SetStv(on)` — explicit-state,
  same shape as `tgp.ir.set` (`docs/tgp-manual-control.md`): `on:true` = STV, `on:false` = WTV.
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

## Testing

`TgpSingleTargetView`'s own reflection/Harmony plumbing is Unity-coupled the same way
`TgpManualControl`/`TgpFeed` already are, so — like them — it isn't unit tested directly; only in
game. The pure client-side highlight rule (`tgpMarks`'s `wtv`/`stv` fields) is covered by
`tgp-marks.test.js`, and the SSE wiring (`tgpStv` → `'tgp'` slice's `stv`) by
`telemetry-source.test.js`.
