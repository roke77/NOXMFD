// ADI/HSI display math (docs/adi-hsi-pages.md) — pure, DOM-free, so adi-math.test.js can check it
// in Node. Smoothing between telemetry frames and the game's unit setting.
(function (root) {
  // Telemetry arrives at FastHz (10 Hz by default); the pages ease toward each new frame on every
  // display frame. TAU is the ease time constant: about one frame of lag at 10 Hz.
  var TAU = 0.1;

  function approach(current, target, dt) {
    return target + (current - target) * Math.exp(-dt / TAU);
  }

  // Signed shortest difference b - a in degrees, in [-180, 180).
  function angleDelta(a, b) {
    return ((b - a) % 360 + 540) % 360 - 180;
  }

  // Eases an angle the short way round, so heading 359 -> 1 doesn't spin through 180.
  function approachAngle(current, target, dt) {
    return current + angleDelta(current, target) * (1 - Math.exp(-dt / TAU));
  }

  // Display units per the game's setting (`metric`), converted from the plugin's SI values.
  // vvi is the VVI bar's scale: +/-max in its own unit (the game's own VSI clamps at 50 m/s),
  // a tick every `tick`, a number every `every`.
  function units(metric) {
    return metric
      ? { spd: { k: 3.6, unit: 'KM/H' }, alt: { k: 1, unit: 'M' },
          vs:  { k: 1, unit: 'M/S', round: 1 },
          vvi: { k: 1, unit: 'M/S', max: 50, tick: 5, every: 25 } }
      : { spd: { k: 1.943844, unit: 'KT' }, alt: { k: 3.28084, unit: 'FT' },
          vs:  { k: 196.8504, unit: 'FPM', round: 50 },
          vvi: { k: 0.1968504, unit: 'x1000 FPM', max: 10, tick: 1, every: 5 } };
  }

  function signed(n) { return (n > 0 ? '+' : '') + n; }

  var api = { TAU: TAU, approach: approach, angleDelta: angleDelta, approachAngle: approachAngle,
              units: units, signed: signed };
  root.AdiMath = api;
  if (typeof module !== 'undefined' && module.exports) module.exports = api;
})(typeof window !== 'undefined' ? window : this);
