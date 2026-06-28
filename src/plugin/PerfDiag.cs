using System.Collections.Generic;
using System.Diagnostics;
using System.Text;

namespace NOXMFD
{
    // Temporary performance instrumentation (Step 0 of docs/performance.md).
    //
    // Accumulates labeled timing samples from ANY thread (the Unity main thread for
    // ScanWorld/BuildUnits/PushSnapshot, the background SSE threads for Serialize) and
    // logs a rollup every FlushIntervalSec from the main thread. Gated by Enabled so it
    // costs nothing when off; toggle it live via the "Diagnostics > PerfLogging" entry in
    // the F1 config menu.
    //
    // Rollup line format (one per flush):
    //   [NOXMFD][perf] units=42 contacts=37  |  ScanWorld: n=5 avg=2.103ms max=3.880ms
    //   |  BuildUnits: n=50 avg=0.412ms ...  |  Serialize: n=50 avg=0.690ms ... avgBytes=8120 maxBytes=9433
    //
    // To turn per-second main-thread cost into a number: PushSnapshot avg × 10 (10 Hz)
    // + ScanWorld avg × 1 (1 Hz). Serialize runs on background threads (per client), so
    // its n reflects clients × 10 Hz — useful for seeing the per-client duplication.
    internal static class PerfDiag
    {
        // Set from the "Diagnostics > PerfLogging" ConfigEntry in Plugin.Awake.
        internal static bool Enabled;

        private const float FlushIntervalSec = 5f;

        private static readonly double TickToMs = 1000.0 / Stopwatch.Frequency;

        private sealed class Stat
        {
            public long   Count;
            public double TotalMs;
            public double MaxMs;
            public long   TotalBytes;
            public long   MaxBytes;
        }

        private static readonly Dictionary<string, Stat> _stats = new Dictionary<string, Stat>();
        private static readonly object _lock = new object();
        private static float _sinceFlush;

        // Record one timing sample, given a Stopwatch.GetTimestamp() taken before the work.
        // Optional payload size in bytes (for Serialize). Allocation-free on the hot path.
        public static void RecordSince(string label, long startTimestamp, long bytes = -1)
        {
            if (!Enabled) return;
            double ms = (Stopwatch.GetTimestamp() - startTimestamp) * TickToMs;
            lock (_lock)
            {
                if (!_stats.TryGetValue(label, out Stat s)) { s = new Stat(); _stats[label] = s; }
                s.Count++;
                s.TotalMs += ms;
                if (ms > s.MaxMs) s.MaxMs = ms;
                if (bytes >= 0) { s.TotalBytes += bytes; if (bytes > s.MaxBytes) s.MaxBytes = bytes; }
            }
        }

        // Call once per frame from the Unity main thread; emits a rollup every interval.
        public static void Tick(float dt, int unitCount, int contactCount)
        {
            if (!Enabled) return;
            _sinceFlush += dt;
            if (_sinceFlush < FlushIntervalSec) return;
            _sinceFlush = 0f;
            Flush(unitCount, contactCount);
        }

        private static void Flush(int unitCount, int contactCount)
        {
            lock (_lock)
            {
                if (_stats.Count == 0) return;
                var sb = new StringBuilder(256);
                sb.Append("[NOXMFD][perf] units=").Append(unitCount).Append(" contacts=").Append(contactCount);
                foreach (var kv in _stats)
                {
                    Stat s = kv.Value;
                    double avg = s.Count > 0 ? s.TotalMs / s.Count : 0.0;
                    sb.Append("  |  ").Append(kv.Key)
                      .Append(": n=").Append(s.Count)
                      .Append(" avg=").Append(avg.ToString("0.000")).Append("ms")
                      .Append(" max=").Append(s.MaxMs.ToString("0.000")).Append("ms");
                    if (s.TotalBytes > 0)
                    {
                        double avgB = s.TotalBytes / (double)s.Count;
                        sb.Append(" avgBytes=").Append(avgB.ToString("0")).Append(" maxBytes=").Append(s.MaxBytes);
                    }
                    // Reset the window so each rollup reflects only the last interval.
                    s.Count = 0; s.TotalMs = 0; s.MaxMs = 0; s.TotalBytes = 0; s.MaxBytes = 0;
                }
                Plugin.Log?.LogInfo(sb.ToString());
            }
        }
    }
}
