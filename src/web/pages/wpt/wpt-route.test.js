// Self-check for the route/waypoint logic. Run: `node wpt-route.test.js`.
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

// ── advanceIfNear: at / just-under / just-over threshold ────────────────────────────────
{
  const rt = route(0, [wp('a', 'WP1', 0, 1000), wp('b', 'WP2', 0, 2000)]);

  // Exactly at threshold advances (shouldAdvance is <=).
  let out = R.advanceIfNear(rt, 0, 0, 1000);
  assert.strictEqual(out.advanced, true);
  assert.strictEqual(out.route.nextIndex, 1);
  assert.notStrictEqual(out.route, rt, 'advancing should return a new object');

  // Just under threshold does not advance.
  out = R.advanceIfNear(rt, 0, 0, 999);
  assert.strictEqual(out.advanced, false);
  assert.strictEqual(out.route, rt, 'no advance should return the same reference');

  // Route already complete (nextIndex === length): never advances further.
  const done = route(2, rt.waypoints);
  out = R.advanceIfNear(done, 0, 0, 999999);
  assert.strictEqual(out.advanced, false);
  assert.strictEqual(out.route.nextIndex, 2);
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

// ── CRUD ops ──────────────────────────────────────────────────────────────────────────
{
  let rt = route(0, [wp('a', 'WP1', 0, 0), wp('b', 'WP2', 0, 0)]);

  rt = R.addWaypoint(rt, wp('c', 'WP3', 0, 0));
  assert.strictEqual(rt.waypoints.length, 3);

  rt = R.renameWaypoint(rt, 0, 'Renamed');
  assert.strictEqual(rt.waypoints[0].name, 'Renamed');

  // Reorder is index-based, not id-based: nextIndex is a COUNT of completed waypoints, so moving
  // waypoints around leaves it numerically unchanged — whichever waypoint now sits at that index
  // inherits "next," even if a different one carried the mark before the move.
  rt = R.reorderWaypoint(rt, 0, 2);
  assert.strictEqual(rt.waypoints.map(w => w.id).join(','), 'b,c,a');
  assert.strictEqual(rt.nextIndex, 0, 'nextIndex should stay pinned to the index, not follow the moved waypoint');

  // Deleting a waypoint AFTER nextIndex doesn't shift it.
  rt = R.removeWaypoint(rt, 2);
  assert.strictEqual(rt.waypoints.map(w => w.id).join(','), 'b,c');
  assert.strictEqual(rt.nextIndex, 0, 'deleting a waypoint after nextIndex should leave it unchanged');

  // Deleting a waypoint BEFORE nextIndex shifts it down by one (one fewer completed waypoint ahead).
  let rt2 = route(1, [wp('x', 'X', 0, 0), wp('y', 'Y', 0, 0), wp('z', 'Z', 0, 0)]);
  rt2 = R.removeWaypoint(rt2, 0);
  assert.strictEqual(rt2.waypoints.map(w => w.id).join(','), 'y,z');
  assert.strictEqual(rt2.nextIndex, 0, 'nextIndex should shift down after removing a waypoint before it');

  // Deleting the CURRENT "next" waypoint itself: nextIndex stays numerically the same, which now
  // names whatever slid up into that slot.
  let rt3 = route(1, [wp('x', 'X', 0, 0), wp('y', 'Y', 0, 0), wp('z', 'Z', 0, 0)]);
  rt3 = R.removeWaypoint(rt3, 1);
  assert.strictEqual(rt3.waypoints.map(w => w.id).join(','), 'x,z');
  assert.strictEqual(rt3.nextIndex, 1, 'deleting the tracked next waypoint should land on whatever slid into its slot');
}

// ── Route collection ops ─────────────────────────────────────────────────────────────────
{
  let routes = [];
  routes = R.addRoute(routes, { id: 'r1', name: 'A', nextIndex: 0, waypoints: [] });
  routes = R.addRoute(routes, { id: 'r2', name: 'B', nextIndex: 0, waypoints: [] });
  assert.strictEqual(routes.length, 2);

  routes = R.renameRoute(routes, 'r1', 'Renamed A');
  assert.strictEqual(R.findRoute(routes, 'r1').name, 'Renamed A');

  routes = R.deleteRoute(routes, 'r2');
  assert.strictEqual(routes.length, 1);
  assert.strictEqual(R.findRoute(routes, 'r2'), null);
}

// ── resetProgress: rewind/jump nextIndex, clamped ────────────────────────────────────────
{
  const rt = route(3, [wp('a', 'A', 0, 0), wp('b', 'B', 0, 0), wp('c', 'C', 0, 0), wp('d', 'D', 0, 0)]);

  let out = R.resetProgress(rt, 1);
  assert.strictEqual(out.nextIndex, 1, 'rewinding to waypoint 1 makes it (and everything after) not-reached');
  assert.notStrictEqual(out, rt, 'resetProgress should return a new object');

  // "Reset the whole route" is just index 0.
  out = R.resetProgress(rt, 0);
  assert.strictEqual(out.nextIndex, 0);

  // Clamped both directions — an out-of-range index can't produce a negative or overshooting count.
  out = R.resetProgress(rt, -5);
  assert.strictEqual(out.nextIndex, 0, 'a negative index clamps to 0');
  out = R.resetProgress(rt, 99);
  assert.strictEqual(out.nextIndex, 4, 'an index past the end clamps to route-complete (waypoints.length)');
}

// ── uniqueRouteName: no two routes may share a name ──────────────────────────────────────
{
  const routes = [{ id: 'r1', name: 'Alpha' }, { id: 'r2', name: 'Bravo' }, { id: 'r3', name: 'Alpha (2)' }];
  assert.strictEqual(R.uniqueRouteName(routes, 'Charlie'), 'Charlie', 'an unused name passes through unchanged');
  assert.strictEqual(R.uniqueRouteName(routes, 'Alpha'), 'Alpha (3)', 'skips straight to the first free suffix, "(2)" already taken');
  assert.strictEqual(R.uniqueRouteName(routes, 'Bravo'), 'Bravo (2)');
  // Renaming a route to its OWN current name must not suffix it against itself.
  assert.strictEqual(R.uniqueRouteName(routes, 'Alpha', 'r1'), 'Alpha', 'excludeId lets a route keep its own name on rename');
}

// ── cycleRoute: wraps both directions through a "none active" stop, handles empty/unknown id ────
{
  const routes = [{ id: 'a' }, { id: 'b' }, { id: 'c' }];
  assert.strictEqual(R.cycleRoute(routes, 'a', 1), 'b');
  assert.strictEqual(R.cycleRoute(routes, 'c', 1), null, 'R+ from the last route parks on "none" before wrapping to the first');
  assert.strictEqual(R.cycleRoute(routes, null, 1), 'a', 'R+ from "none" lands on the first route');
  assert.strictEqual(R.cycleRoute(routes, 'a', -1), null, 'R- from the first route parks on "none" before wrapping to the last');
  assert.strictEqual(R.cycleRoute(routes, null, -1), 'c', 'R- from "none" wraps to the last route');
  assert.strictEqual(R.cycleRoute(routes, 'b', -1), 'a');
  assert.strictEqual(R.cycleRoute([], null, 1), null, 'no routes: nothing to switch to');
  assert.strictEqual(R.cycleRoute(routes, 'gone', 1), 'a', 'an unknown/deleted activeId starts from "none", so R+ lands on the first');
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

  // A missing/blank name parses to '' (waypoints-store.js's importRoute falls back to a generated
  // name at that point, not this pure parser's job).
  assert.deepStrictEqual(R.parseRouteJSON('{"waypoints":[{"x":1,"z":2}]}'),
    { name: '', waypoints: [{ name: '', x: 1, z: 2 }] });
}

// ── activeWaypointArgs: what the in-game HUD cue gets told ─────────────────────────────
{
  const coll = (activeRouteId, routes) => ({ version: 1, activeRouteId, routes });
  const OFF = { on: false, wx: 0, wz: 0, wname: '', index: 0 };

  // The live case: the payload names the route's NEXT waypoint, not its first or last.
  const r = route(1, [wp('w1', 'ALPHA', 10, 20), wp('w2', 'BRAVO', 30, 40), wp('w3', 'CHARLIE', 50, 60)]);
  assert.deepStrictEqual(R.activeWaypointArgs(coll('r1', [r])),
    { on: true, wx: 30, wz: 40, wname: 'BRAVO', index: 1 });

  // Every "nothing to point at" state must produce a real off payload, not null/undefined — it is
  // what clears a bug the plugin is already drawing on the tape.
  assert.deepStrictEqual(R.activeWaypointArgs(coll(null, [r])), OFF, 'no active route');
  assert.deepStrictEqual(R.activeWaypointArgs(coll('nope', [r])), OFF, 'active id matches no route');
  assert.deepStrictEqual(R.activeWaypointArgs(coll('r1', [route(3, r.waypoints)])), OFF, 'route complete');
  assert.deepStrictEqual(R.activeWaypointArgs(coll('r1', [route(0, [])])), OFF, 'empty route');
  assert.deepStrictEqual(R.activeWaypointArgs(null), OFF, 'no collection at all');
  assert.deepStrictEqual(R.activeWaypointArgs({}), OFF, 'collection with no routes array');

  // An unnamed waypoint sends '' rather than undefined — the plugin displays "WPT 2" alone for it,
  // and an undefined here would serialize out of the JSON and leave the field at its C# default.
  assert.deepStrictEqual(R.activeWaypointArgs(coll('r1', [route(0, [wp('w1', '', 1, 2)])])),
    { on: true, wx: 1, wz: 2, wname: '', index: 0 });
}

console.log('wpt-route.test.js: OK');
