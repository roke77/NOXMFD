using UnityEngine;

namespace NOXMFD
{
    // Persistent mission-polling host. Lives on a DontDestroyOnLoad GameObject we create
    // ourselves once a real scene exists (see Plugin.OnSceneLoaded). Spawns the
    // TelemetryReader when a mission is running and tears it down when it ends.
    internal class Worker : MonoBehaviour
    {
        private GameObject? _readerObject;
        private bool        _readerActive;

        private void Update()
        {
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
            _readerObject.AddComponent<HudController>();   // hides native HUD elements per HudConfig
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
