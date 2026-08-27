const assert = require('assert');
const { hsdXY, rangeLabelForTest, geom } = require('./hsd.js');

function near(a, b, label) {
  assert.ok(Math.abs(a - b) < 1e-6, `${label}: got ${a}, expected ${b}`);
}

let p = hsdXY(0, 0, 0, 0, 1000, 2000);
near(p.x, geom.CX, 'north x');
near(p.y, geom.CY - geom.OUTER * 0.5, 'north y');

p = hsdXY(0, 0, 0, 1000, 0, 2000);
near(p.x, geom.CX + geom.OUTER * 0.5, 'east x');
near(p.y, geom.CY, 'east y');

p = hsdXY(0, 0, 90, 0, 1000, 2000);
near(p.x, geom.CX - geom.OUTER * 0.5, 'heading east puts north left');
near(p.y, geom.CY, 'heading east y');

assert.strictEqual(hsdXY(0, 0, 0, 0, 3000, 2000), null, 'outside range culled');
assert.strictEqual(rangeLabelForTest(false, 1852 * 20), '20nm');
assert.strictEqual(rangeLabelForTest(true, 1000 * 40), '40km');

console.log('hsd.test.js: OK');
