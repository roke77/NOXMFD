// Self-check for the MAP view's coordinate maths. Run: `node map-transform.test.js`.
//
// The property worth holding is the one overlayToWorld claims in its own comment: it is the EXACT
// algebraic inverse of worldToOverlay, not an approximation. Everything the pilot reads off the map
// depends on that agreeing in both directions — contacts are drawn world->pixel, while the CURSOR
// chip's grid label is derived pixel->world. If the two drift apart, a contact sits under the
// crosshair while the readout names a different grid square, and neither side looks wrong alone.
const assert = require('assert');
const T = require('./map-transform.js');

// A deliberately awkward geometry: non-square canvas, non-square image, so the letterbox is real
// and any dropped dx/dy offset shows up rather than cancelling to zero.
const base = (over = {}) => Object.assign({
  canvas: { w: 800, h: 600 },
  img:    { w: 1024, h: 1024 },
  view:   { zoom: 1, panX: 0, panY: 0 },
  meta:   { w: 100000, h: 100000 },
}, over);

const near = (a, b, eps, msg) => assert.ok(Math.abs(a - b) < eps, `${msg}: ${a} vs ${b}`);

// ── The letterbox ───────────────────────────────────────────────────────────────────────
{
  // A square image in a 4:3 canvas: full height, centred horizontally.
  const r = T.imgRect(base());
  assert.deepStrictEqual(r, { dx: 100, dy: 0, dw: 600, dh: 600 }, 'square image should pillarbox in a wide canvas');

  // A wide image in a square canvas: full width, centred vertically.
  const r2 = T.imgRect(base({ canvas: { w: 600, h: 600 }, img: { w: 1200, h: 600 } }));
  assert.deepStrictEqual(r2, { dx: 0, dy: 150, dw: 600, dh: 300 }, 'wide image should letterbox in a square canvas');
}

// ── World -> pixel anchors ──────────────────────────────────────────────────────────────
{
  const g = base();
  const r = T.imgRect(g);

  // World origin is the map's centre, which is the image's centre.
  const c = T.worldToOverlay(g, 0, 0);
  near(c.cx, r.dx + r.dw / 2, 1e-9, 'world origin should land on the image centre X');
  near(c.cy, r.dy + r.dh / 2, 1e-9, 'world origin should land on the image centre Y');

  // North (+Z) is up the screen, so it must DECREASE y — the inversion that is easy to lose.
  const north = T.worldToOverlay(g, 0, 40000);
  assert.ok(north.cy < c.cy, 'north (+Z) must map to a smaller screen Y');
  const east = T.worldToOverlay(g, 40000, 0);
  assert.ok(east.cx > c.cx, 'east (+X) must map to a larger screen X');

  // The map's corners land exactly on the image's corners.
  const nw = T.worldToOverlay(g, -50000, 50000);
  near(nw.cx, r.dx, 1e-9, 'west edge should sit on the image left');
  near(nw.cy, r.dy, 1e-9, 'north edge should sit on the image top');
  const se = T.worldToOverlay(g, 50000, -50000);
  near(se.cx, r.dx + r.dw, 1e-9, 'east edge should sit on the image right');
  near(se.cy, r.dy + r.dh, 1e-9, 'south edge should sit on the image bottom');
}

// ── THE invariant: the round trip is exact, at every zoom and pan ────────────────────────
{
  const views = [
    { zoom: 1,   panX: 0,   panY: 0   },
    { zoom: 2,   panX: 0,   panY: 0   },
    { zoom: 2,   panX: 120, panY: -75 },
    { zoom: 0.5, panX: -40, panY: 33  },
    { zoom: 3.7, panX: 11,  panY: 250 },
  ];
  const points = [[0, 0], [12345, -6789], [-50000, 50000], [50000, -50000], [1, 99999]];

  for (const view of views) {
    const g = base({ view });
    for (const [wx, wz] of points) {
      const p = T.worldToOverlay(g, wx, wz);
      const back = T.overlayToWorld(g, p.cx, p.cy);
      near(back.x, wx, 1e-6, `world->pixel->world lost X at zoom ${view.zoom} pan ${view.panX}`);
      near(back.z, wz, 1e-6, `world->pixel->world lost Z at zoom ${view.zoom} pan ${view.panY}`);
    }
    // ...and the other way round, since the cursor readout starts from a pixel.
    for (const [sx, sy] of [[0, 0], [400, 300], [799, 599], [-20, 640]]) {
      const w = T.overlayToWorld(g, sx, sy);
      const p = T.worldToOverlay(g, w.x, w.z);
      near(p.cx, sx, 1e-6, `pixel->world->pixel lost X at zoom ${view.zoom}`);
      near(p.cy, sy, 1e-6, `pixel->world->pixel lost Y at zoom ${view.zoom}`);
    }
  }
}

// ── Zoom is about the canvas centre ─────────────────────────────────────────────────────
// Zooming must not slide the view sideways; the centre pixel is the fixed point.
{
  const cx = 400, cy = 300;   // canvas centre for the base geometry
  for (const zoom of [0.5, 1, 2, 4]) {
    const g = base({ view: { zoom, panX: 0, panY: 0 } });
    const p = T.viewTransform(g, cx, cy);
    near(p.x, cx, 1e-9, `zoom ${zoom} moved the centre X`);
    near(p.y, cy, 1e-9, `zoom ${zoom} moved the centre Y`);
  }
}

// ── clampPan: the map may never expose blank background ─────────────────────────────────
{
  // Compared numerically, not with deepStrictEqual: clamping to zero can yield -0, which is
  // indistinguishable from 0 everywhere it is used and only differs under Object.is.
  const eqPan = (got, x, y, msg) => { near(got.panX, x, 1e-9, msg + ' X'); near(got.panY, y, 1e-9, msg + ' Y'); };

  // At zoom 1 there is no slack at all, so pan pins to 0 — the framing from before zoom existed.
  const g1 = base({ view: { zoom: 1, panX: 999, panY: -999 } });
  eqPan(T.clampPan(g1, 999, -999), 0, 0, 'zoom 1 must pin pan to 0');

  // At zoom 2 the slack is half the scaled overhang on each axis.
  const g2 = base({ view: { zoom: 2, panX: 0, panY: 0 } });
  const r = T.imgRect(g2);
  const maxX = r.dw * (2 - 1) / 2, maxY = r.dh * (2 - 1) / 2;
  eqPan(T.clampPan(g2, 1e6, 1e6), maxX, maxY, 'pan should clamp to the positive limit');
  eqPan(T.clampPan(g2, -1e6, -1e6), -maxX, -maxY, 'pan should clamp to the negative limit');
  eqPan(T.clampPan(g2, 10, -10), 10, -10, 'a pan inside the limit should pass through');

  // Not asserted: zoom < 1. The slack goes negative there and the clamp inverts, but map.js pins
  // zoom to MIN_ZOOM=1..MAX_ZOOM=8 (including when reading the persisted view), so that input
  // cannot occur. Pinning behaviour for it would freeze an accident rather than a contract — the
  // precondition is noted on clampPan instead.
}

// ── No map metadata yet ─────────────────────────────────────────────────────────────────
// Pre-mission the page still draws; the transforms must decline rather than emit NaN pixels.
{
  for (const meta of [null, undefined, { w: 0, h: 0 }, { w: -1, h: 100 }]) {
    const g = base({ meta });
    assert.strictEqual(T.worldToOverlay(g, 0, 0), null, `worldToOverlay should decline meta ${JSON.stringify(meta)}`);
    assert.strictEqual(T.overlayToWorld(g, 0, 0), null, `overlayToWorld should decline meta ${JSON.stringify(meta)}`);
  }
}

console.log('map-transform.test.js: OK');
