// SYS page (id afm) responsive layout rules: pure geometry, DOM-free, so this runs under node for
// the self-check. Sizes are in em of the page's toggle/pylon scale; widths in px.
(function (root) {
  // Per side, a toggle column takes its inset, the button and a clear gap to the drawing.
  const FLAG_COLUMN_EM = 5 + 7.1 + 1;
  // The 4-across toggle band: four buttons, three gaps, an inset each side.
  const FLAG_BAND_EM = 4 * 7.1 + 3 * 0.9 + 2;
  // Per side, an outer pylon box beside the front view: inset, a minimum box and a clear gap.
  const PYLON_SIDE_EM = 5 + 7 + 1;
  // A marker this close to the front view's centre line (as a share of its width) is a centre pylon.
  const PYLON_CENTRE_BAND = 0.03;

  function clamp(v, lo, hi) { return Math.min(hi, Math.max(lo, v)); }

  // Two toggle columns fit when they leave the top-down drawing's width clear between them.
  function flagColumnsFit(panelW, em, drawingW) {
    return panelW >= drawingW + 2 * FLAG_COLUMN_EM * em;
  }

  // The band's scale: as large as fills the width, within the columns' own 9..18px range.
  function bandEm(panelW) {
    return clamp(panelW / FLAG_BAND_EM, 9, 18);
  }

  // The outer two pylon boxes sit beside the front view when both clear its drawn width.
  function pylonSidesFit(n, panelW, em, frontW) {
    return n >= 3 && panelW >= frontW + 2 * PYLON_SIDE_EM * em;
  }

  // Whether n boxes fit on one line at minBoxW each, with gaps between them.
  function pylonRowFits(n, rowW, minBoxW, gap) {
    return n * minBoxW + Math.max(0, n - 1) * gap <= rowW;
  }

  // The stacked arrangement for a pane too narrow for one line: pylons left of the front view's
  // centre line go to a left column and right ones to a right column, outermost at the top, so
  // each line drops straight from its marker and turns outward into its box without crossing
  // another; centre pylons share a bottom row between the columns. Takes [{n, cx}] (cx 0..1 across
  // the front view) and returns {name: {col: 1|2|3, row}} plus the row count.
  function pylonColumns(pylons) {
    const left = [], right = [], centre = [];
    for (const p of pylons) {
      if (p.cx < 0.5 - PYLON_CENTRE_BAND) left.push(p);
      else if (p.cx > 0.5 + PYLON_CENTRE_BAND) right.push(p);
      else centre.push(p);
    }
    left.sort(function(a, b) { return a.cx - b.cx; });
    right.sort(function(a, b) { return b.cx - a.cx; });
    centre.sort(function(a, b) { return a.cx - b.cx; });
    const sideRows = Math.max(left.length, right.length);
    const rows = sideRows + centre.length;
    const place = {};
    left.forEach(function(p, i) { place[p.n] = { col: 1, row: i + 1 }; });
    right.forEach(function(p, i) { place[p.n] = { col: 3, row: i + 1 }; });
    centre.forEach(function(p, i) { place[p.n] = { col: 2, row: sideRows + i + 1 }; });
    return { place: place, rows: Math.max(1, rows) };
  }

  const api = { clamp, flagColumnsFit, bandEm, pylonSidesFit, pylonRowFits, pylonColumns };
  if (typeof module !== 'undefined' && module.exports) module.exports = api;
  else root.AfmLayoutPolicy = api;
})(typeof self !== 'undefined' ? self : this);
