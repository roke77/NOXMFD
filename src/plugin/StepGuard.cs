using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace NOXMFD
{
    // Failure reporting for the per-frame steps of MissionLifecycle.Update and TelemetryReader.Update.
    // Each step runs in its own try/catch so one throwing step can't skip the ones after it (a throw in
    // a squad drain would skip mission detection; one in PushSnapshot would freeze every display).
    // A step that throws every frame would flood the log, so the first failure of a step is logged with
    // its stack trace and repeats are counted and summarised at most once per RepeatMs.
    // Main thread only.
    internal static class StepGuard
    {
        internal static Action<string>? LogWarning;   // BepInEx-free seam, set in Plugin.Awake

        internal const long RepeatMs = 30000;

        private static readonly Stopwatch _clock = Stopwatch.StartNew();
        private static readonly Dictionary<string, (long LastLogMs, int Suppressed)> _state =
            new Dictionary<string, (long, int)>(StringComparer.Ordinal);

        internal static void Failed(string step, Exception ex) => Failed(step, ex, _clock.ElapsedMilliseconds);

        internal static void Failed(string step, Exception ex, long nowMs)
        {
            if (!_state.TryGetValue(step, out var s))
            {
                _state[step] = (nowMs, 0);
                LogWarning?.Invoke($"[NOXMFD] {step} failed: {ex}");
                return;
            }
            if (nowMs - s.LastLogMs < RepeatMs) { _state[step] = (s.LastLogMs, s.Suppressed + 1); return; }
            _state[step] = (nowMs, 0);
            LogWarning?.Invoke($"[NOXMFD] {step} still failing ({s.Suppressed + 1} times since the last report): {ex.GetType().Name}: {ex.Message}");
        }
    }
}
