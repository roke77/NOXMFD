// WPT page (issue #38, docs/wpt-rework.md) — route and steer-point manager. DOM-coupled, not
// unit-tested; its display math lives in wpt-route.js (tested). Route data and every mutation are
// authoritative in the plugin (RouteStore.cs): each WaypointsStore mutator is a POST /command that
// resolves once the plugin's state has been polled back, so callers chain .then(render).
// WptRoute/WaypointsStore are classic <script> globals (wpt.html), loaded before this module.
import { gridLabel, gridToWorld } from '/assets/services/telemetry-source.js';
import { createPadCursor } from '/assets/services/pad-cursor.js';

if (window.parent !== window) {
  const back = document.querySelector('.wpt-back');
  if (back) back.remove();
}

const $ = (id) => document.getElementById(id);
const routesEl = $('wpt-routes');
const rowsEl = $('wpt-rows');
const mainEl = document.querySelector('.wpt-main');

let mapinfo = { x: null, z: null, hdg: null, ox: null, oy: null, w: null, h: null, metric: false };
const sqd = { role: 'none', members: [] };

// What this display has open. Not shared with other displays: two pilots' screens can look at
// different routes while RouteStore holds the one active route.
//   view     — a route id, 'stp' for the steer-point group, or null for "the active route, else
//              the first route, else the steer group"
//   open     — the expanded point row's index
//   renaming — 'route' while the open route's name is being edited, a row index for a point
const ui = { view: null, open: null, renaming: null, renameText: '', adding: false };

// ── small builders ─────────────────────────────────────────────────────────────────────
function el(tag, cls, text) {
  const e = document.createElement(tag);
  if (cls) e.className = cls;
  if (text != null) e.textContent = text;
  return e;
}
function button(label, cls, onClick, aria) {
  const b = el('button', 'wpt-btn pad-hoverable' + (cls ? ' ' + cls : ''), label);
  b.type = 'button';
  if (aria) b.setAttribute('aria-label', aria);
  b.onclick = onClick;
  return b;
}

function fmtDist(m) { return WptRoute.formatDistance(m, mapinfo.metric); }
function gridOf(p) { return mapinfo.ox == null ? '—' : gridLabel(p.x, p.z, { ox: mapinfo.ox, oy: mapinfo.oy }); }
function ownDist(p) { return mapinfo.x == null ? null : WptRoute.distanceBearing(mapinfo.x, mapinfo.z, p.x, p.z).distM; }
function canShare() { return sqd.role === 'leader' && sqd.members.length > 0; }

function closePanels() { $('wpt-io').hidden = true; $('wpt-new-row').hidden = true; }
function openView(view) {
  ui.view = view; ui.open = null; ui.renaming = null; ui.adding = false;
  closePanels();
  render();
}

// ── render ─────────────────────────────────────────────────────────────────────────────
// distCells: the steer pane's DIST cells, refreshed in place on every mapinfo tick (tick() below)
// instead of rebuilding the rows, which would drop focus from a rename being typed.
let distCells = [];

function render() {
  const c = WaypointsStore.load();
  const routes = c.routes || [];
  if (ui.view !== 'stp' && !WptRoute.findRoute(routes, ui.view)) {
    ui.view = c.activeRouteId || (routes[0] && routes[0].id) || 'stp';
    ui.open = null; ui.renaming = null;
  }
  renderRoutes(c);
  renderSteerCard(c);
  if (ui.view === 'stp') renderSteerPane(c);
  else renderRoutePane(c, WptRoute.findRoute(routes, ui.view));
  renderNext();
}

function renderRoutes(c) {
  routesEl.innerHTML = '';
  // Incoming shares first: not routes yet, so ACCEPT/DISMISS is all they offer.
  WaypointsStore.pendingShared().forEach(function (p) {
    const wrap = el('div', 'wpt-cardwrap');
    const card = el('div', 'wpt-card');
    card.appendChild(el('span', 'wpt-lamp'));
    const body = el('span', 'wpt-card-body');
    body.appendChild(el('span', 'wpt-card-name squad', p.name));
    body.appendChild(el('span', 'wpt-card-meta', p.waypointCount + ' PTS'));
    card.appendChild(body);
    const tags = el('span', 'wpt-card-tags');
    tags.appendChild(el('span', 'wpt-tag wpt-tag-squad', 'SHARED'));
    tags.appendChild(el('span', 'wpt-tag-from', p.fromName || 'squad leader'));
    card.appendChild(tags);
    wrap.appendChild(card);
    const acts = el('div', 'wpt-pending-actions');
    acts.appendChild(button('ACCEPT', 'wpt-btn-squad wpt-btn-wide', function () { WaypointsStore.acceptShared(p.id).then(render); }));
    acts.appendChild(button('DISMISS', 'wpt-btn-wide', function () { WaypointsStore.rejectShared(p.id).then(render); }));
    wrap.appendChild(acts);
    routesEl.appendChild(wrap);
  });

  (c.routes || []).forEach(function (route) {
    const active = route.id === c.activeRouteId;
    const viewed = route.id === ui.view;
    const wrap = el('div', 'wpt-cardwrap' + (viewed ? ' open' : ''));
    const card = el('button', 'wpt-card pad-hoverable');
    card.type = 'button';
    card.setAttribute('aria-pressed', viewed ? 'true' : 'false');
    card.onclick = function () { openView(route.id); };
    card.appendChild(el('span', 'wpt-lamp' + (active ? ' on' : '')));
    const body = el('span', 'wpt-card-body');
    body.appendChild(el('span', 'wpt-card-name' + (active ? ' active' : ''), route.name));
    body.appendChild(el('span', 'wpt-card-meta', route.waypoints.length + ' PTS · ' + fmtDist(WptRoute.routeLength(route.waypoints))));
    card.appendChild(body);
    const tags = el('span', 'wpt-card-tags');
    // Shared either way: received from the leader (sharedBy) or this leader's own, auto-resharing.
    if (route.sharedBy || route.sharedWithSquad) {
      tags.appendChild(el('span', 'wpt-tag wpt-tag-squad', 'SHARED'));
      tags.appendChild(el('span', 'wpt-tag-from', route.sharedBy || 'TO SQUAD'));
    } else if (active) tags.appendChild(el('span', 'wpt-tag', 'ACTIVE'));
    card.appendChild(tags);
    card.appendChild(el('span', 'wpt-chev', '›'));
    wrap.appendChild(card);
    routesEl.appendChild(wrap);
  });
  $('wpt-routes-empty').hidden = routesEl.childElementCount > 0;
}

function renderSteerCard(c) {
  const points = c.steerPoints || [];
  // A selected steer point is guiding: RouteStore never keeps one alongside an active route.
  const chosen = WptRoute.findSteerPoint(points, c.activeSteerPointId);
  const pending = WaypointsStore.pendingSharedSteerPoints().length;
  $('wpt-stp-card').setAttribute('aria-pressed', ui.view === 'stp' ? 'true' : 'false');
  $('wpt-stp-lamp').className = 'wpt-lamp' + (chosen ? ' amber' : '');
  $('wpt-stp-meta').textContent = points.length + ' PTS' + (chosen ? ' · STP' + (points.indexOf(chosen) + 1) + ' selected' : '');
  const tag = $('wpt-stp-tag');
  tag.textContent = chosen ? 'GUIDING' : (pending ? pending + ' SHARED' : '');
  tag.className = 'wpt-tag ' + (chosen ? 'wpt-tag-amber' : 'wpt-tag-squad');
}
$('wpt-stp-card').onclick = function () { openView('stp'); };

// The pane head: the title, or the rename field while it's being edited.
function renderPaneHead(title, titleCls, status, statusCls, onRename) {
  const head = $('wpt-pane-head');
  head.innerHTML = '';
  if (ui.renaming === 'route' && onRename) {
    head.appendChild(renameField('wpt-rename', onRename));
    return;
  }
  head.appendChild(el('span', 'wpt-pane-title ' + titleCls, title));
  head.appendChild(el('span', 'wpt-pane-status ' + statusCls, status));
}

// One inline rename field (pane title or a point row). Its text lives in ui.renameText so a
// re-render while typing (another display's edit arriving) rebuilds it with the text intact.
function renameField(cls, onSave) {
  const wrap = el('div', cls);
  const input = el('input');
  input.type = 'text'; input.maxLength = 40; input.value = ui.renameText;
  input.oninput = function () { ui.renameText = input.value; };
  const done = function () { ui.renaming = null; render(); };
  const commit = function () { Promise.resolve(onSave(input.value.trim())).then(done); };
  input.onkeydown = function (e) { if (e.key === 'Enter') commit(); else if (e.key === 'Escape') done(); };
  wrap.appendChild(input);
  wrap.appendChild(button('SAVE', 'wpt-btn-go', commit));
  wrap.appendChild(button('CANCEL', '', done));
  setTimeout(function () { input.focus(); }, 0);
  return wrap;
}
function startRename(which, text) { ui.renaming = which; ui.renameText = text || ''; render(); }

function renderRoutePane(c, route) {
  const active = route.id === c.activeRouteId;
  const received = !!route.sharedBy;   // content is read-only on a route someone shared with you
  const n = route.waypoints.length, next = route.nextIndex;
  renderPaneHead(route.name, active ? 'active' : '',
    received ? (active ? 'ACTIVE · ' : '') + 'SHARED BY ' + route.sharedBy : (active ? 'ACTIVE' : 'NOT ACTIVE'),
    received ? 'squad' : (active ? 'amber' : ''),
    received ? null : function (name) { return name ? WaypointsStore.renameRoute(route.id, name) : undefined; });
  $('wpt-pane-meta').textContent = n + ' POINTS · ' + fmtDist(WptRoute.routeLength(route.waypoints)) + ' TOTAL' +
    (active && n ? (next < n ? ' · ' + (n - next) + ' TO GO' : ' · COMPLETE') : '');

  const acts = $('wpt-pane-actions');
  acts.innerHTML = '';
  acts.classList.add('equal');
  if (active) acts.appendChild(button('DEACTIVATE', 'wpt-btn-amber', function () { WaypointsStore.setActiveRoute(null).then(render); }));
  else acts.appendChild(button('ACTIVATE', 'wpt-btn-go', function () { WaypointsStore.setActiveRoute(route.id).then(render); }));
  if (!received) acts.appendChild(button('RENAME', '', function () { startRename('route', route.name); }));
  acts.appendChild(button('RESET', '', function () { WaypointsStore.resetRoute(route.id).then(render); }, 'Reset progress: every waypoint not reached'));
  acts.appendChild(button('EXPORT', '', function () { openExport(route.id); }));
  if (!received && canShare()) acts.appendChild(shareButton(function () { return WaypointsStore.shareRoute(route.id); }));
  acts.appendChild(button(received ? 'REMOVE' : 'DELETE', 'wpt-btn-del', function () {
    ui.view = null; WaypointsStore.deleteRoute(route.id).then(render);
  }, received ? 'Remove from your routes' : 'Delete route'));

  $('wpt-grid-row').hidden = true;
  $('wpt-lastcol').textContent = 'LEG';
  distCells = [];
  rowsEl.innerHTML = '';
  const legs = WptRoute.legLengths(route.waypoints);
  route.waypoints.forEach(function (wp, i) {
    const passed = active && i < next, isNext = active && i === next, open = ui.open === i;
    const item = el('div', 'wpt-point' + (passed ? ' passed' : '') + (isNext ? ' next' : '') + (open ? ' open' : ''));
    const row = el('button', 'wpt-row pad-hoverable');
    row.type = 'button';
    row.setAttribute('aria-expanded', open ? 'true' : 'false');
    row.onclick = function () { ui.open = open ? null : i; ui.renaming = null; render(); };
    row.appendChild(el('span', 'wpt-lampcol'));
    row.appendChild(el('span', 'wpt-numcol', (i + 1) + '.'));
    row.appendChild(el('span', 'wpt-namecol', wp.name));
    if (isNext) row.appendChild(el('span', 'wpt-tag', 'NEXT'));
    row.appendChild(el('span', 'wpt-gridcol', gridOf(wp)));
    row.appendChild(el('span', 'wpt-distcol', i === 0 ? '—' : fmtDist(legs[i])));
    item.appendChild(row);
    if (open && ui.renaming === i) {
      item.appendChild(renameField('wpt-row-rename', function (name) { return WaypointsStore.renameWaypoint(i, name, route.id); }));
    } else if (open) {
      const pa = el('div', 'wpt-point-actions');
      if (!received) pa.appendChild(button('RENAME', '', function () { startRename(i, wp.name); }));
      // Flying from a point means flying this route: activate it first if it isn't already.
      pa.appendChild(button('FLY FROM HERE', 'wpt-btn-amber', function () {
        (active ? Promise.resolve() : WaypointsStore.setActiveRoute(route.id))
          .then(function () { return WaypointsStore.resetWaypoint(i); }).then(render);
      }));
      if (!received) {
        const up = button('▲', '', function () { ui.open = i - 1; WaypointsStore.reorderWaypoint(i, i - 1, route.id).then(render); }, 'Move point ' + (i + 1) + ' up');
        up.disabled = i === 0;
        const down = button('▼', '', function () { ui.open = i + 1; WaypointsStore.reorderWaypoint(i, i + 1, route.id).then(render); }, 'Move point ' + (i + 1) + ' down');
        down.disabled = i === n - 1;
        pa.appendChild(up); pa.appendChild(down);
        pa.appendChild(button('DELETE', 'wpt-btn-del', function () { ui.open = null; WaypointsStore.removeWaypoint(i, route.id).then(render); }, 'Delete point ' + (i + 1)));
      }
      item.appendChild(pa);
    }
    rowsEl.appendChild(item);
  });
  const empty = $('wpt-rows-empty');
  empty.hidden = n > 0;
  empty.textContent = active ? 'long-press MAP to add a waypoint' : 'activate the route, then long-press MAP to add waypoints';
}

function renderSteerPane(c) {
  const points = c.steerPoints || [];
  const routeOn = !!WptRoute.findRoute(c.routes || [], c.activeRouteId);
  const chosenId = c.activeSteerPointId;
  const guiding = !!WptRoute.findSteerPoint(points, chosenId);
  renderPaneHead('STEER POINTS', 'stp', guiding ? 'GUIDING' : (routeOn ? 'ROUTE ACTIVE' : ''), guiding ? 'amber' : '', null);
  // A route and a steer point never guide together (RouteStore), so GUIDE TO ends the route.
  $('wpt-pane-meta').textContent = points.length + ' POINTS' + (routeOn ? ' · GUIDE TO ends the active route' : '');

  const acts = $('wpt-pane-actions');
  acts.innerHTML = '';
  acts.classList.remove('equal');
  if (!ui.adding) acts.appendChild(button('+ NEW STEER POINT', 'wpt-btn-go', function () {
    ui.adding = true; $('wpt-grid-input').value = ''; render(); $('wpt-grid-input').focus();
  }));
  $('wpt-grid-row').hidden = !ui.adding;
  updateGridHint();

  $('wpt-lastcol').textContent = 'DIST';
  distCells = [];
  rowsEl.innerHTML = '';
  WaypointsStore.pendingSharedSteerPoints().forEach(function (p) {
    const item = el('div', 'wpt-point pending');
    const row = el('div', 'wpt-row');
    row.appendChild(el('span', 'wpt-lampcol'));
    row.appendChild(el('span', 'wpt-numcol', '—'));
    row.appendChild(el('span', 'wpt-namecol', p.name || 'STEER POINT'));
    row.appendChild(el('span', 'wpt-tag wpt-tag-squad', 'FROM ' + (p.fromName || 'squad leader').toUpperCase()));
    item.appendChild(row);
    const pa = el('div', 'wpt-point-actions');
    pa.appendChild(button('ACCEPT', 'wpt-btn-squad', function () { WaypointsStore.acceptSharedSteerPoint(p.id).then(render); }));
    pa.appendChild(button('DISMISS', '', function () { WaypointsStore.rejectSharedSteerPoint(p.id).then(render); }));
    item.appendChild(pa);
    rowsEl.appendChild(item);
  });
  points.forEach(function (p, i) {
    const chosen = p.id === chosenId, open = ui.open === i;
    const received = !!p.sharedBy;
    const item = el('div', 'wpt-point stp' + (chosen ? ' chosen' : '') + (open ? ' open' : ''));
    const row = el('button', 'wpt-row pad-hoverable');
    row.type = 'button';
    row.setAttribute('aria-expanded', open ? 'true' : 'false');
    row.onclick = function () { ui.open = open ? null : i; ui.renaming = null; render(); };
    row.appendChild(el('span', 'wpt-lampcol'));
    row.appendChild(el('span', 'wpt-numcol', (i + 1) + '.'));
    row.appendChild(el('span', 'wpt-namecol', p.name));
    // The guiding point is marked by its lit lamp and amber text (and the pane's GUIDING status);
    // a tag here would squeeze the name out of a narrow row.
    if (received || p.sharedWithSquad) row.appendChild(el('span', 'wpt-tag wpt-tag-squad', 'SQD'));
    row.appendChild(el('span', 'wpt-gridcol', gridOf(p)));
    const dist = el('span', 'wpt-distcol', fmtDist(ownDist(p)));
    distCells.push({ cell: dist, point: p });
    row.appendChild(dist);
    item.appendChild(row);
    if (open && ui.renaming === i) {
      item.appendChild(renameField('wpt-row-rename', function (name) { return WaypointsStore.renameSteerPoint(p.id, name); }));
    } else if (open) {
      const pa = el('div', 'wpt-point-actions');
      if (!chosen) pa.appendChild(button('GUIDE TO', 'wpt-btn-amber', function () { WaypointsStore.setActiveSteerPoint(p.id).then(render); }));
      if (!received) pa.appendChild(button('RENAME', '', function () { startRename(i, p.name); }));
      if (!received && canShare()) pa.appendChild(shareButton(function () { return WaypointsStore.shareSteerPoint(p.id); }));
      pa.appendChild(button(received ? 'REMOVE' : 'DELETE', 'wpt-btn-del', function () { ui.open = null; WaypointsStore.deleteSteerPoint(p.id).then(render); },
        received ? 'Remove from your steer points' : 'Delete steer point'));
      item.appendChild(pa);
    }
    rowsEl.appendChild(item);
  });
  const empty = $('wpt-rows-empty');
  empty.hidden = rowsEl.childElementCount > 0;
  empty.textContent = 'no steer points · + NEW STEER POINT, or long-press MAP with no route active';
}

// Disabled while a send is in flight (and briefly after) so mashing it can't fire a burst of share
// commands. Only the first share needs it: RouteStore re-shares the item on later edits by itself.
function shareButton(send) {
  const b = button('SHARE', 'wpt-btn-squad', function () {
    if (b.disabled) return;
    b.disabled = true;
    send().then(function () {
      b.textContent = 'SHARED';
      setTimeout(function () { b.textContent = 'SHARE'; b.disabled = false; }, 1200);
    }).catch(function () { b.disabled = false; });
  }, 'Share with the squad');
  return b;
}

// ── next-point strip ───────────────────────────────────────────────────────────────────
function renderNext() {
  const c = WaypointsStore.load();
  const route = WptRoute.findRoute(c.routes || [], c.activeRouteId);
  const target = WptRoute.navigationTarget(c);
  const needle = $('wpt-needle');
  const set = function (kind, title, grid, brg, dist, left) {
    $('wpt-next-kind').textContent = kind; $('wpt-next-title').textContent = title; $('wpt-next-grid').textContent = grid;
    $('wpt-next-brg').textContent = brg; $('wpt-next-dist').textContent = dist; $('wpt-next-left').textContent = left;
  };
  if (!target) {
    needle.style.display = 'none';
    set(route ? 'ROUTE' : '', route ? 'COMPLETE' : 'NO NAVIGATION POINT', '', '—', '—', '—');
    return;
  }
  const p = target.point, isWpt = target.kind === 'waypoint';
  const title = (isWpt ? 'WPT ' : 'STP') + (target.index + 1) + (p.name ? ' · ' + p.name : '');
  const grid = gridOf(p).toUpperCase();
  if (mapinfo.x == null) { needle.style.display = 'none'; set(isWpt ? 'NEXT' : 'STEER', title, grid, '—', '—', '—'); return; }
  const db = WptRoute.distanceBearing(mapinfo.x, mapinfo.z, p.x, p.z);
  set(isWpt ? 'NEXT' : 'STEER', title, grid, WptRoute.formatBearing(db.brgDeg), fmtDist(db.distM),
    isWpt ? fmtDist(WptRoute.remainingDistance(route, mapinfo.x, mapinfo.z)) : '—');
  if (typeof mapinfo.hdg === 'number') {
    needle.style.display = '';
    needle.setAttribute('transform', 'rotate(' + WptRoute.relativeBearing(db.brgDeg, mapinfo.hdg) + ' 50 50)');
  } else needle.style.display = 'none';
}

// ── typed-in steer point ───────────────────────────────────────────────────────────────
function mapMeta() { return mapinfo.ox == null || mapinfo.w == null ? null : { ox: mapinfo.ox, oy: mapinfo.oy, w: mapinfo.w, h: mapinfo.h }; }
function parsedGrid() {
  const text = $('wpt-grid-input').value.trim();
  if (!text) return { state: 'empty' };
  if (!/^[a-z]{2}\d{2}$/i.test(text)) return { state: 'bad', why: 'not a grid' };
  const meta = mapMeta();
  if (!meta) return { state: 'bad', why: 'needs a mission' };
  const at = gridToWorld(text, meta);
  if (!at) return { state: 'bad', why: 'off the map' };
  return { state: 'ok', at: at, label: text[0].toUpperCase() + text[1].toLowerCase() + text.slice(2) };
}
function updateGridHint() {
  const g = parsedGrid(), hint = $('wpt-grid-hint'), input = $('wpt-grid-input');
  const count = (WaypointsStore.load().steerPoints || []).length;
  hint.textContent = g.state === 'empty' ? 'two letters, two digits' : g.state === 'ok' ? 'adds STP' + (count + 1) + ' at ' + g.label.toUpperCase() : g.why;
  hint.className = 'wpt-entry-hint' + (g.state === 'ok' ? ' ok' : g.state === 'bad' ? ' bad' : '');
  input.classList.toggle('bad', g.state === 'bad');
  $('wpt-grid-add').disabled = g.state !== 'ok';
}
function addGridPoint() {
  const g = parsedGrid();
  if (g.state !== 'ok') return;
  ui.adding = false;
  WaypointsStore.addSteerPoint(g.at.x, g.at.z, '').then(render);
}
function cancelGrid() { ui.adding = false; render(); }
$('wpt-grid-input').oninput = updateGridHint;
$('wpt-grid-input').onkeydown = function (e) { if (e.key === 'Enter') addGridPoint(); else if (e.key === 'Escape') cancelGrid(); };
$('wpt-grid-add').onclick = addGridPoint;
$('wpt-grid-cancel').onclick = cancelGrid;

// ── new route / clear ──────────────────────────────────────────────────────────────────
const newName = $('wpt-new-name');
$('wpt-new-route').onclick = function () {
  closePanels();
  $('wpt-new-row').hidden = false;
  newName.value = WaypointsStore.freshRouteName();   // pre-filled, editable
  newName.focus(); newName.select();
};
$('wpt-new-cancel').onclick = function () { $('wpt-new-row').hidden = true; };
$('wpt-new-confirm').onclick = function () {
  const name = newName.value.trim();
  $('wpt-new-row').hidden = true;
  ui.view = null;   // the plugin activates a new route; show it once it's polled back
  WaypointsStore.createRoute(name || null).then(render);
};
newName.onkeydown = function (e) { if (e.key === 'Enter') $('wpt-new-confirm').click(); else if (e.key === 'Escape') $('wpt-new-cancel').click(); };
// CLEAR drops every route at once, no confirmation, same as a route's own DELETE.
$('wpt-clear-routes').onclick = function () { closePanels(); ui.view = null; WaypointsStore.clearRoutes().then(render); };

// ── import / export (one panel, two modes) ─────────────────────────────────────────────
const io = { panel: $('wpt-io'), label: $('wpt-io-label'), text: $('wpt-io-text'), error: $('wpt-io-error'),
             primary: $('wpt-io-primary'), copy: $('wpt-io-copy') };
$('wpt-import-route').onclick = function () {
  $('wpt-new-row').hidden = true;
  io.label.textContent = 'IMPORT ROUTE — paste an exported route\'s JSON';
  io.text.value = ''; io.text.readOnly = false; io.error.textContent = '';
  io.primary.hidden = false; io.copy.hidden = true; io.panel.hidden = false;
  io.text.focus();
};
io.primary.onclick = function () {
  // Pre-validated here for an instant error: POST /command can't report a bad paste back.
  // RouteStore.ImportRoute re-parses server-side as the real source of truth.
  if (!WptRoute.parseRouteJSON(io.text.value)) { io.error.textContent = 'Could not read that as a route — check the pasted JSON.'; return; }
  io.panel.hidden = true;
  ui.view = null;
  WaypointsStore.importRoute(io.text.value).then(render);
};
function openExport(id) {
  const json = WaypointsStore.exportRoute(id);
  if (!json) return;   // deleted between the click and here
  $('wpt-new-row').hidden = true;
  io.label.textContent = 'EXPORT ROUTE — copy this and send it to share the route';
  io.text.value = json; io.text.readOnly = true; io.error.textContent = '';
  io.primary.hidden = true; io.copy.hidden = false; io.copy.textContent = 'COPY'; io.panel.hidden = false;
  io.text.focus(); io.text.select();
}
io.copy.onclick = function () {
  io.text.focus(); io.text.select();
  // navigator.clipboard needs a secure context; plain http:// over the LAN (how this mod is
  // reached) doesn't have one, so fall back to execCommand on the selection made above.
  const done = function () { io.copy.textContent = 'COPIED'; setTimeout(function () { io.copy.textContent = 'COPY'; }, 1200); };
  if (navigator.clipboard && navigator.clipboard.writeText) {
    navigator.clipboard.writeText(io.text.value).then(done, function () { try { document.execCommand('copy'); done(); } catch (e) {} });
  } else { try { document.execCommand('copy'); done(); } catch (e) {} }
};
$('wpt-io-close').onclick = function () { io.panel.hidden = true; };

// ── live ticks ─────────────────────────────────────────────────────────────────────────
// Grid labels only change when the map offsets do (the first mapinfo after load, or a new map), so
// that re-renders; every other tick just refreshes the strip and the DIST cells.
let gridMetaKey = mapinfo.ox + ',' + mapinfo.oy;
function tick() {
  const key = mapinfo.ox + ',' + mapinfo.oy;
  if (key !== gridMetaKey) { gridMetaKey = key; render(); return; }
  renderNext();
  distCells.forEach(function (d) { d.cell.textContent = fmtDist(ownDist(d.point)); });
  if (ui.adding) updateGridHint();
}

// ── PAD cursor (docs/page-cursor.md) ───────────────────────────────────────────────────
// Every control is a real button, so Select is a synthetic click at the crosshair, same as HUD.
const CURSORABLE = '.pad-hoverable';
const cursor = createPadCursor({
  el: $('pad-cursor'),
  clampRect: () => ({ dx: 0, dy: 0, dw: window.innerWidth, dh: window.innerHeight }),
  onSelect: function (x, y) { const raw = document.elementFromPoint(x, y); const t = raw && raw.closest(CURSORABLE); if (t) t.click(); },
  onMove: padCursorMoveAt,
});
// Hover feedback: the shared .pad-hoverable/.pad-hover pair (theme.css). A row rebuilt under it just
// fails the identity check and is replaced on the next move.
let hoveredEl = null;
function padCursorMoveAt(x, y) {
  const raw = x == null ? null : document.elementFromPoint(x, y);
  const t = raw && raw.closest(CURSORABLE);
  if (t === hoveredEl) return;
  if (hoveredEl) hoveredEl.classList.remove('pad-hover');
  hoveredEl = t;
  if (hoveredEl) hoveredEl.classList.add('pad-hover');
}

// Zoom In/Out scroll the list (nothing here zooms), same as TGT/HUD.
const SCROLL_STEP = 60;

window.addEventListener('message', function (e) {
  const m = e.data;
  if (!m || m.mfd !== true) return;
  if (m.type === 'mapinfo') { mapinfo = m; tick(); return; }
  if (m.type === 'sqd-state') { applySquad(m.data); return; }
  if (m.action === 'cursor-focus') cursor.setFocus(!!m.on, window.innerWidth / 2, window.innerHeight / 2);
  else if (m.action === 'cursor') cursor.setVector(m.x, m.y);
  else if (m.action === 'cursor-select') cursor.select();
  else if (m.action === 'zoom-in') mainEl.scrollBy({ top: SCROLL_STEP });
  else if (m.action === 'zoom-out') mainEl.scrollBy({ top: -SCROLL_STEP });
  // Route and navigation physical keybinds use the same actions as MAP; RouteStore decides what
  // the navigation pair steps.
  else if (m.action === 'route-next')    { WaypointsStore.cycleActiveRoute(1).then(render); }
  else if (m.action === 'route-prev')    { WaypointsStore.cycleActiveRoute(-1).then(render); }
  else if (m.action === 'waypoint-next') { WaypointsStore.stepNavigation(1).then(render); }
  else if (m.action === 'waypoint-prev') { WaypointsStore.stepNavigation(-1).then(render); }
});

// Any display, device or MAP changed navigation data: the plugin is the source of truth, and
// waypoints-store.js fires this once its cache picks the change up.
window.addEventListener('wptroutes:changed', render);

// ── squad (docs/squadron-transport.md) ─────────────────────────────────────────────────
// Only needed to know whether SHARE can show: squad leader with at least one member. Rides the
// shell's relayed 'sqd-state' push; one bootstrap GET /squad covers the gap before the first push
// and standalone/preview contexts with no shell.
function applySquad(s) {
  if (!s || !s.state) return;
  sqd.role = s.state.role || 'none';
  sqd.members = Array.isArray(s.state.members) ? s.state.members : [];
  render();
}
fetch('/squad').then(r => r.ok ? r.json() : null).then(applySquad)
  .catch(function () { /* standalone/preview without the plugin: SHARE stays hidden */ });

render();
