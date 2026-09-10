using System;
using System.Collections.Generic;
using System.Reflection;

namespace NOXMFD
{
    // TGP page's VIEW toggle (issue #81, docs/tgp-single-target-view.md): WTV (default) leaves the
    // native multi-target framing alone; STV re-frames a 2+ target lock onto just the TGT-focused
    // one (TargetFocus.Id, issue #62). Single/no lock is unaffected either way — the native
    // single-target framing already does what STV asks for.
    //
    // Implemented as a HarmonyPatches.cs postfix on TargetCam.SetTargetCam (same shape as its
    // neighboring TargetCam_SetTargetCam_IrOverride): let the native call compute its normal wide
    // framing first, then — only for a 2+ target STV lock — recompute framing against a synthetic
    // one-target list via the same private SingleTargetPositionAndSize the native single-lock path
    // itself uses, so the result is pixel-identical to "only the focused target was ever locked".
    // AimCamera() is re-invoked immediately after so the reframe applies this same tick rather than
    // lagging a frame behind Update()'s own next AimCamera() call.
    internal static class TgpSingleTargetView
    {
        internal static bool Stv { get; private set; }

        internal static void SetStv(bool on)
        {
            if (on == Stv) return;
            Stv = on;
            Plugin.Log?.LogInfo($"[NOXMFD] TGP view: {(Stv ? "STV" : "WTV")}.");
        }

        // Reused across ticks — one target, replaced in place, not reallocated (WeaponSelectors'
        // own _loadout scratch list is the precedent for this pattern in this codebase).
        private static readonly List<Unit> _singleTargetScratch = new List<Unit>(1);

        private static bool _reflectionTried;
        private static MethodInfo? _singleTargetPositionAndSizeMethod;
        private static FieldInfo? _targetPositionField;
        private static FieldInfo? _targetFovField;
        private static MethodInfo? _aimCameraMethod;

        private static bool Ensure()
        {
            if (_reflectionTried) return _singleTargetPositionAndSizeMethod != null;
            _reflectionTried = true;
            var t = typeof(TargetCam);
            _singleTargetPositionAndSizeMethod = t.GetMethod("SingleTargetPositionAndSize", BindingFlags.NonPublic | BindingFlags.Instance);
            _targetPositionField = t.GetField("targetPosition", BindingFlags.NonPublic | BindingFlags.Instance);
            _targetFovField = t.GetField("targetFOV", BindingFlags.NonPublic | BindingFlags.Instance);
            _aimCameraMethod = t.GetMethod("AimCamera", BindingFlags.NonPublic | BindingFlags.Instance);
            if (_singleTargetPositionAndSizeMethod == null || _targetPositionField == null ||
                _targetFovField == null || _aimCameraMethod == null)
                Plugin.Log?.LogWarning("[NOXMFD] TGP single-target view: could not locate TargetCam internals — STV disabled.");
            return _singleTargetPositionAndSizeMethod != null;
        }

        // Called from the SetTargetCam postfix, every tick a real (non-manual) lock exists. No-op
        // unless STV is on AND 2+ targets are locked — 0-1 already matches STV's own spec.
        internal static void ApplyIfActive(TargetCam tc, List<Unit> targets)
        {
            if (!Stv || targets.Count <= 1) return;
            if (!TargetUnitLookup.TryResolve(TargetFocus.Id, out Unit focused) || !targets.Contains(focused)) return;
            if (!Ensure()) return;

            _singleTargetScratch.Clear();
            _singleTargetScratch.Add(focused);
            var args = new object?[] { _singleTargetScratch, null, null };
            try
            {
                _singleTargetPositionAndSizeMethod!.Invoke(tc, args);
                _targetPositionField!.SetValue(tc, args[1]);
                _targetFovField!.SetValue(tc, args[2]);
                _aimCameraMethod!.Invoke(tc, null);
            }
            catch (Exception ex)
            {
                Plugin.Log?.LogDebug($"[NOXMFD] TGP single-target view: reframe failed: {ex.Message}");
            }
        }
    }
}
