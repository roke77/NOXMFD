// Node self-check for the RDR B-scope projection (rdr.js bscopeXY). Run by hand:
//   node src/web/pages/rdr/rdr.test.js
// Not shipped (excluded from the DLL by NOXMFD.csproj's *.test.js filter), like nav-model.test.js.

const { bscopeXY, geom, toContentSpaceForTest, ZOOM_SCALE } = require('./rdr.js');

let fails = 0;
function ok(cond, msg) { if (!cond) { fails++; console.error('FAIL: ' + msg); } }
function near(a, b, msg) { ok(Math.abs(a - b) < 0.01, msg + ' (got ' + a + ', want ' + b + ')'); }

const RANGE = 74000, CH = 60;   // 60° cone half-angle, 74 km max range

// Boresight, mid-range → dead centre horizontally, half-way up.
const c = bscopeXY(0, RANGE / 2, RANGE, CH);
ok(c !== null, 'centre contact not culled');
near(c.x, geom.MIDX, 'az 0 → mid X');
near(c.y, geom.BOT - geom.HGT / 2, 'half range → half height');

// Cone edges map to the horizontal extremes.
near(bscopeXY(CH, RANGE, RANGE, CH).x, geom.MIDX + geom.HALFW, '+cone → right edge');
near(bscopeXY(-CH, RANGE, RANGE, CH).x, geom.MIDX - geom.HALFW, '-cone → left edge');

// Range extremes map to the vertical extremes.
near(bscopeXY(0, 0, RANGE, CH).y, geom.BOT, 'range 0 → ownship (bottom)');
near(bscopeXY(0, RANGE, RANGE, CH).y, geom.TOP, 'max range → top');

// Outside the cone or past max range → culled (null).
ok(bscopeXY(CH + 1, RANGE / 2, RANGE, CH) === null, 'beyond +cone culled');
ok(bscopeXY(-(CH + 1), RANGE / 2, RANGE, CH) === null, 'beyond -cone culled');
ok(bscopeXY(0, RANGE * 1.1, RANGE, CH) === null, 'past max range culled');

// Cursor-anchored zoom (overlapping-contacts magnifier): toContentSpace is the inverse of the
// forward zoom transform (applyZoomTransform's translate/scale/translate), so a point offset from
// the anchor by d screen units should map back to d/ZOOM_SCALE content units from that same anchor.
near(toContentSpaceForTest(200, 300, 200, 300).x, 200, 'the anchor itself maps to itself');
near(toContentSpaceForTest(200, 300, 200, 300).y, 300, 'the anchor itself maps to itself (y)');
near(toContentSpaceForTest(200, 300, 200 + ZOOM_SCALE * 30, 300).x, 230, '30 content units right of anchor, scaled to screen, maps back to 30');
near(toContentSpaceForTest(200, 300, 200, 300 - ZOOM_SCALE * 30).y, 270, '30 content units above anchor, scaled to screen, maps back to 30');

if (fails) { console.error('rdr.test.js: ' + fails + ' failure(s)'); process.exit(1); }
console.log('rdr.test.js: OK');
