// No DOM refs so this can be unit-checked in Node (bdf-funds.test.js).
//
// Mirrors the game's own UnitConverter.ValueReading scale-by-magnitude format (docs/bdf-page.md —
// funds arrive in MILLIONS), so the MFD reads the same as the in-game panel. Uses a period rather
// than the game's locale-dependent comma.
//
// Bands compare SQUARES rather than using Math.abs, matching the game's own implementation:
// `m*m < 1` is `|m| < 1`, and so on. This also makes negative funds (a faction in debt) pick the
// same band as the equivalent positive amount.
(function (root) {
  function fmtFunds(m) {
    if (typeof m !== 'number' || !isFinite(m)) return '$0';
    const raw = m * 1e6;
    if (raw * raw < 1e8) return '$' + Math.round(raw);                   // |raw| < 10k — plain units
    if (m * m < 1) return '$' + (m * 1000).toFixed(1) + 'k';             // |m| < 1m
    if (m * m < 100) return '$' + m.toFixed(2) + 'm';                    // |m| < 10m
    if (m * m < 1e6) return '$' + m.toFixed(1) + 'm';                    // |m| < 1000m
    if (m * m < 1e12) return '$' + (m * 0.001).toFixed(2) + 'b';         // |m| < 1e6m
    return '$' + (m * 1e-6).toFixed(3) + 't';
  }

  const api = { fmtFunds };
  if (typeof module !== 'undefined' && module.exports) module.exports = api;
  else root.BdfFunds = api;
})(typeof self !== 'undefined' ? self : this);
