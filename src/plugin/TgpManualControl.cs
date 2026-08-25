using System;
using System.Collections.Generic;
using System.Reflection;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace NOXMFD
{
    // Manual pan/tilt/zoom override of the game's own TargetCam (docs/tgp-manual-control.md).
    // Off by default, keybind-driven; auto-exits the instant a real target lock exists, the
    // aircraft is lost, or the native landing cam takes over (NOT on an external /tgp page
    // closing — removed after testing showed manual control is equally useful pointed at the
    // native in-cockpit MFD alone; see docs/tgp-manual-control.md's Status section).
    //
    // A static class (like Keybinds/RatesConfig), not an instance TgpFeed owns: it has its own
    // state machine (ManualMode, pan direction, desired FOV) and its own TargetCam reflection
    // cache, and TgpFeed only needs to know the camera is already enabled — which manual engage
    // arranges by calling the same public TargetCam.SetTargetCam() TgpFeed itself calls on a
    // real lock. No coupling between the two beyond that shared camera.
    //
    // Ported from github.com/9138noms/TargetCamControl's Runner.cs (full source reviewed), with one
    // deliberate departure: Area Track (free aiming, no Point Track lock) stores its aim as an
    // offset from the aircraft's OWN forward (aircraft-local, not world-space) so a centered camera
    // turns with the airframe as it banks/turns, rather than staying pinned to a frozen world
    // bearing — see _localPanDir. Point Track ignores that offset entirely; it drives the world-
    // space _panDir directly from a locked world point instead.
    internal static class TgpManualControl
    {
        // Matches TargetCam.SetTargetCam()'s own targetFOV clamp range — the native camera never
        // goes tighter/wider than this, so reusing it needs no new config for v1.
        private const float MinFov = 0.25f;
        private const float MaxFov = 20f;
        private const float ZoomRateFovPerSec = 6f;
        // How far a bound zoom axis has to move (in its own -1..1 units) before it's treated as
        // "the pilot actually moved it" and reclaims authority from Zoom In/Out — see Tick()'s
        // axisMoved check. Small enough to catch a deliberate nudge, bigger than float noise.
        private const float ZoomAxisMoveEpsilon = 0.01f;
        private const float PanSpeedDegPerSec = 60f;
        private const float TiltSpeedDegPerSec = 45f;
        private const float MaxElevationDeg = 85f;          // stay short of straight up/down — LookRotation degrades near the poles

        internal static bool ManualMode { get; private set; }

        // How far a Point Track raycast reaches — matches AimCamera's own far clip plane (60000f)
        // in the decompile, so a locked point can't sit further out than the camera could ever draw it.
        private const float PointTrackRayDistance = 60000f;
        // World-geometry-only layer mask, matching the game's own spectator camera collision
        // checks (_scratch/full/CameraOrbitState.cs/CameraChaseState.cs/CameraControlledState.cs
        // all use this exact literal for a Physics.Linecast against terrain/scenery). Without it, a
        // maskless Physics.Raycast from the TargetCam mount — sitting inside/near the fuselage —
        // immediately self-hits the aircraft's own collider at point-blank range: the ray "hits"
        // a few meters away, often behind or into the airframe, which is exactly what read as
        // "Point Track snaps to face backward into the aircraft."
        private const int WorldGeometryLayerMask = 64;
        // Bigger than ReadAxis's own 0.03 deadzone (Keybinds.cs) — that one's tuned for a
        // self-centering cursor stick, not for gating something as expensive/disruptive as a
        // redesignate raycast. A physical axis resting a few percent off dead center will still
        // pass 0.03 continuously, which re-raycasts and re-locks Point Track's point every single
        // tick — a tiny, imperceptible aim change is enough for the ray to land on a different
        // nearby surface feature each time, which reads as trembling between two close points.
        private const float PointTrackNudgeThreshold = 0.15f;

        private static Vector3 _panDir = Vector3.forward;   // world-space, unit length — what mount.rotation is actually set from
        // Area Track's real state: an offset from the aircraft's OWN forward, expressed in the
        // aircraft's local space (local (0,0,1) = boresight, dead ahead). _panDir above is derived
        // from this every tick via aircraft.transform.TransformDirection — that's what makes a
        // centered/reset camera turn WITH the airframe instead of staying pinned to a frozen world
        // bearing. Point Track ignores this entirely; it drives _panDir directly from the tracked
        // world point instead (see Tick()).
        private static Vector3 _localPanDir = Vector3.forward;
        private static float   _desiredFov = MaxFov;
        private static float   _panInputX, _panInputY;      // held state, written every frame by Keybinds.Poll
        private static bool    _zoomInHeld, _zoomOutHeld;
        private static float?  _zoomAxisValue;               // non-null while a calibrated zoom axis is bound — absolute, overrides in/out
        private static float?  _zoomAxisAppliedValue;         // the axis value FOV was last set from — compared each tick to detect real movement

        // Point Track (docs/tgp-manual-control.md's "world-hit raycasting" — real TGP pods call
        // this mode "Point Track" vs. free "Area Track" slewing). A raycast hit is stored as a
        // GlobalPosition (not a raw Vector3) so it survives the engine's floating-origin rebase.
        //
        // _panDir while tracking = _pointTrackBaseline rotated by the (_pointTrackOffsetAz,
        // _pointTrackOffsetEl) offset — two INDEPENDENT quantities, not one fought over by two
        // writers. Baseline recomputes directly toward _trackedPoint every tick, unconditionally
        // (this is what counters the aircraft's own translation/rotation — matches TargetCam's own
        // AimCamera(): a direct Quaternion.LookRotation at the target, no rate limit). The offset
        // only changes from nudge input. Coupling these into a single variable (an earlier version
        // did: correction and nudge both writing the same field) either let the correction cancel
        // the nudge every tick (stuck, couldn't pan) or let the nudge run with no correction at all
        // (drifted with the aircraft) depending on which one "won" — decoupling them fixes both.
        private static bool           _pointTrackActive;
        private static GlobalPosition _trackedPoint;
        private static Vector3        _pointTrackBaseline = Vector3.forward;
        private static float          _pointTrackOffsetAz, _pointTrackOffsetEl;
        private static bool           _wasNudgingPointTrack;   // true on a tick where nudge input was above threshold — redesignate fires the tick AFTER this drops back to false

        // Throttled diagnostic log (docs/tgp-manual-control.md testing) — az/el/FOV/Point Track
        // state/raw pan input every ~1s while manual mode is on, so pan/tilt/zoom/Point Track
        // behavior can be checked against the BepInEx log instead of only by eye in-game.
        private const float DiagLogInterval = 1f;
        private static float _diagTimer;

        private static bool      _reflectionTried;
        private static FieldInfo? _camField;
        private static FieldInfo? _currentMountField;
        private static FieldInfo? _currentModeField;
        private static FieldInfo? _canvasObjectLandingField;
        private static FieldInfo? _camTimeoutField;
        private static MethodInfo? _switchIrStateMethod;

        // ── Input (Keybinds.Poll calls these every frame, remote-ready per docs/tgp-manual-
        // control.md — CommandDispatcher can call the same API from a network command later) ────
        internal static void SetPan(float x, float y)
        {
            _panInputX = Mathf.Clamp(x, -1f, 1f);
            _panInputY = Mathf.Clamp(y, -1f, 1f);
        }

        // dir: +1 = zoom in (narrower FOV), -1 = zoom out. Mirrors the remote fire.set shape
        // ({group, on}) so a future remote zoom command needs no new dispatch pattern. Ignored
        // while a calibrated zoom axis is bound (SetZoomAxis) — the axis is authoritative then.
        internal static void SetZoom(int dir, bool on)
        {
            if (dir > 0) _zoomInHeld = on;
            else if (dir < 0) _zoomOutHeld = on;
        }

        // A calibrated physical axis (e.g. a HOTAS slider), driving zoom as an absolute position
        // rather than a rate: -1 = fully zoomed out (MaxFov), +1 = fully zoomed in (MinFov). Pass
        // null when the axis isn't bound, so Tick() falls back to the in/out held buttons above.
        internal static void SetZoomAxis(float? normalized) => _zoomAxisValue = normalized;

        internal static void Toggle()
        {
            if (ManualMode)
            {
                GameManager.GetLocalAircraft(out Aircraft ac);
                ExitManual(ac != null ? ac.targetCam : null, "player toggle");
                return;
            }

            if (!GameManager.GetLocalAircraft(out Aircraft aircraft) || aircraft == null)
            {
                Plugin.Log?.LogInfo("[NOXMFD] TGP manual control: no aircraft — ignored.");
                return;
            }
            TargetCam? tc = aircraft.targetCam;
            if (tc == null)
            {
                Plugin.Log?.LogInfo("[NOXMFD] TGP manual control: aircraft has no TargetCam — ignored.");
                return;
            }
            if (!EnsureReflection())
            {
                Plugin.Log?.LogWarning("[NOXMFD] TGP manual control: could not locate TargetCam fields — feature disabled.");
                return;
            }
            Engage(tc, aircraft);
        }

        internal static void Reset()
        {
            if (!ManualMode) return;
            GameManager.GetLocalAircraft(out Aircraft ac);
            if (ac == null)
            {
                Plugin.Log?.LogInfo("[NOXMFD] TGP manual control: reset ignored — no aircraft.");
                return;
            }
            _localPanDir = Vector3.forward;   // boresight — zero offset from the nose
            _panDir = ac.transform.forward;   // immediate value for the log line below; Tick() re-derives this from _localPanDir every frame regardless
            // Minimum zoom (widest FOV), not the mid-range default — after a reset the pilot has
            // lost track of where they're pointed, and a wide view re-establishes context fastest.
            _desiredFov = MaxFov;
            _pointTrackActive = false;

            // If a held pan/tilt key or a mis-centered axis is still feeding non-zero input right
            // now, the very next Tick() calls NudgeDirection and immediately rotates the aim away
            // from boresight — the reset would then look like it silently didn't take. Zeroing
            // here (same as Engage()) guarantees at least this frame sticks;
            // logging the values makes an ongoing offender (an axis resting off-center) visible
            // instead of just "reset sometimes doesn't work."
            if (_panInputX != 0f || _panInputY != 0f)
                Plugin.Log?.LogWarning($"[NOXMFD] TGP manual control: reset with non-zero pan input still active (x={_panInputX:0.00}, y={_panInputY:0.00}) — a held key or a mis-centered axis will immediately pan back off-center next tick.");
            _panInputX = _panInputY = 0f;

            (float az, float el) = ToAzimuthElevation(_panDir);
            Plugin.Log?.LogInfo($"[NOXMFD] TGP manual control: reset to aircraft-forward (az={az:0.0}, el={el:0.0}), minimum zoom.");
        }

        // Point Track (see the field comment above): raycast along the current aim and lock onto
        // whatever it hits. No-op (logged) if nothing's in range, or if manual mode is off. An
        // internal toggle: press again while already tracking to release back to free Area Track
        // slewing. Pan/tilt input while tracking does NOT release it — see Tick()'s handling.
        internal static void TogglePointTrack()
        {
            if (!ManualMode) return;
            GameManager.GetLocalAircraft(out Aircraft ac);
            if (_pointTrackActive)
            {
                _pointTrackActive = false;
                // Hand off to Area Track from wherever Point Track was actually looking, not
                // wherever _localPanDir was last set (before Point Track engaged) — otherwise the
                // camera would visibly snap back to that stale offset the instant it releases.
                if (ac != null) _localPanDir = ac.transform.InverseTransformDirection(_panDir).normalized;
                Plugin.Log?.LogInfo("[NOXMFD] TGP manual control: Point Track released.");
                return;
            }

            TargetCam? tc = ac != null ? ac.targetCam : null;
            if (tc == null || !EnsureReflection()) return;
            Transform? mount = _currentMountField!.GetValue(tc) as Transform;
            if (mount == null) return;

            if (Physics.Raycast(mount.position, _panDir, out RaycastHit hit, PointTrackRayDistance, WorldGeometryLayerMask))
            {
                _trackedPoint = hit.point.ToGlobalPosition();
                _pointTrackActive = true;
                _pointTrackBaseline = _panDir;
                _pointTrackOffsetAz = _pointTrackOffsetEl = 0f;
                _wasNudgingPointTrack = false;
                Plugin.Log?.LogInfo($"[NOXMFD] TGP manual control: Point Track locked at {hit.distance:0}m.");
            }
            else
            {
                Plugin.Log?.LogInfo("[NOXMFD] TGP manual control: Point Track found nothing to lock onto — ignored.");
            }
        }

        // Manual COLOR/IR toggle. AimCamera() normally decides this automatically by time-of-day/
        // distance (_scratch/full/TargetCam.cs), but that whole method is skipped while ManualMode is
        // on (TargetCam_AimCamera_ManualGate, HarmonyPatches.cs), so IRMode just freezes at whatever
        // it was on entry — nothing else drives it during manual mode, so there's no automatic logic
        // for this to fight. SwitchIRState itself is private but otherwise self-contained (sets a
        // bool, tweaks a post-process ColorAdjustments volume) and isn't patched by anything else.
        internal static void ToggleIR()
        {
            if (!ManualMode) return;
            GameManager.GetLocalAircraft(out Aircraft ac);
            TargetCam? tc = ac != null ? ac.targetCam : null;
            if (tc == null || !EnsureReflection() || _switchIrStateMethod == null) return;
            bool next = !tc.UsingIR();
            _switchIrStateMethod.Invoke(tc, new object[] { next });
            Plugin.Log?.LogInfo($"[NOXMFD] TGP manual control: IR {(next ? "ON" : "OFF")}.");
        }

        // Called every frame from TelemetryReader alongside TgpFeed.Tick — cheap no-op while off.
        internal static void Tick(float dt)
        {
            if (!ManualMode) return;

            // Every exit trigger (docs/tgp-manual-control.md "Lifecycle"), checked every tick. Not
            // gated on TelemetryServer.WantsTgpFrames (any external /tgp page being open) — manual
            // control drives the real TargetCam directly, the same component the native in-cockpit
            // TGP screen renders from, so it works with no browser page open at all. TgpFeed picks
            // up the same camera automatically once/if a page does connect (see TgpFeed.CaptureFrame
            // — it only skips its own SetTargetCam() call while !hasTargets, not capture itself).
            GameManager.GetLocalAircraft(out Aircraft ac);
            TargetCam? tc = ac != null ? ac.targetCam : null;
            if (ac == null || tc == null) { ExitManual(null, "aircraft lost"); return; }
            List<Unit>? targets = ac.weaponManager != null ? ac.weaponManager.GetTargetList() : null;
            if (targets != null && targets.Count > 0) { ExitManual(tc, "real target lock acquired"); return; }

            if (!EnsureReflection()) { ExitManual(tc, "TargetCam reflection failed"); return; }
            Camera? cam = _camField!.GetValue(tc) as Camera;
            Transform? mount = _currentMountField!.GetValue(tc) as Transform;
            if (cam == null || mount == null) { ExitManual(tc, "TargetCam/mount torn down"); return; }

            if (_currentModeField!.GetValue(tc) is TargetCam.CamMode mode && mode == TargetCam.CamMode.landingMode)
            {
                ExitManual(tc, "gear/landing-cam conflict");
                return;
            }

            // Point Track: aim = _pointTrackBaseline (aircraft-motion correction) rotated by the
            // (_pointTrackOffsetAz, _pointTrackOffsetEl) nudge offset — two independent writers,
            // not one field both fight over. See the field comment for why that decoupling matters
            // (two earlier versions each had this coupled, and each broke a different way: stuck
            // unable to pan, or drifting with the aircraft while nudging).
            if (_pointTrackActive)
            {
                bool nudging = Mathf.Abs(_panInputX) > PointTrackNudgeThreshold || Mathf.Abs(_panInputY) > PointTrackNudgeThreshold;

                // Baseline: DIRECT toward the locked point every tick, unconditionally, matching
                // the game's own AimCamera() (a plain Quaternion.LookRotation at the target, no
                // rate limit) — never touched by nudge input, so nudging can never fight it. A
                // near-overhead pass can still swing this fast (the same "keyhole" a real gimbal
                // has); that's a candidate for its own guard later if it reads as broken in play,
                // not something to fix by slowing down every normal tick's tracking.
                Vector3 toPoint = _trackedPoint.ToLocalPosition() - mount.position;
                if (toPoint.sqrMagnitude > 0.0001f) _pointTrackBaseline = toPoint.normalized;

                if (nudging)
                {
                    // Zoom-scaled (see ZoomScale) so the offset itself doesn't feel twitchy at
                    // high zoom — this is the "user-driven slew" case ZoomScale is actually for.
                    float scale = ZoomScale();
                    _pointTrackOffsetAz += _panInputX * PanSpeedDegPerSec * scale * dt;
                    _pointTrackOffsetEl = Mathf.Clamp(_pointTrackOffsetEl + _panInputY * TiltSpeedDegPerSec * scale * dt, -MaxElevationDeg, MaxElevationDeg);
                }
                else if (_wasNudgingPointTrack)
                {
                    // Just released — commit exactly one fresh raycast along the current combined
                    // aim (baseline+offset, i.e. _panDir as of last tick), not one every tick
                    // during the drag. Re-anchor the baseline to that exact direction and zero the
                    // offset so next tick starts clean — no leftover offset double-counted once the
                    // baseline itself already points there.
                    float prevDist = toPoint.magnitude;
                    Vector3 newLocalPoint = Physics.Raycast(mount.position, _panDir, out RaycastHit hit, PointTrackRayDistance, WorldGeometryLayerMask)
                        ? hit.point
                        : mount.position + _panDir * Mathf.Max(prevDist, 1f);
                    _trackedPoint = newLocalPoint.ToGlobalPosition();
                    _pointTrackBaseline = _panDir;
                    _pointTrackOffsetAz = _pointTrackOffsetEl = 0f;
                    Plugin.Log?.LogInfo($"[NOXMFD] TGP manual control: Point Track redesignated at drag release ({(newLocalPoint == hit.point ? hit.distance.ToString("0") + "m hit" : "extrapolated, no hit")}).");
                }
                _wasNudgingPointTrack = nudging;

                (float baseAz, float baseEl) = ToAzimuthElevation(_pointTrackBaseline);
                _panDir = FromAzimuthElevation(baseAz + _pointTrackOffsetAz, Mathf.Clamp(baseEl + _pointTrackOffsetEl, -MaxElevationDeg, MaxElevationDeg));

                Plugin.Log?.LogInfo($"[NOXMFD] TGP Point Track tick: nudging={nudging} panRaw=({_panInputX:0.000},{_panInputY:0.000}) offset=({_pointTrackOffsetAz:0.0},{_pointTrackOffsetEl:0.0}) baseAzEl=({baseAz:0.0},{baseEl:0.0}) panDirAzEl=({ToAzimuthElevation(_panDir).az:0.0},{ToAzimuthElevation(_panDir).el:0.0}).");
            }
            else
            {
                // Area Track: re-derive the world direction from the aircraft's CURRENT attitude
                // every tick, not just when there's pan/tilt input — this is what makes a
                // centered/reset camera turn WITH the airframe as it banks/turns, instead of
                // staying pinned to whatever world bearing happened to be forward at reset time.
                _localPanDir = NudgeDirection(_localPanDir, dt);
                _panDir = ac.transform.TransformDirection(_localPanDir).normalized;
            }
            mount.rotation = Quaternion.LookRotation(_panDir, Vector3.up);

            // Axis and buttons coexist by taking turns, not by one permanently overriding the
            // other: the axis is authoritative only on a tick where it's actually MOVED since the
            // last tick it was applied — a stationary slider stops claiming authority, so Zoom
            // In/Out work normally the rest of the time. Without this, a bound-but-untouched axis
            // silently ate every button press forever (its value never changes on its own, so it
            // "wins" every tick with a value that's just sitting there).
            bool axisMoved = _zoomAxisValue.HasValue &&
                (!_zoomAxisAppliedValue.HasValue || Mathf.Abs(_zoomAxisValue.Value - _zoomAxisAppliedValue.Value) > ZoomAxisMoveEpsilon);
            if (axisMoved)
            {
                // Direct, not rate-limited: a calibrated slider's whole point is that its physical
                // position IS the zoom level, matched instantly — that's what "calibrated to the
                // min and max values of the zoom" means. A rate cap was tried here to tame an
                // erratic-looking raw signal, but the evidence (BepInEx log: the axis value
                // dwelling at each extreme for a full second or more, not single-frame spikes)
                // doesn't match ordinary pot/slider noise a low-pass would fix — it looks like a
                // real signal from the wrong physical control. Smoothing just added lag without
                // addressing that; if it's still erratic, check the Zoom Axis row on /keybinds.
                float t = Mathf.Clamp01((_zoomAxisValue!.Value + 1f) * 0.5f);
                _desiredFov = Mathf.Lerp(MaxFov, MinFov, t);
                _zoomAxisAppliedValue = _zoomAxisValue.Value;
            }
            else
            {
                int zoomDir = (_zoomInHeld ? 1 : 0) - (_zoomOutHeld ? 1 : 0);
                _desiredFov = Mathf.Clamp(_desiredFov - zoomDir * ZoomRateFovPerSec * dt, MinFov, MaxFov);
            }
            cam.fieldOfView = _desiredFov;

            // Belt-and-suspenders: Update()'s own countdown never runs while ManualMode is true
            // (Harmony-gated, HarmonyPatches.cs), but pin it high anyway in case anything else
            // ever reads camTimeout directly.
            _camTimeoutField?.SetValue(tc, 99f);

            _diagTimer += dt;
            if (_diagTimer >= DiagLogInterval)
            {
                _diagTimer = 0f;
                (float az, float el) = ToAzimuthElevation(_panDir);
                string zoomAxisStr = _zoomAxisValue.HasValue ? _zoomAxisValue.Value.ToString("0.00") : "unbound";
                Plugin.Log?.LogInfo($"[NOXMFD] TGP manual control diag: az={az:0.0} el={el:0.0} fov={_desiredFov:0.00} pointTrack={_pointTrackActive} panIn=({_panInputX:0.00},{_panInputY:0.00}) zoomIn={_zoomInHeld} zoomOut={_zoomOutHeld} zoomAxis={zoomAxisStr}.");
            }
        }

        // Rotates an arbitrary direction by the current pan/tilt input: azimuth around its own
        // "up" axis from x, elevation (clamped short of the poles, where LookRotation degrades)
        // from y. Shared by Area Track (nudges _localPanDir, aircraft-relative) and Point Track's
        // redesignate (nudges _panDir, world-space) — same math, different frame of reference,
        // decided entirely by which field the caller passes in and writes back.
        private static Vector3 NudgeDirection(Vector3 dir, float dt)
        {
            if (_panInputX == 0f && _panInputY == 0f) return dir;
            float scale = ZoomScale();
            (float azimuth, float elevation) = ToAzimuthElevation(dir);
            azimuth += _panInputX * PanSpeedDegPerSec * scale * dt;
            elevation = Mathf.Clamp(elevation + _panInputY * TiltSpeedDegPerSec * scale * dt, -MaxElevationDeg, MaxElevationDeg);
            return FromAzimuthElevation(azimuth, elevation);
        }

        // World-angle-per-second rates (PanSpeedDegPerSec, TiltSpeedDegPerSec) are blind to how
        // much of the picture they actually sweep — at MinFov (~40x magnification, 0.25° wide)
        // even a fraction of a degree crosses a huge share of the visible frame, so a rate tuned
        // to feel right at wide FOV reads as violent shake once zoomed in ("the more zoom, the
        // more jumpy" — confirmed directly, not inferred). Scaling every user-driven angular rate by
        // current FOV / MaxFov keeps the picture-relative (on-screen) rate roughly constant across
        // the whole zoom range instead of the world-angular rate staying constant while its visual
        // effect balloons — the same reason real gimbal/TGP slew rate drops as magnification rises.
        private static float ZoomScale() => _desiredFov / MaxFov;

        private static (float az, float el) ToAzimuthElevation(Vector3 dir) =>
            (Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg, Mathf.Asin(Mathf.Clamp(dir.y, -1f, 1f)) * Mathf.Rad2Deg);

        private static Vector3 FromAzimuthElevation(float azimuthDeg, float elevationDeg)
        {
            float az = azimuthDeg * Mathf.Deg2Rad;
            float el = elevationDeg * Mathf.Deg2Rad;
            float cosEl = Mathf.Cos(el);
            return new Vector3(Mathf.Sin(az) * cosEl, Mathf.Sin(el), Mathf.Cos(az) * cosEl);
        }

        // Ordering matters: ManualMode must already be true before tc.SetTargetCam() is called,
        // or its own tail call to AimCamera() runs un-gated and snaps the mount toward whatever
        // an empty target list computes (docs/tgp-manual-control.md's reflection-surface note).
        private static void Engage(TargetCam tc, Aircraft aircraft)
        {
            ManualMode = true;

            if (_currentModeField!.GetValue(tc) is TargetCam.CamMode mode && mode == TargetCam.CamMode.landingMode)
                _currentModeField.SetValue(tc, TargetCam.CamMode.targetForward);
            if (_canvasObjectLandingField?.GetValue(tc) is GameObject landingCanvas && landingCanvas.activeSelf)
                landingCanvas.SetActive(false);

            Camera? cam = _camField!.GetValue(tc) as Camera;
            if (cam == null || !cam.enabled)
            {
                try { tc.SetTargetCam(); }
                catch (Exception ex)
                {
                    Plugin.Log?.LogWarning($"[NOXMFD] TGP manual control: engage SetTargetCam threw: {ex.Message}");
                    ManualMode = false;
                    return;
                }
                cam = _camField.GetValue(tc) as Camera;
            }
            if (cam == null) { ManualMode = false; return; }

            _camTimeoutField?.SetValue(tc, 99f);
            // Centered on the aircraft's nose and minimum zoom, not wherever the native camera
            // happened to leave the mount/FOV — every toggle-on starts from the same known state.
            _localPanDir = Vector3.forward;
            _panDir = aircraft.transform.forward;
            _desiredFov = MaxFov;
            _panInputX = _panInputY = 0f;
            _zoomInHeld = _zoomOutHeld = false;
            _zoomAxisAppliedValue = null;   // force a fresh sync to the axis's current position on the first tick, if one's bound
            _pointTrackActive = false;
            _diagTimer = 0f;

            Plugin.Log?.LogInfo("[NOXMFD] TGP manual control: ON (centered, minimum zoom).");
        }

        private static void ExitManual(TargetCam? tc, string reason)
        {
            if (!ManualMode) return;
            ManualMode = false;
            _pointTrackActive = false;
            if (tc != null && EnsureReflection())
            {
                Camera? cam = _camField!.GetValue(tc) as Camera;
                Transform? mount = _currentMountField!.GetValue(tc) as Transform;
                if (cam != null) cam.transform.localRotation = Quaternion.identity;
                if (mount != null) mount.localRotation = Quaternion.identity;
                try { tc.CancelTarget(); }
                catch (Exception ex) { Plugin.Log?.LogDebug($"[NOXMFD] TGP manual control: CancelTarget threw on exit: {ex.Message}"); }
            }
            Plugin.Log?.LogInfo($"[NOXMFD] TGP manual control: OFF ({reason}).");
        }

        // What the aim is currently pointed at (docs/tgp-manual-control.md's "In-cockpit overlay").
        // While Point Track is locked, reuses the already-tracked point directly — Tick() is already
        // recomputing toward it every frame, so a second raycast here would be redundant. Otherwise
        // (free Area Track), fires one fresh raycast along the current aim, same mask Point Track
        // itself uses to avoid self-hitting the fuselage. Called at whatever cadence the caller
        // needs (TargetScreenUI's own overlay ticks at 10 Hz) — cheap enough not to need throttling
        // of its own.
        private static bool TryGetLookPoint(Transform mount, out Vector3 hitPointLocal, out float rangeM)
        {
            if (_pointTrackActive)
            {
                hitPointLocal = _trackedPoint.ToLocalPosition();
                rangeM = (hitPointLocal - mount.position).magnitude;
                return true;
            }
            if (Physics.Raycast(mount.position, _panDir, out RaycastHit hit, PointTrackRayDistance, WorldGeometryLayerMask))
            {
                hitPointLocal = hit.point;
                rangeM = hit.distance;
                return true;
            }
            hitPointLocal = default;
            rangeM = 0f;
            return false;
        }

        private static GameObject? _crosshairRoot;
        private static GameObject? _pointTrackBox;

        // Boresight crosshair + Point Track marker for the in-cockpit feed (docs/tgp-manual-control.md
        // "In-cockpit overlay"), built once and lazily re-created if TargetScreenUI's canvas is ever
        // destroyed/recreated (Unity's overridden == treats a destroyed reference as null, so this
        // check doubles as respawn handling). Both the crosshair arms and the Point Track box are
        // plain UI Image bars, not the game's own targetLockBox prefab — that prefab's border is a
        // fixed-width sliced sprite that stayed visually thin no matter how the box was resized, so
        // hand-building it from the same bars as the crosshair is what actually guarantees a border
        // as thick as the crosshair lines. Called every tick from the Harmony prefix regardless of
        // ManualMode, so the crosshair is hidden the instant manual mode ends, not left stuck on.
        internal static void SyncNativeCrosshair(Canvas displayCanvas, bool visible)
        {
            if (displayCanvas == null) return;
            if (_crosshairRoot == null)
            {
                // Anchor-stretched (0..1 of the parent rect), not fixed pixel/world-unit sizes: the
                // first version computed sizes from canvasRect.rect.width/height as absolute units,
                // which rendered as "minute" — that canvas's rect isn't necessarily in screen pixels
                // (could be world-space units for a Screen Space - Camera or World Space canvas), so
                // a size computed as "42% of rect.height" has no guaranteed relationship to how big
                // it actually looks on screen. Anchors sidestep that entirely: 0..1 always spans the
                // full parent regardless of its absolute unit scale.
                // Box side length = 2 * gap (a square, half-size = gap). Each crosshair arm's own
                // length must be exactly 2x that box side — i.e. 4 * gap — so armEnd is derived from
                // gap rather than set independently, keeping that ratio exact by construction instead
                // of by hand-tuned numbers that can drift out of the requested 2:1 relationship.
                const float gap = 0.028125f;              // 25% smaller than the previous pass
                const float armLength = 4f * gap;         // = 2x the box's side length (2 * (2 * gap))
                const float armEnd = 0.5f + gap + armLength;
                const float thickness = 0.005f;   // 50% thinner than the previous pass
                float half = thickness / 2f;

                _crosshairRoot = new GameObject("NOXMFD_ManualCrosshair", typeof(RectTransform));
                _crosshairRoot.transform.SetParent(displayCanvas.transform, false);
                var rootRt = (RectTransform)_crosshairRoot.transform;
                rootRt.anchorMin = Vector2.zero;
                rootRt.anchorMax = Vector2.one;
                rootRt.offsetMin = rootRt.offsetMax = Vector2.zero;   // fills the canvas; each arm positions itself via its own anchors below

                CreateBar(rootRt, "Top",    new Vector2(0.5f - half, 0.5f + gap),      new Vector2(0.5f + half, armEnd));
                CreateBar(rootRt, "Bottom", new Vector2(0.5f - half, 1f - armEnd),     new Vector2(0.5f + half, 0.5f - gap));
                CreateBar(rootRt, "Left",   new Vector2(1f - armEnd, 0.5f - half),     new Vector2(0.5f - gap, 0.5f + half));
                CreateBar(rootRt, "Right",  new Vector2(0.5f + gap, 0.5f - half),      new Vector2(armEnd, 0.5f + half));

                // Point Track box — a hollow square built from four bars at the SAME thickness as the
                // crosshair arms, sized so its edges sit exactly at the arms' inner tips (gap).
                _pointTrackBox = new GameObject("PointTrackBox", typeof(RectTransform));
                _pointTrackBox.transform.SetParent(rootRt, false);
                var boxRt = (RectTransform)_pointTrackBox.transform;
                boxRt.anchorMin = Vector2.zero;
                boxRt.anchorMax = Vector2.one;
                boxRt.offsetMin = boxRt.offsetMax = Vector2.zero;

                CreateBar(boxRt, "BoxTop",    new Vector2(0.5f - gap, 0.5f + gap - half), new Vector2(0.5f + gap, 0.5f + gap + half));
                CreateBar(boxRt, "BoxBottom", new Vector2(0.5f - gap, 0.5f - gap - half), new Vector2(0.5f + gap, 0.5f - gap + half));
                CreateBar(boxRt, "BoxLeft",   new Vector2(0.5f - gap - half, 0.5f - gap), new Vector2(0.5f - gap + half, 0.5f + gap));
                CreateBar(boxRt, "BoxRight",  new Vector2(0.5f + gap - half, 0.5f - gap), new Vector2(0.5f + gap + half, 0.5f + gap));
            }

            _crosshairRoot.SetActive(visible);
            if (_pointTrackBox != null) _pointTrackBox.SetActive(visible && _pointTrackActive);
        }

        private static void CreateBar(Transform parent, string name, Vector2 anchorMin, Vector2 anchorMax)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            Image img = go.GetComponent<Image>();
            img.color = Color.white;
            RectTransform rt = img.rectTransform;
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
        }

        // Drives TargetScreenUI's own TextMeshProUGUI/Image elements (docs/tgp-manual-control.md's
        // "In-cockpit overlay") — called from a Harmony prefix (HarmonyPatches.cs) that skips
        // TargetScreenUI.UpdateTargetInfo entirely whenever ManualMode is true, since ManualMode
        // always implies zero real targets (Tick() auto-exits the instant a real lock exists), so
        // there's never a "which one wins" conflict with the native lock display. Reuses the game's
        // own fields rather than adding new Unity UI — see the doc's field-mapping table for what
        // each one shows here vs. its native meaning.
        //
        // TextMeshProUGUI, not UnityEngine.UI.Text: the decompiled reference source
        // (_scratch/full/TargetScreenUI.cs) declares these as legacy Text, but the live 0.34+ game
        // build has switched them to TMP (see NOXMFD.csproj's Unity.TextMeshPro reference comment).
        // Declaring the Harmony ___field parameters as Text anyway didn't fail to patch or throw —
        // it silently produced a type-confused field access that crashed the whole game a few ticks
        // later, deep inside TextMeshProUGUI's own internals (SetArraySizes), not at the call site —
        // caught via a Unity crash dump stack trace, not a caught exception or a Harmony patch-apply
        // warning. Confirm a field's real type against a live crash/decompile before trusting an
        // older decompiled source on a UI type, not just its name.
        internal static void PopulateNativeOverlay(TargetCam tc, Transform mount,
            TextMeshProUGUI typeText, TextMeshProUGUI pilotText, TextMeshProUGUI noLock,
            TextMeshProUGUI distanceText, TextMeshProUGUI headingText, TextMeshProUGUI altitudeText,
            TextMeshProUGUI relAltitudeText, TextMeshProUGUI speedText, TextMeshProUGUI relSpeedText,
            TextMeshProUGUI magText, TextMeshProUGUI modeText, TextMeshProUGUI bearingText,
            TextMeshProUGUI gridText, Image bearingImg)
        {
            noLock.gameObject.SetActive(false);
            typeText.gameObject.SetActive(true);
            pilotText.gameObject.SetActive(false);   // no pilot without a real target
            distanceText.gameObject.SetActive(true);
            headingText.gameObject.SetActive(true);
            altitudeText.gameObject.SetActive(true);
            relAltitudeText.gameObject.SetActive(true);
            speedText.gameObject.SetActive(false);   // own aircraft speed — already on the flight HUD
            relSpeedText.gameObject.SetActive(true);
            magText.gameObject.SetActive(true);
            modeText.gameObject.SetActive(true);
            bearingText.gameObject.SetActive(true);
            bearingImg.gameObject.SetActive(true);
            gridText.gameObject.SetActive(true);

            typeText.text = _pointTrackActive ? "POINT TRACK" : "MANUAL";
            typeText.color = Color.white;

            magText.text = $"Mag x{10f / _desiredFov:F1}";
            modeText.text = tc.UsingIR() ? "MODE: IR" : "MODE: COLOR";

            GameManager.GetLocalAircraft(out Aircraft ac);

            // Aircraft-relative az/el (0° = nose), matching the native bearing readout's own frame
            // (camMount.transform.localEulerAngles.y) — NOT the world-frame azimuth ToAzimuthElevation
            // gives on _panDir directly. A Point Track lock on a fixed ground point makes the WORLD
            // bearing to that point drift continuously just from the aircraft flying past it, which
            // reads as "the needle moves for no reason" even with zero pilot input. Computed from a
            // direction vector via InverseTransformDirection, not Transform.localEulerAngles, to avoid
            // Euler wraparound on the elevation axis (native code never showed elevation, so never hit
            // that problem).
            Vector3 localDir = ac != null ? ac.transform.InverseTransformDirection(_panDir) : _panDir;
            (float az, float el) = ToAzimuthElevation(localDir);
            bearingText.text = $"{az:F0}°";
            bearingImg.rectTransform.localEulerAngles = new Vector3(0f, 0f, -az);
            headingText.text = $"EL {el:F0}°";   // repurposed — elevation has no native readout

            Vector3 hitLocal = default;
            float rangeM = 0f;
            bool hasHit = ac != null && TryGetLookPoint(mount, out hitLocal, out rangeM);
            if (hasHit)
            {
                GlobalPosition hitGlobal = hitLocal.ToGlobalPosition();
                Vector3 rel = hitGlobal - ac!.GlobalPosition();
                // Positive = closing (moving toward the look point, range decreasing) — this is a
                // new label ("CLO", not the native "REL"), so it defines its own clear sign
                // convention rather than matching the native rel_speed formula for a moving target.
                float closure = Vector3.Dot(ac.rb.velocity, rel.normalized);

                distanceText.text    = "RNG " + UnitConverter.DistanceReading(rangeM);
                altitudeText.text    = "ALT " + UnitConverter.AltitudeReading(hitGlobal.y);
                relAltitudeText.text = "REL " + UnitConverter.AltitudeReading(rel.y);
                relSpeedText.text    = "CLO " + UnitConverter.SpeedReading(closure);
                gridText.text        = "GRID: " + SceneSingleton<DynamicMap>.i.gridLabels.GetGridPosition(hitGlobal);
            }
            else
            {
                distanceText.text    = "RNG -";
                altitudeText.text    = "ALT -";
                relAltitudeText.text = "REL -";
                relSpeedText.text    = "CLO -";
                gridText.text        = "GRID: -";
            }

            // Throttled diagnostic (docs/tgp-manual-control.md testing) — confirms this patch is
            // actually firing and shows what it's computing, since TargetScreenUI.UpdateTargetInfo
            // runs on the game's own SlowUpdate timer, outside TgpManualControl.Tick()'s own diag log.
            if (Time.time - _overlayDiagLastLog > 1f)
            {
                _overlayDiagLastLog = Time.time;
                Plugin.Log?.LogInfo($"[NOXMFD] TGP native overlay diag: az={az:0.0} el={el:0.0} pointTrack={_pointTrackActive} hit={hasHit} rangeM={(hasHit ? rangeM.ToString("0") : "-")}.");
            }
        }

        private static float _overlayDiagLastLog;

        private static bool EnsureReflection()
        {
            if (_reflectionTried) return _camField != null && _currentMountField != null && _currentModeField != null;
            _reflectionTried = true;
            var t = typeof(TargetCam);
            _camField                = t.GetField("cam",                  BindingFlags.NonPublic | BindingFlags.Instance);
            _currentMountField       = t.GetField("currentMount",         BindingFlags.NonPublic | BindingFlags.Instance);
            _currentModeField        = t.GetField("currentMode",          BindingFlags.NonPublic | BindingFlags.Instance);
            _canvasObjectLandingField = t.GetField("canvasObjectLanding", BindingFlags.NonPublic | BindingFlags.Instance);
            _camTimeoutField         = t.GetField("camTimeout",           BindingFlags.NonPublic | BindingFlags.Instance);
            _switchIrStateMethod     = t.GetMethod("SwitchIRState",       BindingFlags.NonPublic | BindingFlags.Instance);
            if (_camField == null || _currentMountField == null || _currentModeField == null)
                Plugin.Log?.LogWarning("[NOXMFD] TGP manual control: could not locate TargetCam private fields.");
            return _camField != null && _currentMountField != null && _currentModeField != null;
        }
    }
}
