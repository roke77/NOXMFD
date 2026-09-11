// Self-check for TD's redraw gate (issue #84). Run: `node td-redraw-gate.test.js`.
const assert = require('assert');

(async () => {
  const { idsKey, tgtTargetsRedraw } = await import('./td-redraw-gate.js');

  assert.strictEqual(idsKey([{ id: 3 }, { id: 1 }]), '1,3', 'ids sort numerically and join with a comma');
  assert.strictEqual(idsKey([]), '', 'no targets is the empty key');
  assert.strictEqual(idsKey(undefined), '', 'a missing list is treated as empty, not a throw');

  const targets = [{ id: 3 }, { id: 1 }];

  // Same ids, same metric: neither view redraws — the case that must NOT repaint every 10 Hz frame
  // just because a locked target's range/grid quietly drifted.
  {
    const g = tgtTargetsRedraw(targets, '1,3', true, true);
    assert.strictEqual(g.leaderShouldRedraw, false, 'no change should not redraw the leader table');
    assert.strictEqual(g.memberShouldRedraw, false, 'no change should not redraw the member list');
  }

  // Same ids, metric flipped: both views must redraw exactly once — a units toggle needs a fresh
  // draw on its own, independent of any lock changing.
  {
    const g = tgtTargetsRedraw(targets, '1,3', false, true);
    assert.strictEqual(g.leaderShouldRedraw, true, 'a metric flip alone must still trigger the leader redraw');
    assert.strictEqual(g.memberShouldRedraw, true, 'a metric flip alone must still trigger the member redraw');
  }

  // Ids changed, metric unchanged: leader redraws as before; the member gate is metric-only since
  // renderMember already rebuilds wholesale on every 'td-state-push' regardless of target ids.
  {
    const g = tgtTargetsRedraw(targets, '2,3', true, true);
    assert.strictEqual(g.leaderShouldRedraw, true, 'an id-set change must still trigger the leader redraw');
    assert.strictEqual(g.memberShouldRedraw, false, 'an id-set change alone must not redraw the member list');
  }

  // Both changed at once: still exactly one redraw signal each, not a double-fire.
  {
    const g = tgtTargetsRedraw(targets, '2,3', false, true);
    assert.strictEqual(g.leaderShouldRedraw, true);
    assert.strictEqual(g.memberShouldRedraw, true);
    assert.strictEqual(g.idsKey, '1,3', 'the returned idsKey always reflects the new snapshot');
  }

  console.log('td-redraw-gate.test.js: OK');
})();
