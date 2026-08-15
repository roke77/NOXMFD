// Self-check for the classic bezel's split-pane pagination. Run: `node classic-paging.test.js`.
//
// Unlike the model-based checks next door (layout-sticky.test.js), this imports the REAL module,
// so it fails when mfd.js's behaviour changes rather than when a copy of it does.
//
// The properties worth pinning are the ones a pilot notices when they break: a control pair split
// across two pages (you press ARM and there is no SAFE), a page asking for more keys than the pane
// has, or a stale page index surviving a loadout change and showing a blank pane.
const assert = require('assert');
const P = require('./classic-paging.js');

const { WPN_SPLIT_MAX, MAIN_PANE_SLOTS } = P;

// ── WPN: the pair invariant ────────────────────────────────────────────────────────────
// (ARM,SAFE) and (A/A,A/G) are adjacent entries; a pair must never straddle a page boundary,
// because a pane showing one half of a toggle is a control the pilot cannot complete.
for (let n = 0; n <= 24; n++) {
  const pages = P.buildWpnSplitPages(n);

  pages.forEach((page, i) => {
    assert.ok(page.length <= WPN_SPLIT_MAX,
      `${n} weapons: page ${i} has ${page.length} slots, pane only has ${WPN_SPLIT_MAX}`);
  });

  // Every weapon still appears exactly once, in order — padding must not drop or reorder any.
  const weaponIdx = pages.flat().filter(s => s.type === 'weapon').map(s => s.index);
  assert.deepStrictEqual(weaponIdx, [...Array(n).keys()], `${n} weapons: weapon slots lost or reordered`);

  // All four controls survive, in order.
  const ctrlIds = pages.flat().filter(s => s.type === 'ctrl').map(s => s.id);
  assert.deepStrictEqual(ctrlIds,
    ['master-arms-on', 'master-arms-off', 'combat-mode-aa', 'combat-mode-ag'],
    `${n} weapons: controls lost or reordered`);

  // THE invariant: each pair lands on a single page.
  const pageOf = id => pages.findIndex(pg => pg.some(s => s.id === id));
  assert.strictEqual(pageOf('master-arms-on'), pageOf('master-arms-off'),
    `${n} weapons: ARM/SAFE split across pages`);
  assert.strictEqual(pageOf('combat-mode-aa'), pageOf('combat-mode-ag'),
    `${n} weapons: A/A · A/G split across pages`);
}

// The padding only fires when it must — a count that already leaves a pair aligned gains no blanks.
assert.strictEqual(P.buildWpnSplitPages(4).flat().filter(s => s.type === 'empty').length, 0,
  '4 weapons needs no padding (pairs already land aligned)');
// 3 weapons would put ARM on the last slot of page 0, so one blank is inserted to push the pair on.
assert.strictEqual(P.buildWpnSplitPages(3).flat().filter(s => s.type === 'empty').length, 1,
  '3 weapons should pad exactly one slot');

// ── WPN slice: clamping and the page/pages readout ─────────────────────────────────────
const weapons = n => Array.from({ length: n }, (_, i) => ({ n: 'W' + i }));

// A page index left over from a bigger loadout must clamp, not return an empty pane.
const shrunk = P.wpnPaneSlice(weapons(2), 9);
assert.strictEqual(shrunk.pageIndex, shrunk.pages - 1, 'stale high page did not clamp to the last page');
assert.ok(shrunk.slots.length > 0, 'clamped page must still render slots');
assert.strictEqual(shrunk.hasNext, false, 'last page must not offer NEXT');

const neg = P.wpnPaneSlice(weapons(8), -3);
assert.strictEqual(neg.pageIndex, 0, 'negative page did not clamp to 0');
assert.strictEqual(neg.hasPrev, false, 'first page must not offer PREV');

// Undefined list (no loadout yet) must not throw — the shell calls this before telemetry arrives.
assert.doesNotThrow(() => P.wpnPaneSlice(undefined, 0), 'undefined weapon list threw');

// items are the actual weapon objects the slots point at, in order.
const p0 = P.wpnPaneSlice(weapons(6), 0);
assert.deepStrictEqual(p0.items.map(w => w.n), ['W0', 'W1', 'W2', 'W3'], 'page 0 items wrong');
const p1 = P.wpnPaneSlice(weapons(6), 1);
assert.deepStrictEqual(p1.items.map(w => w.n), ['W4', 'W5'], 'page 1 items wrong');
assert.strictEqual(p1.hasPrev, true, 'page 1 should offer PREV');

// ── AVN: 8 groups over 4-key pages ─────────────────────────────────────────────────────
const GROUPS = ['gear', 'radar', 'guns', 'eng', 'assist', 'nvg', 'lights', 'turret'];
const a0 = P.avnPaneSlice(GROUPS, 0);
assert.deepStrictEqual(a0.items, ['gear', 'radar', 'guns', 'eng'], 'AVN page 0 wrong');
assert.deepStrictEqual([a0.page, a0.pages, a0.hasPrev, a0.hasNext], [1, 2, false, true], 'AVN page 0 nav wrong');
const a1 = P.avnPaneSlice(GROUPS, 1);
assert.deepStrictEqual(a1.items, ['assist', 'nvg', 'lights', 'turret'], 'AVN page 1 wrong');
assert.deepStrictEqual([a1.page, a1.pages, a1.hasPrev, a1.hasNext], [2, 2, true, false], 'AVN page 1 nav wrong');
assert.strictEqual(P.avnPaneSlice(GROUPS, 7).pageIndex, 1, 'AVN page did not clamp');

// ── MAIN: every page but the last is exactly full, and none overflows the pane ─────────
// PREV anchors the first key past page one, NEXT the last key before the last page — so a short
// page anywhere but the end means a wasted key the pilot could have used.
for (let total = 1; total <= 40; total++) {
  const sizes = P.mainPageSizes(total);
  assert.strictEqual(sizes.reduce((a, b) => a + b, 0), total, `${total} items: sizes do not sum to total`);
  sizes.forEach((s, i) => {
    assert.ok(s > 0, `${total} items: page ${i} is empty`);
    const reserved = (i > 0 ? 1 : 0) + (i < sizes.length - 1 ? 1 : 0);   // PREV and/or NEXT
    assert.ok(s + reserved <= MAIN_PANE_SLOTS,
      `${total} items: page ${i} needs ${s + reserved} keys, pane has ${MAIN_PANE_SLOTS}`);
    if (i < sizes.length - 1)
      assert.strictEqual(s + reserved, MAIN_PANE_SLOTS, `${total} items: page ${i} is not full`);
  });
}

// The shape the user specified at eleven items: first page five items + NEXT, a middle page
// PREV + four + NEXT, the last page PREV + two.
assert.deepStrictEqual(P.mainPageSizes(11), [5, 4, 2], 'eleven items must page as 5 / 4 / 2');

// MAIN slice walks the list without gaps or repeats across its pages.
const items = Array.from({ length: 14 }, (_, i) => 'I' + i);
let seen = [];
for (let p = 0; p < P.mainPageSizes(items.length).length; p++) seen = seen.concat(P.mainPaneSlice(items, p).items);
assert.deepStrictEqual(seen, items, 'MAIN pages do not reconstruct the list');
assert.strictEqual(P.mainPaneSlice(items, 99).pageIndex, P.mainPageSizes(items.length).length - 1,
  'MAIN page did not clamp');

console.log('classic-paging.test.js: OK');
