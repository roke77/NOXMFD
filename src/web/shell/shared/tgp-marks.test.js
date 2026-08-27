// Self-check for tgp-marks. Run: node tgp-marks.test.js
const assert = require('assert');
const { tgpMarks } = require('./tgp-marks.js');

// No lock, no manual mode, no feed at all — everything off, including CLR/IR (no feed to mirror).
assert.deepStrictEqual(tgpMarks(0, false, false), { tgt: false, man: false, clr: false, ir: false });

// Real unit lock (cnt > 0), COLOR.
assert.deepStrictEqual(tgpMarks(1, false, false), { tgt: true, man: false, clr: true, ir: false });
// Real unit lock, IR.
assert.deepStrictEqual(tgpMarks(1, false, true), { tgt: true, man: false, clr: false, ir: true });

// Manual camera on, COLOR.
assert.deepStrictEqual(tgpMarks(0, true, false), { tgt: false, man: true, clr: true, ir: false });
// Manual camera on, IR.
assert.deepStrictEqual(tgpMarks(0, true, true), { tgt: false, man: true, clr: false, ir: true });

console.log('tgp-marks: all assertions passed');
