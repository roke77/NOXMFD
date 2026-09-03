// HSD page - 360-degree datalink plan view. See docs/rdr-fcr-hsd.md.
// CEN/DEP display modes (DCS F-16 HSD): CEN centers ownship with quarter-range grid rings; DEP
// (Depressed) pushes ownship down near the bottom edge so the same screen shows much more range
// ahead of the nose, at the cost of most of the rearward picture — the outer ring is still the
// full selected range, at 1/3 and 2/3 for the inner two. Real DCS geometry isn't published to the
// pixel, so CEN_CY/OUTER and DEP_CY/OUTER below are a reasoned approximation: same 80px header/
// footer clearance in both modes, with DEP's ownship low enough that only a small sliver of range
// shows behind. Retune against real DCS reference screenshots if pixel-accurate matching is needed.
var CX = 300;
var CEN_CY = 300, CEN_OUTER = 220;
var DEP_CY = 500, DEP_OUTER = 420;
var CY = CEN_CY, OUTER = CEN_OUTER;
var M_PER_NM = 1852, M_PER_KM = 1000;
// RED (not green) for own-radar-detected contacts — these are enemy air contacts, and dropping
// green here leaves it free to mean "friendly" if that symbology is ever added, matching FCR.
var HSD_PINK = 'var(--no-purple)', RED = 'var(--no-red)', AMBER = 'var(--no-amber)', STALE_WHITE = 'var(--no-white)';
var HSD_PINK_RGB = 'var(--no-hsd-pink-rgb)', TEAL_RGB = 'var(--no-teal-rgb)';
var YELLOW = 'var(--no-hsd-yellow)';   // AA threat rings (issue #74)
var CURSOR_WHITE = 'rgba(255,255,255,0.85)';
var state = { ownX: 0, ownZ: 0, hdg: 0, metric: false, radarPresent: false, radarRange: 0, radarCone: 0, items: [], threats: [], focusedTargetId: 0 };

// DCS's own DEP/CEN range ladders (NM) — same length, and DEP[i] is exactly 1.5x CEN[i] at every
// step (matching real DCS's DEP-range-equals-1.5x-FCR-range relationship), so a single shared
// index translates the selected range across a mode switch instead of each mode remembering its
// own separately: CEN 40nm <-> DEP 60nm are "the same" range setting, not two unrelated ones.
var CEN_RANGE_NM = [10, 20, 40, 80, 160];
var DEP_RANGE_NM = [15, 30, 60, 120, 240];
var RANGE_STORE_KEY = 'noxmfd.hsd.view';
var mode = 'cen';           // 'cen' | 'dep'
var RANGE_NM = CEN_RANGE_NM;   // whichever mode's ladder is active — kept as its own var so
                                // displayRangeM()/setRangeIdx() below don't need to know about modes
var rangeIdx = 2;

// Applies `mode`'s geometry/ladder to the module vars every other function reads — called on load
// and on every toggleMode(), so nothing else needs an explicit mode check. rangeIdx itself carries
// straight across (see the ladder comment above), so it isn't touched here.
function applyMode() {
  RANGE_NM = mode === 'dep' ? DEP_RANGE_NM : CEN_RANGE_NM;
  CY = mode === 'dep' ? DEP_CY : CEN_CY;
  OUTER = mode === 'dep' ? DEP_OUTER : CEN_OUTER;
}
function gridFractions() { return mode === 'dep' ? [1 / 3, 2 / 3, 1] : [0.25, 0.5, 0.75, 1]; }

function loadRange() {
  var saved = null;
  try { saved = JSON.parse(sessionStorage.getItem(RANGE_STORE_KEY) || 'null'); } catch (_) {}
  if (saved) {
    if (saved.mode === 'dep') mode = 'dep';
    if (typeof saved.rangeIdx === 'number')
      rangeIdx = Math.max(0, Math.min(CEN_RANGE_NM.length - 1, saved.rangeIdx));
  }
  applyMode();
}
function saveRange() {
  try { sessionStorage.setItem(RANGE_STORE_KEY, JSON.stringify({ mode: mode, rangeIdx: rangeIdx })); }
  catch (_) {}
}
function setRangeIdx(i) {
  var clamped = Math.max(0, Math.min(RANGE_NM.length - 1, i));
  if (clamped === rangeIdx) return;
  rangeIdx = clamped;
  saveRange();
  render();
}
// The MODE bezel/nav key (docs/rdr-fcr-hsd.md) — a plain toggle, unlike R+/R- which step within
// one mode's ladder. No cursor-panning meaning, so it isn't PAD-cursor-driven.
function toggleMode() {
  mode = mode === 'dep' ? 'cen' : 'dep';
  applyMode();
  saveRange();
  render();
}
function displayRangeM() { return RANGE_NM[rangeIdx] * M_PER_NM; }
function rangeLabel(meters) {
  return state.metric ? Math.round(meters / M_PER_KM) + 'km' : Math.round(meters / M_PER_NM) + 'nm';
}
function rangeUnits(meters) {
  return state.metric ? (meters / M_PER_KM).toFixed(1) : (meters / M_PER_NM).toFixed(1);
}
function altUnits(meters) {
  return state.metric ? Math.round(meters) : Math.round(meters * 3.28084);
}
function pad3(n) { return ('00' + (((n % 360) + 360) % 360)).slice(-3); }
function short(s) {
  s = (s || 'BOGEY').toString().toUpperCase();
  return s.length > 18 ? s.slice(0, 18) : s;
}

// World x/z -> ownship-relative, nose-up screen coordinate. x is east, z is north, heading is deg.
// allowOffscreen skips the dist>rangeM cutoff — a threat ring (renderThreats) can still reach onto
// the scope even when the SAM site itself, at the ring's center, plots outside the selected range.
function hsdXY(ownX, ownZ, hdg, x, z, rangeM, allowOffscreen) {
  var dx = x - ownX, dz = z - ownZ;
  var dist = Math.hypot(dx, dz);
  if (rangeM <= 0 || (!allowOffscreen && dist > rangeM)) return null;
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

// `focused` distinguishes the one locked target the bottom readout currently describes from any
// other simultaneous lock (a planned cycling-locked-targets follow-up, docs/rdr-fcr-hsd.md) — only
// the focused one's icon goes amber. Any other contact's color is purely its source regardless of
// lock state: an unfocused lock keeps its ordinary red/purple, since its ring (drawn by the
// caller) is what shows it's still locked, not its icon. A stale datalink track (its position has
// drifted past the game's own trust radius, docs/tgt-stale-lock.md) goes white instead of its
// source color — still below focused-amber, since a lock stays the more important cue even if the
// position backing it is untrustworthy.
function contactColor(c, focused) {
  if (focused) return AMBER;
  if (c && c.st) return STALE_WHITE;
  if (c && c.rd) return RED;
  return HSD_PINK;
}

function renderGrid() {
  var g = document.getElementById('hsd-grid');
  if (!g) return;
  var out = '';
  gridFractions().forEach(function (f) {
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

// AA threat rings (issue #74): a yellow ring per known enemy ground/naval SAM site, radius scaled
// to that unit's own effective weapon range (t.r, meters) the same way contact distances are
// scaled. Background layer, drawn under contact icons — not part of the PAD cursor hit-test.
function renderThreats() {
  var g = document.getElementById('hsd-threats');
  if (!g) return;
  var out = '', rangeM = displayRangeM();
  (state.threats || []).forEach(function (t) {
    if (!(t.r > 0)) return;
    var p = hsdXY(state.ownX, state.ownZ, state.hdg, t.x || 0, t.z || 0, rangeM, true);
    if (!p) return;
    var r = OUTER * (t.r / rangeM);
    out += '<circle cx="' + p.x.toFixed(1) + '" cy="' + p.y.toFixed(1) + '" r="' + r.toFixed(1) +
           '" fill="none" stroke="' + YELLOW + '" stroke-width="1.5" stroke-opacity="0.75"/>';
  });
  g.innerHTML = out;
}

// PAD acquisition cursor (docs/page-cursor.md): plotted holds each on-scope contact's current
// viewBox position so nearestContact() (below) can hit-test against it, same split FCR's own
// plotted/hoveredId pair uses — renderContacts() must run before a hit-test is meaningful.
var plotted = [];
var hoveredId = null;

function renderContacts() {
  var g = document.getElementById('hsd-contacts');
  if (!g) return;
  var out = '', count = 0, locks = 0, focusedLocked = null, rangeM = displayRangeM();
  plotted = [];
  (state.items || []).forEach(function (c) {
    var p = hsdXY(state.ownX, state.ownZ, state.hdg, c.x || 0, c.z || 0, rangeM);
    if (!p) return;
    plotted.push({ id: c.id, x: p.x, y: p.y });
    count++;
    // The single locked contact Next/Previous currently focuses (issue #62, docs/tgt-cycle-focus.md)
    // — shared across TGT/FCR/HSD via state.focusedTargetId, described by the readout below.
    var focused = false;
    if (c.tg) {
      locks++;
      if (c.id === state.focusedTargetId) { focusedLocked = { c: c, dist: p.dist }; focused = true; }
    }
    var col = contactColor(c, focused);
    var hdg = typeof c.hdg === 'number' ? c.hdg : 0;
    var rot = ((hdg - state.hdg) % 360 + 360) % 360;
    // Hover highlight: a soft ring under whatever the cursor is nearest, same treatment FCR gives
    // its own hoveredId brick.
    if (c.id === hoveredId)
      out += '<circle cx="' + p.x.toFixed(1) + '" cy="' + p.y.toFixed(1) +
             '" r="16" fill="none" stroke="rgba(255,255,255,0.55)" stroke-width="2"/>';
    out += '<g transform="translate(' + p.x.toFixed(1) + ' ' + p.y.toFixed(1) + ') rotate(' + rot.toFixed(1) + ')">';
    out += '<path d="M0 -9 L-6 7 L0 4 L6 7 Z" fill="' + col + '"/>';
    out += '</g>';
    out += '<line x1="' + p.x.toFixed(1) + '" y1="' + p.y.toFixed(1) + '" x2="' +
           (p.x + Math.sin((hdg - state.hdg) * Math.PI / 180) * 18).toFixed(1) + '" y2="' +
           (p.y - Math.cos((hdg - state.hdg) * Math.PI / 180) * 18).toFixed(1) +
           '" stroke="' + col + '" stroke-width="2"/>';
    // Lock circle — always amber regardless of focus, sized close around the small contact
    // triangle rather than floating loose around it.
    if (c.tg)
      out += '<circle cx="' + p.x.toFixed(1) + '" cy="' + p.y.toFixed(1) +
             '" r="10" fill="none" stroke="' + AMBER + '" stroke-width="2"/>';
  });
  g.innerHTML = out;
  renderReadout(focusedLocked, count, locks);
}

// ── PAD acquisition cursor (docs/page-cursor.md) ────────────────────────────────────────
// Select over a contact toggles its lock — reuses FCR's own target set (target.select/deselect
// by id), same reasoning as FCR's own padSelect: this is the same aerial target set TGT and FCR
// display, so selecting from HSD has to mean the same thing.
var HIT_PAD = 26;   // grab radius in viewBox units (contact triangles are ~12 wide)

function send(cmd, args) { if (typeof sendCommand === 'function') sendCommand(cmd, args).catch(function () {}); }
function itemById(id) { return (state.items || []).find(function (c) { return c.id === id; }); }

// The SVG's rendered transform in panel px: uniform scale s (xMidYMid meet) + letterbox offsets.
// Unlike FCR's B-scope band, HSD's whole 600x600 viewBox IS the scope, so the clamp rect is the
// full letterboxed square rather than a sub-rectangle.
function viewport() {
  var p = document.querySelector('.hsd-panel');
  var s = Math.min(p.clientWidth / 600, p.clientHeight / 600);
  return { s: s, ox: (p.clientWidth - 600 * s) / 2, oy: (p.clientHeight - 600 * s) / 2 };
}
function scopeRectPx() {
  var v = viewport();
  return { dx: v.ox, dy: v.oy, dw: 600 * v.s, dh: 600 * v.s };
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
function padSelect(px, py) {
  var pt = nearestContact(px, py);
  if (!pt) return;
  var c = itemById(pt.id);
  send(c && c.tg ? 'target.deselect' : 'target.select', { id: pt.id });
}
// Draw the same F-16 two-bar acquisition gate FCR uses, in viewBox units so it scales with the
// contact symbols. CUR_GAP/CUR_H match FCR's own drawCursor proportions.
var CUR_GAP = 14, CUR_H = 9;
function drawCursor(px, py) {
  var g = document.getElementById('hsd-cursor-g');
  if (!g) return;
  if (px == null) { g.innerHTML = ''; return; }
  var v = viewport(), vx = (px - v.ox) / v.s, vy = (py - v.oy) / v.s;
  g.innerHTML = bar(vx - CUR_GAP, vy) + bar(vx + CUR_GAP, vy);
}
function bar(x, y) {
  return '<line x1="' + x.toFixed(1) + '" y1="' + (y - CUR_H).toFixed(1) +
         '" x2="' + x.toFixed(1) + '" y2="' + (y + CUR_H).toFixed(1) +
         '" stroke="' + CURSOR_WHITE + '" stroke-width="3"/>';
}
// Move the gate and highlight the contact under the cursor (or clear both when it leaves/hides).
function padMove(px, py) {
  drawCursor(px, py);
  var pt = px == null ? null : nearestContact(px, py);
  var id = pt ? pt.id : null;
  if (id === hoveredId) return;
  hoveredId = id;
  renderContacts();
}

function renderReadout(focusedLocked, count, locks) {
  var r1 = document.getElementById('hsd-r1'), r2 = document.getElementById('hsd-r2'),
      link = document.getElementById('hsd-link'), lk = document.getElementById('hsd-locks');
  if (focusedLocked) {
    var c = focusedLocked.c;
    r1.classList.add('big');
    r1.textContent = short(c.n);
    r2.textContent = 'RNG ' + rangeUnits(focusedLocked.dist) +
                     '   ALT ' + altUnits(c.alt || 0) +
                     '   HDG ' + pad3(Math.round(c.hdg || 0));
  } else {
    r1.classList.remove('big');
    r1.textContent = '';
    r2.textContent = '';
  }
  link.textContent = count ? 'LINK ' + count : 'LINK 0';
  lk.textContent = locks ? 'LOCK ' + locks : '';
}

function renderScale() {
  var r = document.getElementById('hsd-range');
  if (r) r.textContent = (mode === 'dep' ? 'DEP ' : 'CEN ') + rangeLabel(displayRangeM());
}

// Same aircraft symbol every mode uses, just redrawn at the current CY — DEP moves ownship, so
// this can't be static markup with a fixed center the way CEN alone could get away with. Sized to
// match a contact triangle exactly (renderContacts' "M0 -9 L-6 7 L0 4 L6 7 Z") so it reads as one
// of the same family of symbols, not a different kind of marker.
function renderOwnship() {
  var g = document.getElementById('hsd-ownship');
  if (!g) return;
  g.innerHTML =
    '<path d="M' + CX + ' ' + (CY - 9) + ' L' + (CX - 6) + ' ' + (CY + 7) + ' L' + CX + ' ' +
    (CY + 4) + ' L' + (CX + 6) + ' ' + (CY + 7) + ' Z" fill="rgba(255,255,255,0.78)"/>' +
    '<line x1="' + CX + '" y1="' + (CY - 18) + '" x2="' + CX + '" y2="' + (CY - 9) +
    '" stroke="rgba(255,255,255,0.45)" stroke-width="2"/>';
}

function render() {
  renderScale();
  renderGrid();
  renderOwnship();
  renderRadarCone();
  renderThreats();
  renderContacts();
}

function demoContacts(ownX, ownZ, hdg) {
  var contacts = [
    { az: -150, rng: 26000, hdg: 20,  tg: 0, rd: 0, dl: 1, n: 'EW-25 Medusa', alt: 9700 },
    { az:  -70, rng: 48000, hdg: 110, tg: 0, rd: 0, dl: 1, n: 'FS-12 Revoker', alt: 7600 },
    { az:   35, rng: 32000, hdg: 260, tg: 1, rd: 0, dl: 1, n: 'KR-67 Ifrit', alt: 6500 },
    { az:   18, rng: 41000, hdg: 205, tg: 0, rd: 1, dl: 0, n: 'SFB-81', alt: 8900 },
    { az:  145, rng: 61000, hdg: 315, tg: 0, rd: 0, dl: 1, st: 1, n: 'AB-4 Alkyon', alt: 8300 }
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
      rd: c.rd,
      dl: c.dl,
      st: c.st,
      n: c.n
    };
  });
}

// AA threat rings (issue #74) demo data for the standalone preview.
function demoThreats(ownX, ownZ, hdg) {
  var threats = [
    { az: -100, rng: 55000, r: 18000, n: 'SA-15 Site' },
    { az:   60, rng: 70000, r: 30000, n: 'Corvette' }
  ];
  return threats.map(function (t, i) {
    var ab = (t.az + hdg) * Math.PI / 180;
    return { id: 9301 + i, x: Math.round(ownX + Math.sin(ab) * t.rng), z: Math.round(ownZ + Math.cos(ab) * t.rng), r: t.r, n: t.n };
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

  // The PAD acquisition cursor (two vertical bars) reuses the shared pad-cursor integrator, same
  // as FCR. Loaded via dynamic import so this file stays a classic script the Node self-check can
  // require; any cursor message that arrives before it resolves is parked and applied on creation.
  var cursor = null, pendingFocus = null, pendingVec = null;
  function centerFocus(on) {
    var r = scopeRectPx();
    cursor.setFocus(on, r.dx + r.dw / 2, r.dy + r.dh / 2);
  }
  // Cursor overflow at the top/bottom edge also steps range (issue #66) — shared with FCR's
  // identical behavior (docs/rdr-fcr-hsd.md), see edge-range-step.js.
  Promise.all([import('/assets/services/pad-cursor.js'), import('/assets/services/edge-range-step.js')])
    .then(function (mods) {
      cursor = mods[0].createPadCursor({
        el: document.getElementById('hsd-cursor'),
        clampRect: scopeRectPx,
        onSelect: padSelect,
        onMove: padMove,
        onEdge: mods[1].createEdgeRangeStepper(function (dir) { setRangeIdx(rangeIdx + dir); })
      });
      if (pendingFocus) { centerFocus(pendingFocus.on); pendingFocus = null; }
      if (pendingVec) { cursor.setVector(pendingVec.x, pendingVec.y); pendingVec = null; }
    });

  // A mouse/touch tap selects the same way the PAD cursor's Select does — same hit-test, same
  // toggle-lock, matching FCR's own click handler.
  document.querySelector('.hsd-panel').addEventListener('click', function (e) {
    var r = this.getBoundingClientRect();
    padSelect(e.clientX - r.left, e.clientY - r.top);
  });

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
        items: Array.isArray(m.items) ? m.items : [],
        threats: Array.isArray(m.threats) ? m.threats : [],
        focusedTargetId: m.focusedTargetId || 0
      };
      render();
    } else if (m.action === 'cursor-focus') {
      if (cursor) centerFocus(!!m.on); else pendingFocus = { on: !!m.on };
    } else if (m.action === 'cursor') {
      if (cursor) cursor.setVector(m.x, m.y); else pendingVec = { x: m.x, y: m.y };
    } else if (m.action === 'cursor-held') {
      if (cursor) cursor.setSelectHeld(!!m.held);
    } else if (m.action === 'zoom-in') {
      setRangeIdx(rangeIdx + 1);
    } else if (m.action === 'zoom-out') {
      setRangeIdx(rangeIdx - 1);
    } else if (m.action === 'hsd-mode') {
      toggleMode();
    }
  });
  if (shouldSeedStandalonePreview()) {
    rangeIdx = 3;
    state = { ownX: 0, ownZ: 0, hdg: 20, metric: false, radarPresent: true,
              radarRange: 40 * M_PER_NM, radarCone: 60, items: demoContacts(0, 0, 20),
              threats: demoThreats(0, 0, 20) };
  }
  render();
}

if (typeof module !== 'undefined' && module.exports)
  module.exports = { hsdXY: hsdXY, rangeLabelForTest: function (metric, meters) { state.metric = metric; return rangeLabel(meters); },
                     rangeUnitsForTest: function (metric, meters) { state.metric = metric; return rangeUnits(meters); },
                     altUnitsForTest: function (metric, meters) { state.metric = metric; return altUnits(meters); },
                     contactColor: contactColor, radarConePath: radarConePath, demoContacts: demoContacts,
                     demoThreats: demoThreats,
                     geom: { CX: CX, CY: CY, OUTER: OUTER },
                     // CEN/DEP mode (docs/rdr-fcr-hsd.md) — DOM-free, so testable directly rather
                     // than through toggleMode()/render(), which touch the page's real DOM.
                     CEN_RANGE_NM: CEN_RANGE_NM, DEP_RANGE_NM: DEP_RANGE_NM,
                     gridFractionsForTest: function (m) { var save = mode; mode = m; var r = gridFractions(); mode = save; return r; },
                     applyModeForTest: function (m, idx) {
                       var saveMode = mode, saveIdx = rangeIdx;
                       mode = m; rangeIdx = idx; applyMode();
                       var result = { RANGE_NM: RANGE_NM, CY: CY, OUTER: OUTER, rangeIdx: rangeIdx };
                       mode = saveMode; rangeIdx = saveIdx; applyMode();
                       return result;
                     } };
