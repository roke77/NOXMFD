// Route/waypoint logic — pure, DOM/storage-free, so it can be unit-checked in Node
// (wpt-route.test.js), the same treatment map-transform.js and nav-model.js get.
//
// Everything here takes and returns plain objects; nothing generates ids or touches storage —
// that's waypoints-store.js's job (issue #38). Keeping id generation out of this module mirrors
// telemetry-source.js's instanceId() split.
//
// World units are meters, matching map.js's GRID_MINOR_UNIT = 1000 (= 1 km).
(function (root) {
  // Absolute compass bearing from (ownX,ownZ) to (wx,wz), 0-360, 0 = north. Not heading-relative —
  // a pilot nav-aids off "fly heading X to the waypoint," unlike RWR/MW's nose-relative plot.
  function distanceBearing(ownX, ownZ, wx, wz) {
    const dx = wx - ownX, dz = wz - ownZ;
    const distM = Math.hypot(dx, dz);
    let brgDeg = Math.atan2(dx, dz) * 180 / Math.PI;
    if (brgDeg < 0) brgDeg += 360;
    return { distM, brgDeg };
  }

  // The compass' needle rotation: waypoint bearing relative to the aircraft's own heading, 0-360,
  // 0 = the nose is already pointed at the waypoint (needle points straight up), positive = the
  // pilot needs to turn clockwise (right) to face it. `((x % 360) + 360) % 360` rather than a plain
  // `%` because JS's remainder operator keeps the dividend's sign — brgDeg - hdg is negative
  // whenever hdg > brgDeg, and a plain % would hand back a negative rotation instead of wrapping.
  function relativeBearing(brgDeg, hdg) {
    return ((brgDeg - hdg) % 360 + 360) % 360;
  }

  function shouldAdvance(distM, thresholdM) {
    return distM <= thresholdM;
  }

  // Bumps route.nextIndex by one when the current "next" waypoint is within thresholdM of
  // (ownX,ownZ). Caps at waypoints.length (meaning "route complete" — nothing left to advance to).
  // Returns { route, advanced } — route is a new object only when it actually advanced, the same
  // reference otherwise, so callers can cheaply skip a re-render.
  function advanceIfNear(route, ownX, ownZ, thresholdM) {
    if (!route || route.nextIndex >= route.waypoints.length) return { route, advanced: false };
    const next = route.waypoints[route.nextIndex];
    const { distM } = distanceBearing(ownX, ownZ, next.x, next.z);
    if (!shouldAdvance(distM, thresholdM)) return { route, advanced: false };
    return { route: Object.assign({}, route, { nextIndex: route.nextIndex + 1 }), advanced: true };
  }

  // Rewinds/jumps progress to `index`: that waypoint (and everything after it) becomes not-reached,
  // with `index` itself the new "next" one — a plain overwrite of the count, same "nextIndex is a
  // count, not an identity" reasoning removeWaypoint/reorderWaypoint already rely on. Clamped to a
  // valid range so an out-of-range index can't produce a negative or overshooting count. Passing 0
  // resets the whole route back to its start.
  function resetProgress(route, index) {
    const nextIndex = Math.max(0, Math.min(index, route.waypoints.length));
    return Object.assign({}, route, { nextIndex });
  }

  function addWaypoint(route, waypoint) {
    const waypoints = route.waypoints.concat([waypoint]);
    return Object.assign({}, route, { waypoints });
  }

  // nextIndex is a COUNT of completed waypoints, not a waypoint's identity — it names "how many are
  // done," not "which one." So a delete before it means one fewer completed waypoint ahead of it
  // (shift down by one); a delete AT it leaves the number as-is, which now names whatever slid up
  // into that slot (skip the removed one, move to what's next); a delete after it doesn't touch it.
  function removeWaypoint(route, index) {
    const waypoints = route.waypoints.slice(0, index).concat(route.waypoints.slice(index + 1));
    let nextIndex = route.nextIndex;
    if (index < nextIndex) nextIndex -= 1;
    nextIndex = Math.max(0, Math.min(nextIndex, waypoints.length));
    return Object.assign({}, route, { waypoints, nextIndex });
  }

  function renameWaypoint(route, index, name) {
    const waypoints = route.waypoints.map((w, i) => (i === index ? Object.assign({}, w, { name }) : w));
    return Object.assign({}, route, { waypoints });
  }

  // Same "count, not identity" reasoning as removeWaypoint: reordering the plan ahead doesn't change
  // how many waypoints are done, so nextIndex stays numerically put — whichever waypoint now sits at
  // that index inherits "next," even if a different one carried the mark before the move.
  function reorderWaypoint(route, fromIndex, toIndex) {
    const waypoints = route.waypoints.slice();
    const [moved] = waypoints.splice(fromIndex, 1);
    waypoints.splice(toIndex, 0, moved);
    return Object.assign({}, route, { waypoints });
  }

  function addRoute(routes, route) {
    return routes.concat([route]);
  }

  function deleteRoute(routes, id) {
    return routes.filter(r => r.id !== id);
  }

  function renameRoute(routes, id, name) {
    return routes.map(r => (r.id === id ? Object.assign({}, r, { name }) : r));
  }

  function findRoute(routes, id) {
    return routes.find(r => r.id === id) || null;
  }

  // Route names must be unique (no two routes read the same in the list/readout) — returns `name`
  // unchanged if nothing else already has it, otherwise the first "name (N)" that's free. `excludeId`
  // lets a rename check against every OTHER route without colliding with its own current name.
  function uniqueRouteName(routes, name, excludeId) {
    const taken = new Set(routes.filter(r => r.id !== excludeId).map(r => r.name));
    if (!taken.has(name)) return name;
    let n = 2;
    while (taken.has(name + ' (' + n + ')')) n++;
    return name + ' (' + n + ')';
  }

  // The id of the route one step (dir = +1/-1) from activeId, wrapping at both ends — MAP's R+/R-
  // (issue #38). No routes: returns activeId unchanged (nothing to switch to). Unknown/null
  // activeId starts from index 0, so R+ from "no active route" lands on the first one.
  function cycleRoute(routes, activeId, dir) {
    if (!routes.length) return activeId;
    const from = Math.max(0, routes.findIndex(r => r.id === activeId));
    const next = (from + dir + routes.length) % routes.length;
    return routes[next].id;
  }

  // The portable export shape: name + ordered waypoint name/x/z only — no internal ids, no live
  // progress (nextIndex). Ids are storage bookkeeping, meaningless to whoever the route is shared
  // with; progress is "how far THIS pilot got," not part of the route's own definition, and
  // importing always starts a route fresh (see waypoints-store.js's importRoute).
  function serializeRoute(route) {
    return {
      name: route.name,
      waypoints: route.waypoints.map(w => ({ name: w.name || '', x: w.x, z: w.z })),
    };
  }

  // Parses + validates a pasted route export. Returns { name, waypoints } (serializeRoute's own
  // shape) on success, or null if the text isn't JSON or doesn't look like a route — the caller
  // decides how to surface that (waypoints-store.js's importRoute has no UI of its own to report
  // through; wpt.js's import panel shows the failure).
  function parseRouteJSON(text) {
    let data;
    try { data = JSON.parse(text); } catch (e) { return null; }
    if (!data || typeof data !== 'object' || !Array.isArray(data.waypoints)) return null;
    const waypoints = [];
    for (const w of data.waypoints) {
      if (!w || typeof w.x !== 'number' || typeof w.z !== 'number') return null;
      waypoints.push({ name: typeof w.name === 'string' ? w.name : '', x: w.x, z: w.z });
    }
    const name = typeof data.name === 'string' ? data.name.trim() : '';
    return { name, waypoints };
  }

  const api = {
    distanceBearing, relativeBearing, shouldAdvance, advanceIfNear,
    resetProgress,
    addWaypoint, removeWaypoint, renameWaypoint, reorderWaypoint,
    addRoute, deleteRoute, renameRoute, findRoute, cycleRoute, uniqueRouteName,
    serializeRoute, parseRouteJSON,
  };
  if (typeof module !== 'undefined' && module.exports) module.exports = api;
  else root.WptRoute = api;
})(typeof self !== 'undefined' ? self : this);
