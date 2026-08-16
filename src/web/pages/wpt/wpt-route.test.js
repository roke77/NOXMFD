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

// ── cycleRoute: wraps both directions, handles empty/unknown activeId ───────────────────
{
  const routes = [{ id: 'a' }, { id: 'b' }, { id: 'c' }];
  assert.strictEqual(R.cycleRoute(routes, 'a', 1), 'b');
  assert.strictEqual(R.cycleRoute(routes, 'c', 1), 'a', 'R+ from the last route should wrap to the first');
  assert.strictEqual(R.cycleRoute(routes, 'a', -1), 'c', 'R- from the first route should wrap to the last');
  assert.strictEqual(R.cycleRoute(routes, 'b', -1), 'a');
  assert.strictEqual(R.cycleRoute([], 'a', 1), 'a', 'no routes: nothing to switch to');
  assert.strictEqual(R.cycleRoute(routes, null, 1), 'b', 'unknown activeId starts from index 0, so R+ lands on the second');
  assert.strictEqual(R.cycleRoute(routes, null, -1), 'c', 'unknown activeId starts from index 0, so R- wraps to the last');
}

console.log('wpt-route.test.js: OK');
