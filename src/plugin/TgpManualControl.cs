using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace NOXMFD
{
    // Manual pan/tilt/zoom override of the game's own TargetCam (docs/tgp-manual-control.md).
    // Off by default, keybind-driven; auto-exits the instant a real target lock exists, the
    // external TGP page closes, the aircraft is lost, or the native landing cam takes over.
    //
    // A static class (like Keybinds/RatesConfig), not an instance TgpFeed owns: it has its own
    // state machine (ManualMode, pan direction, desired FOV) and its own TargetCam reflection
    // cache, and TgpFeed only needs to know the camera is already enabled — which manual engage
    // arranges by calling the same public TargetCam.SetTargetCam() TgpFeed itself calls on a
    // real lock. No coupling between the two beyond that shared camera.
    //
    // Ported from github.com/9138noms/TargetCamControl's Runner.cs (full source reviewed): world-
    // space pan direction (yaw around world-up, pitch clamped to avoid the poles), re-applied to
    // the mount every tick rather than driving a raw angular velocity, so input always composes
    // predictably regardless of how long it's been held.
    internal static class TgpManualControl
    {
        // Matches TargetCam.SetTargetCam()'s own targetFOV clamp range — the native camera never
        // goes tighter/wider than this, so reusing it needs no new config for v1.
        private const float MinFov = 0.25f;
        private const float MaxFov = 20f;
        private const float DefaultFov = 10f;              // TargetCam.SetTargetCam()'s own initial fieldOfView
        private const float ZoomRateFovPerSec = 6f;
        private const float PanSpeedDegPerSec = 60f;
        private const float TiltSpeedDegPerSec = 45f;
        private const float MaxElevationDeg = 85f;          // stay short of straight up/down — LookRotation degrades near the poles

        internal static bool ManualMode { get; private set; }

        private static Vector3 _panDir = Vector3.forward;   // world-space, unit length
        private static float   _desiredFov = DefaultFov;
        private static float   _panInputX, _panInputY;      // held state, written every frame by Keybinds.Poll
        private static bool    _zoomInHeld, _zoomOutHeld;

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
        // ({group, on}) so a future remote zoom command needs no new dispatch pattern.
        internal static void SetZoom(int dir, bool on)
        {
            if (dir > 0) _zoomInHeld = on;
            else if (dir < 0) _zoomOutHeld = on;
        }

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
            if (ac == null) return;
            _panDir = ac.transform.forward;
            _desiredFov = DefaultFov;
            Plugin.Log?.LogInfo("[NOXMFD] TGP manual control: reset to aircraft-forward.");
        }

        // Called every frame from TelemetryReader alongside TgpFeed.Tick — cheap no-op while off.
        internal static void Tick(float dt)
        {
            if (!ManualMode) return;

            // Every exit trigger (docs/tgp-manual-control.md "Lifecycle"), checked every tick:
            GameManager.GetLocalAircraft(out Aircraft ac);
            TargetCam? tc = ac != null ? ac.targetCam : null;
            if (ac == null || tc == null) { ExitManual(null, "aircraft lost"); return; }
            if (!TelemetryServer.WantsTgpFrames) { ExitManual(tc, "external TGP page closed"); return; }
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

            ApplyPan(dt);
            mount.rotation = Quaternion.LookRotation(_panDir, Vector3.up);

            int zoomDir = (_zoomInHeld ? 1 : 0) - (_zoomOutHeld ? 1 : 0);
            _desiredFov = Mathf.Clamp(_desiredFov - zoomDir * ZoomRateFovPerSec * dt, MinFov, MaxFov);
            cam.fieldOfView = _desiredFov;

            // Belt-and-suspenders: Update()'s own countdown never runs while ManualMode is true
            // (Harmony-gated, HarmonyPatches.cs), but pin it high anyway in case anything else
            // ever reads camTimeout directly.
            _camTimeoutField?.SetValue(tc, 99f);
        }

        // Rotates _panDir in world space: azimuth around world-up from x, elevation (clamped
        // short of the poles, where LookRotation degrades) from y. ponytail: fixed-rate, no
        // raycast/relock — a pure world-space direction drifts off a pointed-at spot as the
        // aircraft banks or turns. Named in docs/tgp-manual-control.md as a deliberate v1
        // validation spike, not a settled scope cut — add world-hit raycasting (like
        // TargetCamControl's LastHitGP) here first if that drift reads as broken in play.
        private static void ApplyPan(float dt)
        {
            if (_panInputX == 0f && _panInputY == 0f) return;
            float azimuth = Mathf.Atan2(_panDir.x, _panDir.z) * Mathf.Rad2Deg;
            float elevation = Mathf.Asin(Mathf.Clamp(_panDir.y, -1f, 1f)) * Mathf.Rad2Deg;
            azimuth += _panInputX * PanSpeedDegPerSec * dt;
            elevation = Mathf.Clamp(elevation + _panInputY * TiltSpeedDegPerSec * dt, -MaxElevationDeg, MaxElevationDeg);
            float az = azimuth * Mathf.Deg2Rad;
            float el = elevation * Mathf.Deg2Rad;
            float cosEl = Mathf.Cos(el);
            _panDir = new Vector3(Mathf.Sin(az) * cosEl, Mathf.Sin(el), Mathf.Cos(az) * cosEl);
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
            Transform? mount = _currentMountField!.GetValue(tc) as Transform;
            _panDir = mount != null ? mount.forward : aircraft.transform.forward;
            _desiredFov = cam.fieldOfView > 0f ? cam.fieldOfView : DefaultFov;
            _panInputX = _panInputY = 0f;
            _zoomInHeld = _zoomOutHeld = false;

            Plugin.Log?.LogInfo("[NOXMFD] TGP manual control: ON.");
        }

        private static void ExitManual(TargetCam? tc, string reason)
        {
            if (!ManualMode) return;
            ManualMode = false;
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
