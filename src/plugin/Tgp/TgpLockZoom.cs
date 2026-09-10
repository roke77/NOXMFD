using UnityEngine;

namespace NOXMFD
{
    // TGP page's Z+/Z- during a real (non-manual) lock — issue #83. The native camera always
    // auto-computes targetFOV to frame the current lock (tight on one target, wide enough for
    // several); this lets the pilot nudge that computed value up/down through the same fixed
    // magnification ladder Z+/Z- already use in manual control (TgpManualAimMath.ZoomLevelsMag),
    // rather than only ever seeing whatever the game decided.
    //
    // "The default zoom level is set by the game on first lock" (the feature ask): no override is
    // active until the pilot actually presses Z+/Z- at least once, so a fresh lock always starts at
    // the native auto-computed framing untouched. The first press seeds its own starting level from
    // whatever's on screen at that moment (the live targetFOV — the native default, or already
    // reframed by TgpSingleTargetView if STV is on) rather than some fixed starting point, so the
    // very first step always feels like "one notch from here." Tick() (called every frame from
    // TelemetryReader, regardless of camera/page state) watches TargetFocus.Id for a 0-to-locked
    // transition — the same "nothing focused" signal TargetFocus.cs itself uses — and clears the
    // override right as a brand new lock begins, so the NEXT lock again starts at the game's own
    // default per the same rule.
    //
    // Applied as a HarmonyPatches.cs postfix on TargetCam.SetTargetCam, same shape as its
    // TgpSingleTargetView neighbor — see that postfix for why both share one AimCamera() call at
    // the end rather than each invoking it independently (ordering: a naive per-feature AimCamera()
    // call would let whichever patch runs second silently clobber the other's targetFOV).
    internal static class TgpLockZoom
    {
        private static bool _active;
        private static float _overrideFov;
        private static bool _wasLocked;

        // Called every frame (TelemetryReader.Update), independent of whether the TGP page/camera
        // is even active — TargetFocus.Id already tracks "is anything locked" across the whole mod,
        // regardless of TGP specifically, so this just watches it for a fresh lock. Comparing
        // against the previous tick rather than checking "count == 1" or similar avoids ever
        // needing to know how many targets are locked; only the 0-to-nonzero edge matters.
        internal static void Tick()
        {
            bool locked = TargetFocus.Id != 0;
            if (locked && !_wasLocked) _active = false;
            _wasLocked = locked;
        }

        // MAN's own Z+/Z- pair (TgpManualControl.StepZoom) handles manual mode; the command
        // dispatcher routes here only while ManualMode is off, but this self-guards too — matching
        // TgpManualControl's own defensive shape (Reset/TogglePointTrack, etc. all re-check
        // ManualMode even though today only one call path reaches them) — in case a future keybind
        // ever calls this directly without going through that routing.
        internal static void StepZoom(int dir)
        {
            if (TgpManualControl.ManualMode) return;
            if (!GameManager.GetLocalAircraft(out Aircraft ac) || ac.targetCam == null) return;
            if (!TgpLockCameraAccess.Ensure()) return;

            float baseline = _active ? _overrideFov : TgpLockCameraAccess.GetTargetFov(ac.targetCam);
            if (baseline <= 0f) baseline = TgpManualControl.MaxFov;   // defensive: a zero/negative read would divide-by-zero below
            float targetMag = TgpManualAimMath.NextZoomLevelMag(10f / baseline, dir);
            _overrideFov = Mathf.Clamp(10f / targetMag, TgpManualControl.MinFov, TgpManualControl.MaxFov);
            _active = true;
        }

        // Called from the SetTargetCam postfix every tick a real (non-manual) lock exists. Returns
        // whether it actually overrode the FOV, so the caller knows whether AimCamera() needs to be
        // invoked afterward.
        internal static bool ApplyIfActive(TargetCam tc)
        {
            if (!_active) return false;
            TgpLockCameraAccess.SetTargetFov(tc, _overrideFov);
            return true;
        }
    }
}
