// Self-check for the shared pending-selection tracker. Run: `node pending-selection.test.js`.
const assert = require('assert');

let clock = 0;
globalThis.performance = { now: () => clock };

(async () => {
  const { createPendingSelection } = await import('./pending-selection.js');

  const p = createPendingSelection(50);
  assert.strictEqual(p.isPending(1), false, 'an id with no pending entry is not pending');

  p.mark(1);
  assert.strictEqual(p.isPending(1), true, 'a freshly marked id reads as pending');

  clock += 51;
  assert.strictEqual(p.isPending(1), false, 'an expired entry reads as not pending');

  p.mark(2);
  p.clear(2);
  assert.strictEqual(p.isPending(2), false, 'clear() removes a pending entry immediately');

  // Default hold (no argument) — just confirm it accepts no ctor arg without throwing and behaves.
  const d = createPendingSelection();
  d.mark(3);
  assert.strictEqual(d.isPending(3), true, 'default-hold tracker marks correctly');

  console.log('pending-selection.test.js: OK');
})();
