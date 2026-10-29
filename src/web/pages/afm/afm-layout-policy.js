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
  // another. Centre pylons share one bottom row between the columns, left to right, under an empty
  // row their lines' tracks run through. Takes [{n, cx}] (cx 0..1 across the front view); returns
  // place {name: {col, row}} (grid lines from 1), cols, sideRows and centre (the centre count).
  // Columns: 1 left, 2..cols-1 the centre boxes, cols right. Rows: 1..sideRows the side boxes,
  // then the track row and the centre row.
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
    const cols = 2 + Math.max(1, centre.length);
    const place = {};
    left.forEach(function(p, i) { place[p.n] = { col: 1, row: i + 1 }; });
    right.forEach(function(p, i) { place[p.n] = { col: cols, row: i + 1 }; });
    centre.forEach(function(p, i) { place[p.n] = { col: 2 + i, row: sideRows + 2 }; });
    return { place: place, cols: cols, sideRows: sideRows, centre: centre.length };
  }

  // The pylon area (front view + the gap under it + the boxes) gets at most half the stack, so
  // the top view always keeps the rest. Over budget, the boxes shrink first, to minBoxH, then the
  // front view, to minFrontH. rowsH(boxH) gives the boxes' total height for a box height. Returns
  // {boxH, frontH}.
  function fitPylonArea(available, frontH, gapH, boxH, minBoxH, minFrontH, rowsH) {
    const budget = available / 2;
    if (frontH + gapH + rowsH(boxH) > budget) {
      // rowsH is linear in boxH: rowsH(b) = rowsH(0) + b * k.
      const base = rowsH(0), k = rowsH(1) - base;
      boxH = Math.max(minBoxH, Math.min(boxH, k > 0 ? (budget - frontH - gapH - base) / k : boxH));
    }
    if (frontH + gapH + rowsH(boxH) > budget) {
      frontH = Math.max(minFrontH, budget - gapH - rowsH(boxH));
    }
    return { boxH: boxH, frontH: frontH };
  }

  // Circuit routes for one side's links, each {x1, y1, bx, top}: down from the marker (x1, y1) to
  // its own horizontal track between trackTop and trackBottom, across to bx, down to the box top.
  // The link running furthest takes the highest track, so no track crosses another link's drop.
  // Returns each link's points, in the order given.
  function laneLinks(links, trackTop, trackBottom) {
    const order = links.map(function(l, i) { return i; })
      .sort(function(a, b) { return Math.abs(links[b].bx - links[b].x1) - Math.abs(links[a].bx - links[a].x1); });
    const step = (trackBottom - trackTop) / (links.length + 1);
    const routes = new Array(links.length);
    order.forEach(function(idx, lane) {
      const l = links[idx], ty = trackTop + step * (lane + 1);
      routes[idx] = [[l.x1, l.y1], [l.x1, ty], [l.bx, ty], [l.bx, l.top]];
    });
    return routes;
  }

  const api = { clamp, flagColumnsFit, bandEm, pylonSidesFit, pylonRowFits, pylonColumns, fitPylonArea, laneLinks };
  if (typeof module !== 'undefined' && module.exports) module.exports = api;
  else root.AfmLayoutPolicy = api;
})(typeof self !== 'undefined' ? self : this);
