using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

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
            if (_camField == null || _currentMountField == null || _currentModeField == null)
                Plugin.Log?.LogWarning("[NOXMFD] TGP manual control: could not locate TargetCam private fields.");
            return _camField != null && _currentMountField != null && _currentModeField != null;
        }
    }
}
