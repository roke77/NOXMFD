// Self-check for f35-strip. Run: node f35-strip.test.js
//
// Guards the strip's list edits and the preset round trip: a saved list must come back with only
// known, unique ids (a renamed or removed widget must not break LOAD), a pre-feature preset must
// leave the strip alone, and the readouts must convert units the way ADI does.
const assert = require('assert');
const S = require('./f35-strip.js');
const AdiMath = require('../../pages/adi/adi-math.js');

// normalize: non-arrays are "no strip in this preset"; junk and duplicates drop out.
assert.strictEqual(S.normalize(undefined), null);
assert.strictEqual(S.normalize({}), null);
assert.deepStrictEqual(S.normalize([]), []);
assert.deepStrictEqual(S.normalize(['alt', 'nope', 'alt', 3, 'fll']), ['alt', 'fll']);
assert.deepStrictEqual(S.normalize(S.DEFAULT), S.DEFAULT);

// Every default and catalog id is unique and known.
const ids = S.CATALOG.map(w => w.id);
assert.strictEqual(new Set(ids).size, ids.length);
S.DEFAULT.forEach(id => assert.ok(ids.includes(id), id));

// move: swaps with the neighbour; edges are no-ops.
assert.deepStrictEqual(S.move(['a', 'b', 'c'], 'b', -1), ['b', 'a', 'c']);
assert.deepStrictEqual(S.move(['a', 'b', 'c'], 'b', 1), ['a', 'c', 'b']);
assert.deepStrictEqual(S.move(['a', 'b'], 'a', -1), ['a', 'b']);
assert.deepStrictEqual(S.move(['a', 'b'], 'b', 1), ['a', 'b']);

// add: after the selection, else at the end; never twice, never unknown.
assert.deepStrictEqual(S.add(['logo', 'fll'], 'alt', 'logo'), ['logo', 'alt', 'fll']);
assert.deepStrictEqual(S.add(['logo', 'fll'], 'alt', null), ['logo', 'fll', 'alt']);
assert.deepStrictEqual(S.add(['alt'], 'alt', null), ['alt']);
assert.deepStrictEqual(S.add(['alt'], 'bogus', null), ['alt']);
assert.deepStrictEqual(S.remove(['a', 'b'], 'a'), ['b']);
assert.ok(!S.available(S.DEFAULT).some(w => S.DEFAULT.includes(w.id)));
assert.strictEqual(S.available([]).length, S.CATALOG.length);

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
