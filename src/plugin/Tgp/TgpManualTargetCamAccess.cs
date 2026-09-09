using System;
using System.Collections.Concurrent;
using System.Reflection;
using UnityEngine;

namespace NOXMFD
{
    // Cached private access to TargetCam internals used by manual TGP control. Centralizing the
    // reflection keeps TgpManualControl focused on state/lifecycle decisions.
    internal static class TgpManualTargetCamAccess
    {
        private static bool _reflectionTried;
        private static FieldInfo? _camField;
        private static FieldInfo? _currentMountField;
        private static FieldInfo? _currentModeField;
        private static FieldInfo? _canvasObjectLandingField;
        private static FieldInfo? _camTimeoutField;
        private static MethodInfo? _switchIrStateMethod;
        private static MethodInfo? _updateExposureMethod;
        private static readonly ConcurrentDictionary<string, byte> _loggedAccessFailures =
            new ConcurrentDictionary<string, byte>(StringComparer.Ordinal);

        internal static bool Ensure()
        {
            if (_reflectionTried) return _camField != null && _currentMountField != null && _currentModeField != null;
            _reflectionTried = true;
            var t = typeof(TargetCam);
            _camField                 = t.GetField("cam",                 BindingFlags.NonPublic | BindingFlags.Instance);
            _currentMountField        = t.GetField("currentMount",        BindingFlags.NonPublic | BindingFlags.Instance);
            _currentModeField         = t.GetField("currentMode",         BindingFlags.NonPublic | BindingFlags.Instance);
            _canvasObjectLandingField = t.GetField("canvasObjectLanding", BindingFlags.NonPublic | BindingFlags.Instance);
            _camTimeoutField          = t.GetField("camTimeout",          BindingFlags.NonPublic | BindingFlags.Instance);
            _switchIrStateMethod      = t.GetMethod("SwitchIRState",      BindingFlags.NonPublic | BindingFlags.Instance);
            _updateExposureMethod     = t.GetMethod("UpdateExposure",     BindingFlags.NonPublic | BindingFlags.Instance);
            if (_camField == null || _currentMountField == null || _currentModeField == null)
                Plugin.Log?.LogWarning("[NOXMFD] TGP manual control: could not locate TargetCam private fields.");
            return _camField != null && _currentMountField != null && _currentModeField != null;
        }

        internal static Camera? GetCamera(TargetCam tc) =>
            TryGet(_camField, tc, "cam") as Camera;

        internal static Transform? GetMount(TargetCam tc) =>
            TryGet(_currentMountField, tc, "currentMount") as Transform;

        internal static bool IsLandingMode(TargetCam tc) =>
            TryGet(_currentModeField, tc, "currentMode") is TargetCam.CamMode mode &&
            mode == TargetCam.CamMode.landingMode;

        internal static void ForceTargetForward(TargetCam tc)
        {
            if (TryGet(_currentModeField, tc, "currentMode") is TargetCam.CamMode mode && mode == TargetCam.CamMode.landingMode)
                TrySet(_currentModeField!, tc, TargetCam.CamMode.targetForward, "currentMode");
        }

        internal static void HideLandingCanvas(TargetCam tc)
        {
            if (TryGet(_canvasObjectLandingField, tc, "canvasObjectLanding") is GameObject landingCanvas && landingCanvas.activeSelf)
                landingCanvas.SetActive(false);
        }

        internal static void SetCamTimeout(TargetCam tc, float value)
        {
            if (Ensure() && _camTimeoutField != null) TrySet(_camTimeoutField, tc, value, "camTimeout");
        }

        internal static bool SwitchIR(TargetCam tc, bool on)
        {
            if (!Ensure() || _switchIrStateMethod == null) return false;
            try { _switchIrStateMethod.Invoke(tc, new object[] { on }); return true; }
            catch (Exception ex) { LogAccessFailure("SwitchIRState", ex); return false; }
        }

        internal static void UpdateExposure(TargetCam tc)
        {
            if (!Ensure() || _updateExposureMethod == null) return;
            try { _updateExposureMethod.Invoke(tc, null); }
            catch (Exception ex) { LogAccessFailure("UpdateExposure", ex); }
        }

        private static object? TryGet(FieldInfo? field, TargetCam tc, string member)
        {
            if (!Ensure() || field == null) return null;
            try { return field.GetValue(tc); }
            catch (Exception ex) { LogAccessFailure(member, ex); return null; }
        }

        private static void TrySet(FieldInfo field, TargetCam tc, object value, string member)
        {
            try { field.SetValue(tc, value); }
            catch (Exception ex) { LogAccessFailure(member, ex); }
        }

        private static void LogAccessFailure(string member, Exception ex)
        {
            if (!_loggedAccessFailures.TryAdd(member, 0)) return;
            Plugin.Log?.LogWarning($"[NOXMFD] TGP manual control: TargetCam.{member} access failed; the operation was skipped and will be retried: {ex}");
        }
    }
}
