// Self-check for avn-failure-policy: run with `node avn-failure-policy.test.js`.
// Covers the three real per-aircraft naming schemes seen in the game logs.
const assert = require('assert');
const { failureSide, failureText } = require('./avn-failure-policy.js');

// Side detection across the wording variants.
assert.strictEqual(failureSide('LEFT ENGINE FIRE'),  'L');   // T/A-30 Compass
assert.strictEqual(failureSide('RIGHT ENGINE FIRE'), 'R');
assert.strictEqual(failureSide('ENGINE FIRE L'),     'L');   // F-99 Shrike
assert.strictEqual(failureSide('ENGINE FIRE R'),     'R');
assert.strictEqual(failureSide('LEFT ENGINE FAIL'),  'L');   // SAH-46 Chicane
assert.strictEqual(failureSide('RIGHT ENGINE FAIL'), 'R');
assert.strictEqual(failureSide('TAIL ROTOR FAIL'),   null);  // no standalone L/R
assert.strictEqual(failureSide('MAIN ROTOR DAMAGE'), null);

// Display text: side prefix + ENGINE abbreviated, both wordings collapse to the same label.
assert.strictEqual(failureText('LEFT ENGINE FIRE'),  'L ENG FIRE');
assert.strictEqual(failureText('ENGINE FIRE L'),     'L ENG FIRE');
assert.strictEqual(failureText('RIGHT ENGINE FIRE'), 'R ENG FIRE');
assert.strictEqual(failureText('ENGINE FIRE R'),     'R ENG FIRE');
assert.strictEqual(failureText('LEFT ENGINE FAIL'),  'L ENG FAIL');
assert.strictEqual(failureText('TAIL ROTOR FAIL'),   'TAIL ROTOR FAIL');   // side-less, passes through
assert.strictEqual(failureText('MAIN ROTOR DAMAGE'), 'MAIN ROTOR DAMAGE');

console.log('avn-failure-policy: all assertions passed');
