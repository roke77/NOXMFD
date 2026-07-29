// Self-check for MAIN's paginated nav list in a split pane (mfd.js mainPaneSlice +
// renderSplitLabels' 'main' branch). Models the slicing the way layout-sticky.test.js models the
// head guards, so the property that broke stays locked: a split pane has SIX physical key slots,
// and PREV/NEXT each consume one, so no page may ever ask for more than six.
// Run: node src/web/shell/main-paging.test.js
const assert = require('assert');

const MAIN_PANE_SIZE = 4;   // must match mfd.js
const PANE_SLOTS     = 6;   // listPaneLayout: back slot + four items + next slot

// mfd.js mainPaneSlice, minus the paneMainPage[] clamping (which only keeps the index in range).
function slice(total, page) {
  const start = page * MAIN_PANE_SIZE;
  const count = Math.max(0, Math.min(MAIN_PANE_SIZE, total - start));
  return { start, count, hasPrev: page > 0, hasNext: start + count < total };
}

// A list far longer than today's eleven items — the point is that the shape holds as MAIN grows,
// not that it happens to hold at one length.
for (let total = 1; total <= 40; total++) {
  const pages = Math.ceil(total / MAIN_PANE_SIZE);
  const seen = [];
  for (let p = 0; p < pages; p++) {
    const s = slice(total, p);
    // Labels a page actually draws: PREV + its items + NEXT.
    const cells = s.count + (s.hasPrev ? 1 : 0) + (s.hasNext ? 1 : 0);
    assert.ok(cells <= PANE_SLOTS,
      `total=${total} page=${p} needs ${cells} slots, only ${PANE_SLOTS} exist`);
    assert.ok(s.count > 0, `total=${total} page=${p} is empty`);
    for (let i = 0; i < s.count; i++) seen.push(s.start + i);
  }
  // Every item reachable, exactly once, in order — paging can't skip or repeat a destination.
  assert.deepStrictEqual(seen, Array.from({ length: total }, (_, i) => i),
    `total=${total} does not page through every item exactly once`);
  // Only the first page lacks PREV and only the last lacks NEXT, so the chain is walkable both ways.
  assert.strictEqual(slice(total, 0).hasPrev, false);
  assert.strictEqual(slice(total, pages - 1).hasNext, false);
}

console.log('main-paging: ok');
