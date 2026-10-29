// Self-check for afm-layout-policy. Run: node afm-layout-policy.test.js
const assert = require('assert');
const P = require('./afm-layout-policy.js');

// 1000x1000 full view (em 18): columns fit beside a 356px-wide top view...
assert.strictEqual(P.flagColumnsFit(1000, 18, 356), true, 'full view keeps the side columns');
// ...but a 465px portal can't hold them beside a ~300px drawing.
assert.strictEqual(P.flagColumnsFit(465, 9, 300), false, 'narrow portal drops to the band');

// The band scales with width, held to 9..18px.
assert.strictEqual(P.bandEm(2000), 18, 'band em capped at 18');
assert.strictEqual(P.bandEm(100), 9, 'band em floored at 9');
assert.ok(Math.abs(P.bandEm(465) - 465 / 33.1) < 1e-9, 'band em fills the width between the caps');

// Outer pylon boxes: beside a 400px front view at 1000 wide, not at 465; never under 3 pylons.
assert.strictEqual(P.pylonSidesFit(7, 1000, 18, 400), true);
assert.strictEqual(P.pylonSidesFit(7, 465, 14, 400), false);
assert.strictEqual(P.pylonSidesFit(2, 1000, 18, 100), false, 'two pylons always stay in the row');

// One line: 5 boxes of 126px fit in 820px, 7 of 80px don't fit in 354px.
assert.strictEqual(P.pylonRowFits(5, 820, 126, 10.8), true);
assert.strictEqual(P.pylonRowFits(7, 354, 80, 6.8), false);

// Stacked columns, the SAH-46's seven hardpoints: outermost at the top of each side column, the
// gun alone on the centre row, below the track row.
const cols = P.pylonColumns([
  { n: 'Gun', cx: 0.503 }, { n: 'TipR', cx: 0.14 }, { n: 'PylonR', cx: 0.219 }, { n: 'BayR', cx: 0.375 },
  { n: 'BayL', cx: 0.625 }, { n: 'PylonL', cx: 0.781 }, { n: 'TipL', cx: 0.859 },
]);
assert.deepStrictEqual(cols.place.TipR, { col: 1, row: 1 });
assert.deepStrictEqual(cols.place.BayR, { col: 1, row: 3 });
assert.deepStrictEqual(cols.place.TipL, { col: 3, row: 1 });
assert.deepStrictEqual(cols.place.BayL, { col: 3, row: 3 });
assert.deepStrictEqual(cols.place.Gun,  { col: 2, row: 5 });
assert.deepStrictEqual([cols.cols, cols.sideRows, cols.centre], [3, 3, 1]);
// Three centre pylons (the A-19's) share one row, left to right, between the side columns.
const a19 = P.pylonColumns([{ n: 'c2', cx: 0.51 }, { n: 'c0', cx: 0.49 }, { n: 'c1', cx: 0.5 }, { n: 'l', cx: 0.1 }, { n: 'r', cx: 0.9 }]);
assert.deepStrictEqual([a19.place.c0, a19.place.c1, a19.place.c2], [{ col: 2, row: 3 }, { col: 3, row: 3 }, { col: 4, row: 3 }]);
assert.deepStrictEqual(a19.place.r, { col: 5, row: 1 });
// No centre pylon: just the side rows.
assert.deepStrictEqual(P.pylonColumns([{ n: 'a', cx: 0.2 }, { n: 'b', cx: 0.8 }]).centre, 0);

// The pylon area never takes more than half: 7 rows of 60px boxes plus a 200px front view in an
// 800px stack shrink the boxes to the floor, then the front view, to fit 400.
const rowsH = function(b) { return 7 * b + 6 * 10; };
const fit = P.fitPylonArea(800, 200, 20, 60, 30, 40, rowsH);
assert.strictEqual(fit.boxH, 30, 'boxes shrink to their floor');
assert.strictEqual(fit.frontH, 400 - 20 - rowsH(30), 'then the front view takes what is left');
// Within budget: untouched.
assert.deepStrictEqual(P.fitPylonArea(1000, 200, 36, 56, 30, 40, function(b) { return b; }), { boxH: 56, frontH: 200 });

console.log('afm-layout-policy: all assertions passed');
