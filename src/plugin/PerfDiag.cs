using System;
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
    //   [NOXMFD][perf] fps avg=58.2 1%low=31.4 min=22.1 (frames=291)  |  gc d0=3 d1=1 d2=0
    //   |  units=42 contacts=37  |  ScanWorld: n=5 avg=2.103ms max=3.880ms
    //   |  BuildUnits: n=50 avg=0.412ms ...  |  Serialize: n=50 avg=0.690ms ... avgBytes=8120 maxBytes=9433
    //
    // FPS is the headline A/B metric (docs/performance.md "Next steps"): avg over the window,
    // 1%low = reciprocal of the 99th-percentile frame time (the stutter you feel), min = the
    // single worst frame. gc d0/d1/d2 = GC.CollectionCount deltas per gen over the window
    // (allocation pressure — the other blind spot the CPU timers miss). FPS/gc are sampled
    // every frame; they're only logged while a mission is running (the reader drives Tick).
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

        // Frame-time stats for the window (main thread only — no lock needed).
        private static int    _frameCount;
        private static double _frameSumSec;   // total wall time across the window's frames
        private static double _frameMaxSec;    // worst single frame (-> min FPS)
        // ponytail: capped per-window frame buffer for the 1%-low percentile. 5 s × ~300 fps
        // never reaches 4096; frames past the cap are dropped from the percentile only (avg/min
        // stay exact). Upgrade path if ever hit: reservoir sampling.
        private static readonly double[] _frameDt = new double[4096];
        private static int _frameDtN;

        // GC collection counts at the start of the current window (delta logged on flush).
        private static int  _gc0Base, _gc1Base, _gc2Base;
        private static bool _gcBaseSet;

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

            // Frame-time sampling, every frame.
            _frameCount++;
            _frameSumSec += dt;
            if (dt > _frameMaxSec) _frameMaxSec = dt;
            if (_frameDtN < _frameDt.Length) _frameDt[_frameDtN++] = dt;
            if (!_gcBaseSet)
            {
                _gc0Base = GC.CollectionCount(0);
                _gc1Base = GC.CollectionCount(1);
                _gc2Base = GC.CollectionCount(2);
                _gcBaseSet = true;
            }

            _sinceFlush += dt;
            if (_sinceFlush < FlushIntervalSec) return;
            _sinceFlush = 0f;
            Flush(unitCount, contactCount);
        }

        // 1%-low FPS = reciprocal of the 99th-percentile frame time over the window.
        private static double OnePercentLowFps()
        {
            if (_frameDtN == 0) return 0.0;
            Array.Sort(_frameDt, 0, _frameDtN);
            int idx = (int)(_frameDtN * 0.99);
            if (idx >= _frameDtN) idx = _frameDtN - 1;
            double dt = _frameDt[idx];
            return dt > 0.0 ? 1.0 / dt : 0.0;
        }

        private static void Flush(int unitCount, int contactCount)
        {
            // Frame-time + GC stats first (main-thread fields, computed outside the path lock).
            double avgFps = _frameSumSec > 0.0 ? _frameCount / _frameSumSec : 0.0;
            double minFps = _frameMaxSec > 0.0 ? 1.0 / _frameMaxSec : 0.0;
            double lowFps = OnePercentLowFps();
            int frames = _frameCount;
            int d0 = GC.CollectionCount(0) - _gc0Base;
            int d1 = GC.CollectionCount(1) - _gc1Base;
            int d2 = GC.CollectionCount(2) - _gc2Base;
            // Reset the frame/GC window so each rollup reflects only the last interval.
            _frameCount = 0; _frameSumSec = 0.0; _frameMaxSec = 0.0; _frameDtN = 0; _gcBaseSet = false;

            lock (_lock)
            {
                var sb = new StringBuilder(320);
                sb.Append("[NOXMFD][perf] fps avg=").Append(avgFps.ToString("0.0"))
                  .Append(" 1%low=").Append(lowFps.ToString("0.0"))
                  .Append(" min=").Append(minFps.ToString("0.0"))
                  .Append(" (frames=").Append(frames).Append(')')
                  .Append("  |  gc d0=").Append(d0).Append(" d1=").Append(d1).Append(" d2=").Append(d2)
                  .Append("  |  units=").Append(unitCount).Append(" contacts=").Append(contactCount);
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
