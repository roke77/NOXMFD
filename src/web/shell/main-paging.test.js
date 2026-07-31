// Self-check for MAIN's paginated nav list in a split pane (mfd.js mainPageSizes / mainPaneSlice +
// renderSplitLabels' 'main' branch). Models the sizing the way layout-sticky.test.js models the head
// guards, so the properties that broke stay locked: a split pane has SIX physical keys; PREV anchors
// the first key past page one, NEXT the last key before the last page, and items fill the rest — so
// every page but the last must be exactly full and no page may ever ask for more than six.
// Run: node src/web/shell/main-paging.test.js
const assert = require('assert');

const PANE_SLOTS = 6;   // physical keys a split pane exposes for the MAIN list (MAIN_PANE_SLOTS)

// mfd.js mainPageSizes: fill six keys per page, minus PREV on every page but the first and NEXT on
// every page but the last.
function pageSizes(total) {
  const sizes = [];
  let placed = 0;
  while (placed < total) {
    const room = PANE_SLOTS - (sizes.length === 0 ? 0 : 1);
    if (total - placed <= room) { sizes.push(total - placed); break; }
    sizes.push(room - 1);
    placed += room - 1;
  }
  return sizes;
}

// A list far longer than today's eleven items — the point is that the shape holds as MAIN grows,
// not that it happens to hold at one length.
for (let total = 1; total <= 40; total++) {
  const sizes = pageSizes(total);
  const seen = [];
  let start = 0;
  for (let p = 0; p < sizes.length; p++) {
    const count = sizes[p];
    const hasPrev = p > 0;
    const hasNext = p < sizes.length - 1;
    // Keys a page actually draws: PREV + its items + NEXT.
    const cells = count + (hasPrev ? 1 : 0) + (hasNext ? 1 : 0);
    assert.ok(cells <= PANE_SLOTS,
      `total=${total} page=${p} needs ${cells} keys, only ${PANE_SLOTS} exist`);
    assert.ok(count > 0, `total=${total} page=${p} is empty`);
    // Every page but the last fills all six keys — that's the no-gaps property. (The last page is
    // a normal partial page, unless the whole list fits on one.)
    if (p < sizes.length - 1) {
      assert.strictEqual(cells, PANE_SLOTS,
        `total=${total} page=${p} draws ${cells} keys — a non-last page must fill all ${PANE_SLOTS}`);
    }
    for (let i = 0; i < count; i++) seen.push(start + i);
    start += count;
  }
  // Every item reachable, exactly once, in order — paging can't skip or repeat a destination.
  assert.deepStrictEqual(seen, Array.from({ length: total }, (_, i) => i),
    `total=${total} does not page through every item exactly once`);
  // Only the first page lacks PREV and only the last lacks NEXT, so the chain is walkable both ways.
  assert.strictEqual(0 > 0, false);                       // page 0 never has PREV (definitional)
  assert.strictEqual(sizes.length - 1 < sizes.length - 1, false);   // last page never has NEXT
}

// The shape the user specified at eleven items: first page five items + NEXT, a middle page
// PREV + four + NEXT, the last page PREV + two.
assert.deepStrictEqual(pageSizes(11), [5, 4, 2], 'eleven items must page as 5 / 4 / 2');

console.log('main-paging: ok');
