// Route/waypoint DISPLAY logic — pure, DOM/storage-free, so it can be unit-checked in Node
// (wpt-route.test.js), the same treatment map-transform.js and nav-model.js get.
//
// docs/hud-waypoint-indicator.md (Option 2): route MUTATION (create/rename/delete/reorder/
// advance/etc.) is authoritative in the plugin's RouteStore.cs — the browser only ever
// renders whatever /wpt-options reports. What's left here is the half that stays genuinely
// client-side: math combining server-pushed route data with THIS browser's own live ownship
// position, plus export/import's plain data-shape conversion (no route/waypoint identity
// involved, so it has nothing that needs to live server-side).
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

  // Marker state for waypoint `index` in a route whose next waypoint is `nextIndex` (map.js's
  // drawWaypoints marker coloring): 'next' (the one the WPT readout is tracking), 'reached' (already
  // flown past), or 'pending' (not yet reached).
  function waypointMarkerState(index, nextIndex) {
    if (index === nextIndex) return 'next';
    return index < nextIndex ? 'reached' : 'pending';
  }

  // Whether the route LINE segment from waypoint `index` to `index + 1` is already-flown (drawn
  // gray, map.js's drawWaypoints) — true only when BOTH ends are reached (index + 1 < nextIndex).
  // The segment leading INTO nextIndex shares its far end with it and stays the active line color
  // instead — the same leg the WPT readout's bearing/distance is tracking. Getting this off by one
  // (index < nextIndex, grabbing the wrong end) was a real shipped bug; that's exactly why this is
  // its own pure, tested function rather than inline canvas code.
  function segmentReached(index, nextIndex) {
    return index + 1 < nextIndex;
  }

  // Read-only lookup against an already-fetched routes array (from /wpt-options) — no mutation, so
  // it stays in the same category as the display math above rather than moving to RouteStore.cs.
  function findRoute(routes, id) {
    return routes.find(r => r.id === id) || null;
  }

  function findSteerPoint(points, id) {
    return (points || []).find(p => p.id === id) || null;
  }

  // One navigation target feeds both the WPT compass/readout and the native HUD cue. An active
  // route owns the target even after completion; steer points are only the explicit no-route
  // fallback, so completing a route cannot silently switch the aircraft to unrelated guidance.
  function navigationTarget(data) {
    const route = findRoute(data.routes || [], data.activeRouteId);
    if (route) {
      if (route.nextIndex >= route.waypoints.length) return null;
      return { kind: 'waypoint', point: route.waypoints[route.nextIndex], index: route.nextIndex };
    }
    const point = findSteerPoint(data.steerPoints || [], data.activeSteerPointId);
    return point ? { kind: 'steerpoint', point, index: (data.steerPoints || []).indexOf(point) } : null;
  }

  // The portable export shape: name + ordered waypoint name/x/z only — no internal ids, no live
  // progress (nextIndex). Ids are storage bookkeeping, meaningless to whoever the route is shared
  // with; progress is "how far THIS pilot got," not part of the route's own definition, and
  // importing always starts a route fresh (RouteStore.ImportRoute).
  function serializeRoute(route) {
    return {
      name: route.name,
      waypoints: route.waypoints.map(w => ({ name: w.name || '', x: w.x, z: w.z })),
    };
  }

  // Parses + validates a pasted route export for IMMEDIATE client-side feedback in wpt.js's import
  // panel. The actual import is authoritative server-side (RouteStore.ImportRoute, which
  // independently re-parses) — POST /command is fire-and-forget, with no synchronous way for the
  // server to say "that wasn't a route," so this pre-validator is what lets a garbage paste show an
  // inline error instantly instead of silently doing nothing for up to a poll interval.
  // Returns { name, waypoints } (serializeRoute's own shape) on success, or null.
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

  function serializeSteerPoints(points) {
    return { steerPoints: (points || []).map(p => ({ name: p.name || '', x: p.x, z: p.z })) };
  }

  function parseSteerPointsJSON(text) {
    let data;
    try { data = JSON.parse(text); } catch (e) { return null; }
    if (!data || typeof data !== 'object' || !Array.isArray(data.steerPoints) || data.steerPoints.length === 0) return null;
    const steerPoints = [];
    for (const p of data.steerPoints) {
      if (!p || typeof p.x !== 'number' || typeof p.z !== 'number') return null;
      steerPoints.push({ name: typeof p.name === 'string' ? p.name : '', x: p.x, z: p.z });
    }
    return { steerPoints };
  }

  const api = {
    distanceBearing, relativeBearing,
    waypointMarkerState, segmentReached,
    findRoute, findSteerPoint, navigationTarget,
    serializeRoute, parseRouteJSON, serializeSteerPoints, parseSteerPointsJSON,
  };
  if (typeof module !== 'undefined' && module.exports) module.exports = api;
  else root.WptRoute = api;
})(typeof self !== 'undefined' ? self : this);
