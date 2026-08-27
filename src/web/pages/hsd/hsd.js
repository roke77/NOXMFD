// HSD page - 360-degree datalink plan view. See docs/rdr-fcr-hsd.md.
var CX = 300, CY = 300, OUTER = 220;
var M_PER_NM = 1852, M_PER_KM = 1000;
var HSD_PINK = 'var(--no-hsd-pink)', AMBER = 'var(--no-amber)';
var HSD_PINK_RGB = 'var(--no-hsd-pink-rgb)', TEAL_RGB = 'var(--no-teal-rgb)';
var state = { ownX: 0, ownZ: 0, hdg: 0, metric: false, radarPresent: false, radarRange: 0, radarCone: 0, items: [] };

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

function polarPoint(angleDeg, radiusPx) {
  var a = angleDeg * Math.PI / 180;
  return { x: CX + Math.sin(a) * radiusPx, y: CY - Math.cos(a) * radiusPx };
}

function radarConePath(rangeM, radarRange, radarCone) {
  if (rangeM <= 0 || radarRange <= 0 || radarCone <= 0) return '';
  var cone = Math.max(1, Math.min(89, radarCone));
  var r = OUTER * (Math.min(radarRange, rangeM) / rangeM);
  if (r <= 0) return '';
  var left = polarPoint(-cone, r);
  var right = polarPoint(cone, r);
  return 'M' + CX + ' ' + CY +
         ' L' + left.x.toFixed(1) + ' ' + left.y.toFixed(1) +
         ' A' + r.toFixed(1) + ' ' + r.toFixed(1) + ' 0 0 1 ' +
         right.x.toFixed(1) + ' ' + right.y.toFixed(1) +
         ' Z';
}

function renderGrid() {
  var g = document.getElementById('hsd-grid');
  if (!g) return;
  var out = '';
  [0.25, 0.5, 0.75, 1].forEach(function (f) {
    out += '<circle cx="' + CX + '" cy="' + CY + '" r="' + (OUTER * f).toFixed(1) +
           '" fill="none" stroke="rgba(' + HSD_PINK_RGB + ',' + (f === 1 ? '0.70' : '0.36') + ')" stroke-width="' +
           (f === 1 ? '2' : '1.5') + '"/>';
  });
  g.innerHTML = out;
}

function renderRadarCone() {
  var g = document.getElementById('hsd-radar');
  if (!g) return;
  var path = state.radarPresent ? radarConePath(displayRangeM(), state.radarRange, state.radarCone) : '';
  g.innerHTML = path ? '<path d="' + path + '" fill="rgba(' + TEAL_RGB + ',0.07)" ' +
                      'stroke="rgba(' + TEAL_RGB + ',0.64)" stroke-width="2"/>' : '';
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
    var col = c.tg ? AMBER : HSD_PINK;
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
  document.getElementById('hsd-count').textContent = count ? 'LINK ' + count : 'LINK 0';
  document.getElementById('hsd-locks').textContent = locks ? 'LOCK ' + locks : '';
}

function renderScale() {
  var r = document.getElementById('hsd-range');
  if (r) r.textContent = rangeLabel(displayRangeM());
}

function render() {
  renderScale();
  renderGrid();
  renderRadarCone();
  renderContacts();
}

function demoContacts(ownX, ownZ, hdg) {
  var contacts = [
    { az: -150, rng: 26000, hdg: 20,  tg: 0, n: 'EW-25 Medusa', alt: 9700 },
    { az:  -70, rng: 48000, hdg: 110, tg: 0, n: 'FS-12 Revoker', alt: 7600 },
    { az:   35, rng: 32000, hdg: 260, tg: 1, n: 'KR-67 Ifrit', alt: 6500 },
    { az:  145, rng: 61000, hdg: 315, tg: 0, n: 'SFB-81', alt: 8900 }
  ];
  return contacts.map(function (c, i) {
    var ab = (c.az + hdg) * Math.PI / 180;
    return {
      id: 9201 + i,
      x: Math.round(ownX + Math.sin(ab) * c.rng),
      z: Math.round(ownZ + Math.cos(ab) * c.rng),
      alt: c.alt,
      hdg: c.hdg,
      tg: c.tg,
      n: c.n
    };
  });
}

function shouldSeedStandalonePreview() {
  if (typeof window === 'undefined') return false;
  if (window.top !== window) return false;
  if (window.__NOXMFD_DISABLE_HSD_PREVIEW__) return false;
  if (window.__NOXMFD_HSD_PREVIEW__) return true;
  var host = window.location && window.location.hostname;
  return host === '127.0.0.1' || host === 'localhost';
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
        radarPresent: !!m.radarPresent,
        radarRange: typeof m.radarRange === 'number' ? m.radarRange : 0,
        radarCone: typeof m.radarCone === 'number' ? m.radarCone : 0,
        items: Array.isArray(m.items) ? m.items : []
      };
      render();
    } else if (m.action === 'zoom-in') {
      setRangeIdx(rangeIdx + 1);
    } else if (m.action === 'zoom-out') {
      setRangeIdx(rangeIdx - 1);
    }
  });
  if (shouldSeedStandalonePreview()) {
    rangeIdx = 3;
    state = { ownX: 0, ownZ: 0, hdg: 20, metric: false, radarPresent: true,
              radarRange: 40 * M_PER_NM, radarCone: 60, items: demoContacts(0, 0, 20) };
  }
  render();
}

if (typeof module !== 'undefined' && module.exports)
  module.exports = { hsdXY: hsdXY, rangeLabelForTest: function (metric, meters) { state.metric = metric; return rangeLabel(meters); },
                     radarConePath: radarConePath, demoContacts: demoContacts, geom: { CX: CX, CY: CY, OUTER: OUTER } };
