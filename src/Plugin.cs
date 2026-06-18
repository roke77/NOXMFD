using BepInEx;
using BepInEx.Logging;
using UnityEngine;

namespace NOTelemetryReader
{
    [BepInPlugin("com.roque.NOTelemetryReader", "NOTelemetryReader", "0.1.0")]
    [BepInProcess("NuclearOption.exe")]
    [BepInProcess("NuclearOptionServer.exe")]
    public class Plugin : BaseUnityPlugin
    {
        internal static ManualLogSource? Log;

        private GameObject? _readerObject;
        private bool        _readerActive;

        private void Awake()
        {
            Log = Logger;
            TelemetryServer.Start();
            Log.LogInfo("NOTelemetryReader loaded. Waiting for a mission to start...");
        }

        private void OnDestroy()
        {
            StopReader();
            TelemetryServer.Stop();
        }

        private void Update()
        {
            bool missionRunning = MissionManager.IsRunning;

            if (missionRunning && !_readerActive)
                StartReader();
            else if (!missionRunning && _readerActive)
                StopReader();
        }

        private void StartReader()
        {
            _readerActive  = true;
            _readerObject  = new GameObject("NOTelemetryReader_Runner");
            _readerObject.AddComponent<TelemetryReader>();
            Log?.LogInfo("Mission started -> telemetry reader ON.");
        }

        private void StopReader()
        {
            if (!_readerActive) return;
            _readerActive = false;
            if (_readerObject != null)
                Destroy(_readerObject);
            _readerObject = null;
            Log?.LogInfo("Mission ended -> telemetry reader OFF.");
        }
    }
}
