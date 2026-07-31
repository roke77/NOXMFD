// Self-check for the MAP cursor's two pure algorithms (map.js, docs/map-cursor.md): the rAF
// integrator's clamp-to-rect, and selectAt's nearest-unselected-in-reach hit test. Modeled the way
// soi-focus.test.js models the C# focus rules: map.js runs in a DOM/canvas context Node doesn't
// have, so this mirrors the algorithm rather than importing the module — the thing worth locking is
// the behaviour, not the wiring.
// Run: node tools/map-cursor.test.js
const assert = require('assert');

// Mirrors driveCursor's per-tick move + clampCursor's rect clamp.
function integrate(pos, vec, dt, speed, rect) {
  const x = Math.max(rect.dx, Math.min(rect.dx + rect.dw, pos.x + vec.x * speed * dt));
  const y = Math.max(rect.dy, Math.min(rect.dy + rect.dh, pos.y + vec.y * speed * dt));
  return { x, y };
}

// Mirrors selectAt: nearest unselected target within reach (r + pad), or null.
function nearestUnselected(targets, px, py, pad) {
  let hit = null, bestD2 = Infinity;
  for (const t of targets) {
    if (t.selected) continue;
    const dx = px - t.cx, dy = py - t.cy, d2 = dx * dx + dy * dy;
    const reach = t.r + pad;
    if (d2 <= reach * reach && d2 < bestD2) { bestD2 = d2; hit = t; }
  }
  return hit;
}

const RECT = { dx: 0, dy: 0, dw: 200, dh: 100 };

// ── Integrator ─────────────────────────────────────────────────────────────────────────
{
  const p = integrate({ x: 50, y: 50 }, { x: 1, y: 0 }, 0.1, 700, RECT);
  assert.deepStrictEqual(p, { x: 120, y: 50 }, 'moves by vec * speed * dt');
}
{
  const p = integrate({ x: 50, y: 50 }, { x: 0, y: 0 }, 0.1, 700, RECT);
  assert.deepStrictEqual(p, { x: 50, y: 50 }, 'a zero vector does not move it');
}
{
  const p = integrate({ x: 195, y: 50 }, { x: 1, y: 0 }, 1, 700, RECT);
  assert.strictEqual(p.x, RECT.dw, 'clamps to the right edge of the rect, not past it');
}
{
  const p = integrate({ x: 5, y: 95 }, { x: -1, y: 1 }, 1, 700, RECT);
  assert.deepStrictEqual(p, { x: RECT.dx, y: RECT.dy + RECT.dh }, 'clamps both axes independently');
}

// ── selectAt / nearestUnselected ───────────────────────────────────────────────────────
{
  const targets = [
    { id: 1, cx: 100, cy: 100, r: 10, selected: false },
    { id: 2, cx: 105, cy: 100, r: 10, selected: false },   // closer to the cursor below
  ];
  const hit = nearestUnselected(targets, 106, 100, 0);
  assert.strictEqual(hit.id, 2, 'picks the nearer of two overlapping-reach targets');
}
{
  const targets = [{ id: 1, cx: 100, cy: 100, r: 10, selected: true }];
  assert.strictEqual(nearestUnselected(targets, 100, 100, 0), null,
    'an already-selected target is never re-picked (selection only ever adds)');
}
{
  const targets = [{ id: 1, cx: 100, cy: 100, r: 5, selected: false }];
  assert.strictEqual(nearestUnselected(targets, 130, 100, 0), null, 'out of reach → no-op');
  assert.strictEqual(nearestUnselected(targets, 130, 100, 30).id, 1, 'a wider pad reaches it');
}
{
  assert.strictEqual(nearestUnselected([], 0, 0, 100), null, 'no targets → no-op');
}

console.log('map-cursor: ok');
