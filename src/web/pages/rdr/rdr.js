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
  (state.items || []).forEach(function (c) {
    var p = plot(c);
    if (!p) return;
    var locked = !!c.tg;
    var col = locked ? AMBER : GREEN;
    if (locked && !first) first = c;
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
    }
  });
  render();
}

if (typeof module !== 'undefined' && module.exports)
  module.exports = { bscopeXY: bscopeXY, geom: { MIDX: MIDX, HALFW: HALFW, TOP: TOP, BOT: BOT, HGT: HGT } };
