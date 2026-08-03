// RDR page — F-16 FCR B-scope. A pure reactive renderer driven by the shell over postMessage;
// single source of truth for BOTH layouts. See rdr.html for the message contract, docs/rdr-page.md
// for the design.

// Scope geometry in the 520x600 viewBox: ownship at bottom-centre, bearing across, range up.
var L = 60, R = 460, TOP = 70, BOT = 510;      // scope rectangle
var MIDX = 260, HALFW = 200, HGT = BOT - TOP;  // (R-L)/2, height
var DEF_CONE = 60;                             // fallback azimuth half-angle when the radar reports none
var M_PER_NM = 1852, M_TO_KFT = 3.28084 / 1000;

var GREEN = '#39ff14', AMBER = '#ffaa00';
var state = { present: false, range: 0, cone: 0, items: [] };

function coneHalf() { return state.cone > 0 ? state.cone : DEF_CONE; }

// Pure B-scope projection: bearing off nose (az, deg) × range (world units) → scope x,y, or null
// when the contact falls outside the cone half-angle or past max range (culled). Kept free of
// module state so it's unit-checkable (rdr.test.js). ch = cone half-angle (deg), range = max range.
function bscopeXY(az, rng, range, ch) {
  var fx = az / ch;                              // -1..1 across the cone
  var fy = range > 0 ? rng / range : 0;          // 0 at ownship, 1 at max range
  if (Math.abs(fx) > 1 || fy > 1) return null;
  return { x: MIDX + fx * HALFW, y: BOT - fy * HGT };
}

// A contact's scope position against the current scope scale, or null when culled.
function plot(c) { return bscopeXY(c.az, c.rng, state.range, coneHalf()); }

// ── Acquisition cursor (docs/rdr-page.md, step 4) ────────────────────────────────────────
// The PAD cursor slews the two-bar #rdr-cursor over the scope; Select toggles a lock on the
// nearest contact. pad-cursor.js works in the panel's px space, so we map px ↔ viewBox using the
// SVG's xMidYMid-meet transform (derived from the panel size), and hit-test in viewBox units.
var HIT_PAD = 42;             // grab radius in viewBox units (bricks are 16 wide)
var plotted = [];             // {id,x,y} in viewBox coords for the contacts currently on the scope
var hoveredId = null;         // contact nearest the cursor, highlighted

function send(cmd, args) { if (typeof sendCommand === 'function') sendCommand(cmd, args).catch(function () {}); }
function itemById(id) { return (state.items || []).find(function (c) { return c.id === id; }); }

// The SVG's rendered transform in panel px: uniform scale s (xMidYMid meet) + letterbox offsets.
function viewport() {
  var p = document.querySelector('.rdr-panel');
  var s = Math.min(p.clientWidth / 520, p.clientHeight / 600);
  return { s: s, ox: (p.clientWidth - 520 * s) / 2, oy: (p.clientHeight - 600 * s) / 2 };
}
// The scope rectangle in panel px — the cursor's clamp region.
function scopeRectPx() {
  var v = viewport();
  return { dx: v.ox + L * v.s, dy: v.oy + TOP * v.s, dw: (R - L) * v.s, dh: HGT * v.s };
}
// Nearest plotted contact to a panel-px point, within HIT_PAD (viewBox units); null if none close.
function nearestContact(px, py) {
  var v = viewport(), vx = (px - v.ox) / v.s, vy = (py - v.oy) / v.s;
  var best = null, bestD = HIT_PAD;
  plotted.forEach(function (pt) {
    var d = Math.hypot(pt.x - vx, pt.y - vy);
    if (d <= bestD) { bestD = d; best = pt; }
  });
  return best;
}
// Select over a contact toggles its lock — reusing TGT's target set (target.select/deselect by id).
function padSelect(px, py) {
  var pt = nearestContact(px, py);
  if (!pt) return;
  var c = itemById(pt.id);
  send(c && c.tg ? 'target.deselect' : 'target.select', { id: pt.id });
}
// Highlight the contact under the cursor (or clear it when the cursor leaves/hides).
function padMove(px, py) {
  var pt = px == null ? null : nearestContact(px, py);
  var id = pt ? pt.id : null;
  if (id === hoveredId) return;
  hoveredId = id;
  renderContacts();
}

function renderGrid() {
  var g = document.getElementById('rdr-grid');
  if (!g) return;
  // Scan-limit lines converging on ownship at the cone half-angle, + a mid-range reference line.
  var ch = coneHalf();
  var topHalf = Math.tan(ch * Math.PI / 180) * HGT;   // horizontal spread of the cone at scope top
  var lx = Math.max(L, MIDX - topHalf), rx = Math.min(R, MIDX + topHalf);
  var out = '';
  out += line(MIDX, BOT, lx, TOP, 'rgba(255,255,255,0.22)', 1.5);
  out += line(MIDX, BOT, rx, TOP, 'rgba(255,255,255,0.22)', 1.5);
  out += line(MIDX, BOT, MIDX, TOP, 'rgba(255,255,255,0.14)', 1);
  out += line(L, TOP + HGT / 2, R, TOP + HGT / 2, 'rgba(255,255,255,0.28)', 1.5, '6 12');
  g.innerHTML = out;
}

function line(x1, y1, x2, y2, col, w, dash) {
  return '<line x1="' + x1.toFixed(1) + '" y1="' + y1.toFixed(1) + '" x2="' + x2.toFixed(1) +
         '" y2="' + y2.toFixed(1) + '" stroke="' + col + '" stroke-width="' + w + '"' +
         (dash ? ' stroke-dasharray="' + dash + '"' : '') + '/>';
}

function short(n) {
  if (!n) return '';
  return String(n).toUpperCase();
}

function renderContacts() {
  var g = document.getElementById('rdr-contacts');
  if (!g) return;
  var out = '', first = null;
  plotted = [];
  (state.items || []).forEach(function (c) {
    var p = plot(c);
    if (!p) return;
    plotted.push({ id: c.id, x: p.x, y: p.y });
    var locked = !!c.tg;
    var col = locked ? AMBER : GREEN;
    if (locked && !first) first = c;
    // Hover highlight: a soft ring under whatever the cursor is nearest (docs/rdr-page.md).
    if (c.id === hoveredId)
      out += '<circle cx="' + p.x.toFixed(1) + '" cy="' + p.y.toFixed(1) +
             '" r="22" fill="none" stroke="rgba(255,255,255,0.55)" stroke-width="2"/>';
    // Brick.
    out += '<rect x="' + (p.x - 8).toFixed(1) + '" y="' + (p.y - 8).toFixed(1) +
           '" width="16" height="16" fill="' + col + '"/>';
    // Velocity-vector stub: travel heading relative to nose, 0 = up (away). Length ~75% of a brick+.
    var a = (c.rhdg || 0) * Math.PI / 180, LEN = 19;
    out += '<line x1="' + p.x.toFixed(1) + '" y1="' + p.y.toFixed(1) + '" x2="' +
           (p.x + Math.sin(a) * LEN).toFixed(1) + '" y2="' + (p.y - Math.cos(a) * LEN).toFixed(1) +
           '" stroke="' + col + '" stroke-width="3"/>';
    // Lock circle.
    if (locked)
      out += '<circle cx="' + p.x.toFixed(1) + '" cy="' + p.y.toFixed(1) +
             '" r="17" fill="none" stroke="' + AMBER + '" stroke-width="2"/>';
  });
  g.innerHTML = out;
  renderReadout(first);
}

// Bottom readout: always the FIRST locked contact (or blank), plus the total locked count.
function renderReadout(first) {
  var r1 = document.getElementById('rdr-r1'), r2 = document.getElementById('rdr-r2'),
      lk = document.getElementById('rdr-lk');
  var locked = (state.items || []).filter(function (c) { return c.tg; }).length;
  if (first) {
    r1.classList.add('big');
    r1.textContent = short(first.n);
    r2.textContent = 'RNG ' + Math.round(first.rng / M_PER_NM) +
                     '   ALT ' + Math.round(first.alt * M_TO_KFT) +
                     '   HDG ' + pad3(Math.round(((first.rhdg + heading()) % 360 + 360) % 360));
  } else {
    r1.classList.remove('big');
    r1.textContent = '';
    r2.textContent = '';
  }
  lk.textContent = locked ? 'LOCK ' + locked : '';
}

// The readout's HDG is a world compass heading; rhdg is relative to nose, so add the ownship
// heading back. Ownship heading isn't in the 'rdr' message, so we carry the last value the scale
// implies via _hdg (updated when we know it); default 0 keeps it defined.
var _hdg = 0;
function heading() { return _hdg; }
function pad3(n) { return ('00' + n).slice(-3); }

function renderScale() {
  var range = document.getElementById('rdr-range');
  var azl = document.getElementById('rdr-azl'), azr = document.getElementById('rdr-azr');
  range.textContent = state.range > 0 ? Math.round(state.range / M_PER_NM) : '';
  var ch = Math.round(coneHalf());
  azl.textContent = '-' + ch;
  azr.textContent = '+' + ch;
}

function render() {
  var na = document.getElementById('rdr-na');
  var svg = document.querySelector('.rdr-scope');
  if (!state.present) {
    if (na) na.hidden = false;
    if (svg) svg.style.visibility = 'hidden';
    return;
  }
  if (na) na.hidden = true;
  if (svg) svg.style.visibility = '';
  renderScale();
  renderGrid();
  renderContacts();
}

// Browser-only bootstrap (skipped under Node so rdr.test.js can require the pure helpers).
if (typeof window !== 'undefined' && window.addEventListener) {
  // The PAD acquisition cursor (two vertical bars) reuses the shared pad-cursor integrator. Loaded
  // via dynamic import so this file stays a classic script the Node self-check can require; any
  // cursor message that arrives before it resolves is parked and applied on creation.
  var cursor = null, pendingFocus = null, pendingVec = null;
  function centerFocus(on) {
    var r = scopeRectPx();
    cursor.setFocus(on, r.dx + r.dw / 2, r.dy + r.dh / 2);
  }
  import('/assets/services/pad-cursor.js').then(function (mod) {
    cursor = mod.createPadCursor({
      el: document.getElementById('rdr-cursor'),
      clampRect: scopeRectPx,
      onSelect: padSelect,
      onMove: padMove
    });
    if (pendingFocus) { centerFocus(pendingFocus.on); pendingFocus = null; }
    if (pendingVec) { cursor.setVector(pendingVec.x, pendingVec.y); pendingVec = null; }
  });

  window.addEventListener('message', function (e) {
    var m = e.data;
    if (!m || !m.mfd) return;
    if (m.type === 'rdr') {
      state = {
        present: !!m.present,
        range: m.range || 0,
        cone: m.cone || 0,
        items: Array.isArray(m.items) ? m.items : []
      };
      if (typeof m.hdg === 'number') _hdg = m.hdg;
      render();
    } else if (m.action === 'cursor-focus') {
      if (cursor) centerFocus(!!m.on); else pendingFocus = { on: !!m.on };
    } else if (m.action === 'cursor') {
      if (cursor) cursor.setVector(m.x, m.y); else pendingVec = { x: m.x, y: m.y };
    } else if (m.action === 'cursor-held') {
      if (cursor) cursor.setSelectHeld(!!m.held);
    }
  });
  render();
}

if (typeof module !== 'undefined' && module.exports)
  module.exports = { bscopeXY: bscopeXY, geom: { MIDX: MIDX, HALFW: HALFW, TOP: TOP, BOT: BOT, HGT: HGT } };
