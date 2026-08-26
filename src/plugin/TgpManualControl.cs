using System;
using System.Collections.Generic;
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
        internal static bool PointTrackActive => _pointTrackActive;

        // Read-only seam for the in-game HUD line-of-sight cue. The controller remains the sole
        // writer of _panDir; consumers get the final normalized world direction only while manual
        // mode actually owns TargetCam.
        internal static bool TryGetAimDirection(out Vector3 direction)
        {
            direction = _panDir;
            if (!ManualMode || float.IsNaN(direction.x) || float.IsNaN(direction.y) || float.IsNaN(direction.z) ||
                float.IsInfinity(direction.x) || float.IsInfinity(direction.y) || float.IsInfinity(direction.z) ||
                direction.sqrMagnitude <= 0.0001f)
            {
                direction = Vector3.zero;
                return false;
            }

            direction.Normalize();
            return true;
        }

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
        // Cursor Select can promote Point Track into the game's normal unit lock. The tracked
        // ground point must be within this many metres of a unit's pivot, or within one full unit
        // length for unusually large units. This is deliberately tighter than the camera view:
        // zooming out should not make an unrelated unit elsewhere in frame selectable.
        private const float UnitLockMinRadiusM = 50f;

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
        private static float?  _zoomAxisValue;               // non-null while a calibrated zoom axis is bound — absolute, applied when moved
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
        // only changes from nudge input. Keeping these separate prevents aircraft-motion correction
        // from fighting pilot nudge input.
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

        // ── Input (Keybinds.Poll calls these every frame, remote-ready per docs/tgp-manual-
        // control.md — CommandDispatcher can call the same API from a network command later) ────
        internal static void SetPan(float x, float y)
        {
            _panInputX = Mathf.Clamp(x, -1f, 1f);
            _panInputY = Mathf.Clamp(y, -1f, 1f);
        }

        // dir: +1 = zoom in (narrower FOV), -1 = zoom out. Mirrors the remote fire.set shape
        // ({group, on}) so a future remote zoom command needs no new dispatch pattern. Zoom Axis
        // and buttons coexist: a moved axis jumps to its absolute value, then buttons work while
        // that axis is stationary.
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
            if (!TgpManualTargetCamAccess.Ensure())
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
            if (tc == null || !TgpManualTargetCamAccess.Ensure()) return;
            Transform? mount = TgpManualTargetCamAccess.GetMount(tc);
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

        // PAD Cursor Select handoff: when Point Track is close to a live/selectable unit, put that
        // unit into WeaponManager's real target list, then release manual ownership immediately;
        // TargetCam/TgpFeed resume the native locked-camera path on their next update.
        internal static void TryLockTrackedUnit()
        {
            if (!ManualMode || !_pointTrackActive) return;
            if (!GameManager.GetLocalAircraft(out Aircraft ac) || ac == null || ac.weaponManager == null) return;

            TargetListSelector tgtSel = SceneSingleton<TargetListSelector>.i;
            Unit? nearest = null;
            float nearestDistance = float.MaxValue;

            foreach (Unit unit in UnitRegistry.allUnits)
            {
                if (unit == null || unit.disabled || unit is Scenery || ReferenceEquals(unit, ac)) continue;
                if (DynamicMap.GetFactionMode(unit.NetworkHQ) == FactionMode.NoFaction) continue;
                if (tgtSel != null && tgtSel.CheckExclusions(unit)) continue;
                if (ac.weaponManager.CheckIsTarget(unit)) continue;

                float distance = (unit.GlobalPosition() - _trackedPoint).magnitude;
                float unitLength = unit.definition != null ? unit.definition.length : 0f;
                float lockRadius = Mathf.Max(UnitLockMinRadiusM, unitLength);
                if (distance <= lockRadius && distance < nearestDistance)
                {
                    nearest = unit;
                    nearestDistance = distance;
                }
            }

            if (nearest == null)
            {
                Plugin.Log?.LogInfo("[NOXMFD] TGP unit lock: no selectable unit near Point Track — ignored.");
                return;
            }

            if (CommandDispatcher.TrySelectTarget(nearest, "TGP unit lock"))
            {
                Plugin.Log?.LogInfo($"[NOXMFD] TGP unit lock: Point Track handoff at {nearestDistance:0}m.");
                ExitManual(ac.targetCam, "Point Track promoted to unit lock");
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
            if (tc == null || !TgpManualTargetCamAccess.Ensure()) return;
            bool next = !tc.UsingIR();
            if (!TgpManualTargetCamAccess.SwitchIR(tc, next)) return;
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

            if (!TgpManualTargetCamAccess.Ensure()) { ExitManual(tc, "TargetCam reflection failed"); return; }
            Camera? cam = TgpManualTargetCamAccess.GetCamera(tc);
            Transform? mount = TgpManualTargetCamAccess.GetMount(tc);
            if (cam == null || mount == null) { ExitManual(tc, "TargetCam/mount torn down"); return; }

            if (TgpManualTargetCamAccess.IsLandingMode(tc))
            {
                ExitManual(tc, "gear/landing-cam conflict");
                return;
            }

            // The upgrade path TargetCam_Update_ManualGate's own comment names (HarmonyPatches.cs):
            // skipping the whole native Update() also skips its cosmetic per-second exposure ramp
            // (UpdateExposure — ambient-light-driven postExposure/contrast on the screen volume).
            // Harmless on a real lock, where a prior tick already ran it — but on the FIRST manual
            // engage of a fresh mission, before any real lock has ever run Update() even once, the
            // volume is still sitting at Awake()'s cold-start values: a visibly darker, lower-
            // contrast picture than the normal feed, until a real lock finally ran the ramp for the
            // first time. Calling the same private method directly here (not reimplementing its
            // ambient-light formula) keeps this correct even if the game changes that formula later.
            TgpManualTargetCamAccess.UpdateExposure(tc);

            // Point Track: aim = _pointTrackBaseline (aircraft-motion correction) rotated by the
            // (_pointTrackOffsetAz, _pointTrackOffsetEl) nudge offset — two independent writers,
            // not one field both fight over. See the field comment for why that decoupling matters.
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
                _desiredFov = TgpManualAimMath.ZoomFromAxis(_zoomAxisValue!.Value, MinFov, MaxFov);
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
            TgpManualTargetCamAccess.SetCamTimeout(tc, 99f);

            _diagTimer += dt;
            if (_diagTimer >= DiagLogInterval)
            {
                _diagTimer = 0f;
                (float az, float el) = ToAzimuthElevation(_panDir);
                string zoomAxisStr = _zoomAxisValue.HasValue ? _zoomAxisValue.Value.ToString("0.00") : "unbound";
                Plugin.Log?.LogDebug($"[NOXMFD] TGP manual control diag: az={az:0.0} el={el:0.0} fov={_desiredFov:0.00} pointTrack={_pointTrackActive} panIn=({_panInputX:0.00},{_panInputY:0.00}) zoomIn={_zoomInHeld} zoomOut={_zoomOutHeld} zoomAxis={zoomAxisStr}.");
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
            TgpManualAimMath.AimVector v = TgpManualAimMath.NudgeDirection(dir.x, dir.y, dir.z,
                _panInputX, _panInputY, dt,
                _desiredFov, MaxFov,
                PanSpeedDegPerSec, TiltSpeedDegPerSec, MaxElevationDeg);
            return new Vector3(v.X, v.Y, v.Z);
        }

        // World-angle-per-second rates (PanSpeedDegPerSec, TiltSpeedDegPerSec) are blind to how
        // much of the picture they actually sweep — at MinFov (~40x magnification, 0.25° wide)
        // even a fraction of a degree crosses a huge share of the visible frame, so a rate tuned
        // to feel right at wide FOV reads as violent shake once zoomed in ("the more zoom, the
        // more jumpy" — confirmed directly, not inferred). Scaling every user-driven angular rate by
        // current FOV / MaxFov keeps the picture-relative (on-screen) rate roughly constant across
        // the whole zoom range instead of the world-angular rate staying constant while its visual
        // effect balloons — the same reason real gimbal/TGP slew rate drops as magnification rises.
        private static float ZoomScale() => TgpManualAimMath.ZoomScale(_desiredFov, MaxFov);

        private static (float az, float el) ToAzimuthElevation(Vector3 dir) =>
            TgpManualAimMath.ToAzimuthElevation(dir.x, dir.y, dir.z);

        private static Vector3 FromAzimuthElevation(float azimuthDeg, float elevationDeg)
        {
            TgpManualAimMath.AimVector v = TgpManualAimMath.FromAzimuthElevation(azimuthDeg, elevationDeg);
            return new Vector3(v.X, v.Y, v.Z);
        }

        // Ordering matters: ManualMode must already be true before tc.SetTargetCam() is called,
        // or its own tail call to AimCamera() runs un-gated and snaps the mount toward whatever
        // an empty target list computes (docs/tgp-manual-control.md's reflection-surface note).
        private static void Engage(TargetCam tc, Aircraft aircraft)
        {
            ManualMode = true;

            TgpManualTargetCamAccess.ForceTargetForward(tc);
            TgpManualTargetCamAccess.HideLandingCanvas(tc);

            Camera? cam = TgpManualTargetCamAccess.GetCamera(tc);
            if (cam == null || !cam.enabled)
            {
                try { tc.SetTargetCam(); }
                catch (Exception ex)
                {
                    Plugin.Log?.LogWarning($"[NOXMFD] TGP manual control: engage SetTargetCam threw: {ex.Message}");
                    ManualMode = false;
                    return;
                }
                cam = TgpManualTargetCamAccess.GetCamera(tc);
            }
            if (cam == null) { ManualMode = false; return; }

            TgpManualTargetCamAccess.SetCamTimeout(tc, 99f);
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

            // PAD Cursor consolidation (docs/tgp-manual-control.md) — the camera is now a cyclable
            // SOI target; engaging steals focus onto it immediately rather than making the pilot Tab
            // to the newly-added ring entry by hand.
            TelemetryServer.ClaimNativeTgpSoi();

            Plugin.Log?.LogInfo("[NOXMFD] TGP manual control: ON (centered, minimum zoom).");
        }

        private static void ExitManual(TargetCam? tc, string reason)
        {
            if (!ManualMode) return;
            ManualMode = false;
            _pointTrackActive = false;
            // Must run after ManualMode flips false, so the camera is already gone from the SOI
            // ring by the time this looks for somewhere else to send focus.
            TelemetryServer.ReleaseNativeTgpSoi();
            if (tc != null && TgpManualTargetCamAccess.Ensure())
            {
                Camera? cam = TgpManualTargetCamAccess.GetCamera(tc);
                Transform? mount = TgpManualTargetCamAccess.GetMount(tc);
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

        // Everything a manual-mode overlay needs to show, computed once and shared by both
        // consumers instead of each re-deriving it: TgpNativeOverlay (in-cockpit TextMeshPro
        // fields) and TgpOverlay.PopulateManual (the external web TGP page, docs/tgp-manual-
        // control.md's "In-cockpit overlay" — same data, second surface). Az/el are aircraft-
        // relative (0° = nose) for the same reason TgpNativeOverlay always used that frame: a
        // Point Track lock on a fixed ground point drifts the WORLD bearing continuously just
        // from the aircraft's own translation, which reads as "the needle moves for no reason"
        // with zero pilot input.
        internal readonly struct ManualOverlaySample
        {
            public float AzimuthDeg { get; }
            public float ElevationDeg { get; }
            public float Mag { get; }
            public bool IR { get; }
            public bool PointTrackActive { get; }
            public bool HasHit { get; }
            public float RangeM { get; }
            public float AltitudeM { get; }
            public float RelAltitudeM { get; }
            public float ClosureMps { get; }
            public string Grid { get; }

            public ManualOverlaySample(float azimuthDeg, float elevationDeg, float mag, bool ir,
                bool pointTrackActive, bool hasHit, float rangeM, float altitudeM, float relAltitudeM,
                float closureMps, string grid)
            {
                AzimuthDeg = azimuthDeg;
                ElevationDeg = elevationDeg;
                Mag = mag;
                IR = ir;
                PointTrackActive = pointTrackActive;
                HasHit = hasHit;
                RangeM = rangeM;
                AltitudeM = altitudeM;
                RelAltitudeM = relAltitudeM;
                ClosureMps = closureMps;
                Grid = grid;
            }
        }

        internal static ManualOverlaySample ComputeOverlaySample(TargetCam tc, Transform mount, Aircraft? ac)
        {
            Vector3 localDir = ac != null ? ac.transform.InverseTransformDirection(_panDir) : _panDir;
            (float az, float el) = ToAzimuthElevation(localDir);
            float mag = 10f / _desiredFov;
            bool ir = tc.UsingIR();

            Vector3 hitLocal = default;
            float rangeM = 0f;
            bool hasHit = ac != null && TryGetLookPoint(mount, out hitLocal, out rangeM);
            if (!hasHit)
                return new ManualOverlaySample(az, el, mag, ir, _pointTrackActive, false, 0f, 0f, 0f, 0f, "");

            GlobalPosition hitGlobal = hitLocal.ToGlobalPosition();
            Vector3 rel = hitGlobal - ac!.GlobalPosition();
            // Positive = closing (moving toward the look point, range decreasing) — a new value
            // ("CLO"), not the native rel_speed formula for a moving target, so it defines its
            // own clear sign convention instead.
            float closure = Vector3.Dot(ac.rb.velocity, rel.normalized);
            string grid = SceneSingleton<DynamicMap>.i.gridLabels.GetGridPosition(hitGlobal);
            return new ManualOverlaySample(az, el, mag, ir, _pointTrackActive, true, rangeM, hitGlobal.y, rel.y, closure, grid);
        }
    }
}
