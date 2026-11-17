// node src/web/pages/avn/avn-eng-policy.test.js
const assert = require('assert');
const P = require('./avn-eng-policy.js');

const E = { prop: false }, PR = { prop: true };
assert.deepStrictEqual(P.engineLabels([E]), ['ENG']);
assert.deepStrictEqual(P.engineLabels([E, E]), ['L ENG', 'R ENG']);
assert.deepStrictEqual(P.engineLabels([E, E, E, E]), ['ENG 1', 'ENG 2', 'ENG 3', 'ENG 4']);
// Tarantula: turboshafts and the props they drive, interleaved left to right, numbered apart.
assert.deepStrictEqual(P.engineLabels([E, PR, E, PR, E, PR, E, PR]),
  ['ENG 1', 'PROP 1', 'ENG 2', 'PROP 2', 'ENG 3', 'PROP 3', 'ENG 4', 'PROP 4']);
assert.deepStrictEqual(P.engineLabels([PR, PR]), ['L PROP', 'R PROP']);
assert.strictEqual(P.pedestalSplit(1), 1);
assert.strictEqual(P.pedestalSplit(2), 1);
assert.strictEqual(P.pedestalSplit(3), 2);
assert.strictEqual(P.sideColumns(1), 1);
assert.strictEqual(P.sideColumns(2), 1);
assert.strictEqual(P.sideColumns(4), 2);

// Jet: thrust. Turboshaft (0 max thrust): power. Neither: unavailable.
assert.deepStrictEqual(P.engineOutput({ thr: 31000, thrMax: 79000, pwr: -1, pwrMax: -1 }),
  { label: 'THR', value: 31000 / 79000, text: '31 kN', num: '31', unit: 'kN' });
assert.deepStrictEqual(P.engineOutput({ thr: 0, thrMax: 0, pwr: 900000, pwrMax: 1200000 }),
  { label: 'PWR', value: 0.75, text: '900 kW', num: '900', unit: 'kW' });
assert.strictEqual(P.engineOutput({ thr: 0, thrMax: 0, pwr: -1, pwrMax: -1 }).value, -1);
assert.strictEqual(P.engineOutput({ thr: 90000, thrMax: 79000 }).value, 1);
// Prop: thrust with no max -> a readout, not a dial.
assert.deepStrictEqual(P.engineOutput({ thr: 18400, thrMax: 0, pwr: -1, pwrMax: -1, prop: true }),
  { label: 'THR', value: -1, text: '18 kN', num: '18', unit: 'kN', readout: true });

assert.strictEqual(P.engineStatus(0).cls, 'ok');
assert.strictEqual(P.engineStatus(1).text, 'FAULT');
assert.strictEqual(P.engineStatus(2).cls, 'inop');

assert.strictEqual(P.tankLabel('wing_tank_L'), 'WING TAN');
assert.strictEqual(P.tankLabel('fuselage_F'), 'FUSE F');
assert.strictEqual(P.tankLabel('fuel_L'), 'FUEL L');
assert.strictEqual(P.tankLabel(''), 'TANK');

// 3420 kg at 41 kg/min, 780 km/h over the ground -> 1:23 and ~1,084 km.
const eng = { fuelKg: 3420, fuelCapKg: 5100, burn: 41 / 60, gs: 780 / 3.6 };
const m = P.fuelReadouts(eng, true);
assert.strictEqual(m.fuel, '3,420');
assert.strictEqual(m.fuelCap, '5,100');
assert.strictEqual(m.burn, '41');
assert.strictEqual(m.endurance, '1:23');
assert.strictEqual(m.range, '1,084');
assert.strictEqual(m.gs, '780');

const imp = P.fuelReadouts(eng, false);
assert.strictEqual(imp.fuel, '7,540');
assert.strictEqual(imp.fuelUnit, 'lb');
assert.strictEqual(imp.fuelCap, '11,244');
assert.strictEqual(imp.rangeUnit, 'nm');
assert.strictEqual(imp.range, '586');

// No estimate yet, engines off, and parked.
assert.strictEqual(P.fuelReadouts({ fuelKg: 3420, burn: -1, gs: 200 }, true).endurance, '--:--');
assert.strictEqual(P.fuelReadouts({ fuelKg: 3420, burn: 0, gs: 200 }, true).burn, '0');
assert.strictEqual(P.fuelReadouts({ fuelKg: 3420, burn: 0, gs: 200 }, true).endurance, '--:--');
assert.strictEqual(P.fuelReadouts({ fuelKg: 3420, burn: 0.5, gs: 2 }, true).range, '--');
assert.strictEqual(P.fuelReadouts({ fuelKg: 3420, burn: 0.5, gs: 2 }, true).endurance, '1:54');
assert.strictEqual(P.fuelReadouts(null, true).fuel, '--');
assert.strictEqual(P.fuelReadouts({ fuelKg: 3420, fuelCapKg: 0 }, true).fuelCap, '--');

console.log('avn-eng-policy: all checks passed');
