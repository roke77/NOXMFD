// WPT page (issue #38) — route/waypoint list editor + distance/bearing readout. DOM-coupled, not
// unit-tested. docs/hud-waypoint-indicator.md (Option 2): route data and its mutation logic are
// authoritative in the plugin (RouteStore.cs) — every WaypointsStore mutator is a POST
// /command that resolves once the plugin's response has been polled back in, so callers chain
// .then(render) instead of calling render() synchronously afterward. WptRoute/WaypointsStore are
// classic <script> globals (wpt.html), loaded before this module.
import { gridLabel } from '/assets/services/telemetry-source.js';
import { createPadCursor } from '/assets/services/pad-cursor.js';

if (window.parent !== window) {
  const back = document.querySelector('.wpt-back');
  if (back) back.remove();
}

const readoutEl   = document.getElementById('wpt-readout');
const compassNeedle = document.getElementById('wpt-compass-needle');
const routesEl     = document.getElementById('wpt-routes');
const waypointsEl  = document.getElementById('wpt-waypoints');
const newRouteBtn  = document.getElementById('wpt-new-route');
const clearBtn     = document.getElementById('wpt-clear-routes');
const newRow       = document.getElementById('wpt-new-row');
const newNameInput = document.getElementById('wpt-new-name');
const importBtn    = document.getElementById('wpt-import-route');
const ioRow        = document.getElementById('wpt-io-row');
const ioLabel      = document.getElementById('wpt-io-label');
const ioText       = document.getElementById('wpt-io-text');
const ioError      = document.getElementById('wpt-io-error');
const ioPrimary    = document.getElementById('wpt-io-primary');
const ioCopy       = document.getElementById('wpt-io-copy');
const ioClose      = document.getElementById('wpt-io-close');

let mapinfo = { x: null, z: null, hdg: null, ox: null, oy: null };

function render() {
  const c = WaypointsStore.load();
  renderRoutes(c);
  renderWaypoints(WptRoute.findRoute(c.routes, c.activeRouteId));
  // Every button that mutates route/waypoint state (reset, rename, delete, reorder, switch/create
  // route) calls render() — without this, the readout at top would stay showing whatever waypoint
  // was NEXT before the click until the next live 'mapinfo' tick (which never arrives at all when
  // this page is opened standalone, and is a periodic ~100ms lag even embedded in the shell).
  renderReadout();
}

function renderRoutes(c) {
  routesEl.innerHTML = '';

  // Shares awaiting THIS pilot's own accept/reject — rendered first (need attention), with only
  // those two actions: not yet a real route, so nothing else (rename/reset/export/delete/activate)
  // applies to it yet. See RouteStore.cs's own header comment on this group for why a duplicate
  // share never produces a second one of these.
  WaypointsStore.pendingShared().forEach(function (p) {
    const row = document.createElement('div');
    row.className = 'wpt-row wpt-row-pending';

    const name = document.createElement('span');
    name.className = 'wpt-row-name';
    name.appendChild(document.createTextNode(p.name + ' (' + p.waypointCount + ') — from '));
    const leaderName = document.createElement('span');
    leaderName.className = 'wpt-row-pending-leader';
    leaderName.textContent = p.fromName || 'squad leader';
    name.appendChild(leaderName);

    const accept = document.createElement('button');
    accept.className = 'wpt-btn'; accept.textContent = 'ACCEPT';
    accept.onclick = function () { WaypointsStore.acceptShared(p.id).then(render); };

    const reject = document.createElement('button');
    reject.className = 'wpt-btn wpt-btn-ghost'; reject.textContent = 'REJECT';
    reject.onclick = function () { WaypointsStore.rejectShared(p.id).then(render); };

    row.appendChild(name); row.appendChild(accept); row.appendChild(reject);
    routesEl.appendChild(row);
  });

  c.routes.forEach(function (route) {
    const isActive = route.id === c.activeRouteId;
    const isShared = !!route.sharedBy;
    const row = document.createElement('div');
    row.className = 'wpt-row' + (isActive ? ' active' : '');

    const name = document.createElement('span');
    name.className = 'wpt-row-name pad-hoverable';
    name.title = isActive ? 'Click to deactivate' : 'Click to activate';
    // A route can be saved but none active (issue #38 follow-up) — clicking the already-ACTIVE
    // route deactivates it instead of being a no-op.
    name.onclick = function () { WaypointsStore.setActiveRoute(isActive ? null : route.id).then(render); };
    name.appendChild(document.createTextNode(route.name + ' (' + route.waypoints.length + ')'));

    // Shared with the squad right now — either this pilot accepted it FROM the leader (isShared,
    // read-only, sharedBy non-empty) or it's this pilot's OWN route with the leader-side
    // auto-reshare on (route.sharedWithSquad — only ever true while this pilot IS the leader,
    // since ShareRoute/BroadcastIfShared are leader-only; a member never sees this on their own
    // routes). Nested inside the name span (not a sibling flex item) so it sits right after the
    // name text itself instead of being pushed to the row's far edge by name's flex:1 width.
    if (isShared || route.sharedWithSquad) {
      const sqdMark = document.createElement('span');
      sqdMark.className = 'wpt-row-sqd-mark';
      sqdMark.textContent = ' SQD';
      name.appendChild(sqdMark);
    }

    const mark = document.createElement('span');
    mark.className = 'wpt-row-mark';
    mark.textContent = isActive ? 'ACTIVE' : '';

    const reset = document.createElement('button');
    reset.className = 'wpt-row-btn pad-hoverable'; reset.textContent = '↺'; reset.title = 'Reset route (mark every waypoint not-reached)';
    reset.onclick = function () { WaypointsStore.resetRoute(route.id).then(render); };

    const exportBtn = document.createElement('button');
    exportBtn.className = 'wpt-row-btn pad-hoverable'; exportBtn.textContent = '⇩'; exportBtn.title = 'Export route as JSON';
    exportBtn.onclick = function () { openExportPanel(route.id); };

    // Share with the squad (docs/squadron-transport.md, SQD page). Only the LEADER can share
    // (Squad.SendData is leader-only), and only once at least one member has joined — squad
    // membership itself is managed on the SQD page, not here. A route someone ELSE shared with US
    // never shows this button at all: Squad.cs's Role is a single value (none/leader/member), so
    // holding a route someone shared with us and being a leader ourselves can't both be true.
    const share = document.createElement('button');
    share.className = 'wpt-row-btn pad-hoverable'; share.textContent = '⇪'; share.title = 'Share route with squad';
    share.onclick = function () { shareRoute(route.id, share); };

    const del = document.createElement('button');
    del.className = 'wpt-row-btn pad-hoverable';
    del.textContent = '×';
    del.title = isShared ? 'Remove from your routes' : 'Delete route';
    del.onclick = function () { WaypointsStore.deleteRoute(route.id).then(render); };

    row.appendChild(name); row.appendChild(mark);
    // Rename is content editing — not available on a route someone else shared with you. Everything
    // else (progress reset, export, deleting YOUR OWN copy) still applies regardless of origin.
    if (!isShared) {
      const edit = document.createElement('button');
      edit.className = 'wpt-row-btn pad-hoverable'; edit.textContent = '✎'; edit.title = 'Rename route';
      // Empty stays the route's current (generated) name — a route always keeps SOME name.
      edit.onclick = function () {
        editRow(row, route.name, null, function (name) { return name ? WaypointsStore.renameRoute(route.id, name) : undefined; });
      };
      row.appendChild(edit);
    }
    row.appendChild(reset);
    row.appendChild(exportBtn);
    if (!isShared && sqd.role === 'leader' && sqd.members.length) row.appendChild(share);
    row.appendChild(del);
    routesEl.appendChild(row);
  });
}

// Shared inline-edit UI for a route/waypoint row's name — a pencil button (route/waypoint rows
// below) swaps the row for a text input + Save button. Enter also saves; Escape discards and just
// re-renders. onSave receives the trimmed value (may be empty — the waypoint case allows clearing
// a name back to "unnamed"; the route case's own callback decides whether to accept empty) and
// returns the mutator's promise (or undefined if it decided not to save) — commit waits for that
// before re-rendering, so the row doesn't briefly flash back to its pre-edit value.
function editRow(row, value, placeholder, onSave) {
  row.innerHTML = '';
  const input = document.createElement('input');
  input.type = 'text'; input.maxLength = 40; input.value = value;
  if (placeholder) input.placeholder = placeholder;
  const commit = function () { Promise.resolve(onSave(input.value.trim())).then(render); };
  const save = document.createElement('button');
  save.className = 'wpt-row-btn wpt-row-save pad-hoverable'; save.textContent = '✓'; save.title = 'Save';
  save.onclick = commit;
  input.onkeydown = function (e) { if (e.key === 'Enter') commit(); else if (e.key === 'Escape') render(); };
  row.appendChild(input);
  row.appendChild(save);
  input.focus(); input.select();
}

function renderWaypoints(route) {
  waypointsEl.innerHTML = '';
  if (!route) return;
  const isShared = !!route.sharedBy;   // content read-only — see renderRoutes' own comment
  route.waypoints.forEach(function (wp, i) {
    const row = document.createElement('div');
    row.className = 'wpt-row' + (i === route.nextIndex ? ' next' : '');

    const name = document.createElement('span');
    name.className = 'wpt-row-name';
    name.textContent = wp.name ? (i + 1) + '. ' + wp.name : (i + 1) + '.';

    const mark = document.createElement('span');
    mark.className = 'wpt-row-mark';
    mark.textContent = i === route.nextIndex ? 'NEXT' : '';

    const grid = document.createElement('span');
    grid.className = 'wpt-row-grid';
    grid.textContent = gridLabel(wp.x, wp.z, { ox: mapinfo.ox, oy: mapinfo.oy });

    const reset = document.createElement('button');
    reset.className = 'wpt-row-btn pad-hoverable'; reset.textContent = '↺';
    reset.title = 'Rewind here — this waypoint (and every one after it) becomes not-reached, this one NEXT';
    reset.onclick = function () { WaypointsStore.resetWaypoint(i).then(render); };

    row.appendChild(name); row.appendChild(mark); row.appendChild(grid);
    // Progress (NEXT/reset) is personal and always yours to change; the route's own content
    // (rename/reorder/delete a waypoint) is read-only on a route someone else shared with you —
    // RouteStore.cs's RenameWaypoint/ReorderWaypoint/RemoveWaypoint already refuse these
    // server-side, so this only saves the pilot a wasted click, not the actual enforcement.
    if (!isShared) {
      const edit = document.createElement('button');
      edit.className = 'wpt-row-btn pad-hoverable'; edit.textContent = '✎'; edit.title = 'Rename waypoint';
      // Unlike routes, an empty save is valid here — it clears the name back to "unnamed" (position
      // number only), matching a fresh waypoint's own default.
      edit.onclick = function () {
        editRow(row, wp.name, 'Name (optional)', function (name) { return WaypointsStore.renameWaypoint(i, name); });
      };
      row.appendChild(edit);
    }
    row.appendChild(reset);
    if (!isShared) {
      const up = document.createElement('button');
      up.className = 'wpt-row-btn pad-hoverable'; up.textContent = '▲'; up.title = 'Move up';
      up.disabled = i === 0;
      up.onclick = function () { WaypointsStore.reorderWaypoint(i, i - 1).then(render); };
      row.appendChild(up);

      const down = document.createElement('button');
      down.className = 'wpt-row-btn pad-hoverable'; down.textContent = '▼'; down.title = 'Move down';
      down.disabled = i === route.waypoints.length - 1;
      down.onclick = function () { WaypointsStore.reorderWaypoint(i, i + 1).then(render); };
      row.appendChild(down);

      const del = document.createElement('button');
      del.className = 'wpt-row-btn pad-hoverable'; del.textContent = '×'; del.title = 'Delete waypoint';
      del.onclick = function () { WaypointsStore.removeWaypoint(i).then(render); };
      row.appendChild(del);
    }
    waypointsEl.appendChild(row);
  });
}

newRouteBtn.onclick = function () {
  closeIOPanel();
  newRow.style.display = 'flex';
  newNameInput.value = WaypointsStore.freshRouteName();   // pre-filled, editable — accept or type over
  newNameInput.focus(); newNameInput.select();
};
document.getElementById('wpt-new-cancel').onclick = function () { newRow.style.display = 'none'; };
document.getElementById('wpt-new-confirm').onclick = function () {
  const name = newNameInput.value.trim();
  newRow.style.display = 'none';
  WaypointsStore.createRoute(name || null).then(render);
};
newNameInput.onkeydown = function (e) { if (e.key === 'Enter') document.getElementById('wpt-new-confirm').click(); };

// CLEAR — drop every route at once, same no-confirmation style as the per-route × button.
clearBtn.onclick = function () { closeIOPanel(); newRow.style.display = 'none'; WaypointsStore.clearRoutes().then(render); };

// ── Import/export (one shared panel, two modes — see wpt.html's comment on wpt-io-row) ──────
function closeIOPanel() { ioRow.style.display = 'none'; ioError.textContent = ''; }

function openImportPanel() {
  newRow.style.display = 'none';
  ioLabel.textContent = 'IMPORT ROUTE — paste an exported route\'s JSON below';
  ioText.value = '';
  ioText.readOnly = false;
  ioError.textContent = '';
  ioPrimary.style.display = '';
  ioCopy.style.display = 'none';
  ioRow.style.display = 'block';
  ioText.focus();
}
importBtn.onclick = openImportPanel;

ioPrimary.onclick = function () {
  // Pre-validate client-side (WptRoute.parseRouteJSON) for an instant error — the actual import
  // is a fire-and-forget POST /command, with no synchronous way back from the server to say the
  // paste wasn't a route (docs/hud-waypoint-indicator.md). RouteStore.ImportRoute independently
  // re-parses server-side as the real source of truth.
  if (!WptRoute.parseRouteJSON(ioText.value)) {
    ioError.textContent = 'Could not read that as a route — check the pasted JSON.';
    return;
  }
  WaypointsStore.importRoute(ioText.value).then(render);
  closeIOPanel();
};

function openExportPanel(id) {
  const json = WaypointsStore.exportRoute(id);
  if (!json) return;   // the route vanished (deleted) between the click and here
  newRow.style.display = 'none';
  ioLabel.textContent = 'EXPORT ROUTE — copy this and send it to share the route';
  ioText.value = json;
  ioText.readOnly = true;
  ioError.textContent = '';
  ioPrimary.style.display = 'none';
  ioCopy.style.display = '';
  ioCopy.textContent = 'COPY';
  ioRow.style.display = 'block';
  ioText.focus(); ioText.select();
}

ioCopy.onclick = function () {
  ioText.focus(); ioText.select();
  // navigator.clipboard needs a secure context (https, or localhost) — plain http:// over the LAN
  // (how this mod is normally reached) doesn't have it, so fall back to the old execCommand path,
  // which works off the selection this handler just made regardless of context.
  const done = function () { ioCopy.textContent = 'COPIED'; setTimeout(function () { ioCopy.textContent = 'COPY'; }, 1200); };
  if (navigator.clipboard && navigator.clipboard.writeText) {
    navigator.clipboard.writeText(ioText.value).then(done, function () {
      try { document.execCommand('copy'); done(); } catch (e) {}
    });
  } else {
    try { document.execCommand('copy'); done(); } catch (e) {}
  }
};

ioClose.onclick = closeIOPanel;

// An unnamed waypoint has no wp.name — fall back to its position number rather than showing nothing.
function waypointLabel(wp, index) { return wp.name || ('WAYPOINT ' + (index + 1)); }

function renderReadout() {
  const c = WaypointsStore.load();
  const route = WptRoute.findRoute(c.routes, c.activeRouteId);
  if (!route) { readoutEl.textContent = 'NO ACTIVE ROUTE'; hideNeedle(); return; }
  if (route.nextIndex >= route.waypoints.length) {
    readoutEl.textContent = 'ROUTE COMPLETE'; hideNeedle(); return;
  }
  const next = route.waypoints[route.nextIndex];
  const label = waypointLabel(next, route.nextIndex);
  if (mapinfo.x == null) { readoutEl.textContent = 'NEXT: ' + label; hideNeedle(); return; }
  const { distM, brgDeg } = WptRoute.distanceBearing(mapinfo.x, mapinfo.z, next.x, next.z);
  readoutEl.textContent = 'NEXT: ' + label + '  BRG ' + Math.round(brgDeg) + '°  DIST ' + (distM / 1000).toFixed(1) + ' km';
  updateCompass(brgDeg);
}

// The ring always shows; only the needle hides when there's nothing to point at (no active route,
// route complete, or no position/heading yet) — the ring reads as "no bearing right now", not gone.
function hideNeedle() { compassNeedle.style.display = 'none'; }

// The needle points at WptRoute.relativeBearing(brgDeg, hdg) — 0° (straight up) when the aircraft
// is already pointed at the waypoint, sweeping clockwise the same direction the pilot would need
// to turn — a compass rose read nose-relative, not north-up.
function updateCompass(brgDeg) {
  if (typeof mapinfo.hdg !== 'number') { hideNeedle(); return; }
  compassNeedle.style.display = '';
  const rel = WptRoute.relativeBearing(brgDeg, mapinfo.hdg);
  compassNeedle.setAttribute('transform', 'rotate(' + rel + ' 50 50)');
}

// The waypoint list's grid-label column is only rebuilt by render() (on load/edit/storage) — this
// page paints once, synchronously, before the shell's first 'mapinfo' message can possibly have
// arrived, so mapinfo.ox/oy are still null at that first render() and the column would otherwise
// stay stuck on '—' forever, even once real values start flowing through renderReadout() below.
let gridMetaKey = mapinfo.ox + ',' + mapinfo.oy;

function tick() {
  // Proximity-advance is no longer this page's job (docs/hud-waypoint-indicator.md, Option 2) —
  // the plugin ticks RouteStore.AdvanceIfNear itself every second regardless of what page is
  // open anywhere, so the shell's relayed 'wpt-options-push' (docs/sse-push-refactor.md) is what
  // surfaces an advance here, the same as any other change made from a different display.
  const key = mapinfo.ox + ',' + mapinfo.oy;
  if (key !== gridMetaKey) { gridMetaKey = key; render(); }
  renderReadout();
}

// ── PAD cursor (docs/page-cursor.md) ──────────────────────────────────────────────────
// Same crosshair/transport MAP/TGT/HUD use (pad-cursor.js), driven here only while this WPT is the
// SOI's focused surface. Every clickable control already has a real onclick — no per-element-type
// dispatch needed (contrast TGT's tgt.set/clear-datalink/clear-stale split), so Select is just a
// synthetic click at the crosshair's point, same as HUD. #pad-cursor is position:fixed (wpt.css) —
// the one MFD page whose own body scrolls rather than a fixed-size panel — so (x, y) here are
// already plain viewport coordinates; no panel-rect offset math needed, unlike TGT/HUD.
const CURSORABLE = '.pad-hoverable';
const padCursorEl = document.getElementById('pad-cursor');
const cursor = createPadCursor({
  el: padCursorEl,
  clampRect: () => ({ dx: 0, dy: 0, dw: window.innerWidth, dh: window.innerHeight }),
  onSelect: padCursorSelectAt,
  onMove: padCursorMoveAt,
});

function padCursorSelectAt(x, y) {
  const raw = document.elementFromPoint(x, y);
  const el = raw && raw.closest(CURSORABLE);
  if (el) el.click();
}

// Hover feedback (docs/page-cursor.md #2): the shared .pad-hoverable/.pad-hover pair (theme.css).
// Tolerates the row being destroyed/recreated out from under it (render() rebuilds the lists on
// every edit) — a stale hoveredEl just fails the `=== ` check and gets replaced next move.
let hoveredEl = null;
function padCursorMoveAt(x, y) {
  const raw = x == null ? null : document.elementFromPoint(x, y);
  const el = raw && raw.closest(CURSORABLE);
  if (el === hoveredEl) return;
  if (hoveredEl) hoveredEl.classList.remove('pad-hover');
  hoveredEl = el;
  if (hoveredEl) hoveredEl.classList.add('pad-hover');
}

// Zoom In/Out (map-act's zoom-in/zoom-out) are repurposed here to scroll the page — nothing on this
// page to zoom, and the binds already exist end-to-end (docs/page-cursor.md), same as TGT/HUD.
const SCROLL_STEP = 60;   // flat constant tuned by feel, like pad-cursor.js's own SPEED

window.addEventListener('message', function (e) {
  const m = e.data;
  if (!m || m.mfd !== true) return;
  if (m.type === 'mapinfo') { mapinfo = m; tick(); return; }
  if (m.action === 'cursor-focus') cursor.setFocus(!!m.on, window.innerWidth / 2, window.innerHeight / 2);
  else if (m.action === 'cursor') cursor.setVector(m.x, m.y);
  else if (m.action === 'cursor-select') cursor.select();
  else if (m.action === 'zoom-in') window.scrollBy({ top: SCROLL_STEP });
  else if (m.action === 'zoom-out') window.scrollBy({ top: -SCROLL_STEP });
  // R+/R-/W+/W- physical keybinds (issue #38 follow-up) — same 'map-act' transport MAP already
  // handles (map.js), delivered here too since WPT is a PAD_CURSOR_PAGES page (mfd.js/f35.js); only
  // wpt.js itself had never listened for these four actions.
  else if (m.action === 'route-next')    { WaypointsStore.cycleActiveRoute(1).then(render); }
  else if (m.action === 'route-prev')    { WaypointsStore.cycleActiveRoute(-1).then(render); }
  else if (m.action === 'waypoint-next') { WaypointsStore.stepWaypoint(1).then(render); }
  else if (m.action === 'waypoint-prev') { WaypointsStore.stepWaypoint(-1).then(render); }
});

// Another tab/pane, another DEVICE, or MAP itself changed a route — the plugin is the single
// source of truth now (docs/hud-waypoint-indicator.md), so this fires off WaypointsStore's own
// poll of /wpt-options rather than a same-PC-only localStorage 'storage' event. A route arriving
// from a squadmate lands the same way: the shell hands it to the plugin (wpt.import), and this
// fires once RouteStore's next poll picks it up.
window.addEventListener('wptroutes:changed', render);

// ── Squad (docs/squadron-transport.md) ─────────────────────────────────────────────────
// Squad membership/invites live on the dedicated SQD page — this page only needs to know whether
// IT can share a route right now, i.e. whether we're the squad leader with at least one member.
// Rides the shell's relayed 'sqd-state' push (docs/sse-push-refactor.md) — one bootstrap GET /squad
// on load for the brief gap before the first push (and for standalone/preview contexts with no
// shell), then just a message listener; no recurring poll of its own.
const sqd = { role: 'none', members: [] };

function applySquad(s) {
  if (!s || !s.state) return;
  sqd.role    = s.state.role || 'none';
  sqd.members = Array.isArray(s.state.members) ? s.state.members : [];
  render();   // the per-route share button appears/disappears with leadership + membership
}

function refreshSquad() {
  return fetch('/squad').then(r => r.ok ? r.json() : null).then(applySquad)
    .catch(function () { /* standalone/preview without the plugin — share stays hidden */ });
}

// Disabled while a send is in flight (and briefly after) so mashing the button can't fire a burst
// of wpt.share commands — RouteStore.ShareRoute/BroadcastIfShared already ignore a duplicate id
// server-side, so this is a courtesy against needless network chatter, not the actual dedup
// enforcement. Only the FIRST share needs this button at all — RouteStore flips on auto-reshare
// for the route from then on, so later edits push on their own with no further clicks.
function shareRoute(id, btn) {
  if (btn.disabled) return;
  const was = btn.textContent;
  btn.disabled = true;
  WaypointsStore.shareRoute(id)
    .then(function () {
      btn.textContent = '✓';
      setTimeout(function () { btn.textContent = was; btn.disabled = false; }, 1200);
    })
    .catch(function () { btn.disabled = false; });
}

refreshSquad();
window.addEventListener('message', function (e) {
  const m = e.data;
  if (!m || m.mfd !== true || m.type !== 'sqd-state') return;
  applySquad(m.data);
});
render();   // also paints the readout — see render()'s own comment
