// HSD page - 360-degree datalink plan view. See docs/rdr-fcr-hsd.md.
var CX = 300, CY = 300, OUTER = 220;
var M_PER_NM = 1852, M_PER_KM = 1000;
var PURPLE = 'rgb(179, 136, 255)', AMBER = '#ffaa00', GREEN = '#39ff14';
var state = { ownX: 0, ownZ: 0, hdg: 0, metric: false, items: [] };

var RANGE_NM = [10, 20, 40, 80];
var RANGE_STORE_KEY = 'noxmfd.hsd.view';
var rangeIdx = 2;

function loadRange() {
  var saved = null;
  try { saved = JSON.parse(sessionStorage.getItem(RANGE_STORE_KEY) || 'null'); } catch (_) {}
  if (saved && typeof saved.rangeIdx === 'number')
    rangeIdx = Math.max(0, Math.min(RANGE_NM.length - 1, saved.rangeIdx));
}
function saveRange() {
  try { sessionStorage.setItem(RANGE_STORE_KEY, JSON.stringify({ rangeIdx: rangeIdx })); } catch (_) {}
}
function setRangeIdx(i) {
  var clamped = Math.max(0, Math.min(RANGE_NM.length - 1, i));
  if (clamped === rangeIdx) return;
  rangeIdx = clamped;
  saveRange();
  render();
}
function displayRangeM() { return RANGE_NM[rangeIdx] * M_PER_NM; }
function rangeLabel(meters) {
  return state.metric ? Math.round(meters / M_PER_KM) + 'km' : Math.round(meters / M_PER_NM) + 'nm';
}

// World x/z -> ownship-relative, nose-up screen coordinate. x is east, z is north, heading is deg.
function hsdXY(ownX, ownZ, hdg, x, z, rangeM) {
  var dx = x - ownX, dz = z - ownZ;
  var dist = Math.hypot(dx, dz);
  if (rangeM <= 0 || dist > rangeM) return null;
  var bearing = Math.atan2(dx, dz) * 180 / Math.PI;
  var rel = (bearing - hdg) * Math.PI / 180;
  var r = OUTER * (dist / rangeM);
  return { x: CX + Math.sin(rel) * r, y: CY - Math.cos(rel) * r, dist: dist, rel: rel * 180 / Math.PI };
}

function line(x1, y1, x2, y2, col, w) {
  return '<line x1="' + x1.toFixed(1) + '" y1="' + y1.toFixed(1) + '" x2="' + x2.toFixed(1) +
         '" y2="' + y2.toFixed(1) + '" stroke="' + col + '" stroke-width="' + w + '"/>';
}

function renderGrid() {
  var g = document.getElementById('hsd-grid');
  if (!g) return;
  var out = '';
  [0.25, 0.5, 0.75, 1].forEach(function (f) {
    out += '<circle cx="' + CX + '" cy="' + CY + '" r="' + (OUTER * f).toFixed(1) +
           '" fill="none" stroke="rgba(179,136,255,' + (f === 1 ? '0.70' : '0.36') + ')" stroke-width="' +
           (f === 1 ? '2' : '1.5') + '"/>';
  });
  out += line(CX, CY - OUTER, CX, CY + OUTER, 'rgba(179,136,255,0.20)', 1);
  out += line(CX - OUTER, CY, CX + OUTER, CY, 'rgba(179,136,255,0.20)', 1);
  g.innerHTML = out;
}

function renderContacts() {
  var g = document.getElementById('hsd-contacts');
  if (!g) return;
  var out = '', count = 0, locks = 0, rangeM = displayRangeM();
  (state.items || []).forEach(function (c) {
    var p = hsdXY(state.ownX, state.ownZ, state.hdg, c.x || 0, c.z || 0, rangeM);
    if (!p) return;
    count++;
    if (c.tg) locks++;
    var col = c.tg ? AMBER : PURPLE;
    var hdg = typeof c.hdg === 'number' ? c.hdg : 0;
    var rot = ((hdg - state.hdg) % 360 + 360) % 360;
    out += '<g transform="translate(' + p.x.toFixed(1) + ' ' + p.y.toFixed(1) + ') rotate(' + rot.toFixed(1) + ')">';
    out += '<path d="M0 -9 L-6 7 L0 4 L6 7 Z" fill="' + col + '"/>';
    out += '</g>';
    out += '<line x1="' + p.x.toFixed(1) + '" y1="' + p.y.toFixed(1) + '" x2="' +
           (p.x + Math.sin((hdg - state.hdg) * Math.PI / 180) * 18).toFixed(1) + '" y2="' +
           (p.y - Math.cos((hdg - state.hdg) * Math.PI / 180) * 18).toFixed(1) +
           '" stroke="' + col + '" stroke-width="2"/>';
    if (c.tg)
      out += '<circle cx="' + p.x.toFixed(1) + '" cy="' + p.y.toFixed(1) +
             '" r="15" fill="none" stroke="' + AMBER + '" stroke-width="2"/>';
  });
  g.innerHTML = out;
  document.getElementById('hsd-count').textContent = count ? 'DL ' + count : 'DL 0';
  document.getElementById('hsd-locks').textContent = locks ? 'LOCK ' + locks : '';
}

function renderScale() {
  var r = document.getElementById('hsd-range');
  if (r) r.textContent = rangeLabel(displayRangeM());
}

function render() {
  renderScale();
  renderGrid();
  renderContacts();
}

if (typeof window !== 'undefined' && window.addEventListener) {
  loadRange();
  window.addEventListener('message', function (e) {
    var m = e.data;
    if (!m || !m.mfd) return;
    if (m.type === 'hsd') {
      state = {
        ownX: typeof m.ownX === 'number' ? m.ownX : 0,
        ownZ: typeof m.ownZ === 'number' ? m.ownZ : 0,
        hdg: typeof m.hdg === 'number' ? m.hdg : 0,
        metric: !!m.metric,
        items: Array.isArray(m.items) ? m.items : []
      };
      render();
    } else if (m.action === 'zoom-in') {
      setRangeIdx(rangeIdx + 1);
    } else if (m.action === 'zoom-out') {
      setRangeIdx(rangeIdx - 1);
    }
  });
  render();
}

if (typeof module !== 'undefined' && module.exports)
  module.exports = { hsdXY: hsdXY, rangeLabelForTest: function (metric, meters) { state.metric = metric; return rangeLabel(meters); },
                     geom: { CX: CX, CY: CY, OUTER: OUTER } };
