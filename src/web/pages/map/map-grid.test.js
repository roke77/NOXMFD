// Self-check for the GRID overlay's line-placement math (issue #41). map.js's drawGrid() can't be
// imported directly (it's canvas-coupled, not a pure module), so this re-derives the same integer
// index math against known inputs — the one thing worth guarding: floating-point drift silently
// turning a major (10 km) line into a minor one, or vice versa, after many float additions.
// Run: `node map-grid.test.js`.
const assert = require('assert');

const GRID_MINOR_UNIT = 1000;
const GRID_LINES_PER_MAJOR = 10;

// Mirrors drawGrid()'s bound derivation: gridLabel's vx = ox+wx, vz = oy-wz, clamped to 0.
function gridBounds(meta) {
  const wMinX = -meta.w / 2, wMaxX = meta.w / 2;
  const wMinZ = -meta.h / 2, wMaxZ = meta.h / 2;
  const vMinX = Math.max(0, meta.ox + wMinX), vMaxX = meta.ox + wMaxX;
  const vMinZ = Math.max(0, meta.oy - wMaxZ), vMaxZ = meta.oy - wMinZ;
  return {
    iMinX: Math.ceil(vMinX / GRID_MINOR_UNIT), iMaxX: Math.floor(vMaxX / GRID_MINOR_UNIT),
    iMinZ: Math.ceil(vMinZ / GRID_MINOR_UNIT), iMaxZ: Math.floor(vMaxZ / GRID_MINOR_UNIT),
  };
}
function isMajor(i) { return i % GRID_LINES_PER_MAJOR === 0; }
function majorLabelX(i) { return String(i / GRID_LINES_PER_MAJOR); }
function majorLabelZ(i) { return String.fromCharCode(65 + i / GRID_LINES_PER_MAJOR); }

// A 100 km square map centred on the world origin, offset so the whole map sits in positive
// grid-space (100000, 100000) — the same shape the preview mock and a real 100 km mission use.
const meta = { w: 100000, h: 100000, ox: 50000, oy: 50000 };
const b = gridBounds(meta);

// 100 km / 1 km minor spacing = 100 lines per axis, index 0..100.
assert.strictEqual(b.iMinX, 0);
assert.strictEqual(b.iMaxX, 100);
assert.strictEqual(b.iMinZ, 0);
assert.strictEqual(b.iMaxZ, 100);

// Every 10th minor index is a major line — 11 majors per axis (0, 10, ..., 100).
let majorCount = 0;
for (let i = b.iMinX; i <= b.iMaxX; i++) if (isMajor(i)) majorCount++;
assert.strictEqual(majorCount, 11);

// Labels match gridLabel()'s own scheme for a point sitting exactly on a major line.
assert.strictEqual(majorLabelX(0),  '0');
assert.strictEqual(majorLabelX(30), '3');
assert.strictEqual(majorLabelZ(0),  'A');
assert.strictEqual(majorLabelZ(80), 'I');

// No float drift after 100 additions of a non-round step would have been possible here since the
// loop is integer-indexed, not accumulated — this just pins the index arithmetic stays exact.
assert.strictEqual(isMajor(100), true);
assert.strictEqual(isMajor(99), false);

console.log('map-grid.test.js: OK');
