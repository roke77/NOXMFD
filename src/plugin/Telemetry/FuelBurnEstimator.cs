using System;

namespace NOXMFD
{
    // Smoothed fuel-burn rate from successive total-fuel readings (docs/eng-page.md). The game has
    // no fuel-flow value of its own: engines draw through Aircraft.UseFuel and leaking tanks drain
    // their own fuelMass, so the drop in total mass over time is the only measure that counts both.
    //
    // Fed game time (Time.time), so a paused game produces no samples rather than a zero rate.
    // Unity-free so tools/tests can compile it directly.
    internal sealed class FuelBurnEstimator
    {
        // A sample spans at least this long: fuelMass falls in tiny per-physics-step amounts, and a
        // short window would make each rate sample mostly tick jitter.
        internal const float SampleSeconds = 1f;
        // Exponential smoothing time constant: settles within a few seconds of a throttle change
        // without the readout tracking every afterburner blip.
        internal const float TimeConstantSeconds = 10f;

        private float _lastKg = -1f;
        private float _lastTime;
        private float _rate = -1f;

        // kg/s; -1 until the first full sample window has passed.
        public float RateKgPerSec => _rate;

        public void Reset()
        {
            _lastKg = -1f;
            _rate = -1f;
        }

        public void Sample(float fuelKg, float time)
        {
            if (float.IsNaN(fuelKg) || float.IsInfinity(fuelKg)) return;
            // First reading, or the clock went backwards (mission restart): start a new baseline.
            if (_lastKg < 0f || time < _lastTime)
            {
                _lastKg = fuelKg;
                _lastTime = time;
                return;
            }
            float dt = time - _lastTime;
            if (dt < SampleSeconds) return;

            float burned = _lastKg - fuelKg;
            _lastKg = fuelKg;
            _lastTime = time;
            // Fuel went up (refuel/rearm): the old rate describes a different load, so drop it and
            // let the next window start fresh.
            if (burned < 0f) { _rate = -1f; return; }

            float sampleRate = burned / dt;
            if (_rate < 0f) { _rate = sampleRate; return; }
            float alpha = 1f - (float)Math.Exp(-dt / TimeConstantSeconds);
            _rate += (sampleRate - _rate) * alpha;
        }
    }
}
