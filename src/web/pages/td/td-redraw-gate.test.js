// Self-check for TD's redraw gate (issue #84). Run: `node td-redraw-gate.test.js`.
const assert = require('assert');

(async () => {
  const { idsKey, tgtTargetsRedraw } = await import('./td-redraw-gate.js');

  assert.strictEqual(idsKey([{ id: 3 }, { id: 1 }]), '1,3', 'ids sort numerically and join with a comma');
  assert.strictEqual(idsKey([]), '', 'no targets is the empty key');
  assert.strictEqual(idsKey(undefined), '', 'a missing list is treated as empty, not a throw');

  const targets = [{ id: 3 }, { id: 1 }];

  // Same ids, same metric: the table does not redraw — the case that must NOT repaint every 10 Hz frame
  // just because a locked target's range/grid quietly drifted.
  {
    const g = tgtTargetsRedraw(targets, '1,3', true, true);
    assert.strictEqual(g.leaderShouldRedraw, false, 'no change should not redraw the leader table');
  }

  // Same ids, metric flipped: the table must redraw — a units toggle needs a fresh
  // draw on its own, independent of any lock changing.
  {
    const g = tgtTargetsRedraw(targets, '1,3', false, true);
    assert.strictEqual(g.leaderShouldRedraw, true, 'a metric flip alone must still trigger the leader redraw');
  }

  // Ids changed, metric unchanged: a real select/deselect redraws.
  {
    const g = tgtTargetsRedraw(targets, '2,3', true, true);
    assert.strictEqual(g.leaderShouldRedraw, true, 'an id-set change must still trigger the leader redraw');
  }

  // Both changed at once: one redraw signal.
  {
    const g = tgtTargetsRedraw(targets, '2,3', false, true);
    assert.strictEqual(g.leaderShouldRedraw, true);
    assert.strictEqual(g.idsKey, '1,3', 'the returned idsKey always reflects the new snapshot');
  }

  console.log('td-redraw-gate.test.js: OK');
})();
