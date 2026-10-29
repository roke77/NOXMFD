// Self-check for avn-status-policy. Run: node avn-status-policy.test.js
const assert = require('assert');
const { tileClass } = require('./avn-status-policy.js');

// GEAR is the odd one out: DOWN (active) is a RED alert, not green.
assert.strictEqual(tileClass('gear',  true),  'gear-down');
assert.strictEqual(tileClass('gear',  false), 'off');
// RADAR / GUNS: green when on, gray when off.
assert.strictEqual(tileClass('radar', true),  'on');
assert.strictEqual(tileClass('radar', false), 'off');
assert.strictEqual(tileClass('guns',  true),  'on');
assert.strictEqual(tileClass('guns',  false), 'off');
// ENG / ASSIST / NVG / LIGHTS / TURRET all follow the plain green-on / gray-off rule.
for (const kind of ['eng', 'assist', 'nvg', 'lights', 'turret']) {
  assert.strictEqual(tileClass(kind, true),  'on',  kind + ' on');
  assert.strictEqual(tileClass(kind, false), 'off', kind + ' off');
}

console.log('avn-status-policy: all assertions passed');
