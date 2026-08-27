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
            Ensure() ? _camField!.GetValue(tc) as Camera : null;

        internal static Transform? GetMount(TargetCam tc) =>
            Ensure() ? _currentMountField!.GetValue(tc) as Transform : null;

        internal static bool IsLandingMode(TargetCam tc) =>
            Ensure() &&
            _currentModeField!.GetValue(tc) is TargetCam.CamMode mode &&
            mode == TargetCam.CamMode.landingMode;

        internal static void ForceTargetForward(TargetCam tc)
        {
            if (!Ensure()) return;
            if (_currentModeField!.GetValue(tc) is TargetCam.CamMode mode && mode == TargetCam.CamMode.landingMode)
                _currentModeField!.SetValue(tc, TargetCam.CamMode.targetForward);
        }

        internal static void HideLandingCanvas(TargetCam tc)
        {
            if (Ensure() && _canvasObjectLandingField?.GetValue(tc) is GameObject landingCanvas && landingCanvas.activeSelf)
                landingCanvas.SetActive(false);
        }

        internal static void SetCamTimeout(TargetCam tc, float value)
        {
            if (Ensure()) _camTimeoutField?.SetValue(tc, value);
        }

        internal static bool SwitchIR(TargetCam tc, bool on)
        {
            if (!Ensure() || _switchIrStateMethod == null) return false;
            _switchIrStateMethod.Invoke(tc, new object[] { on });
            return true;
        }

        internal static void UpdateExposure(TargetCam tc)
        {
            if (Ensure()) _updateExposureMethod?.Invoke(tc, null);
        }
    }
}
