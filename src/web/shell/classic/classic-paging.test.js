// Self-check for the classic bezel's split-pane pagination. Run: `node classic-paging.test.js`.
// Imports the real module directly.
const assert = require('assert');
const P = require('./classic-paging.js');

const { WPN_SPLIT_MAX, MAIN_PANE_SLOTS } = P;

// ── WPN: the pair invariant ────────────────────────────────────────────────────────────
// A pair (ARM,SAFE / A-A,A-G) must never straddle a page boundary.
for (let n = 0; n <= 24; n++) {
  const pages = P.buildWpnSplitPages(n);

  pages.forEach((page, i) => {
    assert.ok(page.length <= WPN_SPLIT_MAX,
      `${n} weapons: page ${i} has ${page.length} slots, pane only has ${WPN_SPLIT_MAX}`);
  });

  // Every weapon appears exactly once, in order.
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

assert.strictEqual(P.buildWpnSplitPages(4).flat().filter(s => s.type === 'empty').length, 0,
  '4 weapons needs no padding (pairs already land aligned)');
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

// ── pageOfSelection: which page opens when the pilot picks a weapon ────────────────────
const wl = n => Array.from({ length: n }, (_, i) => ({ n: 'W' + i }));

assert.strictEqual(P.pageOfSelection(wl(6), null, 4), -1, 'no selection should be -1');
assert.strictEqual(P.pageOfSelection(wl(6), '', 4), -1, 'empty selection should be -1');
assert.strictEqual(P.pageOfSelection(wl(6), 'W99', 4), -1, 'selection not in the loadout should be -1');
assert.strictEqual(P.pageOfSelection(undefined, 'W0', 4), -1, 'undefined list should be -1, not a throw');

// The two layouts differ only by page size, which is the whole reason they share this helper.
assert.strictEqual(P.pageOfSelection(wl(12), 'W4', P.WPN_SPLIT_MAX), 1, 'split: W4 is on page 1 of 4-up');
assert.strictEqual(P.pageOfSelection(wl(12), 'W4', P.WPN_MAX_DISPLAY), 0, 'full: W4 is still on page 0 of 5-up');
assert.strictEqual(P.pageOfSelection(wl(12), 'W5', P.WPN_MAX_DISPLAY), 1, 'full: W5 opens page 1 of 5-up');

// A plain divide is only correct if padding never displaces a weapon. Cross-check the helper
// against where buildWpnSplitPages actually puts each weapon, for every loadout size.
for (let n = 1; n <= 24; n++) {
  const pages = P.buildWpnSplitPages(n);
  for (let i = 0; i < n; i++) {
    const actual = pages.findIndex(pg => pg.some(s => s.type === 'weapon' && s.index === i));
    assert.strictEqual(P.pageOfSelection(wl(n), 'W' + i, P.WPN_SPLIT_MAX), actual,
      `${n} weapons: selecting W${i} would open page ${P.pageOfSelection(wl(n), 'W' + i, P.WPN_SPLIT_MAX)}, but it is on page ${actual}`);
  }
}

// ── listPaneLayout: no two labels may land on the same physical key ────────────────────
// It's a hand-maintained table, and a typo there doesn't crash — it quietly stacks two labels on
// one key, or leaves a control unreachable. A bezel column has 6 keys; in 'h' split both panes
// share all 12, so pane 0 and pane 1 must not overlap either.
const BANK_KEYS = 6;
const cell = k => k.bank + k.index;
const allCells = L => [L.main, L.next].concat(L.items).map(cell);

for (const variant of ['h', 'v', 'vw']) {
  for (const page of ['wpn', 'avn', 'main', 'tgt']) {
    const panes = [0, 1].map(i => P.listPaneLayout(variant, i, page));

    panes.forEach((L, i) => {
      const cells = allCells(L);
      assert.strictEqual(new Set(cells).size, cells.length,
        `${variant}/${page} pane ${i}: two labels share a key (${cells.join(' ')})`);

      [L.main, L.next].concat(L.items).forEach(k => {
        assert.ok(k.bank === 'left' || k.bank === 'right', `${variant}/${page} pane ${i}: bad bank ${k.bank}`);
        assert.ok(k.index >= 0 && k.index < BANK_KEYS,
          `${variant}/${page} pane ${i}: key index ${k.index} outside 0..${BANK_KEYS - 1}`);
      });

      assert.strictEqual(L.items.length, 4, `${variant}/${page} pane ${i}: expected 4 item slots`);
      // itemSides duplicates items[].bank — pin them together so they can't drift apart.
      assert.deepStrictEqual(L.itemSides, L.items.map(k => k.bank),
        `${variant}/${page} pane ${i}: itemSides disagrees with items[].bank`);
    });

    // The two panes are on screen at once, so their key sets must be disjoint.
    const overlap = allCells(panes[0]).filter(c => allCells(panes[1]).includes(c));
    assert.strictEqual(overlap.length, 0,
      `${variant}/${page}: panes 0 and 1 both claim ${overlap.join(' ')}`);
  }
}

// A vertical pane owns one column outright and uses all six of its keys.
const v0 = P.listPaneLayout('v', 0, 'wpn');
assert.deepStrictEqual(allCells(v0).sort(), ['left0', 'left1', 'left2', 'left3', 'left4', 'left5'],
  'vertical pane 0 should fill the left column');
assert.deepStrictEqual(allCells(P.listPaneLayout('v', 1, 'wpn')).sort(),
  ['right0', 'right1', 'right2', 'right3', 'right4', 'right5'],
  'vertical pane 1 should fill the right column');

// WPN/AVN put NEXT top-right; other list pages have no top-right control and shift it to the
// column's end. Pinned because the two branches are easy to conflate when editing the table.
assert.deepStrictEqual(P.listPaneLayout('h', 0, 'wpn').next, { bank: 'right', index: 0 },
  'h/wpn NEXT should sit top-right');
assert.deepStrictEqual(P.listPaneLayout('h', 0, 'tgt').next, { bank: 'right', index: 2 },
  'h/tgt NEXT should sit at the end of the right column');

console.log('classic-paging.test.js: OK');
