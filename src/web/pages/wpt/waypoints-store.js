// The network half of the route/steer-point feature (issues #38/#73, docs/steer-points.md) —
// GET /wpt-options + POST /command, wrapping wpt-route.js's remaining pure display-derivation
// helpers. Classic <script>, not a module, same as map-transform.js, so it's usable from both
// map.js and wpt.js without a build step.
//
// The plugin, not this browser, is the single source of truth (Option 2): RouteStore.cs owns the
// whole navigation library, persists it to disk, and ticks route proximity-advance every second
// regardless of what page any browser has open. There is no local push mechanism here — no
// per-browser route state to disagree across devices, and no ambiguity over which browser's HUD
// cue is authoritative.
//
// Callers (wpt.js, map.js) use plain `.then(...)` against these exports, no rename sweep needed.
(function (root) {
  const R = (typeof module !== 'undefined' && module.exports) ? require('./wpt-route.js') : root.WptRoute;

  // Last-known server state, refreshed by poll() — synchronous reads and exports use this cache
  // rather than fetching, same as hud.js's own `data` cache.
  let cache = { activeRouteId: null, activeSteerPointId: null, routes: [], steerPoints: [] };

  function poll() {
    if (typeof fetch !== 'function') return Promise.resolve();   // no fetch in this context (Node tests)
    return fetch('/wpt-options', { cache: 'no-store' }).then(function (r) { return r.text(); }).then(function (text) {
      const data = JSON.parse(text);
      const changed = JSON.stringify(data) !== JSON.stringify(cache);
      cache = data;
      if (changed && typeof window !== 'undefined') window.dispatchEvent(new Event('wptroutes:changed'));
    }).catch(function () { /* transient network error — next poll retries */ });
  }
  // Only the TOP window talks to the plugin directly — every document that loads this file (the
  // shell, plus each open MAP/WPT iframe/pane) would otherwise run its own independent poll,
  // multiplying requests and the redraws each one triggers by however many are open. An embedded
  // page instead asks its parent for the current cache once on load (the parent only pushes on real
  // changes, so a freshly loaded iframe needs to explicitly catch up) and then just listens —
  // poll() itself is still called directly by every mutator below for instant feedback on THIS
  // document's own edits.
  //
  // For OTHER browsers'/devices' edits to show up, the top window relies on the SSE-relayed
  // 'wpt-options-push' message (docs/sse-push-refactor.md) — telemetry-source.js's one
  // EventSource('/stream') connection (always alive in MAP's own always-loaded iframe) already
  // carries this change-gated, so a recurring HTTP poll for the same data would be pure waste. The
  // one-time poll() below still runs so a document with no shell/telemetry-source at all (a
  // standalone /wpt or /map-view preview) still shows something — it just won't see a later
  // cross-device change without a reload, an accepted limitation of that dev-only path.
  const isTop = typeof window !== 'undefined' && window === window.top;
  if (isTop) {
    poll();
    if (typeof window !== 'undefined') {
      window.addEventListener('message', function (e) {
        const m = e.data;
        if (!m || m.mfd !== true || m.type !== 'wpt-options-push') return;
        const changed = JSON.stringify(m.data) !== JSON.stringify(cache);
        cache = m.data;
        if (changed) window.dispatchEvent(new Event('wptroutes:changed'));
      });
    }
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

  function getActiveSteerPoint() {
    return R.findSteerPoint(cache.steerPoints || [], cache.activeSteerPointId);
  }

  function getNavigationTarget() { return R.navigationTarget(cache); }

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
  function stepNavigation(dir)             { return sendCommand('wpt.step-navigation', { index: dir }).then(poll); }
  // Placed waypoints get no default name — WPT shows one by its position number alone (the
  // "1. 2. 3." list index already identifies it); the pilot can name it later if they want to.
  function addWaypointToActive(x, z, name) { return sendCommand('wpt.add-waypoint', { wx: x, wz: z, wname: name || '' }).then(poll); }
  function addNavigationPoint(x, z, name)  { return sendCommand('wpt.add-navigation-point', { wx: x, wz: z, wname: name || '' }).then(poll); }
  function addSteerPoint(x, z, name)       { return sendCommand('wpt.add-steerpoint', { wx: x, wz: z, wname: name || '' }).then(poll); }
  function renameSteerPoint(id, name)      { return sendCommand('wpt.rename-steerpoint', { bind: id, wname: name }).then(poll); }
  function deleteSteerPoint(id)            { return sendCommand('wpt.delete-steerpoint', { bind: id }).then(poll); }
  function setActiveSteerPoint(id)         { return sendCommand('wpt.set-active-steerpoint', { bind: id || '' }).then(poll); }
  function cycleSteerPoint(dir)            { return sendCommand('wpt.cycle-steerpoint', { index: dir }).then(poll); }
  function importSteerPoints(text)         { return sendCommand('wpt.import-steerpoints', { text: text }).then(poll); }

  // Pretty-printed JSON a pilot can paste to another WPT instance — null if the route doesn't
  // exist (deleted out from under an open export panel, say). Stays fully client-side: it's a
  // read of already-fetched data, no server round trip needed.
  function exportRoute(id) {
    const route = R.findRoute(cache.routes, id);
    return route ? JSON.stringify(R.serializeRoute(route), null, 2) : null;
  }

  function exportSteerPoints() {
    return JSON.stringify(R.serializeSteerPoints(cache.steerPoints || []), null, 2);
  }

  // ── squad-shared navigation data (docs/squadron-transport.md) ─────────────────────────
  // Pending shares ride the existing /wpt-options cache. They remain separate from pasted
  // imports because squad updates and delete tombstones need stable sender-owned identities.
  function pendingShared() { return cache.pendingShared || []; }
  function pendingSharedSteerPoints() { return cache.pendingSharedSteerPoints || []; }

  // The plugin builds the payload and sends it (RouteStore.ShareRoute), and flips on auto-reshare
  // for this route — later edits push to the squad on their own, no repeat click needed here.
  function shareRoute(id) { return sendCommand('wpt.share', { bind: id }).then(poll); }

  // A leader's incoming share/delete (wpt.route / wpt.route-deleted) is applied directly plugin-side
  // (Squad.HandleData) the instant it arrives over Steam, not routed through a browser command —
  // this page (and every other open display) just sees the result via the SSE-pushed
  // 'wpt-options-push'/'wptroutes:changed' path, same as any other plugin-side route change.
  // ACCEPT/REJECT stay real browser actions: only the pilot's own decision on an already-pending
  // share, never applied automatically.
  function acceptShared(id)      { return sendCommand('wpt.accept-shared', { bind: id }).then(poll); }
  function rejectShared(id)      { return sendCommand('wpt.reject-shared', { bind: id }).then(poll); }

  // Steer points use the same share/accept/reject model as routes above — a leader's incoming
  // share/delete is applied directly plugin-side (Squad.HandleData), never routed through a
  // browser command; only the manual SHARE button and the pilot's own ACCEPT/REJECT are real
  // browser actions.
  function shareSteerPoint(id)        { return sendCommand('wpt.share-steerpoint', { bind: id }).then(poll); }
  function acceptSharedSteerPoint(id) { return sendCommand('wpt.accept-shared-steerpoint', { bind: id }).then(poll); }
  function rejectSharedSteerPoint(id) { return sendCommand('wpt.reject-shared-steerpoint', { bind: id }).then(poll); }

  const api = {
    freshRouteName,
    load, poll, getActiveRoute, getActiveSteerPoint, getNavigationTarget,
    setActiveRoute, cycleActiveRoute, createRoute, renameRoute, deleteRoute, clearRoutes,
    addWaypointToActive, addNavigationPoint, renameWaypoint, removeWaypoint, reorderWaypoint,
    resetWaypoint, resetRoute, stepWaypoint, stepNavigation, exportRoute, importRoute,
    addSteerPoint, renameSteerPoint, deleteSteerPoint, setActiveSteerPoint, cycleSteerPoint,
    exportSteerPoints, importSteerPoints,
    pendingShared, pendingSharedSteerPoints,
    shareRoute, acceptShared, rejectShared,
    shareSteerPoint, acceptSharedSteerPoint, rejectSharedSteerPoint,
  };
  if (typeof module !== 'undefined' && module.exports) module.exports = api;
  else root.WaypointsStore = api;
})(typeof self !== 'undefined' ? self : this);
