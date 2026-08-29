using UnityEngine;

namespace NOXMFD
{
    // Full HUD kill-switch (issue #69). Unlike HudDeclutter's per-widget hiding, Power OFF hides the
    // entire in-cockpit HUD in one shot via FlightHud.EnableCanvas — CombatHUD instantiates every
    // weapon/boresight/missile UI as a child of FlightHud.i.GetHUDCenter() (_scratch/full/CombatHUD.cs),
    // the same canvas tree, so a single SetActive(false) already covers the boxed readouts, compass,
    // weapon panel, unit markers, and this mod's own added cues — no per-element enumeration needed.
    //
    // FlightHud.EnableCanvas is itself only called by the game on specific event-driven transitions
    // (camera state changes, pause/resume — _scratch/full/GameplayUI.cs), not polled every frame, so
    // we reassert "off" every tick to win over any native re-enable while Power is off, and restore
    // once ourselves when Power comes back on rather than waiting for a native transition that might
    // never happen.
    internal class HudPower : MonoBehaviour
    {
        private bool _forcedOff;

        private void Update()
        {
            if (!ImmersionState.PowerOn)
            {
                FlightHud.EnableCanvas(false);
                _forcedOff = true;
                return;
            }
            if (!_forcedOff) return;
            // Only restore in cockpit view — matches GameplayUI.ResumeGame's own guard, so powering
            // back on mid chase-cam doesn't fight the native camera-driven hide.
            CameraStateManager cam = SceneSingleton<CameraStateManager>.i;
            if (cam != null && cam.currentState != cam.cockpitState) return;
            FlightHud.EnableCanvas(true);
            _forcedOff = false;
        }
    }
}
