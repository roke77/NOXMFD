// Self-check for f35-wpn-paging. Run: node f35-wpn-paging.test.js
//
// Guards the paging math and the top-row label switch the F-35 layout leans on: the visible slice
// must stay within maxDisplay and page bounds, and the nav must read MAIN on page 0 / PREV after
// it, with NEXT present only while pages remain. A drift here silently pages the wrong weapons or
// strands the pilot on a page with no way back.
const assert = require('assert');
const { wpnPaging } = require('./f35-wpn-paging.js');

const D = 5;   // WPN_MAX_DISPLAY in f35.js (ROWS - 1)
const items = n => Array.from({ length: n }, (_, i) => ({ n: 'W' + i }));
const names = arr => arr.map(it => it.n);

// Empty loadout → single page, no NEXT, top-left is MAIN (not PREV).
let r = wpnPaging([], 0, D);
assert.strictEqual(r.maxPage, 0);
assert.strictEqual(r.page, 0);
assert.deepStrictEqual(r.visible, []);
assert.deepStrictEqual(r.nav.map(i => i.action), ['main']);

// Exactly maxDisplay items → still one page: everything fits, so no NEXT.
r = wpnPaging(items(D), 0, D);
assert.strictEqual(r.maxPage, 0);
assert.strictEqual(r.visible.length, D);
assert.deepStrictEqual(r.nav.map(i => i.action), ['main']);

// One past the page → a second page appears. Page 0 shows the first maxDisplay and gains NEXT.
r = wpnPaging(items(D + 1), 0, D);
assert.strictEqual(r.maxPage, 1);
assert.deepStrictEqual(names(r.visible), ['W0', 'W1', 'W2', 'W3', 'W4']);
assert.deepStrictEqual(r.nav.map(i => i.action), ['main', 'wpn-next']);

// Page 1 of that loadout: the remainder, MAIN→PREV on the left, and NEXT gone (last page).
r = wpnPaging(items(D + 1), 1, D);
assert.strictEqual(r.page, 1);
assert.deepStrictEqual(names(r.visible), ['W5']);
assert.deepStrictEqual(r.nav.map(i => i.action), ['wpn-prev']);

// Page clamps to [0, maxPage] — an over- or under-shoot (from a stale prev/next) never escapes.
assert.strictEqual(wpnPaging(items(D + 1), 99, D).page, 1);
assert.strictEqual(wpnPaging(items(D + 1), -3, D).page, 0);

// maxPage boundaries: a full multiple of D does NOT add an empty page; one over does.
assert.strictEqual(wpnPaging(items(2 * D), 0, D).maxPage, 1);       // 10 items → pages 0..1
assert.strictEqual(wpnPaging(items(2 * D + 1), 0, D).maxPage, 2);   // 11 items → pages 0..2

// NEXT belongs top-right, so it (and only it) names its cell; MAIN/PREV take the index's cell.
r = wpnPaging(items(D + 1), 0, D);
const next = r.nav.find(i => i.action === 'wpn-next');
assert.deepStrictEqual(next.cell, { row: 1, col: 2 });
assert.ok(!('cell' in r.nav[0]), 'MAIN/PREV must not carry placement');

console.log('f35-wpn-paging.test.js: OK');
