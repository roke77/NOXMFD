using UnityEngine;

namespace NOXMFD
{
    // Persistent mission-polling host. Lives on a DontDestroyOnLoad GameObject we create
    // ourselves once a real scene exists (see Plugin.OnSceneLoaded). Spawns the
    // TelemetryReader when a mission is running and tears it down when it ends.
    internal class MissionLifecycle : MonoBehaviour
    {
        private GameObject? _readerObject;
        private bool        _readerActive;

        private void Update()
        {
            // Poll the countermeasure keybinds here (not in the mission-scoped TelemetryReader) so input
            // works at the main menu too — the joystick-button CAPTURE flow needs to run while you're in
            // the F1 config menu before a mission exists. Deploy no-ops when there's no local aircraft.
            Keybinds.Poll();

            bool missionRunning = MissionManager.IsRunning;

            if (missionRunning && !_readerActive)
                StartReader();
            else if (!missionRunning && _readerActive)
                StopReader();
        }

        private void OnDestroy()
        {
            StopReader();
            // Intentionally NOT stopping TelemetryServer here — it's static and survives
            // for the process lifetime, even if this worker is somehow torn down.
        }

        private void StartReader()
        {
            _readerActive  = true;
            _readerObject  = new GameObject("NOXMFD_Runner");
            _readerObject.AddComponent<TelemetryReader>();
            _readerObject.AddComponent<HudDeclutter>();   // hides native HUD elements per HudDeclutterConfig
            Plugin.Log?.LogInfo("Mission started -> telemetry reader ON.");
        }

        private void StopReader()
        {
            if (!_readerActive) return;
            _readerActive = false;
            if (_readerObject != null)
                Destroy(_readerObject);
            _readerObject = null;
            TelemetryServer.Reset();   // clear per-mission data so the client wipes its display
            Plugin.Log?.LogInfo("Mission ended -> telemetry reader OFF.");
        }
    }
}
