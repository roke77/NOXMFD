// Self-check for TD's assignment matrix rules. Run: `node td-matrix.test.js`.
const assert = require('assert');

(async () => {
  const { has, toggleCell, toggleRow, toggleColumn, slotIds, slotStatus } = await import('./td-matrix.js');
  const SLOTS = [1, 2, 3];

  // Cell: one tap on, a second off; the input is never mutated.
  const base = {};
  const one = toggleCell(base, 7, 2);
  assert.deepStrictEqual(one, { '7': [2] });
  assert.deepStrictEqual(base, {}, 'toggleCell must not mutate its input');
  assert.deepStrictEqual(toggleCell(one, 7, 2), {}, 'an emptied target drops out entirely');

  // Row: a partly-assigned target gains the missing slots; a full row empties.
  const row = toggleRow({ '1': [2] }, 1, SLOTS);
  assert.ok(SLOTS.every(function (s) { return has(row, 1, s); }), 'row fills every slot');
  assert.deepStrictEqual(toggleRow(row, 1, SLOTS), {}, 'a full row empties');

  // Column: same rule over the table's targets; other slots are left alone.
  const col = toggleColumn({ '1': [3], '2': [2] }, 3, [1, 2]);
  assert.ok(has(col, 1, 3) && has(col, 2, 3) && has(col, 2, 2));
  const emptied = toggleColumn(col, 3, [1, 2]);
  assert.deepStrictEqual(emptied, { '2': [2] });

  // slotIds follows table order.
  assert.deepStrictEqual(slotIds({ '5': [2], '1': [2] }, 2, [1, 9, 5]), [1, 5]);

  // Status: MARKER for the leader's own slot, EMPTY/UNSENT before any send, SENT while the table
  // matches the last send in any order, CHANGED once it doesn't.
  const ids = [1, 2, 3];
  assert.strictEqual(slotStatus({ '1': [1] }, {}, 1, ids).status, 'MARKER');
  assert.strictEqual(slotStatus({}, {}, 2, ids).status, 'EMPTY');
  const unsent = slotStatus({ '1': [2] }, {}, 2, ids);
  assert.deepStrictEqual(unsent, { n: 1, status: 'UNSENT', waiting: true });
  assert.strictEqual(slotStatus({ '1': [2], '3': [2] }, { '2': [3, 1] }, 2, ids).status, 'SENT');
  assert.strictEqual(slotStatus({ '1': [2] }, { '2': [3, 1] }, 2, ids).status, 'CHANGED');
  assert.strictEqual(slotStatus({}, { '2': [1] }, 2, ids).status, 'CHANGED', 'CLEAR after a send still needs a DESIGNATE');
  assert.strictEqual(slotStatus({}, { '2': [] }, 2, ids).status, 'SENT', 'an empty send is still a send');

  console.log('td-matrix.test.js: OK');
})().catch(function (e) { console.error(e); process.exit(1); });
