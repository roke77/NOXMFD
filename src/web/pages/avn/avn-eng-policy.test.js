// node src/web/pages/avn/avn-eng-policy.test.js
const assert = require('assert');
const P = require('./avn-eng-policy.js');

assert.deepStrictEqual(P.engineLabels(1), ['ENG']);
assert.deepStrictEqual(P.engineLabels(2), ['L ENG', 'R ENG']);
assert.deepStrictEqual(P.engineLabels(4), ['ENG 1', 'ENG 2', 'ENG 3', 'ENG 4']);
assert.strictEqual(P.pedestalSplit(1), 1);
assert.strictEqual(P.pedestalSplit(2), 1);
assert.strictEqual(P.pedestalSplit(3), 2);

// Jet: thrust. Turboshaft (0 max thrust): power. Neither: unavailable.
assert.deepStrictEqual(P.engineOutput({ thr: 31000, thrMax: 79000, pwr: -1, pwrMax: -1 }),
  { label: 'THR', value: 31000 / 79000, text: '31 kN' });
assert.deepStrictEqual(P.engineOutput({ thr: 0, thrMax: 0, pwr: 900000, pwrMax: 1200000 }),
  { label: 'PWR', value: 0.75, text: '900 kW' });
assert.strictEqual(P.engineOutput({ thr: 0, thrMax: 0, pwr: -1, pwrMax: -1 }).value, -1);
assert.strictEqual(P.engineOutput({ thr: 90000, thrMax: 79000 }).value, 1);

assert.strictEqual(P.engineStatus(0).cls, 'ok');
assert.strictEqual(P.engineStatus(1).text, 'FAULT');
assert.strictEqual(P.engineStatus(2).cls, 'inop');

assert.strictEqual(P.tankLabel('wing_tank_L'), 'WING TAN');
assert.strictEqual(P.tankLabel('fuselage_F'), 'FUSE F');
assert.strictEqual(P.tankLabel('fuel_L'), 'FUEL L');
assert.strictEqual(P.tankLabel(''), 'TANK');

// 3420 kg at 41 kg/min, 780 km/h over the ground -> 1:23 and ~1,084 km.
const eng = { fuelKg: 3420, burn: 41 / 60, gs: 780 / 3.6 };
const m = P.fuelReadouts(eng, true);
assert.strictEqual(m.fuel, '3,420');
assert.strictEqual(m.burn, '41');
assert.strictEqual(m.endurance, '1:23');
assert.strictEqual(m.range, '1,084');
assert.strictEqual(m.gs, '780');

const imp = P.fuelReadouts(eng, false);
assert.strictEqual(imp.fuel, '7,540');
assert.strictEqual(imp.fuelUnit, 'lb');
assert.strictEqual(imp.rangeUnit, 'nm');
assert.strictEqual(imp.range, '586');

// No estimate yet, engines off, and parked.
assert.strictEqual(P.fuelReadouts({ fuelKg: 3420, burn: -1, gs: 200 }, true).endurance, '--:--');
assert.strictEqual(P.fuelReadouts({ fuelKg: 3420, burn: 0, gs: 200 }, true).burn, '0');
assert.strictEqual(P.fuelReadouts({ fuelKg: 3420, burn: 0, gs: 200 }, true).endurance, '--:--');
assert.strictEqual(P.fuelReadouts({ fuelKg: 3420, burn: 0.5, gs: 2 }, true).range, '--');
assert.strictEqual(P.fuelReadouts({ fuelKg: 3420, burn: 0.5, gs: 2 }, true).endurance, '1:54');
assert.strictEqual(P.fuelReadouts(null, true).fuel, '--');

console.log('avn-eng-policy: all checks passed');
