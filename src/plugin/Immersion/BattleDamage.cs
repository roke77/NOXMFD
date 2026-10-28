namespace NOXMFD
{
    // Battle damage (issue #110, docs/battle-damage.md): whether the local aircraft's radar,
    // targeting camera and laser designator are working, lost, or never fitted. The game has no
    // display-damage state; it destroys each system's component when its part is shot off or hits
    // 0 HP, so TelemetryReader feeds in whether each component is still alive and this decides the
    // rest. Unity-free so tools/tests compiles it directly.
    internal static class BattleDamage
    {
        internal enum SysState { None, Ok, Fail }

        // "Fitted" latches the first time a system is seen alive on the current aircraft, so a
        // destroyed component reads Fail while one the airframe never carried stays None. Latching
        // rather than inspecting the dead reference: the game re-points some fields on destroy
        // (Radar.OnDestroy → SetRadar(null) falls back to defaultRadar).
        private static object? _aircraft;
        private static bool _radarFitted, _tgpFitted, _laserFitted;
        private static bool _on;
        private static SysState _radar, _tgp, _laser;

        // The SSE hub change-gates on this string (SseHub's "sysdmg" event); rebuilt only on a change.
        internal static volatile string StateJson = Json(false, SysState.None, SysState.None, SysState.None);

        internal static SysState Track(ref bool fitted, bool alive)
        {
            if (alive) { fitted = true; return SysState.Ok; }
            return fitted ? SysState.Fail : SysState.None;
        }

        // aircraft: the local aircraft, or null with none (everything reads None). A new reference
        // is a respawn, so the fitted latches start over.
        internal static void Update(object? aircraft, bool on, bool radarAlive, bool tgpAlive, bool laserAlive)
        {
            if (!ReferenceEquals(aircraft, _aircraft))
            {
                _aircraft = aircraft;
                _radarFitted = _tgpFitted = _laserFitted = false;
            }
            SysState radar = Track(ref _radarFitted, aircraft != null && radarAlive);
            SysState tgp   = Track(ref _tgpFitted,   aircraft != null && tgpAlive);
            SysState laser = Track(ref _laserFitted, aircraft != null && laserAlive);
            if (on == _on && radar == _radar && tgp == _tgp && laser == _laser) return;
            _on = on; _radar = radar; _tgp = tgp; _laser = laser;
            StateJson = Json(on, radar, tgp, laser);
        }

        // Main-thread reads for the internal MFD panes, same rules as services/battle-damage.js.
        internal static bool RadarFailed => _on && _radar == SysState.Fail;
        internal static bool TgpFailed   => _on && _tgp == SysState.Fail;
        internal static bool LaserFailed => _on && _laser == SysState.Fail && _tgp != SysState.Fail;

        internal static string Json(bool on, SysState radar, SysState tgp, SysState laser) =>
            "{\"on\":" + (on ? "true" : "false") +
            ",\"radar\":\"" + Name(radar) + "\",\"tgp\":\"" + Name(tgp) + "\",\"laser\":\"" + Name(laser) + "\"}";

        private static string Name(SysState s) =>
            s == SysState.Ok ? "ok" : s == SysState.Fail ? "fail" : "none";
    }
}
