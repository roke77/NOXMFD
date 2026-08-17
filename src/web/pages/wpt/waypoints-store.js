// The storage-coupled half of the waypoints/routes feature (issue #38) — localStorage read/write
// and id generation, wrapping wpt-route.js's pure CRUD/math. Classic <script>, not a module, same
// as map-transform.js, so it's usable from both map.js and wpt.js without a build step.
//
// Not unit-tested: DOM/storage-coupled code is left to the harness and the eye here, matching the
// repo's own convention (src/web/README.md) — the logic worth pinning down (advance thresholds,
// nextIndex tracking across edits) already lives in wpt-route.js's tests.
//
// localStorage, NOT sessionStorage (contrast map.js's VIEW_STORE_KEY): routes are pilot-authored
// planning data meant to persist across reloads and be shared across every tab/display on the same
// PC, the opposite tradeoff from per-tab pan/zoom/follow view state.
(function (root) {
  const STORE_KEY = 'noxmfd.map.waypoints';
  const R = (typeof module !== 'undefined' && module.exports) ? require('./wpt-route.js') : root.WptRoute;

  // Same fallback shape as telemetry-source.js's instanceId(): crypto.randomUUID needs a secure
  // context, which plain http:// over the LAN (how this mod is normally reached) doesn't have.
  function freshId(prefix) {
    return prefix + (crypto.randomUUID ? crypto.randomUUID()
                                        : Date.now().toString(36) + Math.random().toString(36).slice(2, 10));
  }

  // A short, human-typeable default DISPLAY name for a new route — distinct from freshId (the
  // internal id, never shown). The UI pre-fills its rename field with this so the pilot can accept
  // it as-is or type over it before confirming (wpt.js's "+ NEW ROUTE" flow).
  function freshRouteName() {
    const raw = crypto.randomUUID ? crypto.randomUUID().replace(/-/g, '') : Math.random().toString(36).slice(2);
    return 'RT-' + raw.slice(0, 5).toUpperCase();
  }

  function load() {
    try {
      const raw = localStorage.getItem(STORE_KEY);
      const parsed = raw && JSON.parse(raw);
      if (parsed && parsed.version === 1 && Array.isArray(parsed.routes)) return parsed;
    } catch (e) { /* corrupt or inaccessible — fall through to a fresh collection */ }
    return { version: 1, activeRouteId: null, routes: [] };
  }

  function save(collection) {
    try { localStorage.setItem(STORE_KEY, JSON.stringify(collection)); } catch (e) { /* private mode, quota, etc. */ }
  }

  function getActiveRoute() {
    const c = load();
    return R.findRoute(c.routes, c.activeRouteId);
  }

  // Whether any route exists at all, active or not — R+/R- (issue #38 follow-up) stay usable to
  // cycle INTO a route as long as one is saved, unlike W+/W- which need an active route to step.
  function hasRoutes() {
    return load().routes.length > 0;
  }

  function setActiveRoute(id) {
    const c = load();
    c.activeRouteId = id;
    save(c);
  }

  // MAP's R+/R- (issue #38): switch to the next/previous route in the list, wrapping. Returns the
  // new active route's id (or null if there are no routes at all).
  function cycleActiveRoute(dir) {
    const c = load();
    c.activeRouteId = R.cycleRoute(c.routes, c.activeRouteId, dir);
    save(c);
    return c.activeRouteId;
  }

  // Names must be unique (issue #38 follow-up) — a typed or auto-generated name that collides with
  // an existing route gets "(2)", "(3)", … appended (R.uniqueRouteName) rather than silently
  // creating two routes a pilot can't tell apart in the list or the readout.
  function createRoute(name) {
    const c = load();
    const route = { id: freshId('r_'), name: R.uniqueRouteName(c.routes, name || freshRouteName()), nextIndex: 0, waypoints: [] };
    c.routes = R.addRoute(c.routes, route);
    c.activeRouteId = route.id;
    save(c);
    return route;
  }

  function renameRoute(id, name) {
    const c = load();
    c.routes = R.renameRoute(c.routes, id, R.uniqueRouteName(c.routes, name, id));
    save(c);
  }

  // Pretty-printed JSON a pilot can paste to another WPT instance — null if the route doesn't
  // exist (deleted out from under an open export panel, say).
  function exportRoute(id) {
    const c = load();
    const route = R.findRoute(c.routes, id);
    return route ? JSON.stringify(R.serializeRoute(route), null, 2) : null;
  }

  // The reverse of exportRoute: parses `text`, and on success adds it as a brand-new route (fresh
  // id, fresh waypoint ids, name deduped against what's already here, progress reset to 0 — an
  // imported route always starts unflown, even if it was exported mid-flight) and makes it active.
  // Returns the new route, or null if `text` didn't parse as a route (waypoints-store.js has no UI
  // of its own to report that through — wpt.js's import panel shows the failure).
  function importRoute(text) {
    const parsed = R.parseRouteJSON(text);
    if (!parsed) return null;
    const c = load();
    const route = {
      id: freshId('r_'),
      name: R.uniqueRouteName(c.routes, parsed.name || freshRouteName()),
      nextIndex: 0,
      waypoints: parsed.waypoints.map(w => ({ id: freshId('w_'), name: w.name, x: w.x, z: w.z })),
    };
    c.routes = R.addRoute(c.routes, route);
    c.activeRouteId = route.id;
    save(c);
    return route;
  }

  function deleteRoute(id) {
    const c = load();
    c.routes = R.deleteRoute(c.routes, id);
    if (c.activeRouteId === id) c.activeRouteId = c.routes.length ? c.routes[0].id : null;
    save(c);
  }

  // CLEAR (issue #38 follow-up): drop every route at once — the "+ NEW ROUTE"/IMPORT row's third
  // button. No per-route confirmation, same as every other delete in this page.
  function clearRoutes() {
    save({ version: 1, activeRouteId: null, routes: [] });
  }

  function withActiveRoute(mutate) {
    const c = load();
    const route = R.findRoute(c.routes, c.activeRouteId);
    if (!route) return null;
    const updated = mutate(route);
    c.routes = c.routes.map(r => (r.id === updated.id ? updated : r));
    save(c);
    return updated;
  }

  // Creates a default route on first-ever placement, so a long-press works before any visit to WPT.
  // A placed waypoint gets no default name — WPT shows it by its position number alone (the
  // "1. 2. 3." list index already identifies it); the pilot can name it later if they want to.
  function addWaypointToActive(x, z, name) {
    let c = load();
    if (!c.activeRouteId || !R.findRoute(c.routes, c.activeRouteId)) {
      const route = { id: freshId('r_'), name: R.uniqueRouteName(c.routes, freshRouteName()), nextIndex: 0, waypoints: [] };
      c.routes = R.addRoute(c.routes, route);
      c.activeRouteId = route.id;
      save(c);
    }
    return withActiveRoute(route => R.addWaypoint(route, {
      id: freshId('w_'), name: name || '', x, z,
    }));
  }

  function renameWaypoint(index, name) { return withActiveRoute(route => R.renameWaypoint(route, index, name)); }
  function removeWaypoint(index)       { return withActiveRoute(route => R.removeWaypoint(route, index)); }
  function reorderWaypoint(from, to)   { return withActiveRoute(route => R.reorderWaypoint(route, from, to)); }

  // Rewind the active route's progress to `index` — that waypoint becomes NEXT again, and it (plus
  // everything after it) counts as not-reached. A per-waypoint "reset" button's action.
  function resetWaypoint(index) { return withActiveRoute(route => R.resetProgress(route, index)); }

  // MAP's W+/W- (issue #38): manually step the active route's "next" waypoint by one, without
  // waiting to fly into proximity range. Just resetProgress by nextIndex+dir — resetProgress already
  // clamps to [0, waypoints.length], so stepping past either end simply holds at the boundary.
  function stepWaypoint(dir) { return withActiveRoute(route => R.resetProgress(route, route.nextIndex + dir)); }

  // Reset an ENTIRE route's progress back to its start — a per-route "reset" button's action. Takes
  // an explicit id (not just the active route) since every row in the route list gets this button,
  // not only the currently-active one.
  function resetRoute(id) {
    const c = load();
    c.routes = c.routes.map(r => (r.id === id ? R.resetProgress(r, 0) : r));
    save(c);
  }

  function advanceIfNear(ownX, ownZ, thresholdM) {
    const c = load();
    const route = R.findRoute(c.routes, c.activeRouteId);
    if (!route) return { advanced: false, route: null };
    const out = R.advanceIfNear(route, ownX, ownZ, thresholdM);
    if (out.advanced) {
      c.routes = c.routes.map(r => (r.id === out.route.id ? out.route : r));
      save(c);
    }
    return out;
  }

  const api = {
    STORE_KEY, freshRouteName,
    load, save, getActiveRoute, hasRoutes, setActiveRoute, cycleActiveRoute, createRoute, renameRoute, deleteRoute, clearRoutes,
    addWaypointToActive, renameWaypoint, removeWaypoint, reorderWaypoint, advanceIfNear,
    resetWaypoint, resetRoute, stepWaypoint, exportRoute, importRoute,
  };
  if (typeof module !== 'undefined' && module.exports) module.exports = api;
  else root.WaypointsStore = api;
})(typeof self !== 'undefined' ? self : this);
