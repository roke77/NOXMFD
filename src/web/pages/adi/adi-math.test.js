// Self-check for adi-math.js. Run: `node adi-math.test.js`.
const assert = require('assert');
const M = require('./adi-math.js');

// Easing converges and never overshoots.
let v = 0;
for (let i = 0; i < 60; i++) v = M.approach(v, 10, 1 / 60);
assert.ok(v > 9.9 && v <= 10, 'approach converges on the target within a second');

// Heading eases the short way across north.
assert.strictEqual(M.angleDelta(359, 1), 2);
assert.strictEqual(M.angleDelta(1, 359), -2);
assert.strictEqual(M.angleDelta(90, 270), -180);
const h = M.approachAngle(359, 1, 1 / 60);
assert.ok(h > 359 || h < 1, 'approachAngle moves toward 001 through 000, not back through 180');

// Units follow the game's setting.
assert.strictEqual(M.units(false).spd.unit, 'KT');
assert.strictEqual(M.units(true).alt.unit, 'M');
assert.ok(Math.abs(220 * M.units(false).spd.k - 427.6) < 0.1);

// The VVI bar spans the game's own +/-50 m/s in either unit.
assert.ok(Math.abs(50 * M.units(false).vvi.k - 9.84) < 0.01);
assert.strictEqual(M.units(true).vvi.max, 50);

assert.strictEqual(M.signed(2400), '+2400');
assert.strictEqual(M.signed(-50), '-50');
assert.strictEqual(M.signed(0), '0');

console.log('adi-math.test.js: OK');
