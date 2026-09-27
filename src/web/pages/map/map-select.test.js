// Self-check for nearestContact, the hit test behind selectAt/deselectAt (map.js, docs/map-cursor.md):
// the nearest contact within reach (r + pad) in the wanted selection state, or nothing. map.js runs in a DOM/canvas context Node doesn't have, so this
// mirrors the algorithm rather than importing the module — the thing worth locking is the
// behaviour, not the wiring. The PAD cursor's movement/clamp is covered separately against the real
// module by src/web/services/pad-cursor.test.js.
// Run: `node map-select.test.js`.
const assert = require('assert');

// Mirrors nearestContact: nearest target within reach (r + pad) whose selected flag matches, or null.
function nearestContact(targets, px, py, pad, selected) {
  let hit = null, bestD2 = Infinity;
  for (const t of targets) {
    if (t.selected !== selected) continue;
    const dx = px - t.cx, dy = py - t.cy, d2 = dx * dx + dy * dy;
    const reach = t.r + pad;
    if (d2 <= reach * reach && d2 < bestD2) { bestD2 = d2; hit = t; }
  }
  return hit;
}
const nearestUnselected = (targets, px, py, pad) => nearestContact(targets, px, py, pad, false);

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

// Deselect (right-click / Cursor Deselect) is the mirror image: only selected contacts are candidates.
{
  const targets = [
    { id: 1, cx: 100, cy: 100, r: 10, selected: true },
    { id: 2, cx: 104, cy: 100, r: 10, selected: false },   // nearer, but not selected
    { id: 3, cx: 112, cy: 100, r: 10, selected: true },
  ];
  assert.strictEqual(nearestContact(targets, 105, 100, 0, true).id, 1,
    'deselect skips unselected contacts and picks the nearest selected one');
  assert.strictEqual(nearestContact([{ id: 1, cx: 100, cy: 100, r: 10, selected: false }], 100, 100, 0, true), null,
    'nothing selected in reach → deselect is a no-op');
}

console.log('map-select: ok');
