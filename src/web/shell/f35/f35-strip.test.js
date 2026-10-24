// Self-check for f35-strip. Run: node f35-strip.test.js
//
// Guards the slot edits and the preset round trip: a saved arrangement must come back as exactly
// SLOTS entries of known, unique ids or null (a renamed or removed widget must not break LOAD), a
// pre-feature preset must leave the strip alone, and the readouts must convert units the way ADI
// does.
const assert = require('assert');
const S = require('./f35-strip.js');
const AdiMath = require('../../pages/adi/adi-math.js');

assert.strictEqual(S.SLOTS, 2);

// normalize: non-arrays are "no strip in this preset"; always SLOTS long; junk and repeats → null.
assert.strictEqual(S.normalize(undefined), null);
assert.strictEqual(S.normalize({}), null);
assert.deepStrictEqual(S.normalize([]), [null, null]);
assert.deepStrictEqual(S.normalize(['flight']), ['flight', null]);
assert.deepStrictEqual(S.normalize(['flight', 'flight']), ['flight', null]);
assert.deepStrictEqual(S.normalize(['nope', 'gauges', 'flags']), [null, 'gauges']);
assert.deepStrictEqual(S.normalize([null, 'clocks']), [null, 'clocks']);
assert.deepStrictEqual(S.normalize(['engine', 'gauges']), ['engine', 'gauges']);
// The readouts are inside FLIGHT DATA / CLOCKS, never slot contents on their own.
assert.deepStrictEqual(S.normalize(['alt', 'clock']), [null, null]);
// The brand block and WAKE/FULLSCREEN are fixed chrome, never slot contents.
assert.deepStrictEqual(S.normalize(['logo', 'wake']), [null, null]);
assert.deepStrictEqual(S.normalize(S.DEFAULT), S.DEFAULT);

// Every default and catalog id is unique and known.
const ids = S.CATALOG.map(w => w.id);
assert.strictEqual(new Set(ids).size, ids.length);
S.DEFAULT.forEach(id => assert.ok(ids.includes(id), id));

// place: fills a slot; a widget already elsewhere moves; unknown ids and bad slots are no-ops.
assert.deepStrictEqual(S.place([null, null], 1, 'flight'), [null, 'flight']);
assert.deepStrictEqual(S.place(['flight', 'clocks'], 1, 'flight'), [null, 'flight']);
assert.deepStrictEqual(S.place([null, null], 0, 'bogus'), [null, null]);
assert.deepStrictEqual(S.place([null, null], 2, 'flight'), [null, null]);
assert.deepStrictEqual(S.clear(['flight', 'clocks'], 0), [null, 'clocks']);
assert.ok(!S.available(S.DEFAULT).some(w => S.DEFAULT.includes(w.id)));
assert.strictEqual(S.available([null, null]).length, S.CATALOG.length);
// The ADD list is alphabetical by label.
const labels = S.available([null, null]).map(w => w.label);
assert.deepStrictEqual(labels, labels.slice().sort());
assert.strictEqual(labels[0], 'AVIONICS FLAGS');

// WPN MODES switches: step right, wrap at the end; no data yet steps to the first state.
assert.strictEqual(S.nextState('master', 'safe'), 'arm');
assert.strictEqual(S.nextState('master', 'arm'), 'safe');
assert.strictEqual(S.nextState('mode', 'aa'), 'all');
assert.strictEqual(S.nextState('mode', 'all'), 'ag');
assert.strictEqual(S.nextState('mode', 'ag'), 'aa');
assert.strictEqual(S.nextState('mode', undefined), 'aa');

// Selected weapon: pips up to 12 rounds of capacity, a fill above, empty when out of rounds.
const lo = { selWeapon: 'AIM-9X', items: [{ n: 'GUN', a: 300, f: 400 }, { n: 'AIM-9X', a: 2, f: 2 }, { n: 'GBU-12', a: 0, f: 2 }] };
assert.deepStrictEqual(S.selectedAmmo(lo), { name: 'AIM-9X', a: 2, f: 2, bar: 'pips', empty: false });
assert.strictEqual(S.selectedAmmo(Object.assign({}, lo, { selWeapon: 'GUN' })).bar, 'fill');
assert.strictEqual(S.selectedAmmo(Object.assign({}, lo, { selWeapon: 'GBU-12' })).empty, true);
assert.strictEqual(S.selectedAmmo({ selWeapon: 'X', items: [{ n: 'X', a: 0, f: 12 }] }).bar, 'pips');
assert.strictEqual(S.selectedAmmo({ selWeapon: 'X', items: [{ n: 'X', a: 1, f: 0 }] }).bar, 'none');
assert.strictEqual(S.selectedAmmo(Object.assign({}, lo, { selWeapon: 'NOPE' })), null);
assert.strictEqual(S.selectedAmmo({ selWeapon: null, items: lo.items }), null);
assert.strictEqual(S.selectedAmmo(undefined), null);

// hms
assert.strictEqual(S.hms(0), '00:00:00');
assert.strictEqual(S.hms(3661), '01:01:01');
assert.strictEqual(S.hms(-5), '00:00:00');

// readouts: imperial and metric, and the no-data dash.
const bearing = d => String(Math.round(d)).padStart(3, '0') + '°';
const adi = { present: true, alt: 1000, agl: 500, tas: 100, hdg: 5, vs: 10, g: 1.23, mach: 0.456 };
const imp = AdiMath.units(false), met = AdiMath.units(true);
assert.deepStrictEqual(S.readout('alt', adi, imp, bearing), { label: 'ALT FT', value: '3281' });
assert.deepStrictEqual(S.readout('alt', adi, met, bearing), { label: 'ALT M', value: '1000' });
assert.deepStrictEqual(S.readout('tas', adi, met, bearing), { label: 'TAS KM/H', value: '360' });
assert.deepStrictEqual(S.readout('hdg', adi, imp, bearing), { label: 'HDG', value: '005°' });
assert.deepStrictEqual(S.readout('vs', adi, imp, bearing), { label: 'V/S FPM', value: '+1950' });
assert.deepStrictEqual(S.readout('g', adi, imp, bearing), { label: 'G', value: '1.2' });
assert.deepStrictEqual(S.readout('mach', adi, imp, bearing), { label: 'MACH', value: '0.46' });
assert.strictEqual(S.readout('alt', { present: false }, imp, bearing).value, '--');
assert.strictEqual(S.readout('alt', null, imp, bearing).value, '--');
assert.strictEqual(S.readout('alt', { present: true, alt: null }, imp, bearing).value, '--');
assert.strictEqual(S.readout('logo', adi, imp, bearing), null);

// mission clocks
assert.deepStrictEqual(S.misClock('mis-time', { present: true, tod: 13.5 }), { label: 'MSN TIME', value: '13:30:00' });
assert.deepStrictEqual(S.misClock('mis-dur', { present: true, duration: 75 }), { label: 'ELAPSED', value: '00:01:15' });
assert.strictEqual(S.misClock('mis-dur', { present: false }).value, '--:--:--');
assert.strictEqual(S.localClock(new Date(2026, 0, 1, 9, 5, 7)).value, '09:05:07');

console.log('f35-strip: ok');
