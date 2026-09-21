// Self-check for RWR's incoming-missile range label. Run: `node rwr.test.js`.
const assert = require('assert');
const { fmtMwRngForTest } = require('./rwr.js');

assert.strictEqual(fmtMwRngForTest(true, 9.26), '9.3 km', 'metric shows km, one decimal');
assert.strictEqual(fmtMwRngForTest(false, 9.26), '5.0 nm', 'imperial converts km to nm (0.539957/km)');
assert.strictEqual(fmtMwRngForTest(false, 1.852), '1.0 nm', '1 km is ~1 nm (sanity check on the conversion factor)');

console.log('rwr.test.js: OK');
