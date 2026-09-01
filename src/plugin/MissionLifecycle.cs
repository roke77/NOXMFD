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

            // Drain the squad channel here for the same reason: squad formation and route planning
            // happen BEFORE a flight, so protocol traffic has to flow at the main menu, not only
            // during a mission (docs/squadron-transport.md). No-ops when Steam is unavailable.
            Squadron.Poll();
            Squad.Drain();
            // Presence.cs rides the same shared inbox with its own cursor (see its own header
            // comment) — drained here too so "who's running NOXMFD" stays current at the main menu.
            Presence.Drain();

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
            // The process-wide server is stopped by Plugin's static Application.quitting handler.
        }

        private void StartReader()
        {
            _readerActive  = true;
            _readerObject  = new GameObject("NOXMFD_Runner");
            _readerObject.AddComponent<TelemetryReader>();
            _readerObject.AddComponent<HudDeclutter>();
            _readerObject.AddComponent<HudPower>();
            _readerObject.AddComponent<HudWaypointCue>();
            _readerObject.AddComponent<HudTgpCue>();
            _readerObject.AddComponent<HudTtiCue>();
            _readerObject.AddComponent<HudFocusMark>();
            _readerObject.AddComponent<HudSquadTargetMark>();   // issue #49
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
            // issue #47 follow-up audit's gap #4: PlayerRoster.Refresh() only ever ran from the
            // reader we just destroyed, so its aircraft-by-SteamID dictionaries otherwise freeze at
            // their last in-mission values and SQD keeps showing everyone's last mission's aircraft
            // indefinitely at the main menu. Refresh() already clears everything itself the moment
            // GameManager.GetLocalHQ fails (its own header comment: "the main menu, or between
            // missions") — calling it here, on the main thread, right as the mission ends, just
            // makes that documented behavior actually happen instead of waiting for a reader that no
            // longer exists.
            PlayerRoster.Refresh();
            Plugin.Log?.LogInfo("Mission ended -> telemetry reader OFF.");
        }
    }
}
