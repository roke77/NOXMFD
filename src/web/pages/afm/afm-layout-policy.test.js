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
// gun alone on a bottom row between them.
const cols = P.pylonColumns([
  { n: 'Gun', cx: 0.503 }, { n: 'TipR', cx: 0.14 }, { n: 'PylonR', cx: 0.219 }, { n: 'BayR', cx: 0.375 },
  { n: 'BayL', cx: 0.625 }, { n: 'PylonL', cx: 0.781 }, { n: 'TipL', cx: 0.859 },
]);
assert.deepStrictEqual(cols.place.TipR, { col: 1, row: 1 });
assert.deepStrictEqual(cols.place.BayR, { col: 1, row: 3 });
assert.deepStrictEqual(cols.place.TipL, { col: 3, row: 1 });
assert.deepStrictEqual(cols.place.BayL, { col: 3, row: 3 });
assert.deepStrictEqual(cols.place.Gun,  { col: 2, row: 4 });
assert.strictEqual(cols.rows, 4);
// No centre pylon: just the side rows.
assert.strictEqual(P.pylonColumns([{ n: 'a', cx: 0.2 }, { n: 'b', cx: 0.8 }]).rows, 1);

console.log('afm-layout-policy: all assertions passed');
