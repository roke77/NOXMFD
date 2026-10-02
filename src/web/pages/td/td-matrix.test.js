// Self-check for TD's assignment matrix rules. Run: `node td-matrix.test.js`.
const assert = require('assert');

(async () => {
  const { has, toggleCell, toggleRow, toggleColumn, memberIds, memberStatus } = await import('./td-matrix.js');
  // Real SteamIDs are 17 digits: strings, since a JS number can't hold them exactly.
  const A = '76561198000000001', B = '76561198000000002', C = '76561198000000003';
  const MEMBERS = [A, B, C];

  // Cell: one tap on, a second off; the input is never mutated.
  const base = {};
  const one = toggleCell(base, 7, B);
  assert.deepStrictEqual(one, { '7': [B] });
  assert.deepStrictEqual(base, {}, 'toggleCell must not mutate its input');
  assert.deepStrictEqual(toggleCell(one, 7, B), {}, 'an emptied target drops out entirely');

  // Row: a partly-assigned target gains the missing members; a full row empties.
  const row = toggleRow({ '1': [B] }, 1, MEMBERS);
  assert.ok(MEMBERS.every(function (m) { return has(row, 1, m); }), 'row fills every member');
  assert.deepStrictEqual(toggleRow(row, 1, MEMBERS), {}, 'a full row empties');

  // Column: same rule over the table's targets; other members are left alone.
  const col = toggleColumn({ '1': [C], '2': [B] }, C, [1, 2]);
  assert.ok(has(col, 1, C) && has(col, 2, C) && has(col, 2, B));
  const emptied = toggleColumn(col, C, [1, 2]);
  assert.deepStrictEqual(emptied, { '2': [B] });

  // memberIds follows table order.
  assert.deepStrictEqual(memberIds({ '5': [B], '1': [B] }, B, [1, 9, 5]), [1, 5]);

  // Status: MARKER for the leader's own column, EMPTY/UNSENT before any send, SENT while the table
  // matches the last send in any order, CHANGED once it doesn't.
  const ids = [1, 2, 3];
  assert.strictEqual(memberStatus({ '1': [A] }, {}, A, ids, true).status, 'MARKER');
  assert.strictEqual(memberStatus({}, {}, B, ids, false).status, 'EMPTY');
  const unsent = memberStatus({ '1': [B] }, {}, B, ids, false);
  assert.deepStrictEqual(unsent, { n: 1, status: 'UNSENT', waiting: true });
  assert.strictEqual(memberStatus({ '1': [B], '3': [B] }, { [B]: [3, 1] }, B, ids, false).status, 'SENT');
  assert.strictEqual(memberStatus({ '1': [B] }, { [B]: [3, 1] }, B, ids, false).status, 'CHANGED');
  assert.strictEqual(memberStatus({}, { [B]: [1] }, B, ids, false).status, 'CHANGED', 'CLEAR after a send still needs a DESIGNATE');
  assert.strictEqual(memberStatus({}, { [B]: [] }, B, ids, false).status, 'SENT', 'an empty send is still a send');

  console.log('td-matrix.test.js: OK');
})().catch(function (e) { console.error(e); process.exit(1); });
