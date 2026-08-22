// Mirrors the game's own ThrottleGauge: afterburner airframes split the 0..1 axis at abStart
// (below = MIL, a plain 0-100% read; above = reheat, rescaled 0-100%, shown red). Aircraft
// without afterburner (Compass, helicopters) report hasAb=false and get a plain 0-100% bar.
(function (root) {
  function clamp01(x) { return x < 0 ? 0 : x > 1 ? 1 : x; }

  function throttleReadout(value01, hasAb, abStart) {
    if (typeof value01 !== 'number' || value01 < 0) {
      return { na: true, fill: 0, text: '--', zone: 'plain', boundary: null };
    }
    const v = clamp01(value01);
    // No afterburner, or a degenerate split (0 or 1) → plain single-scale bar.
    if (!hasAb || !(abStart > 0 && abStart < 1)) {
      return { na: false, fill: v, text: Math.round(v * 100) + '%', zone: 'plain', boundary: null };
    }
    if (v > abStart) {
      const p = (v - abStart) / (1 - abStart);
      return { na: false, fill: v, text: Math.round(p * 100) + '%', zone: 'ab', boundary: abStart };
    }
    // At the detent (100% MIL) the "MIL" label replaces the number, mirroring the in-game gauge.
    const pct = Math.round((v / abStart) * 100);
    return { na: false, fill: v, text: pct >= 100 ? 'MIL' : pct + '%', zone: 'mil', boundary: abStart };
  }

  const api = { throttleReadout: throttleReadout };
  if (typeof module !== 'undefined' && module.exports) module.exports = api;
  else root.AvnThrottlePolicy = api;
})(typeof self !== 'undefined' ? self : this);
