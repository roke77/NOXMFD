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

// Tape marks: 428 kt with labels every 10 and ticks every 5, +/-5 labels shown.
const marks = M.tapeMarks(428, 10, 5, 5);
assert.strictEqual(marks[0].v, 380);
assert.strictEqual(marks[marks.length - 1].v, 475);
const m430 = marks.find((m) => m.v === 430);
assert.ok(m430.major && Math.abs(m430.off - 0.2) < 1e-9, '430 is a labelled mark 0.2 steps above 428');
assert.strictEqual(marks.find((m) => m.v === 425).major, false);

assert.strictEqual(M.signed(2400), '+2400');
assert.strictEqual(M.signed(-50), '-50');
assert.strictEqual(M.signed(0), '0');

console.log('adi-math.test.js: OK');
