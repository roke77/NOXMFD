using BepInEx;
using BepInEx.Logging;
using UnityEngine;

namespace NOTelemetryReader
{
    // [BepInPlugin] registers this class with BepInEx. The GUID must be unique;
    // the name and version show up in the BepInEx console at startup.
    // [BepInProcess] restricts the plugin to these executables (game + dedicated server).
    [BepInPlugin("com.roque.NOTelemetryReader", "NOTelemetryReader", "0.1.0")]
    [BepInProcess("NuclearOption.exe")]
    [BepInProcess("NuclearOptionServer.exe")]
    public class Plugin : BaseUnityPlugin
    {
        // A static logger so our MonoBehaviour (TelemetryReader) can log too.
        internal static ManualLogSource? Log;

        // The GameObject that holds our per-frame TelemetryReader component.
        // We create it when a mission starts and destroy it when the mission ends.
        private GameObject? _readerObject;
        private bool _readerActive;

        // Awake() is called once by Unity when BepInEx loads this plugin at game start.
        private void Awake()
        {
            Log = Logger;
            Log.LogInfo("NOTelemetryReader loaded. Waiting for a mission to start...");
        }

        // Update() runs every frame for the lifetime of the game process.
        // We use it purely to watch whether a mission is running, and to
        // spawn/destroy our telemetry reader accordingly.
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
            _readerActive = true;
            _readerObject = new GameObject("NOTelemetryReader_Runner");
            // Adding the component makes Unity start calling its Update() every frame.
            _readerObject.AddComponent<TelemetryReader>();
            Log?.LogInfo("Mission started -> telemetry reader ON.");
        }

        private void StopReader()
        {
            _readerActive = false;
            if (_readerObject != null)
                Destroy(_readerObject);
            _readerObject = null;
            Log?.LogInfo("Mission ended -> telemetry reader OFF.");
        }
    }
}
