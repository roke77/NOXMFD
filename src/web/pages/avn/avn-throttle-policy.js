// Pure policy for the AVN THROTTLE readout. Mirrors the game's own ThrottleGauge: the 0..1
// throttle axis is one bar, but afterburner airframes split it at abStart — below is MIL
// (a plain 0-100% throttle read), above is reheat (rescaled 0-100%, shown red). Aircraft without
// afterburner (Compass, helicopters) report hasAb=false and fall back to a plain 0-100% bar.
//
// throttleReadout(value01, hasAb, abStart) -> {
//   na:      true when there is no throttle value (renders '--')
//   fill:    0..1 fraction of the tube to fill (the raw throttle, unchanged)
//   text:    the readout string ('--', '60%', 'MIL')  — always a bare number/label
//   zone:    'plain' | 'mil' | 'ab'  (drives the readout colour + the AFTERBURNER tag: ab = red)
//   boundary: 0..1 MIL/AB split for the fill's green→red gradient, or null when no afterburner
// }
// The reheat readout is just the rescaled percentage; the page shows a separate "AFTERBURNER"
// tag above the bar when zone === 'ab' rather than squeezing an "AB" label into the number box.
(function (root) {
  function clamp01(x) { return x < 0 ? 0 : x > 1 ? 1 : x; }

  function throttleReadout(value01, hasAb, abStart) {
    if (typeof value01 !== 'number' || value01 < 0) {
      return { na: true, fill: 0, text: '--', zone: 'plain', boundary: null };
    }
    const v = clamp01(value01);
    // No afterburner, or a degenerate split (0 or 1) → plain single-scale bar, exactly as before.
    if (!hasAb || !(abStart > 0 && abStart < 1)) {
      return { na: false, fill: v, text: Math.round(v * 100) + '%', zone: 'plain', boundary: null };
    }
    if (v > abStart) {
      const p = (v - abStart) / (1 - abStart);
      return { na: false, fill: v, text: Math.round(p * 100) + '%', zone: 'ab', boundary: abStart };
    }
    // MIL zone reads like a normal throttle (just the number) until it tops out; at the detent
    // (100% MIL) the "MIL" label replaces the number, mirroring the in-game gauge.
    const pct = Math.round((v / abStart) * 100);
    return { na: false, fill: v, text: pct >= 100 ? 'MIL' : pct + '%', zone: 'mil', boundary: abStart };
  }

  const api = { throttleReadout: throttleReadout };
  if (typeof module !== 'undefined' && module.exports) module.exports = api;
  else root.AvnThrottlePolicy = api;
})(typeof self !== 'undefined' ? self : this);
