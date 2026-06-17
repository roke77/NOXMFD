using System.Globalization;
using UnityEngine;

namespace NOTelemetryReader
{
    // A MonoBehaviour: Unity calls Update() on this every frame once it's attached
    // to a GameObject (which Plugin does when a mission starts).
    //
    // This is the whole point of the mod: read the game's live in-memory objects
    // and print their telemetry. There is no special "telemetry API" in the game --
    // we just read public fields off the same objects the game uses to fly the plane.
    internal class TelemetryReader : MonoBehaviour
    {
        // Throttle: log roughly once per second instead of every frame (~60/s).
        private const float LogIntervalSeconds = 1f;
        private float _timer;

        private void Update()
        {
            // Time.deltaTime is the seconds elapsed since the last frame.
            _timer += Time.deltaTime;
            if (_timer < LogIntervalSeconds)
                return;
            _timer = 0f;

            LogLocalAircraft();
            LogWorldSummary();
        }

        // The player's own aircraft. GameManager hands it to us via an out-parameter.
        private void LogLocalAircraft()
        {
            GameManager.GetLocalAircraft(out Aircraft aircraft);
            if (aircraft == null)
                return;

            // World position. Note: the game uses a "floating origin", so for a real
            // mod you'd convert to global coords (see NOBlackBox's transform.GlobalX()).
            // For a hello-world, the raw transform position is plenty.
            Vector3 pos = aircraft.transform.position;

            Plugin.Log?.LogInfo(string.Format(
                CultureInfo.InvariantCulture,
                "[ME] {0} | pos=({1:0.0}, {2:0.0}, {3:0.0}) | TAS={4:0.0} m/s | AGL={5:0.0} m | gear={6}",
                aircraft.definition.unitName,
                pos.x, pos.y, pos.z,
                aircraft.speed,
                Mathf.Max(0f, aircraft.radarAlt),
                aircraft.gearDeployed ? "down" : "up"));
        }

        // Everything in the world right now. This is the same discovery call
        // NOBlackBox uses (Recorder_mono.cs) -- it returns every aircraft, ship,
        // missile, ground vehicle, building, etc. currently spawned.
        private void LogWorldSummary()
        {
            Unit[] units = Object.FindObjectsByType<Unit>(FindObjectsSortMode.None);
            int aircraftCount = 0;
            foreach (Unit unit in units)
                if (unit is Aircraft)
                    aircraftCount++;

            Plugin.Log?.LogInfo(string.Format(
                CultureInfo.InvariantCulture,
                "[WORLD] {0} units total ({1} aircraft)",
                units.Length, aircraftCount));
        }
    }
}
