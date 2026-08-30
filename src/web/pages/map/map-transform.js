// The MAP view's coordinate maths, split out of map.js so it carries no canvas or DOM refs and can
// be unit-checked in Node (see map-transform.test.js) — the same treatment nav-model.js and
// classic-paging.js get in the shell.
//
// Every function here is pure: map.js keeps the live canvas/image/view/meta state and passes it in.
// `geom` is that state, in one bag:
//   canvas {w,h}  the overlay's pixel size
//   img    {w,h}  the map image's natural size (letterboxed inside the canvas by imgRect)
//   view   {zoom, panX, panY}   the pan/zoom the pilot has applied
//   meta   {w,h}  the world extent the map image covers, centred on the world origin
//
// The chain is world -> base pixel -> view pixel, and overlayToWorld is its exact algebraic
// inverse, not an approximation — that round trip is what the CURSOR chip's grid label depends on,
// and it is the property map-transform.test.js exists to hold.
(function (root) {
  // The map image letterboxed into the canvas: aspect-fit, centred on the short axis.
  function imgRect(geom) {
    const iw = geom.img.w, ih = geom.img.h;
    const cw = geom.canvas.w, ch = geom.canvas.h;
    const ia = iw / ih, ca = cw / ch;
    let dw, dh, dx, dy;
    if (ia > ca) { dw = cw; dh = cw / ia; dx = 0;             dy = (ch - dh) / 2; }
    else         { dh = ch; dw = ch * ia; dx = (cw - dw) / 2; dy = 0; }
    return { dx, dy, dw, dh };
  }

  // Apply the zoom/pan view transform to a base (zoom=1) overlay pixel. Zoom is about the canvas
  // centre, so pan=0 reproduces the plain centred framing exactly.
  function viewTransform(geom, px, py) {
    const ox = geom.canvas.w / 2, oy = geom.canvas.h / 2;
    return { x: ox + (px - ox) * geom.view.zoom + geom.view.panX,
             y: oy + (py - oy) * geom.view.zoom + geom.view.panY };
  }

  // World (X east, Z north) -> base (zoom=1) overlay pixel. The map is centred on the world origin
  // spanning meta.w x meta.h, so this is a direct mapping — no calibration. The extracted image is
  // north-up, so screen Y is inverted relative to Z.
  function worldToBase(geom, wx, wz) {
    if (!geom.meta || geom.meta.w <= 0 || geom.meta.h <= 0) return null;
    const relX = (wx + geom.meta.w * 0.5) / geom.meta.w;   // 0 = west,  1 = east
    const relY = (wz + geom.meta.h * 0.5) / geom.meta.h;   // 0 = south, 1 = north
    const r = imgRect(geom);
    return { x: r.dx + relX * r.dw, y: r.dy + (1 - relY) * r.dh };
  }

  function worldToOverlay(geom, wx, wz) {
    const b = worldToBase(geom, wx, wz);
    if (!b) return null;
    const v = viewTransform(geom, b.x, b.y);
    return { cx: v.x, cy: v.y };
  }

  // Overlay pixel -> world. No bounds check: a point outside the map's extent (letterbox margin,
  // panned/zoomed past an edge) still resolves to whatever square the maths extrapolates to, same as
  // the player's own GRID chip has never special-cased being off the labelled grid (gridLabel itself
  // already returns a dash for a negative grid square).
  function overlayToWorld(geom, sx, sy) {
    if (!geom.meta || geom.meta.w <= 0 || geom.meta.h <= 0) return null;
    const ox = geom.canvas.w / 2, oy = geom.canvas.h / 2;
    const bx = ox + (sx - ox - geom.view.panX) / geom.view.zoom;
    const by = oy + (sy - oy - geom.view.panY) / geom.view.zoom;
    const r = imgRect(geom);
    const relX = (bx - r.dx) / r.dw, relY = 1 - (by - r.dy) / r.dh;
    return { x: relX * geom.meta.w - geom.meta.w * 0.5, z: relY * geom.meta.h - geom.meta.h * 0.5 };
  }

  // The pan that keeps the scaled map covering its zoom=1 footprint, plus an optional extra
  // margin per axis (marginFracX/Y, each a fraction of the image's own dw/dh) past that footprint.
  // At zoom=1 with margin 0 this pins pan to 0 (framing unchanged from before zoom existed); a
  // nonzero margin allows that much overshoot at every zoom level, including 1. marginFracY
  // defaults to marginFracX when omitted, for a caller with one uniform margin.
  //
  // The margin exists for issue #65: a mission's real terrain/spawns can sit past the square
  // MapSettings.MapSize covers (confirmed in-game — an aircraft carrier and the mission's own
  // GridSizeX/Y both sat well beyond it), so a zero margin can clamp the camera/cursor just short
  // of real content. There's no map image data out there to reveal, only flat background — this
  // buys reachability, not detail. Returns the clamped pair rather than mutating, so the caller
  // owns its own view state.
  //
  // Assumes zoom >= 1, which is map.js's MIN_ZOOM (enforced there on every zoom step and when
  // restoring the persisted view). Below 1 the slack turns negative and the clamp inverts, forcing
  // pan to a limit instead of to zero — meaningless rather than merely unused, so if a zoomed-out
  // view is ever wanted this needs rewriting, not just a wider range.
  function clampPan(geom, panX, panY, marginFracX, marginFracY) {
    const r = imgRect(geom);
    const fx = marginFracX || 0;
    const fy = marginFracY != null ? marginFracY : fx;
    const maxX = r.dw * ((geom.view.zoom - 1) / 2 + fx * geom.view.zoom);
    const maxY = r.dh * ((geom.view.zoom - 1) / 2 + fy * geom.view.zoom);
    return { panX: Math.max(-maxX, Math.min(maxX, panX)),
             panY: Math.max(-maxY, Math.min(maxY, panY)) };
  }

  const api = { imgRect, viewTransform, worldToBase, worldToOverlay, overlayToWorld, clampPan };
  if (typeof module !== 'undefined' && module.exports) module.exports = api;
  else root.MapTransform = api;
})(typeof self !== 'undefined' ? self : this);
