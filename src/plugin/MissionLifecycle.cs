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
            // the /keybinds page before a mission exists. Deploy no-ops when there's no local aircraft.
            Keybinds.Poll();

            // Drain inbound web commands here too (main thread, persistent) — the /keybinds page writes
            // binds from the main menu, where no mission-scoped reader exists to drain the queue. Every
            // handler validates against live state, so gameplay commands just no-op without a mission.
            CommandDispatcher.Drain();
            // Same reasoning, for extension mods' own commands (docs/extensions-api.md) — an
            // extension's /ext/<id>/command endpoint should work at the main menu too.
            ExtensionRegistry.Drain();

            // Drain the squad channel here for the same reason: squad formation and route planning
            // happen BEFORE a flight, so protocol traffic has to flow at the main menu, not only
            // during a mission (docs/squadron-transport.md). No-ops when Steam is unavailable.
            Squadron.Poll();
            Squad.Drain();
            // Presence.cs rides the same shared inbox with its own cursor (see its own header
            // comment) — drained here too so "who's running NOXMFD" stays current at the main menu.
            Presence.Drain();

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
            _readerObject.AddComponent<HudWaypointCue>(); // draws the waypoint bug on the heading tape
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
