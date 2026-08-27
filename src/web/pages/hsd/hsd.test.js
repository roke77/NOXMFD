const assert = require('assert');
const { hsdXY, rangeLabelForTest, radarConePath, demoContacts, geom } = require('./hsd.js');

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
assert.ok(radarConePath(80 * 1852, 40 * 1852, 60).includes('A110.0 110.0'), '40nm radar cone reaches half of an 80nm HSD');
assert.strictEqual(radarConePath(80 * 1852, 0, 60), '', 'missing radar range hides cone');

const demo = demoContacts(0, 0, 20);
assert.strictEqual(demo.length, 4, 'standalone preview seeds four contacts');
assert.strictEqual(demo.filter(c => c.tg).length, 1, 'standalone preview includes one lock');
assert.ok(demo.every(c => hsdXY(0, 0, 20, c.x, c.z, 40 * 1852)), 'default preview range shows every demo contact');

console.log('hsd.test.js: OK');
