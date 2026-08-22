using UnityEngine;

namespace NOXMFD
{
    // Lives on a DontDestroyOnLoad GameObject created in Plugin.OnSceneLoaded. Spawns the
    // TelemetryReader when a mission is running and tears it down when it ends.
    internal class MissionLifecycle : MonoBehaviour
    {
        private GameObject? _readerObject;
        private bool        _readerActive;

        private void Update()
        {
            // Polled here, not in the mission-scoped TelemetryReader, so input works at the main
            // menu (keybind capture flow runs before a mission exists).
            Keybinds.Poll();

            // Drained here so the /keybinds page works from the main menu with no mission-scoped
            // reader running. Handlers validate against live state and no-op without a mission.
            CommandDispatcher.Drain();
            ExtensionRegistry.Drain();

            bool missionRunning = MissionManager.IsRunning;
            // A mission can be running with no local aircraft yet (spawn/loadout screen), so this
            // is tracked separately from PushSnapshot's aircraft-gated push.
            TelemetryServer.SetMissionRunning(missionRunning);

            if (missionRunning && !_readerActive)
                StartReader();
            else if (!missionRunning && _readerActive)
                StopReader();
        }

        private void OnDestroy()
        {
            StopReader();
            // TelemetryServer is static and survives for the process lifetime; not stopped here.
        }

        private void StartReader()
        {
            _readerActive  = true;
            _readerObject  = new GameObject("NOXMFD_Runner");
            _readerObject.AddComponent<TelemetryReader>();
            _readerObject.AddComponent<HudDeclutter>();
            _readerObject.AddComponent<HudWaypointCue>();
            Plugin.Log?.LogInfo("Mission started -> telemetry reader ON.");
        }

        private void StopReader()
        {
            if (!_readerActive) return;
            _readerActive = false;
            if (_readerObject != null)
                Destroy(_readerObject);
            _readerObject = null;
            TelemetryServer.Reset();
            Plugin.Log?.LogInfo("Mission ended -> telemetry reader OFF.");
        }
    }
}
