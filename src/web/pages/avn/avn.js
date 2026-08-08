// AVN page — avionics. A pure reactive renderer driven by the shell over postMessage; single
// source of truth for BOTH layouts (full-screen iframe + split pane). No damage silhouette or
// airframe name here — both moved out (a future AFM page owns airframe status). See avn.html for
// the message contract.

// ── DOM refs ───────────────────────────────────────────────────────────────────────
const avnPanel     = document.getElementById('avn-panel');
const avnEmptyEl   = document.getElementById('avn-empty');
const avnContentEl  = document.getElementById('avn-content');
const avnIconGridEl = document.getElementById('avn-icon-grid');
const avnPageIndEl  = document.getElementById('avn-page-ind');
const avnGaugeFuel = document.getElementById('avn-gauge-fuel');
const avnGaugeRpm  = document.getElementById('avn-gauge-rpm');
const avnGaugeHeat = document.getElementById('avn-gauge-heat');
const avnGaugeThr  = document.getElementById('avn-gauge-thr');
const avnTileGear  = document.getElementById('avn-tile-gear');
const avnTileRadar = document.getElementById('avn-tile-radar');
const avnTileGuns  = document.getElementById('avn-tile-guns');
const avnTileEng    = document.getElementById('avn-tile-eng');
const avnTileAssist = document.getElementById('avn-tile-assist');
const avnTileNvg    = document.getElementById('avn-tile-nvg');
const avnTileLights = document.getElementById('avn-tile-lights');
const avnTileTurret = document.getElementById('avn-tile-turret');

// ── State ──────────────────────────────────────────────────────────────────────────
let avnData = { name: null, fuel: -1, throttle: -1, heat: -1, rpm: -1, hasAb: false, abStart: 1, gearDown: false, radar: false, guns: false, ignition: false, assist: false, turret: false, nvg: false, navLights: false, visible: null, page: 1, pages: 1 };
let layout         = 'compact';   // 'compact' (split pane) | 'full' (full-screen iframe)
// {frameTop, frameHeight} forwarded by the shell in full — the vertical band .avn-content centres
// itself in (below the top bezel row). No per-tile geometry any more: the icon grid and gauge grid
// are plain CSS grids, sized/positioned entirely by CSS once .avn-content itself is placed.
let avnFullGeom    = null;
let avnLastType    = null;   // last aircraft type rendered, so a respawn on the SAME type still re-renders

// ── Renderer ───────────────────────────────────────────────────────────────────────
function renderAvn() {
  const type = avnData.name;
  avnLastType = type;
  if (!type) {
    avnContentEl.classList.remove('placed');
    avnEmptyEl.style.display = '';
    return;
  }
  avnEmptyEl.style.display = 'none';

  paintAvnStatus();
  paintAvnGauges();
  layoutAvnIconPaging();
  layoutAvnContent();
}

// Recolour each tile from the live booleans (avn-status-policy maps state -> the 'on'/'off'/
// 'gear-down' modifier class; the CSS turns that into green/gray/red on label + icon).
function setAvnTile(el, kind, active) {
  el.classList.remove('on', 'off', 'gear-down');
  el.classList.add(AvnStatusPolicy.tileClass(kind, active));
}
function paintAvnStatus() {
  setAvnTile(avnTileGear,   'gear',   avnData.gearDown);
  setAvnTile(avnTileRadar,  'radar',  avnData.radar);
  setAvnTile(avnTileGuns,   'guns',   avnData.guns);
  setAvnTile(avnTileEng,    'eng',    avnData.ignition);
  setAvnTile(avnTileAssist, 'assist', avnData.assist);
  setAvnTile(avnTileNvg,    'nvg',    avnData.nvg);
  setAvnTile(avnTileLights, 'lights', avnData.navLights);
  setAvnTile(avnTileTurret, 'turret', avnData.turret);
}

// id->element map, in mfd.js's AVN_TOGGLE_GROUPS order — the shell indexes `visible` by these same
// ids, so a mismatch here would hide/show the wrong tile.
const AVN_TILE_BY_ID = {
  gear: avnTileGear, radar: avnTileRadar, guns: avnTileGuns, eng: avnTileEng,
  assist: avnTileAssist, nvg: avnTileNvg, lights: avnTileLights, turret: avnTileTurret,
};

// A split pane only has 4 avn.toggle keys (PREV/NEXT page the other 4 in, mfd.js avnPaneSlice) —
// show just avnData.visible's 4 tiles there, with a PAGE x/y indicator so it's clear the other 4
// exist. Full view never sends `visible` (it has all 8 keys at once), so this is a no-op there:
// every tile stays shown, .paged never applies, and the indicator stays hidden (updateAvnPageInd's
// pages<=1 check).
function layoutAvnIconPaging() {
  const visible = Array.isArray(avnData.visible) ? avnData.visible : null;
  avnIconGridEl.classList.toggle('paged', !!visible);
  Object.keys(AVN_TILE_BY_ID).forEach(function (id) {
    AVN_TILE_BY_ID[id].style.display = (!visible || visible.indexOf(id) >= 0) ? '' : 'none';
  });
  updateAvnPageInd(avnData.page, avnData.pages);
}

// Mirrors wpn.js's updatePageInd exactly: hidden unless there's more than one page.
function updateAvnPageInd(page, pages) {
  if (pages > 1) {
    avnPageIndEl.textContent = 'PAGE ' + page + '/' + pages;
    avnPageIndEl.classList.remove('empty');
  } else {
    avnPageIndEl.classList.add('empty');
  }
}

// The vertical band .avn-content centres itself in: full uses the shell-forwarded bezel geometry
// (the same "below the top bezel row" band the old icon columns sat within); compact has no such
// geometry, so it's just a small top margin down to a small bottom margin.
const AVN_TOP_MARGIN = 12;
const AVN_BOTTOM_MARGIN = 12;
function avnContentVerticalExtent() {
  if (layout === 'full' && avnFullGeom && typeof avnFullGeom.frameTop === 'number') {
    return { top: avnFullGeom.frameTop, height: avnFullGeom.frameHeight };
  }
  const panelRect = avnPanel.getBoundingClientRect();
  return { top: AVN_TOP_MARGIN, height: Math.max(0, panelRect.height - AVN_TOP_MARGIN - AVN_BOTTOM_MARGIN) };
}

// .avn-content spans the FULL panel width (no more tile-column inset — the icon grid is now a
// sibling row above the gauges, not side columns beside them) and the full vertical extent, so it
// reads as "as wide as possible, vertically centred" per that extent. The icon grid and gauge grid
// split that height between them via flex-grow (avn.css) — no further JS sizing needed for either.
function layoutAvnContent() {
  const panelRect = avnPanel.getBoundingClientRect();
  if (!panelRect.width || !panelRect.height) {
    avnContentEl.classList.remove('placed');
    return;
  }
  const vert = avnContentVerticalExtent();
  avnContentEl.style.width  = panelRect.width + 'px';
  avnContentEl.style.height = vert.height + 'px';
  avnContentEl.style.left   = '0px';
  avnContentEl.style.top    = vert.top + 'px';
  avnContentEl.classList.add('placed');
}

function paintAvnGauges() {
  paintAvnGauge(avnGaugeFuel, avnData.fuel, 0.25, 0.10);
  paintAvnGauge(avnGaugeRpm,  avnData.rpm,  null, null);   // no caution/critical — low RPM at idle is normal, not a warning
  paintAvnGauge(avnGaugeHeat, avnData.heat, null, null);   // no caution/critical — heat has no "low is bad" sense
  paintAvnThrottle();
}

// Needle rotation: the SVG needle is drawn pointing at the gauge's zero position (-135deg, see
// avn.html), so rotating it (v * 270)deg clockwise lands it at -135 + v*270 — the same 270deg
// sweep every dial's tick ring covers (avn.html's shared <defs>).
function avnNeedleAngle(v) { return (v * 270).toFixed(1) + 'deg'; }

// The amber fill arc from zero to v, over .avn-gauge-track's identical curve — the classic SVG
// "circular progress" trick: dasharray = the path's own length L turns it into one dash of length
// L then one gap of length L; dashoffset = L*(1-v) slides that dash so only the first v*L of the
// path (from the zero end, where the path's `d` in avn.html starts) stays visible.
// getTotalLength() is cached per element (a WeakMap, not a data attribute, since the value is a
// number, not markup) — every dial's path is geometrically identical, but reading it straight off
// each element is one line simpler than threading a shared constant through four call sites.
const avnGaugeFillLengths = new WeakMap();
function avnGaugeFillLength(fillEl) {
  let L = avnGaugeFillLengths.get(fillEl);
  if (L === undefined) { L = fillEl.getTotalLength(); avnGaugeFillLengths.set(fillEl, L); }
  return L;
}
function setAvnGaugeFill(fillEl, v) {
  const L = avnGaugeFillLength(fillEl);
  // No unit suffix: both resolve in the path's own user-space coordinate system (the viewBox's
  // 0-100 grid), matching getTotalLength()'s own units. 'px' here would mean CSS pixels of the
  // rendered (cqmin-scaled) box instead, which drifts from L as soon as a dial isn't rendered at
  // exactly 100x100 device px — every size except the reference.
  fillEl.style.strokeDasharray = L;
  fillEl.style.strokeDashoffset = L * (1 - v);
}
// Same dash trick as setAvnGaugeFill, generalized to light up an arbitrary [s, e] slice of the
// path instead of always starting at zero — THRL's reheat overlay uses this to paint only the
// [abStart, value] segment red, on top of the plain green fill underneath. dasharray is an
// explicit [onLen, gapLen] pair (rather than a single L) so the "on" length can be less than L;
// dashoffset lands that dash's start exactly at s by walking it one full period back from there.
function setAvnGaugeFillRange(fillEl, s, e) {
  const L = avnGaugeFillLength(fillEl);
  if (e <= s) { fillEl.style.strokeDasharray = '0 ' + L; return; }
  const onLen = (e - s) * L;
  const period = onLen + L;
  fillEl.style.strokeDasharray = onLen + ' ' + L;
  fillEl.style.strokeDashoffset = period - s * L;
}

function paintAvnGauge(gaugeEl, value01, cautionAt, criticalAt) {
  const needle = gaugeEl.querySelector('.avn-gauge-needle');
  const fill   = gaugeEl.querySelector('.avn-gauge-fill');
  const valEl  = gaugeEl.querySelector('.avn-gauge-val');
  gaugeEl.classList.remove('na', 'caution', 'critical');
  if (typeof value01 !== 'number' || value01 < 0) {
    gaugeEl.classList.add('na');
    needle.style.transform = 'rotate(' + avnNeedleAngle(0) + ')';
    setAvnGaugeFill(fill, 0);
    valEl.textContent = '--';
    return;
  }
  const v = Math.max(0, Math.min(1, value01));
  if      (criticalAt !== null && v <= criticalAt) gaugeEl.classList.add('critical');
  else if (cautionAt  !== null && v <= cautionAt)  gaugeEl.classList.add('caution');
  needle.style.transform = 'rotate(' + avnNeedleAngle(v) + ')';
  setAvnGaugeFill(fill, v);
  valEl.textContent = Math.round(v * 100) + '%';
}

// THROTTLE is its own paint: AvnThrottlePolicy gives the needle position (`fill`, still 0..1 even
// though it's no longer a bar height), the readout text ('MIL nn%' / red 'AB nn%'), and the zone.
// Non-AB airframes just get a plain 0-100% needle sweep, same as before. The fill itself mirrors
// the old vertical bar's green/red split: the green .avn-gauge-fill only ever runs up to boundary
// (or the raw value, on a plain non-AB bar / while still in MIL), and the red .avn-gauge-fill-hot
// overlay lights up just the [boundary, value] slice once past it — never both past boundary at once.
function paintAvnThrottle() {
  const r = AvnThrottlePolicy.throttleReadout(avnData.throttle, avnData.hasAb, avnData.abStart);
  const needle = avnGaugeThr.querySelector('.avn-gauge-needle');
  const fill   = avnGaugeThr.querySelector('.avn-gauge-fill');
  const fillHot = avnGaugeThr.querySelector('.avn-gauge-fill-hot');
  const valEl  = avnGaugeThr.querySelector('.avn-gauge-val');
  avnGaugeThr.classList.remove('caution', 'critical');
  avnGaugeThr.classList.toggle('na', r.na);
  avnGaugeThr.classList.toggle('ab-active', r.zone === 'ab');
  needle.style.transform = 'rotate(' + avnNeedleAngle(r.fill) + ')';
  setAvnGaugeFill(fill, r.boundary !== null ? Math.min(r.fill, r.boundary) : r.fill);
  setAvnGaugeFillRange(fillHot, r.boundary !== null ? r.boundary : 1, r.fill);
  valEl.textContent = r.text;
}

// ── Shell → page forwarding ──────────────────────────────────────────────────────────
window.addEventListener('message', function(e) {
  const m = e.data;
  if (!m || m.mfd !== true) return;
  if (m.type === 'avn') {
    avnData = {
      name: m.name || null,   // presence-only now — never displayed, just gates the empty state
      fuel:     typeof m.fuel     === 'number' ? m.fuel     : -1,
      throttle: typeof m.throttle === 'number' ? m.throttle : -1,
      heat:     typeof m.heat     === 'number' ? m.heat     : -1,
      rpm:      typeof m.rpm      === 'number' ? m.rpm      : -1,
      hasAb:    m.hasAb === true,
      abStart:  typeof m.abStart === 'number' ? m.abStart : 1,
      gearDown: m.gearDown === true,
      radar:    m.radar    === true,
      guns:     m.guns     === true,
      ignition: m.ignition === true,
      assist:   m.assist   === true,
      turret:   m.turret   === true,
      nvg:      m.nvg      === true,
      navLights: m.navLights === true,
      // compact split pane's current 4-of-8 page (mfd.js avnPaneSlice); absent (full, or no
      // shell) shows all 8 — see layoutAvnIconPaging.
      visible: Array.isArray(m.visible) ? m.visible : null,
      page:  typeof m.page  === 'number' ? m.page  : 1,
      pages: typeof m.pages === 'number' ? m.pages : 1,
    };
    // Full render on aircraft change, or whenever there's no aircraft.
    if (avnLastType !== avnData.name || !avnData.name) renderAvn();
    else { paintAvnGauges(); paintAvnStatus(); layoutAvnIconPaging(); }
  } else if (m.type === 'avn-layout') {
    // Geometry profile from the shell. full forwards the bezel-anchored vertical band
    // (geom.frameTop/frameHeight); compact carries no geometry at all any more.
    layout = (m.layout === 'full') ? 'full' : 'compact';
    document.body.classList.toggle('full', layout === 'full');
    avnFullGeom = (layout === 'full') ? (m.geom || null) : null;
    renderAvn();
  } else if (m.type === 'orient') {
    // App-wide orientation forwarded by the shell (see body.portrait rules in the CSS).
    document.body.classList.toggle('portrait',  m.orientation === 'portrait');
    document.body.classList.toggle('landscape', m.orientation !== 'portrait');
    renderAvn();   // re-layout in case orientation-dependent sizing changed
  }
});

window.addEventListener('resize', renderAvn);
renderAvn();   // initial empty-state paint
