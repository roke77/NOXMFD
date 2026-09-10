using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Reflection;

namespace NOXMFD
{
    // Cached private TargetCam access shared by the real-lock camera overrides — TgpSingleTargetView
    // (WTV/STV reframe, issue #81) and TgpLockZoom (Z+/Z- during a real lock, issue #83). Same
    // "one reflection cache" shape as TgpManualTargetCamAccess.cs (rate-limited per-member failure
    // logging included), split out separately because that one is scoped to manual control
    // (docs/tgp-manual-control.md) while this covers the opposite case — a real (non-manual) lock —
    // so both overrides can share one lookup instead of each caching the same fields/methods
    // independently.
    internal static class TgpLockCameraAccess
    {
        private static bool _reflectionTried;
        private static MethodInfo? _singleTargetPositionAndSizeMethod;
        private static FieldInfo? _targetPositionField;
        private static FieldInfo? _targetFovField;
        private static MethodInfo? _aimCameraMethod;
        private static readonly ConcurrentDictionary<string, byte> _loggedAccessFailures =
            new ConcurrentDictionary<string, byte>(StringComparer.Ordinal);

        internal static bool Ensure()
        {
            if (_reflectionTried) return _targetFovField != null;
            _reflectionTried = true;
            var t = typeof(TargetCam);
            _singleTargetPositionAndSizeMethod = t.GetMethod("SingleTargetPositionAndSize", BindingFlags.NonPublic | BindingFlags.Instance);
            _targetPositionField = t.GetField("targetPosition", BindingFlags.NonPublic | BindingFlags.Instance);
            _targetFovField = t.GetField("targetFOV", BindingFlags.NonPublic | BindingFlags.Instance);
            _aimCameraMethod = t.GetMethod("AimCamera", BindingFlags.NonPublic | BindingFlags.Instance);
            if (_singleTargetPositionAndSizeMethod == null || _targetPositionField == null ||
                _targetFovField == null || _aimCameraMethod == null)
                Plugin.Log?.LogWarning("[NOXMFD] TGP lock camera access: could not locate TargetCam internals — WTV/STV and lock zoom disabled.");
            return _targetFovField != null;
        }

        // The same private method the native single-lock path itself uses, called with a synthetic
        // one-target list so a caller's reframe is pixel-identical to a real single lock. Returns
        // false (leaving position/fov at their defaults) on a missing member or a reflection failure.
        internal static bool TryComputeSingleTargetFraming(TargetCam tc, List<Unit> singleTarget, out GlobalPosition position, out float fov)
        {
            position = default;
            fov = 0f;
            if (!Ensure()) return false;
            try
            {
                var args = new object?[] { singleTarget, null, null };
                _singleTargetPositionAndSizeMethod!.Invoke(tc, args);
                position = (GlobalPosition)args[1]!;
                fov = (float)args[2]!;
                return true;
            }
            catch (Exception ex)
            {
                LogAccessFailure("SingleTargetPositionAndSize", ex);
                return false;
            }
        }

        internal static void SetTargetPosition(TargetCam tc, GlobalPosition position)
        {
            if (!Ensure()) return;
            try { _targetPositionField!.SetValue(tc, position); }
            catch (Exception ex) { LogAccessFailure("targetPosition", ex); }
        }

        // 0f on a missing member or a reflection failure — callers already treat 0f/negative as "no
        // usable reading" (TgpLockZoom's own baseline read falls back to MaxFov in that case).
        internal static float GetTargetFov(TargetCam tc)
        {
            if (!Ensure()) return 0f;
            try { return (float)_targetFovField!.GetValue(tc)!; }
            catch (Exception ex) { LogAccessFailure("targetFOV (get)", ex); return 0f; }
        }

        internal static void SetTargetFov(TargetCam tc, float fov)
        {
            if (!Ensure()) return;
            try { _targetFovField!.SetValue(tc, fov); }
            catch (Exception ex) { LogAccessFailure("targetFOV (set)", ex); }
        }

        internal static void InvokeAimCamera(TargetCam tc)
        {
            if (!Ensure()) return;
            try { _aimCameraMethod!.Invoke(tc, null); }
            catch (Exception ex) { LogAccessFailure("AimCamera", ex); }
        }

        private static void LogAccessFailure(string member, Exception ex)
        {
            if (!_loggedAccessFailures.TryAdd(member, 0)) return;
            Plugin.Log?.LogWarning($"[NOXMFD] TGP lock camera access: TargetCam.{member} access failed; the operation was skipped and will be retried: {ex}");
        }
    }
}
