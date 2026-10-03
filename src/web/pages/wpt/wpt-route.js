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

  // Signed turn to the target: negative = turn left, positive = turn right, in -180..180. The RTD/SPD
  // heading tape and cue read it this way; relativeBearing above is the compass needle's 0-360.
  function signedTurn(brgDeg, hdg) {
    return ((brgDeg - hdg) % 360 + 540) % 360 - 180;
  }

  // [value, unit] for a distance in meters, in the player's unit (km when metric, else nm) — the
  // split HSD/FCR/OBJ's range readouts use. ['—', ''] when there's no distance.
  function distanceParts(m, metric) {
    if (m == null) return ['—', ''];
    const km = m / 1000;
    return metric ? [km.toFixed(1), 'km'] : [(km * 0.539957).toFixed(1), 'nm'];
  }
  function formatDistance(m, metric) {
    const p = distanceParts(m, metric);
    return p[1] ? p[0] + ' ' + p[1] : p[0];
  }

  // Three-digit bearing, 0-359: "005°".
  function formatBearing(deg) {
    return ('00' + Math.round(((deg % 360) + 360) % 360) % 360).slice(-3) + '°';
  }

  // Leg length into each waypoint (meters); the first point starts the route, so its leg is 0.
  function legLengths(points) {
    return points.map((p, i) => i === 0 ? 0 : Math.hypot(p.x - points[i - 1].x, p.z - points[i - 1].z));
  }

  function routeLength(points) {
    return legLengths(points).reduce((a, b) => a + b, 0);
  }

  // Distance still to fly: ownship to the next waypoint, then every leg after it. null once the
  // route is complete or ownship isn't known yet.
  function remainingDistance(route, ownX, ownZ) {
    const pts = route.waypoints, next = route.nextIndex;
    if (next >= pts.length || ownX == null || ownZ == null) return null;
    const legs = legLengths(pts);
    let d = Math.hypot(pts[next].x - ownX, pts[next].z - ownZ);
    for (let i = next + 1; i < pts.length; i++) d += legs[i];
    return d;
  }

  // The RTD timeline: up to `perRow` connected points per row, then the next row. Positions are
  // slot units (col 0..perRow-1, row 0..); the page turns them into pixels. A leg whose two ends
  // sit on different rows is split in two: `out` leaves its row at the right edge and `in`
  // re-enters the next row from the left edge, which is where the leg's label goes.
  function timeline(count, perRow) {
    const nodes = [], segments = [];
    for (let i = 0; i < count; i++) {
      nodes.push({ index: i, row: Math.floor(i / perRow), col: i % perRow });
      if (i === 0) continue;
      const a = nodes[i - 1], b = nodes[i];
      if (a.row === b.row) segments.push({ index: i, row: b.row, from: a.col, to: b.col, label: true });
      else {
        segments.push({ index: i, row: a.row, from: a.col, to: 'edge', label: false });
        segments.push({ index: i, row: b.row, from: 'edge', to: b.col, label: true });
      }
    }
    return { nodes, segments, rows: count ? Math.ceil(count / perRow) : 0 };
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
    distanceBearing, relativeBearing, signedTurn,
    distanceParts, formatDistance, formatBearing,
    legLengths, routeLength, remainingDistance, timeline,
    waypointMarkerState, segmentReached,
    findRoute, findSteerPoint, navigationTarget,
    serializeRoute, parseRouteJSON, serializeSteerPoints, parseSteerPointsJSON,
  };
  if (typeof module !== 'undefined' && module.exports) module.exports = api;
  else root.WptRoute = api;
})(typeof self !== 'undefined' ? self : this);
