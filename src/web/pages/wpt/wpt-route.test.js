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

console.log('wpt-route.test.js: OK');
