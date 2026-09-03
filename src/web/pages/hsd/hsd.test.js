const assert = require('assert');
const { hsdXY, rangeLabelForTest, rangeUnitsForTest, altUnitsForTest, contactColor, radarConePath, demoContacts, geom,
        CEN_RANGE_NM, DEP_RANGE_NM, gridFractionsForTest, applyModeForTest,
        toContentSpaceForTest, ZOOM_SCALE, pendingSel, isPending,
        iconTransformForTest, ICON_SHRINK } = require('./hsd.js');

function near(a, b, label) {
  assert.ok(Math.abs(a - b) < 1e-6, `${label}: got ${a}, expected ${b}`);
}

let p = hsdXY(0, 0, 0, 0, 1000, 2000);
near(p.x, geom.CX, 'north x');
near(p.y, geom.CY - geom.OUTER * 0.5, 'north y');

p = hsdXY(0, 0, 0, 1000, 0, 2000);
near(p.x, geom.CX + geom.OUTER * 0.5, 'east x');
near(p.y, geom.CY, 'east y');

p = hsdXY(0, 0, 90, 0, 1000, 2000);
near(p.x, geom.CX - geom.OUTER * 0.5, 'heading east puts north left');
near(p.y, geom.CY, 'heading east y');

assert.strictEqual(hsdXY(0, 0, 0, 0, 3000, 2000), null, 'outside range culled');
assert.strictEqual(rangeLabelForTest(false, 1852 * 20), '20nm');
assert.strictEqual(rangeLabelForTest(true, 1000 * 40), '40km');
assert.strictEqual(rangeUnitsForTest(false, 1852 * 12.5), '12.5');
assert.strictEqual(altUnitsForTest(false, 1000), 3281);
assert.ok(radarConePath(80 * 1852, 40 * 1852, 60).includes('A110.0 110.0'), '40nm radar cone reaches half of an 80nm HSD');
assert.strictEqual(radarConePath(80 * 1852, 0, 60), '', 'missing radar range hides cone');
assert.strictEqual(contactColor({ dl: 1 }), 'var(--no-purple)');
assert.strictEqual(contactColor({ rd: 1 }), 'var(--no-red)');
// A contact is amber only when it's the focused lock (the bottom readout's own target,
// docs/rdr-fcr-hsd.md's cycling-locked-targets follow-up) — any other lock (focused=false) keeps
// its ordinary source color instead, whether that's radar-red or datalink-purple; the ring (drawn
// by the caller, not this function) is what shows it's still locked.
assert.strictEqual(contactColor({ tg: 1, rd: 1 }, true), 'var(--no-amber)');
assert.strictEqual(contactColor({ tg: 1, rd: 1 }, false), 'var(--no-red)');
assert.strictEqual(contactColor({ tg: 1, dl: 1 }, false), 'var(--no-purple)');

// A stale datalink track (position drifted past the trust radius, docs/tgt-stale-lock.md) goes
// white instead of its source color, but a focused lock still wins over staleness.
assert.strictEqual(contactColor({ rd: 1, st: 1 }), 'var(--no-white)');
assert.strictEqual(contactColor({ dl: 1, st: 1 }), 'var(--no-white)');
assert.strictEqual(contactColor({ tg: 1, dl: 1, st: 1 }, true), 'var(--no-amber)');

// CEN/DEP display modes (docs/rdr-fcr-hsd.md). The whole "one shared range index instead of a
// per-mode one" design (hsd.js's applyMode) depends on DEP's ladder being exactly 1.5x CEN's at
// every step, matching real DCS's DEP-range-equals-1.5x-FCR-range ratio — this pins that down so
// an edit to either ladder that breaks the ratio fails loudly here instead of silently shipping.
assert.strictEqual(CEN_RANGE_NM.length, DEP_RANGE_NM.length, 'CEN/DEP ladders must stay the same length');
CEN_RANGE_NM.forEach((nm, i) =>
  assert.strictEqual(DEP_RANGE_NM[i], nm * 1.5, `DEP_RANGE_NM[${i}] should be 1.5x CEN_RANGE_NM[${i}]`));

assert.deepStrictEqual(gridFractionsForTest('cen'), [0.25, 0.5, 0.75, 1], 'CEN uses quarter-range rings');
assert.deepStrictEqual(gridFractionsForTest('dep'), [1 / 3, 2 / 3, 1], 'DEP uses third-range rings');

// applyMode derives the geometry/ladder from `mode` alone — rangeIdx itself is untouched by a
// mode switch, which is what makes "CEN 40nm <-> DEP 60nm are the same setting" work.
const cenAt2 = applyModeForTest('cen', 2);
assert.strictEqual(cenAt2.RANGE_NM, CEN_RANGE_NM);
assert.strictEqual(cenAt2.rangeIdx, 2);
const depAt2 = applyModeForTest('dep', 2);
assert.strictEqual(depAt2.RANGE_NM, DEP_RANGE_NM);
assert.strictEqual(depAt2.rangeIdx, 2);
assert.strictEqual(DEP_RANGE_NM[2], CEN_RANGE_NM[2] * 1.5, 'same index, 1.5x the NM value, across the switch');
assert.notStrictEqual(depAt2.CY, cenAt2.CY, 'DEP recentres ownship away from CEN\'s centre');
assert.notStrictEqual(depAt2.OUTER, cenAt2.OUTER, 'DEP uses a different outer-ring pixel radius than CEN');

const demo = demoContacts(0, 0, 20);
assert.strictEqual(demo.length, 5, 'standalone preview seeds five contacts');
assert.strictEqual(demo.filter(c => c.tg).length, 1, 'standalone preview includes one lock');
assert.strictEqual(demo.filter(c => c.rd).length, 1, 'standalone preview includes one radar-only contact');
assert.ok(demo.every(c => hsdXY(0, 0, 20, c.x, c.z, 40 * 1852)), 'default preview range shows every demo contact');

// Cursor-anchored zoom (overlapping-contacts magnifier): toContentSpace is the inverse of the
// forward zoom transform (applyZoomTransform's translate/scale/translate), so a point offset from
// the anchor by d screen units should map back to d/ZOOM_SCALE content units from that same anchor.
near(toContentSpaceForTest(300, 300, 300, 300).x, 300, 'the anchor itself maps to itself');
near(toContentSpaceForTest(300, 300, 300, 300).y, 300, 'the anchor itself maps to itself (y)');
near(toContentSpaceForTest(300, 300, 300 + ZOOM_SCALE * 40, 300).x, 340, '40 content units right of anchor, scaled to screen, maps back to 40');
near(toContentSpaceForTest(300, 300, 300, 300 - ZOOM_SCALE * 40).y, 260, '40 content units above anchor, scaled to screen, maps back to 40');

// Cursor Select's optimistic pending-selection (never-deselects behavior): a just-selected id
// reads as "locked" (isPending) until its entry expires, so a rapid burst of presses advances past
// it instead of re-selecting the same nearest-unselected contact before telemetry's tg catches up.
pendingSel.clear();
assert.ok(!isPending(99), 'an id with no pending entry is not pending');
pendingSel.set(99, performance.now() + 50);
assert.ok(isPending(99), 'a fresh pending entry reads as pending');
pendingSel.set(99, performance.now() - 1);   // already expired
assert.ok(!isPending(99), 'an expired pending entry reads as not pending');
assert.ok(!pendingSel.has(99), 'isPending self-cleans an expired entry once observed');

// Icon shrink while zoomed: identity when not zoomed, and while zoomed the net effect (icon's own
// counter-scale composed with the outer zoom transform) must be exactly ICON_SHRINK, regardless of
// ZOOM_SCALE's actual value — otherwise a change to one constant would silently throw off the other.
assert.strictEqual(iconTransformForTest(false, 100, 200), '', 'no icon transform when not zoomed');
const iconT = iconTransformForTest(true, 100, 200);
assert.ok(iconT.indexOf('scale(' + (ICON_SHRINK / ZOOM_SCALE).toFixed(4)) >= 0,
  'icon transform scales by ICON_SHRINK/ZOOM_SCALE so the net on-screen size is ICON_SHRINK x normal');

console.log('hsd.test.js: OK');
