// The network half of the waypoints/routes feature (issue #38, docs/hud-waypoint-indicator.md) —
// GET /wpt-options + POST /command, wrapping wpt-route.js's remaining pure display-derivation
// helpers. Classic <script>, not a module, same as map-transform.js, so it's usable from both
// map.js and wpt.js without a build step.
//
// The plugin, not this browser, is the single source of truth (Option 2): RouteStore.cs owns the
// whole route library, persists it to disk, and ticks proximity-advance itself every second
// regardless of what page any browser has open. There is no local push mechanism here — no
// per-browser route state to disagree across devices, and no ambiguity over which browser's HUD
// cue is authoritative.
//
// Callers (wpt.js, map.js) use plain `.then(...)` against these exports, no rename sweep needed.
(function (root) {
  const R = (typeof module !== 'undefined' && module.exports) ? require('./wpt-route.js') : root.WptRoute;

  // Last-known server state, refreshed by poll() — every sync read (load/getActiveRoute/
  // exportRoute) reads this cache rather than fetching, same as hud.js's own `data` cache.
  let cache = { activeRouteId: null, routes: [] };

  function poll() {
    if (typeof fetch !== 'function') return Promise.resolve();   // no fetch in this context (Node tests)
    return fetch('/wpt-options', { cache: 'no-store' }).then(function (r) { return r.text(); }).then(function (text) {
      const data = JSON.parse(text);
      const changed = JSON.stringify(data) !== JSON.stringify(cache);
      cache = data;
      if (changed && typeof window !== 'undefined') window.dispatchEvent(new Event('wptroutes:changed'));
    }).catch(function () { /* transient network error — next poll retries */ });
  }
  // Perf fix (2026-08-18, docs/hud-waypoint-indicator.md): only the TOP window runs the recurring
  // poll — before this, every document that loaded this file (the shell, plus each open MAP/WPT
  // iframe/pane) ran its OWN independent 1.2s loop, multiplying requests and the redraws each one
  // triggers by however many were open (confirmed by profiling: ~3x the expected request rate for
  // one device). An embedded page instead asks its parent for the current cache once on load (the
  // parent only pushes on real changes, so a freshly loaded iframe needs to explicitly catch up)
  // and then just listens — poll() itself is unchanged and still called directly by every mutator
  // below for instant feedback on THIS document's own edits; only the recurring background loop is
  // gated.
  const isTop = typeof window !== 'undefined' && window === window.top;
  if (isTop) {
    poll(); setInterval(poll, 1200);   // same cadence as hud.js's /hud-options poll
  } else if (typeof window !== 'undefined') {
    window.parent.postMessage({ mfd: true, type: 'wpt-routes-request' }, '*');
    window.addEventListener('message', function (e) {
      const m = e.data;
      if (!m || m.mfd !== true || m.type !== 'wpt-routes') return;
      const changed = JSON.stringify(m.data) !== JSON.stringify(cache);
      cache = m.data;
      if (changed) window.dispatchEvent(new Event('wptroutes:changed'));
    });
  }

  function load() { return cache; }

  function getActiveRoute() {
    return R.findRoute(cache.routes, cache.activeRouteId);
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
    load, poll, getActiveRoute, setActiveRoute, cycleActiveRoute, createRoute, renameRoute, deleteRoute, clearRoutes,
    addWaypointToActive, renameWaypoint, removeWaypoint, reorderWaypoint,
    resetWaypoint, resetRoute, stepWaypoint, exportRoute, importRoute,
  };
  if (typeof module !== 'undefined' && module.exports) module.exports = api;
  else root.WaypointsStore = api;
})(typeof self !== 'undefined' ? self : this);
