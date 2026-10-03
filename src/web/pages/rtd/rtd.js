// RTD / SPD page (docs/wpt-rework.md) — route and steer-point details. DOM-coupled, not unit-tested;
// the math it draws (legs, distance left, the timeline's rows, the turn cue) is wpt-route.js's,
// which is. /rtd shows the active route, /rtd?spd the steer points. WptRoute/WaypointsStore are
// classic <script> globals (rtd.html), loaded before this module.
import { gridLabel } from '/assets/services/telemetry-source.js';
import { createPadCursor } from '/assets/services/pad-cursor.js';

const SPD = new URLSearchParams(location.search).has('spd');
const PER_ROW = 6;   // timeline points per row before it wraps (design "WPT B1")
const TAPE_SPAN = 120;   // degrees across the heading tape

if (window.parent !== window) {
  const back = document.querySelector('.rtd-back');
  if (back) back.remove();
}

const $ = (id) => document.getElementById(id);
const bodyEl = document.querySelector('.rtd-body');
let mapinfo = { x: null, z: null, hdg: null, ox: null, oy: null, metric: false };

function el(tag, cls, text) {
  const e = document.createElement(tag);
  if (cls) e.className = cls;
  if (text != null) e.textContent = text;
  return e;
}
function distParts(m) { return WptRoute.distanceParts(m, mapinfo.metric); }
function fmtDist(m) { return WptRoute.formatDistance(m, mapinfo.metric); }
const fmtDeg = WptRoute.formatBearing;
function gridOf(p) { return mapinfo.ox == null ? '—' : gridLabel(p.x, p.z, { ox: mapinfo.ox, oy: mapinfo.oy }); }

// ── what this page is showing ──────────────────────────────────────────────────────────
// The point being guided to (or null) and the label set that goes with it.
function subject(c) {
  const route = WptRoute.findRoute(c.routes || [], c.activeRouteId);
  if (!SPD) {
    if (!route) return { route: null };
    const n = route.waypoints.length, next = route.nextIndex;
    const p = next < n ? route.waypoints[next] : null;
    return { route, point: p, kind: p ? 'NEXT WAYPOINT' : 'ROUTE', big: p ? 'WPT ' + (next + 1) : 'COMPLETE' };
  }
  const points = c.steerPoints || [];
  const p = WptRoute.findSteerPoint(points, c.activeSteerPointId);
  return { route, point: p, kind: 'STEER POINT', big: p ? 'STP' + (points.indexOf(p) + 1) : '—' };
}

// ── render ─────────────────────────────────────────────────────────────────────────────
// Rebuilt on route data changes; a mapinfo tick only refreshes the live parts (tick()).
function render() {
  const c = WaypointsStore.load();
  const s = subject(c);
  const points = c.steerPoints || [];
  if (SPD) {
    const i = s.point ? points.indexOf(s.point) : -1;
    $('rtd-title').textContent = 'STEERPOINTS';
    // None is selected while a route guides (RouteStore keeps the two exclusive).
    $('rtd-hint').textContent = i >= 0 ? 'STP ' + (i + 1) + ' OF ' + points.length
      : s.route ? 'ROUTE ACTIVE · tap a point to guide to it' : points.length + ' STEER POINTS';
  } else {
    $('rtd-title').textContent = s.route ? 'ROUTE ' + s.route.name : 'NO ACTIVE ROUTE';
    $('rtd-hint').textContent = s.route && s.point ? 'WPT ' + (s.route.nextIndex + 1) + ' OF ' + s.route.waypoints.length : '';
  }
  $('rtd-strip').hidden = SPD || !s.route || !s.route.waypoints.length;
  $('rtd-cards').hidden = SPD || !s.route || !s.route.waypoints.length;
  $('rtd-stps').hidden = !SPD || !points.length;
  const empty = $('rtd-empty');
  empty.hidden = SPD ? points.length > 0 : !!(s.route && s.route.waypoints.length);
  empty.textContent = SPD ? 'no steer points · add them on WPT, or long-press MAP with no route active'
    : s.route ? 'no waypoints · long-press MAP to add one' : 'activate a route on WPT to fly it here';
  if (SPD) renderSteerButtons(c, s); else if (s.route) { renderStrip(s.route); renderCards(s.route); }
  tick();
}

// The live parts: tape, readout, the YOU marker and the distance-left card.
function tick() {
  const c = WaypointsStore.load();
  const s = subject(c);
  const p = s.point;
  const db = p && mapinfo.x != null ? WptRoute.distanceBearing(mapinfo.x, mapinfo.z, p.x, p.z) : null;
  $('rtd-kind').textContent = s.route || SPD ? s.kind : 'NEXT WAYPOINT';
  $('rtd-point').textContent = s.big || '—';
  $('rtd-name').textContent = p ? (p.name || '—') : '';
  $('rtd-grid').textContent = p ? gridOf(p).toUpperCase() : '';
  const dp = distParts(db ? db.distM : null);
  $('rtd-dist').textContent = dp[0]; $('rtd-unit').textContent = dp[1];
  $('rtd-brg').textContent = db ? fmtDeg(db.brgDeg) : '—';
  const cue = $('rtd-cue');
  if (db && typeof mapinfo.hdg === 'number') {
    const rel = WptRoute.signedTurn(db.brgDeg, mapinfo.hdg), ar = Math.abs(Math.round(rel));
    cue.textContent = ar < 4 ? '● ON COURSE' : rel < 0 ? '◀ TURN LEFT ' + ar + '°' : 'TURN RIGHT ' + ar + '° ▶';
    cue.classList.toggle('on', ar < 4);
  } else { cue.textContent = ''; }
  renderTape(db ? db.brgDeg : null);
  if (!SPD && s.route) { placeYou(s.route); updateRouteLeft(s.route); }
}

// ── heading tape ───────────────────────────────────────────────────────────────────────
// Built once: a scale four tape-widths long (-60° to 420°, so any heading's ±60° window is on it)
// that each tick only slides, plus a bug and an edge arrow that move or swap text. Rebuilding the
// ticks on every 10 Hz mapinfo tick would churn the DOM for nothing.
const SCALE_FROM = -TAPE_SPAN / 2, SCALE_TO = 360 + TAPE_SPAN / 2;
const tapeParts = {};
function buildTape() {
  const tape = $('rtd-tape');
  const scale = el('div', 'rtd-tape-scale');
  const at = (a) => ((a - SCALE_FROM) / (SCALE_TO - SCALE_FROM) * 100) + '%';
  for (let a = SCALE_FROM; a <= SCALE_TO; a += 5) {
    const t = el('div', 'rtd-tick' + (a % 10 === 0 ? ' big' : ''));
    t.style.left = at(a);
    scale.appendChild(t);
    const n = ((a % 360) + 360) % 360;
    if (n % 30 === 0) {
      const l = el('span', 'rtd-tick-label', { 0: 'N', 90: 'E', 180: 'S', 270: 'W' }[n] || ('0' + n / 10).slice(-2));
      l.style.left = at(a);
      scale.appendChild(l);
    }
  }
  tape.appendChild(scale);
  tape.appendChild(el('div', 'rtd-lubber'));
  const bug = document.createElementNS('http://www.w3.org/2000/svg', 'svg');
  bug.setAttribute('class', 'rtd-bug');
  bug.setAttribute('viewBox', '0 0 18 20');
  bug.innerHTML = '<path d="M 0 0 L 18 0 L 18 10 L 9 20 L 0 10 Z"/>';
  tape.appendChild(bug);
  Object.assign(tapeParts, { scale, bug, hdg: el('span', 'rtd-hdg'), edge: el('span', 'rtd-edge') });
  tape.appendChild(tapeParts.hdg);
  tape.appendChild(tapeParts.edge);
}
function renderTape(brg) {
  if (!tapeParts.scale) buildTape();
  const hdg = mapinfo.hdg, has = typeof hdg === 'number';
  tapeParts.scale.hidden = tapeParts.hdg.hidden = !has;
  tapeParts.bug.style.display = 'none';
  tapeParts.edge.hidden = true;
  if (!has) return;
  const h = ((hdg % 360) + 360) % 360;
  // Put heading h at the tape's centre: the scale is 4 tape-widths, translate in its own percent.
  tapeParts.scale.style.transform = 'translateX(' + ((0.5 - (h - SCALE_FROM) / TAPE_SPAN) * 25) + '%)';
  tapeParts.hdg.textContent = fmtDeg(h);
  if (brg == null) return;
  const rel = WptRoute.signedTurn(brg, h);
  if (Math.abs(rel) <= TAPE_SPAN / 2 - 3) {
    tapeParts.bug.style.display = '';
    tapeParts.bug.style.left = (50 + rel / TAPE_SPAN * 100) + '%';
  } else {
    tapeParts.edge.hidden = false;
    tapeParts.edge.className = 'rtd-edge ' + (rel < 0 ? 'left' : 'right');
    tapeParts.edge.textContent = rel < 0 ? '◀ ' + fmtDeg(brg) : fmtDeg(brg) + ' ▶';
  }
}

// ── RTD timeline ───────────────────────────────────────────────────────────────────────
// Placed in calc() of the strip's width so a row reflows with the pane: point centres sit 4fs in
// from each edge and share the rest evenly; a row is 7fs tall.
const FS = 'var(--fs)';
const SPAN = '(100% - ' + FS + ' * 8)';
function cx(col, dx) { return 'calc(' + FS + ' * ' + (4 + dx) + ' + ' + SPAN + ' * ' + (col / (PER_ROW - 1)) + ')'; }
function cy(row, dy) { return 'calc(' + FS + ' * ' + (row * 7 + dy) + ')'; }
function place(e, left, top, width) { e.style.left = left; e.style.top = top; if (width) e.style.width = width; return e; }

let youEl = null;
function renderStrip(route) {
  const strip = $('rtd-strip');
  strip.innerHTML = '';
  const n = route.waypoints.length, next = route.nextIndex;
  const t = WptRoute.timeline(n, PER_ROW);
  const legs = WptRoute.legLengths(route.waypoints);
  strip.style.height = cy(t.rows, 1.375);
  t.segments.forEach(function (s) {
    // The leg INTO point s.index: flown once that point is passed, current while it's next.
    const st = s.index < next ? 'passed' : s.index === next ? 'cur' : '';
    let left, width;
    if (s.to === 'edge') { left = cx(s.from, 1.0625); width = 'calc(100% - ' + cx(s.from, 1.0625).slice(5); }
    else if (s.from === 'edge') { left = '0px'; width = cx(s.to, -1.0625); }
    else { left = cx(s.from, 1.0625); width = 'calc(' + SPAN + ' * ' + ((s.to - s.from) / (PER_ROW - 1)) + ' - ' + FS + ' * 2.125)'; }
    strip.appendChild(place(el('div', 'rtd-seg ' + st), left, cy(s.row, 3.875), width));
    if (s.label) strip.appendChild(place(el('span', 'rtd-leg ' + st, distParts(legs[s.index])[0]), left, cy(s.row, 2.25), width));
  });
  t.nodes.forEach(function (nd) {
    const i = nd.index, wp = route.waypoints[i];
    const st = i < next ? 'passed' : i === next ? 'next' : '';
    const b = el('button', 'rtd-node pad-hoverable ' + st);
    b.type = 'button';
    b.appendChild(el('span', '', String(i + 1)));
    b.setAttribute('aria-label', 'Fly direct to waypoint ' + (i + 1));
    b.onclick = function () { WaypointsStore.resetWaypoint(i).then(render); };
    strip.appendChild(place(b, cx(nd.col, -1), cy(nd.row, 3)));
    strip.appendChild(place(el('span', 'rtd-nlabel ' + st, wp.name || 'WPT ' + (i + 1)), cx(nd.col, -3), cy(nd.row, 5.5), 'calc(' + FS + ' * 6)'));
    strip.appendChild(place(el('span', 'rtd-nlabel grid ' + st, gridOf(wp).toUpperCase()), cx(nd.col, -3), cy(nd.row, 6.625), 'calc(' + FS + ' * 6)'));
  });
  youEl = el('span', 'rtd-you', '▼ YOU');
  strip.appendChild(youEl);
  placeYou(route);
}

// The aircraft's place along the leg into the next point: how much of that leg is already flown,
// kept off both ends so the marker never sits on a point.
function placeYou(route) {
  if (!youEl) return;
  const next = route.nextIndex, n = route.waypoints.length;
  if (next <= 0 || next >= n || mapinfo.x == null) { youEl.hidden = true; return; }
  const legs = WptRoute.legLengths(route.waypoints);
  const p = route.waypoints[next];
  const f = Math.max(0.15, Math.min(0.85, 1 - Math.hypot(p.x - mapinfo.x, p.z - mapinfo.z) / Math.max(legs[next], 1)));
  const row = Math.floor(next / PER_ROW), col = next % PER_ROW;
  const from = col ? cx(col - 1, 0) : '0px';
  youEl.hidden = false;
  place(youEl, 'max(' + FS + ' * 0.25, calc(' + from + ' + (' + cx(col, 0) + ' - ' + from + ') * ' + f + ' - ' + FS + ' * 1.375))', cy(row, 0.625));
}

// THEN / AFTER / ROUTE LEFT.
let leftCard = null;
function renderCards(route) {
  const cards = $('rtd-cards');
  cards.innerHTML = '';
  const n = route.waypoints.length, next = route.nextIndex;
  const legs = WptRoute.legLengths(route.waypoints);
  const after = function (k, i) {
    const card = el('div', 'rtd-card');
    card.appendChild(el('span', 'rtd-k', k));
    if (i < n) {
      const w = route.waypoints[i];
      card.appendChild(el('span', 'rtd-card-t', 'WPT ' + (i + 1) + (w.name ? ' ' + w.name : '')));
      card.appendChild(el('span', 'rtd-card-s', gridOf(w).toUpperCase() + ' · +' + fmtDist(legs[i])));
    } else {
      card.appendChild(el('span', 'rtd-card-t dim', i === n ? 'END OF ROUTE' : '—'));
      card.appendChild(el('span', 'rtd-card-s', '—'));
    }
    return card;
  };
  cards.appendChild(after('THEN', next + 1));
  cards.appendChild(after('AFTER', next + 2));
  leftCard = el('div', 'rtd-card');
  leftCard.appendChild(el('span', 'rtd-k', 'ROUTE LEFT'));
  leftCard.appendChild(el('span', 'rtd-card-t white', '—'));
  leftCard.appendChild(el('span', 'rtd-card-s', Math.max(0, n - next) + ' points to go'));
  cards.appendChild(leftCard);
  updateRouteLeft(route);
}
function updateRouteLeft(route) {
  if (!leftCard) return;
  const done = route.nextIndex >= route.waypoints.length;
  leftCard.children[1].textContent = done ? fmtDist(0) : fmtDist(WptRoute.remainingDistance(route, mapinfo.x, mapinfo.z));
}

// ── SPD buttons ────────────────────────────────────────────────────────────────────────
function renderSteerButtons(c, s) {
  const box = $('rtd-stps');
  box.innerHTML = '';
  (c.steerPoints || []).forEach(function (p, i) {
    const chosen = p === s.point;
    const b = el('button', 'rtd-stp pad-hoverable');
    b.type = 'button';
    b.setAttribute('aria-pressed', chosen ? 'true' : 'false');
    b.onclick = function () { WaypointsStore.setActiveSteerPoint(p.id).then(render); };
    b.appendChild(el('span', 'rtd-stp-lamp'));
    b.appendChild(el('span', 'rtd-stp-id', 'STP' + (i + 1)));
    b.appendChild(el('span', 'rtd-stp-name', p.name || '—'));
    b.appendChild(el('span', 'rtd-stp-grid', gridOf(p)));
    box.appendChild(b);
  });
}

// ── PAD cursor, keybinds, data ─────────────────────────────────────────────────────────
const CURSORABLE = '.pad-hoverable';
let hoveredEl = null;
const cursor = createPadCursor({
  el: $('pad-cursor'),
  clampRect: () => ({ dx: 0, dy: 0, dw: window.innerWidth, dh: window.innerHeight }),
  onSelect: function (x, y) { const raw = document.elementFromPoint(x, y); const t = raw && raw.closest(CURSORABLE); if (t) t.click(); },
  onMove: function (x, y) {
    const raw = x == null ? null : document.elementFromPoint(x, y);
    const t = raw && raw.closest(CURSORABLE);
    if (t === hoveredEl) return;
    if (hoveredEl) hoveredEl.classList.remove('pad-hover');
    hoveredEl = t;
    if (hoveredEl) hoveredEl.classList.add('pad-hover');
  },
});
const SCROLL_STEP = 60;

// Grid labels only change with the map offsets (first mapinfo after load, or a new map).
let gridMetaKey = null;
window.addEventListener('message', function (e) {
  const m = e.data;
  if (!m || m.mfd !== true) return;
  if (m.type === 'mapinfo') {
    mapinfo = m;
    const key = m.ox + ',' + m.oy;
    if (key !== gridMetaKey) { gridMetaKey = key; render(); } else tick();
    return;
  }
  if (m.action === 'cursor-focus') cursor.setFocus(!!m.on, window.innerWidth / 2, window.innerHeight / 2);
  else if (m.action === 'cursor') cursor.setVector(m.x, m.y);
  else if (m.action === 'cursor-select') cursor.select();
  else if (m.action === 'zoom-in') bodyEl.scrollBy({ top: SCROLL_STEP });
  else if (m.action === 'zoom-out') bodyEl.scrollBy({ top: -SCROLL_STEP });
  // Same route/navigation keybind actions as MAP and WPT.
  else if (m.action === 'route-next')    { WaypointsStore.cycleActiveRoute(1).then(render); }
  else if (m.action === 'route-prev')    { WaypointsStore.cycleActiveRoute(-1).then(render); }
  else if (m.action === 'waypoint-next') { WaypointsStore.stepNavigation(1).then(render); }
  else if (m.action === 'waypoint-prev') { WaypointsStore.stepNavigation(-1).then(render); }
});
window.addEventListener('wptroutes:changed', render);

render();
