// Self-check for MapHit.nearestContact, the hit test behind selectAt/deselectAt (map.js,
// docs/map-cursor.md): the nearest contact within reach (r + pad) in the wanted selection state, or
// nothing. The PAD cursor's movement/clamp is covered separately by src/web/services/pad-cursor.test.js.
// Run: `node map-select.test.js`.
const assert = require('assert');
const { nearestContact } = require('./map-hit.js');

const isSel = (t) => !!t.selected;
const nearestUnselected = (targets, px, py, pad) => nearestContact(targets, px, py, pad, false, isSel);

{
  const targets = [
    { id: 1, cx: 100, cy: 100, r: 10, selected: false },
    { id: 2, cx: 105, cy: 100, r: 10, selected: false },   // closer to the cursor below
  ];
  assert.strictEqual(nearestUnselected(targets, 106, 100, 0).id, 2, 'picks the nearer of two overlapping-reach targets');
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
assert.strictEqual(nearestUnselected([], 0, 0, 100), null, 'no targets → no-op');
{
  const targets = [{ id: null, cx: 100, cy: 100, r: 10, selected: false }];   // the player's own icon
  assert.strictEqual(nearestUnselected(targets, 100, 100, 0), null, 'a contact with no id is never a candidate');
}
{
  const targets = [
    { id: 1, cx: 100, cy: 100, r: 10, selected: false },
    { id: 2, cx: 100, cy: 100, r: 10, selected: false },   // drawn last = on top
  ];
  assert.strictEqual(nearestUnselected(targets, 100, 100, 0).id, 2, 'an exact tie goes to the topmost contact');
}

// Deselect (right-click / Cursor Deselect) is the mirror image: only selected contacts are candidates.
{
  const targets = [
    { id: 1, cx: 100, cy: 100, r: 10, selected: true },
    { id: 2, cx: 104, cy: 100, r: 10, selected: false },   // nearer, but not selected
    { id: 3, cx: 112, cy: 100, r: 10, selected: true },
  ];
  assert.strictEqual(nearestContact(targets, 105, 100, 0, true, isSel).id, 1,
    'deselect skips unselected contacts and picks the nearest selected one');
  assert.strictEqual(nearestContact([{ id: 1, cx: 100, cy: 100, r: 10, selected: false }], 100, 100, 0, true, isSel), null,
    'nothing selected in reach → deselect is a no-op');
}

// A contact the game refuses to select (neutral, or excluded by the TGT filters) must not shadow
// the selectable ones around it: the player's report was a neutral dot on top of a launcher site.
{
  const targets = [
    { id: 10, cx: 100, cy: 100, r: 10, selected: false },                      // the launcher
    { id: 11, cx: 100, cy: 100, r: 10, selected: false, selectable: false },   // neutral dot, drawn on top
  ];
  assert.strictEqual(nearestUnselected(targets, 100, 100, 22).id, 10,
    'an unselectable contact on top does not block the selectable one under it');

  const stack = [
    { id: 10, cx: 100, cy: 100, r: 10, selected: false },
    { id: 11, cx: 101, cy: 100, r: 10, selected: false, selectable: false },
    { id: 12, cx: 99, cy: 100, r: 10, selected: false, selectable: false },
  ];
  assert.strictEqual(nearestUnselected(stack, 101, 100, 22).id, 10, 'nor when it is the nearest of the stack');
  assert.strictEqual(nearestUnselected([stack[1], stack[2]], 100, 100, 22), null,
    'a stack of only unselectable contacts is a no-op, not an endless re-pick');

  // Deselecting is not restricted: an already-selected contact is always removable.
  assert.strictEqual(nearestContact([{ id: 5, cx: 0, cy: 0, r: 10, selected: true, selectable: false }], 0, 0, 0, true, isSel).id, 5,
    'a selected contact can always be deselected');
}

console.log('map-select: ok');
