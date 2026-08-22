// Self-check for afm-failure-policy: run with `node afm-failure-policy.test.js`.
const assert = require('assert');
const { failureSide, failureText } = require('./afm-failure-policy.js');

assert.strictEqual(failureSide('LEFT ENGINE FIRE'),  'L');
assert.strictEqual(failureSide('RIGHT ENGINE FIRE'), 'R');
assert.strictEqual(failureSide('ENGINE FIRE L'),     'L');
assert.strictEqual(failureSide('ENGINE FIRE R'),     'R');
assert.strictEqual(failureSide('LEFT ENGINE FAIL'),  'L');
assert.strictEqual(failureSide('RIGHT ENGINE FAIL'), 'R');
assert.strictEqual(failureSide('TAIL ROTOR FAIL'),   null);
assert.strictEqual(failureSide('MAIN ROTOR DAMAGE'), null);

assert.strictEqual(failureText('LEFT ENGINE FIRE'),  'L ENG FIRE');
assert.strictEqual(failureText('ENGINE FIRE L'),     'L ENG FIRE');
assert.strictEqual(failureText('RIGHT ENGINE FIRE'), 'R ENG FIRE');
assert.strictEqual(failureText('ENGINE FIRE R'),     'R ENG FIRE');
assert.strictEqual(failureText('LEFT ENGINE FAIL'),  'L ENG FAIL');
assert.strictEqual(failureText('TAIL ROTOR FAIL'),   'TAIL ROTOR FAIL');
assert.strictEqual(failureText('MAIN ROTOR DAMAGE'), 'MAIN ROTOR DAMAGE');

console.log('afm-failure-policy: all assertions passed');
