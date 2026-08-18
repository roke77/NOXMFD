// The network half of the waypoints/routes feature (issue #38, docs/hud-waypoint-indicator.md) —
// GET /wpt-options + POST /command, wrapping wpt-route.js's remaining pure display-derivation
// helpers. Classic <script>, not a module, same as map-transform.js, so it's usable from both
// map.js and wpt.js without a build step.
//
// The plugin, not this browser, is the single source of truth (Option 2): RouteStore.cs owns the
// whole route library, persists it to disk, and ticks proximity-advance itself every second
// regardless of what page any browser has open. This file used to be a localStorage wrapper with
// its own push mechanism (publishActive/republishActive) — that mechanism was the actual root of
// the cross-device bug it was meant to work around (two browsers' local routes disagreeing, and
// the HUD cue mirroring whichever one last spoke), so it's gone rather than kept alongside this.
//
// Every exported function name is unchanged from the old localStorage version, so callers
// (wpt.js, map.js) need only mechanical `.then(...)` edits, not a rename sweep.
(function (root) {
  const R = (typeof module !== 'undefined' && module.exports) ? require('./wpt-route.js') : root.WptRoute;

  // Last-known server state, refreshed by poll() — every sync read (load/getActiveRoute/hasRoutes/
  // exportRoute) reads this cache rather than fetching, same as hud.js's own `data` cache.
  let cache = { activeRouteId: null, routes: [] };

  function poll() {
    if (typeof fetch !== 'function') return Promise.resolve();   // no fetch in this context (Node tests)
    return fetch('/wpt-options', { cache: 'no-store' }).then(function (r) { return r.json(); }).then(function (data) {
      const changed = JSON.stringify(data) !== JSON.stringify(cache);
      cache = data;
      if (changed && typeof window !== 'undefined') window.dispatchEvent(new Event('wptroutes:changed'));
    }).catch(function () { /* transient network error — next poll retries */ });
  }
  if (typeof window !== 'undefined') { poll(); setInterval(poll, 1200); }   // same cadence as hud.js's /hud-options poll

  function load() { return cache; }

  function getActiveRoute() {
    return R.findRoute(cache.routes, cache.activeRouteId);
  }

  // Whether any route exists at all, active or not — R+/R- (issue #38 follow-up) stay usable to
  // cycle INTO a route as long as one is saved, unlike W+/W- which need an active route to step.
  function hasRoutes() {
    return cache.routes.length > 0;
  }

  // A short, human-typeable default DISPLAY name for a new route — the UI pre-fills its rename
  // field with this so the pilot can accept it as-is or type over it before confirming (wpt.js's
  // "+ NEW ROUTE" flow). Purely cosmetic; the plugin generates its own if this is sent empty.
  function freshRouteName() {
    const raw = crypto.randomUUID ? crypto.randomUUID().replace(/-/g, '') : Math.random().toString(36).slice(2);
    return 'RT-' + raw.slice(0, 5).toUpperCase();
  }

  // Every mutator: POST the command, then force an immediate poll rather than waiting up to 1.2s
  // for the interval — the pilot's own edit should show up in well under a second.
  function createRoute(name)               { return sendCommand('wpt.create', { wname: name || '' }).then(poll); }
  function renameRoute(id, name)           { return sendCommand('wpt.rename', { bind: id, wname: name }).then(poll); }
  function deleteRoute(id)                 { return sendCommand('wpt.delete', { bind: id }).then(poll); }
  function setActiveRoute(id)              { return sendCommand('wpt.set-active', { bind: id || '' }).then(poll); }
  function clearRoutes()                   { return sendCommand('wpt.clear', {}).then(poll); }
  function resetRoute(id)                  { return sendCommand('wpt.reset-route', { bind: id }).then(poll); }
  function importRoute(text)               { return sendCommand('wpt.import', { text: text }).then(poll); }
  function renameWaypoint(index, name)     { return sendCommand('wpt.rename-waypoint', { index: index, wname: name }).then(poll); }
  function reorderWaypoint(from, to)       { return sendCommand('wpt.reorder-waypoint', { index: from, n: to }).then(poll); }
  function resetWaypoint(index)            { return sendCommand('wpt.reset-waypoint', { index: index }).then(poll); }
  function removeWaypoint(index)           { return sendCommand('wpt.remove-waypoint', { index: index }).then(poll); }
  function cycleActiveRoute(dir)           { return sendCommand('wpt.cycle-route', { index: dir }).then(poll); }
  function stepWaypoint(dir)               { return sendCommand('wpt.step-waypoint', { index: dir }).then(poll); }
  // Placed waypoints get no default name — WPT shows one by its position number alone (the
  // "1. 2. 3." list index already identifies it); the pilot can name it later if they want to.
  function addWaypointToActive(x, z, name) { return sendCommand('wpt.add-waypoint', { wx: x, wz: z, wname: name || '' }).then(poll); }

  // Pretty-printed JSON a pilot can paste to another WPT instance — null if the route doesn't
  // exist (deleted out from under an open export panel, say). Stays fully client-side: it's a
  // read of already-fetched data, no server round trip needed.
  function exportRoute(id) {
    const route = R.findRoute(cache.routes, id);
    return route ? JSON.stringify(R.serializeRoute(route), null, 2) : null;
  }

  const api = {
    freshRouteName,
    load, poll, getActiveRoute, hasRoutes, setActiveRoute, cycleActiveRoute, createRoute, renameRoute, deleteRoute, clearRoutes,
    addWaypointToActive, renameWaypoint, removeWaypoint, reorderWaypoint,
    resetWaypoint, resetRoute, stepWaypoint, exportRoute, importRoute,
  };
  if (typeof module !== 'undefined' && module.exports) module.exports = api;
  else root.WaypointsStore = api;
})(typeof self !== 'undefined' ? self : this);
