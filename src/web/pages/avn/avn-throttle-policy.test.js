// Self-check for avn-throttle-policy. Run: node avn-throttle-policy.test.js
const assert = require('assert');
const { throttleReadout } = require('./avn-throttle-policy.js');

// No value → NA
let r = throttleReadout(-1, true, 0.8);
assert.strictEqual(r.na, true);
assert.strictEqual(r.text, '--');

// Non-AB aircraft (Compass / helo): plain 0-100%, no boundary
r = throttleReadout(0.5, false, 1);
assert.deepStrictEqual([r.zone, r.text, r.boundary], ['plain', '50%', null]);
assert.strictEqual(r.fill, 0.5);

// AB aircraft, throttle inside MIL zone → NUMBER ONLY (no "MIL" until the detent)
r = throttleReadout(0.4, true, 0.8);          // 0.4 / 0.8 = 50%
assert.deepStrictEqual([r.zone, r.text, r.boundary], ['mil', '50%', 0.8]);
assert.strictEqual(r.fill, 0.4);               // fill is the raw throttle

// AB aircraft, throttle at the MIL detent (100%) → the "MIL" label replaces the number
r = throttleReadout(0.8, true, 0.8);
assert.deepStrictEqual([r.zone, r.text], ['mil', 'MIL']);

// AB aircraft, throttle in reheat zone → rescaled 0-100% as a BARE number, red zone.
// (The page renders the "AFTERBURNER" tag above the bar; the readout stays just the percentage.)
r = throttleReadout(0.9, true, 0.8);          // (0.9-0.8)/(1-0.8) = 50%
assert.deepStrictEqual([r.zone, r.text, r.boundary], ['ab', '50%', 0.8]);

// Full reheat
r = throttleReadout(1, true, 0.8);
assert.deepStrictEqual([r.zone, r.text], ['ab', '100%']);

// Degenerate split (abStart 1 or 0) falls back to plain even if hasAb
assert.strictEqual(throttleReadout(0.5, true, 1).zone, 'plain');
assert.strictEqual(throttleReadout(0.5, true, 0).zone, 'plain');

// Out-of-range throttle clamps
assert.strictEqual(throttleReadout(1.5, true, 0.8).text, '100%');

console.log('avn-throttle-policy: all assertions passed');
