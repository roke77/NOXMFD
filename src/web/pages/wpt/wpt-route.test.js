// Self-check for the route/waypoint DISPLAY logic. Run: `node wpt-route.test.js`.
// docs/hud-waypoint-indicator.md (Option 2): the mutation logic this file used to also cover
// (advanceIfNear, CRUD, resetProgress, uniqueRouteName, cycleRoute, activeWaypointArgs) moved to
// the plugin's RouteStore.cs, which has no equivalent runnable check — see that doc for why.
const assert = require('assert');
const R = require('./wpt-route.js');

const wp = (id, name, x, z) => ({ id, name, x, z });
const route = (nextIndex, waypoints) => ({ id: 'r1', name: 'Route 1', nextIndex, waypoints });

// ── distanceBearing: absolute compass bearing, not heading-relative ─────────────────────
{
  // Waypoint due north (+Z): bearing 0.
  let r = R.distanceBearing(0, 0, 0, 1000);
  assert.strictEqual(r.distM, 1000);
  assert.ok(Math.abs(r.brgDeg - 0) < 1e-9, `expected bearing 0, got ${r.brgDeg}`);

  // Due east (+X): bearing 90.
  r = R.distanceBearing(0, 0, 1000, 0);
  assert.ok(Math.abs(r.brgDeg - 90) < 1e-9, `expected bearing 90, got ${r.brgDeg}`);

  // Due south (-Z): bearing 180.
  r = R.distanceBearing(0, 0, 0, -1000);
  assert.ok(Math.abs(r.brgDeg - 180) < 1e-9, `expected bearing 180, got ${r.brgDeg}`);

  // Due west (-X): bearing 270 (normalized from a negative atan2 result).
  r = R.distanceBearing(0, 0, -1000, 0);
  assert.ok(Math.abs(r.brgDeg - 270) < 1e-9, `expected bearing 270, got ${r.brgDeg}`);
}

// ── navigationTarget: route priority with a static steer-point fallback ────────────────
{
  const steerPoints = [{ id: 's1', name: 'IP', x: 7, z: 8 }];
  const activeRoute = route(0, [wp('w1', 'WP', 1, 2)]);
  assert.deepStrictEqual(R.navigationTarget({
    activeRouteId: 'r1', routes: [activeRoute], activeSteerPointId: 's1', steerPoints,
  }), { kind: 'waypoint', point: activeRoute.waypoints[0], index: 0 });

  assert.deepStrictEqual(R.navigationTarget({
    activeRouteId: null, routes: [activeRoute], activeSteerPointId: 's1', steerPoints,
  }), { kind: 'steerpoint', point: steerPoints[0], index: 0 });

  const complete = route(1, [wp('w1', 'WP', 1, 2)]);
  assert.strictEqual(R.navigationTarget({
    activeRouteId: 'r1', routes: [complete], activeSteerPointId: 's1', steerPoints,
  }), null, 'a complete but active route must not fall through to the selected steer point');
}

// ── relativeBearing: the compass needle's rotation, wraps to [0,360) ────────────────────
{
  // Heading already matches the waypoint's bearing: needle points straight up.
  assert.strictEqual(R.relativeBearing(90, 90), 0);

  // Waypoint dead ahead-right of the nose: needle rotates clockwise (positive).
  assert.strictEqual(R.relativeBearing(90, 0), 90);

  // Waypoint behind-left of the nose (bearing < heading): must wrap to a positive rotation, not
  // JS's raw '%' (which would hand back -90, a footgun this function exists to avoid).
  assert.strictEqual(R.relativeBearing(0, 90), 270);

  // Both operands already outside 0-360: still normalizes correctly.
  assert.strictEqual(R.relativeBearing(370, -10), 20);

  // Exact wrap boundary — 360 relative reduces to 0, not 360 (the needle shouldn't visibly
  // "overshoot" a full turn back to its own start).
  assert.strictEqual(R.relativeBearing(45, 45), 0);
}

// ── waypointMarkerState / segmentReached: map.js's drawWaypoints coloring (issue #38, the exact
// off-by-one that shipped in segment coloring — pinning the fixed behavior down directly) ─────────
{
  const nextIndex = 2;   // waypoints 0,1 already flown; 2 is NEXT; 3+ still pending

  assert.strictEqual(R.waypointMarkerState(0, nextIndex), 'reached');
  assert.strictEqual(R.waypointMarkerState(1, nextIndex), 'reached');
  assert.strictEqual(R.waypointMarkerState(2, nextIndex), 'next');
  assert.strictEqual(R.waypointMarkerState(3, nextIndex), 'pending');
  // Route complete (nextIndex === waypoints.length): every waypoint reads as reached, none as next.
  assert.strictEqual(R.waypointMarkerState(3, 4), 'reached');

  // Segment 0->1 (both ends reached): gray.
  assert.strictEqual(R.segmentReached(0, nextIndex), true);
  // Segment 1->2 (leads INTO next): stays the active line color, NOT gray — this was the shipped
  // bug (a plain `i < nextIndex` grayed this one too, since its start index (1) is < nextIndex).
  assert.strictEqual(R.segmentReached(1, nextIndex), false);
  // Segment 2->3 (leads OUT of next, into pending): not gray.
  assert.strictEqual(R.segmentReached(2, nextIndex), false);
  // No active route / route not yet started (nextIndex 0): no segment is ever reached.
  assert.strictEqual(R.segmentReached(0, 0), false);
}

// ── findRoute: read-only lookup against an already-fetched routes array ─────────────────
{
  const routes = [{ id: 'r1', name: 'A' }, { id: 'r2', name: 'B' }];
  assert.strictEqual(R.findRoute(routes, 'r2').name, 'B');
  assert.strictEqual(R.findRoute(routes, 'gone'), null);
}

// ── serializeRoute / parseRouteJSON: export/import round-trip ───────────────────────────
{
  const rt = route(1, [wp('a', 'IP', 100, 200), wp('b', '', 300, -400)]);

  const exported = R.serializeRoute(rt);
  assert.deepStrictEqual(exported, {
    name: 'Route 1',
    waypoints: [{ name: 'IP', x: 100, z: 200 }, { name: '', x: 300, z: -400 }],
  }, 'serializeRoute should drop ids and nextIndex — only name + waypoint name/x/z travel');

  const roundTripped = R.parseRouteJSON(JSON.stringify(exported));
  assert.deepStrictEqual(roundTripped, exported, 'a serialized route should parse back identically');

  // Malformed input: not JSON, not an object, no waypoints array, a waypoint missing x/z.
  assert.strictEqual(R.parseRouteJSON('not json'), null);
  assert.strictEqual(R.parseRouteJSON('null'), null);
  assert.strictEqual(R.parseRouteJSON('{}'), null, 'no waypoints array at all');
  assert.strictEqual(R.parseRouteJSON('{"waypoints":"nope"}'), null, 'waypoints must be an array');
  assert.strictEqual(R.parseRouteJSON('{"waypoints":[{"name":"x"}]}'), null, 'a waypoint missing x/z is rejected');

  // A missing/blank name parses to '' (RouteStore.ImportRoute falls back to a generated name at
  // that point, not this pure parser's job).
  assert.deepStrictEqual(R.parseRouteJSON('{"waypoints":[{"x":1,"z":2}]}'),
    { name: '', waypoints: [{ name: '', x: 1, z: 2 }] });
}

// ── serializeSteerPoints / parseSteerPointsJSON: portable collection, no identities/selection ──
{
  const points = [
    { id: 's1', name: 'IP', x: 10, z: 20, sharedBy: '', sharedWithSquad: true },
    { id: 's2', name: '', x: 30, z: 40 },
  ];
  const exported = R.serializeSteerPoints(points);
  assert.deepStrictEqual(exported, {
    steerPoints: [{ name: 'IP', x: 10, z: 20 }, { name: '', x: 30, z: 40 }],
  });
  assert.deepStrictEqual(R.parseSteerPointsJSON(JSON.stringify(exported)), exported);
  assert.strictEqual(R.parseSteerPointsJSON('{"steerPoints":[]}'), null);
  assert.strictEqual(R.parseSteerPointsJSON('{"steerPoints":[{"name":"bad"}]}'), null);
}

// ── signedTurn: left is negative, right positive, wrapping across north ──────────────────
assert.strictEqual(R.signedTurn(10, 350), 20);    // target just right of north, heading just left
assert.strictEqual(R.signedTurn(350, 10), -20);
assert.strictEqual(R.signedTurn(90, 90), 0);
assert.strictEqual(R.signedTurn(270, 90), -180);  // dead astern reads as a full left turn

// ── distance / bearing formatting ────────────────────────────────────────────────────────
assert.deepStrictEqual(R.distanceParts(12345, true), ['12.3', 'km']);
assert.deepStrictEqual(R.distanceParts(1852, false), ['1.0', 'nm']);
assert.deepStrictEqual(R.distanceParts(null, true), ['—', '']);
assert.strictEqual(R.formatDistance(500, true), '0.5 km');
assert.strictEqual(R.formatDistance(null, false), '—');
assert.strictEqual(R.formatBearing(5), '005°');
assert.strictEqual(R.formatBearing(-10), '350°');
assert.strictEqual(R.formatBearing(359.6), '000°');   // rounds up to 360, which reads as north

// ── legLengths / routeLength / remainingDistance ─────────────────────────────────────────
{
  const pts = [wp('a', '', 0, 0), wp('b', '', 0, 3000), wp('c', '', 4000, 3000)];
  assert.deepStrictEqual(R.legLengths(pts), [0, 3000, 4000]);
  assert.strictEqual(R.routeLength(pts), 7000);
  // Ownship 1000 m short of b (next = 1): 1000 to b, then the 4000 leg to c.
  assert.strictEqual(R.remainingDistance(route(1, pts), 0, 2000), 5000);
  assert.strictEqual(R.remainingDistance(route(3, pts), 0, 0), null);   // complete
  assert.strictEqual(R.remainingDistance(route(1, pts), null, null), null);   // no ownship yet
  assert.deepStrictEqual(R.legLengths([]), []);
}

// ── timeline: 6 per row, a wrapping leg splits into an out/in pair, label on the in half ──
{
  const t = R.timeline(9, 6);
  assert.strictEqual(t.rows, 2);
  assert.deepStrictEqual(t.nodes[6], { index: 6, row: 1, col: 0 });
  const into7 = t.segments.filter(s => s.index === 6);   // the leg into the 7th point wraps
  assert.deepStrictEqual(into7, [
    { index: 6, row: 0, from: 5, to: 'edge', label: false },
    { index: 6, row: 1, from: 'edge', to: 0, label: true },
  ]);
  assert.strictEqual(t.segments.filter(s => s.label).length, 8);   // one label per leg
  assert.deepStrictEqual(R.timeline(0, 6), { nodes: [], segments: [], rows: 0 });
  assert.strictEqual(R.timeline(6, 6).rows, 1);
}

console.log('wpt-route.test.js: OK');
