// Self-check for split-mode slot coverage. Run: `node split-slots.test.js`.
//
// The invariant this guards: SPLIT_SLOTS[page] must be index-aligned with NAV[page] — entry i
// places NAV[page][i] (renderSplitLabels in mfd.js). A NAV list that grows without a matching
// SPLIT_SLOTS update silently drops items in a split pane; that's exactly the bug the CFG group
// (issue #39) shipped with — NAV.keys grew from 1 item to 4 but SPLIT_SLOTS.keys stayed at 1, so
// KEY/LYT/RTS were unreachable in a split pane until caught by hand in-game. This test exists so
// the next one is caught here instead.
const assert = require('assert');
const { NAV } = require('../nav-model.js');
const { SPLIT_SLOTS } = require('./split-slots.js');

// 'main' and 'map' are documented exceptions (split-slots.js's header comment): both are paginated
// in mfd.js (MAIN_SPLIT_ITEMS / mapNavPaneSlice) rather than a fixed slot table, since MAIN has
// eleven destinations and MAP has eight (issue #38's R+/R-), both past six keys. 'lyt' isn't in
// NAV at all (nav-model.test.js asserts that), so it never reaches this loop — picking it from a
// pane collapses the split instead.
const NO_SPLIT_TABLE = new Set(['main', 'map']);

for (const [page, items] of Object.entries(NAV)) {
  if (NO_SPLIT_TABLE.has(page)) continue;
  const slots = SPLIT_SLOTS[page];
  assert.ok(slots, `NAV.${page} (${items.length} item(s)) has no SPLIT_SLOTS entry at all — it would be unreachable in a split pane`);
  assert.strictEqual(slots.length, items.length,
    `NAV.${page} has ${items.length} item(s) but SPLIT_SLOTS.${page} has ${slots.length} slot(s) — ` +
    `a split pane would silently drop ${items.length - slots.length > 0 ? 'the extra NAV item(s)' : 'nothing, but has unused slot(s)'}`);
}

// The reverse direction: no orphaned SPLIT_SLOTS entry for a page NAV doesn't know about.
for (const page of Object.keys(SPLIT_SLOTS)) {
  assert.ok(page in NAV, `SPLIT_SLOTS.${page} exists but NAV.${page} doesn't — dead entry?`);
}

// Every slot's own shape — same PLACEMENT_KEYS idea as nav-model.test.js, but for slots this IS
// the point: side must be 'left' or 'right', slot a small non-negative integer.
for (const [page, slots] of Object.entries(SPLIT_SLOTS)) {
  slots.forEach((s, i) => {
    const where = `SPLIT_SLOTS.${page}[${i}]`;
    assert.ok(s.side === 'left' || s.side === 'right', `${where}.side must be 'left' or 'right', got ${JSON.stringify(s.side)}`);
    assert.ok(Number.isInteger(s.slot) && s.slot >= 0, `${where}.slot must be a non-negative integer, got ${JSON.stringify(s.slot)}`);
  });
}

console.log('split-slots.test.js: OK');
