using System.Collections.Generic;

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
    // one-target list via TgpLockCameraAccess (the same private SingleTargetPositionAndSize the
    // native single-lock path itself uses, so the result is pixel-identical to "only the focused
    // target was ever locked"). The postfix invokes AimCamera() itself once, after this AND
    // TgpLockZoom's own override have both had a chance to run — see HarmonyPatches.cs — so this
    // class only ever sets targetPosition/targetFOV, never calls AimCamera directly.
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

        // Called from the SetTargetCam postfix every tick a real (non-manual) lock exists — resolves
        // and validates everything itself, same self-contained shape TgpManualControl.SetIR already
        // uses. No-op unless STV is on AND 2+ targets are locked — 0-1 already matches STV's own
        // spec. Returns whether it actually reframed, so the caller knows whether AimCamera() needs
        // to be invoked afterward.
        internal static bool ApplyIfActive(TargetCam tc)
        {
            if (!Stv) return false;
            if (!GameManager.GetLocalAircraft(out Aircraft ac) || ac.weaponManager == null) return false;
            List<Unit>? targets = ac.weaponManager.GetTargetList();
            if (targets == null || targets.Count <= 1) return false;
            if (!TargetUnitLookup.TryResolve(TargetFocus.Id, out Unit focused) || !targets.Contains(focused)) return false;

            _singleTargetScratch.Clear();
            _singleTargetScratch.Add(focused);
            if (!TgpLockCameraAccess.TryComputeSingleTargetFraming(tc, _singleTargetScratch, out GlobalPosition position, out float fov))
                return false;
            TgpLockCameraAccess.SetTargetPosition(tc, position);
            TgpLockCameraAccess.SetTargetFov(tc, fov);
            return true;
        }
    }
}
