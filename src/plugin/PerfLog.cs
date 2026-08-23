using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace NOXMFD
{
    // ponytail: temporary rollup timer for the TGP-safety-baseline investigation
    // (docs/performance.md, "cfg-rates branch" + docs/tgp-high-quality-mode.md). Narrower than the
    // older, already-removed PerfDiag apparatus: wrap a hot-path block in
    // `using (PerfLog.Time("name")) { ... }` and it logs avg/max/count per name every 5s via the
    // normal BepInEx log. Same lifecycle as PerfDiag before it — delete this file and its call
    // sites once this investigation's findings are banked into docs/performance.md; don't leave it
    // wired into normal play.
    internal static class PerfLog
    {
        private const float  RollupInterval = 5f;
        private const double SpikeThresholdMs = 20.0; // ~1.2 frames at 60fps; Unity clamps deltaTime at 100ms

        private sealed class Stat
        {
            public double SumMs;
            public double MaxMs;
            public int    Count;
            public int    Spikes;
        }

        private static readonly Dictionary<string, Stat> _blocks = new Dictionary<string, Stat>();
        private static readonly Dictionary<string, Stat> _frames = new Dictionary<string, Stat>();
        private static int   _tgpSkipped;
        private static float _timer;

        // Returns the struct directly (not boxed as IDisposable) — `using` resolves Dispose() by
        // pattern match, so this timer allocates nothing in the hot paths it measures.
        public static Scope Time(string name) => new Scope(name);

        internal readonly struct Scope : IDisposable
        {
            private readonly string _name;
            private readonly long   _start;
            public Scope(string name) { _name = name; _start = Stopwatch.GetTimestamp(); }
            public void Dispose() => Record(_blocks, _name, ElapsedMs(_start));
        }

        // Call once per Update with the frame's real dt, gated on whether TGP currently has
        // subscribers — this is the frame(tgpOpen) vs frame(tgpClosed) split that first surfaced
        // the TGP feed's GPU cost (docs/performance.md, Finding 1).
        public static void Frame(float dtMs, bool tgpOpen) => Record(_frames, tgpOpen ? "tgpOpen" : "tgpClosed", dtMs);

        // TgpFeed's _readbackInFlight guard calls this each time a capture tick is dropped because
        // the previous AsyncGPUReadback hadn't completed yet.
        public static void RecordTgpSkip() => _tgpSkipped++;

        private static double ElapsedMs(long start) => (Stopwatch.GetTimestamp() - start) * 1000.0 / Stopwatch.Frequency;

        private static void Record(Dictionary<string, Stat> table, string name, double ms)
        {
            if (!table.TryGetValue(name, out Stat s)) { s = new Stat(); table[name] = s; }
            s.SumMs += ms;
            if (ms > s.MaxMs) s.MaxMs = ms;
            s.Count++;
            if (ms > SpikeThresholdMs) s.Spikes++;
        }

        // Drives the 5s rollup log. Call once per Update with the real dt.
        public static void Tick(float dt)
        {
            _timer += dt;
            if (_timer < RollupInterval) return;
            _timer = 0f;
            Flush();
        }

        private static void Flush()
        {
            foreach (KeyValuePair<string, Stat> kv in _blocks)
                Plugin.Log?.LogInfo($"[PerfLog] {kv.Key} avg={kv.Value.SumMs / kv.Value.Count:0.000}ms max={kv.Value.MaxMs:0.000}ms n={kv.Value.Count}");
            foreach (KeyValuePair<string, Stat> kv in _frames)
                Plugin.Log?.LogInfo($"[PerfLog] frame({kv.Key}) avg={kv.Value.SumMs / kv.Value.Count:0.000}ms max={kv.Value.MaxMs:0.000}ms spikes={kv.Value.Spikes}/{kv.Value.Count}");
            if (_tgpSkipped > 0 || _frames.ContainsKey("tgpOpen"))
                Plugin.Log?.LogInfo($"[PerfLog] tgpSkipped={_tgpSkipped}");

            _blocks.Clear();
            _frames.Clear();
            _tgpSkipped = 0;
        }
    }
}
