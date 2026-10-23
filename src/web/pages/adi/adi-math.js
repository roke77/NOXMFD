// ADI/HSI display math (docs/adi-hsi-pages.md) — pure, DOM-free, so adi-math.test.js can check it
// in Node. Smoothing between telemetry frames, the game's unit setting, and the tape windows.
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
  // step = tape label spacing, minor = tick spacing, both in display units.
  function units(metric) {
    return metric
      ? { spd: { k: 3.6, unit: 'KM/H', step: 20, minor: 10 },
          alt: { k: 1, unit: 'M', step: 50, minor: 25 },
          vs:  { k: 1, unit: 'M/S', round: 1 } }
      : { spd: { k: 1.943844, unit: 'KT', step: 10, minor: 5 },
          alt: { k: 3.28084, unit: 'FT', step: 100, minor: 50 },
          vs:  { k: 196.8504, unit: 'FPM', round: 50 } };
  }

  // Tape marks around `value`: every `minor` step within +/- span labelled steps, each with its
  // offset in steps from the value (positive = above). major marks carry a label.
  function tapeMarks(value, step, minor, span) {
    var marks = [];
    var lo = Math.ceil((value - span * step) / minor) * minor;
    for (var v = lo; v <= value + span * step + 1e-9; v += minor) {
      var r = Math.round(v / minor) * minor;
      marks.push({ v: r, off: (r - value) / step, major: Math.abs(r % step) < 1e-9 });
    }
    return marks;
  }

  function signed(n) { return (n > 0 ? '+' : '') + n; }

  var api = { TAU: TAU, approach: approach, angleDelta: angleDelta, approachAngle: approachAngle,
              units: units, tapeMarks: tapeMarks, signed: signed };
  root.AdiMath = api;
  if (typeof module !== 'undefined' && module.exports) module.exports = api;
})(typeof window !== 'undefined' ? window : this);
