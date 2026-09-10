// Self-check for tgp-marks. Run: node tgp-marks.test.js
const assert = require('assert');
const { tgpMarks } = require('./tgp-marks.js');

// No lock, no manual mode, no feed at all — everything off, including CLR/IR (no feed to mirror).
// WTV is still lit (default view, no stv arg) even with no feed at all.
assert.deepStrictEqual(tgpMarks(0, false, false), { man: false, clr: false, ir: false, wtv: true, stv: false });

// Real unit lock (cnt > 0), COLOR.
assert.deepStrictEqual(tgpMarks(1, false, false), { man: false, clr: true, ir: false, wtv: true, stv: false });
// Real unit lock, IR.
assert.deepStrictEqual(tgpMarks(1, false, true), { man: false, clr: false, ir: true, wtv: true, stv: false });

// Manual camera on, COLOR.
assert.deepStrictEqual(tgpMarks(0, true, false), { man: true, clr: true, ir: false, wtv: true, stv: false });
// Manual camera on, IR.
assert.deepStrictEqual(tgpMarks(0, true, true), { man: true, clr: false, ir: true, wtv: true, stv: false });

// STV (issue #81) — a standing preference, lit regardless of lock/manual state, same as WTV's
// default above; exactly one of wtv/stv is ever lit.
assert.deepStrictEqual(tgpMarks(0, false, false, true), { man: false, clr: false, ir: false, wtv: false, stv: true });
assert.deepStrictEqual(tgpMarks(2, false, false, true), { man: false, clr: true, ir: false, wtv: false, stv: true });

console.log('tgp-marks: all assertions passed');
