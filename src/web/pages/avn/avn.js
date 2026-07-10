// AVN page — avionics. A pure reactive renderer driven by the shell over postMessage; single
// source of truth for BOTH layouts (full-screen iframe + split pane). The full-view overlay
// twin in MfdPage.cs is gone. See avn.html for the message contract.
//
// compact (default) places name + frame with fixed CSS offsets. full (body.full) overrides the
// name/frame placement from the bezel geometry the shell forwards in 'avn-layout'.

// ── DOM refs ───────────────────────────────────────────────────────────────────────
const avnPanel    = document.getElementById('avn-panel');
const avnNameEl   = document.getElementById('avn-name');
const avnFrame    = document.getElementById('avn-frame');
const avnBg       = document.getElementById('avn-bg');
const avnPartsEl  = document.getElementById('avn-parts');
const avnEmptyEl  = document.getElementById('avn-empty');
const avnFuelBar  = document.getElementById('avn-fuel-bar');
const avnFuelFill = document.getElementById('avn-fuel-fill');
const avnFuelVal  = document.getElementById('avn-fuel-val');
const avnThrBar   = document.getElementById('avn-thr-bar');
const avnThrFill  = document.getElementById('avn-thr-fill');
const avnThrVal   = document.getElementById('avn-thr-val');
const avnHeaderEl  = document.getElementById('avn-header');
const avnTileGear  = document.getElementById('avn-tile-gear');
const avnTileRadar = document.getElementById('avn-tile-radar');
const avnTileGuns  = document.getElementById('avn-tile-guns');
const avnTileEng    = document.getElementById('avn-tile-eng');
const avnTileAssist = document.getElementById('avn-tile-assist');
const avnTileNvg    = document.getElementById('avn-tile-nvg');
const avnTileLights = document.getElementById('avn-tile-lights');
const avnTileTurret = document.getElementById('avn-tile-turret');

// ── State ──────────────────────────────────────────────────────────────────────────
let avnData = { name: null, parts: null, failures: null, fuel: -1, throttle: -1, gearDown: false, radar: false, guns: false, ignition: false, assist: false, turret: false, nvg: false, navLights: false };
let layout         = 'compact';   // 'compact' (split pane) | 'full' (full-screen iframe)
let avnFullGeom    = null;        // {headerTop, headerHeight, frameTop, frameHeight} forwarded by the shell in full
let avnLayoutType  = null;
let avnLayoutCache = Object.create(null);
let avnLayoutTries = Object.create(null);   // per-type layout-fetch retry counts
let avnPartEls     = Object.create(null);
let avnFailureEls  = Object.create(null);
let avnBgType = null, avnBgTries = 0, avnBgLoaded = false;   // background-image request/retry state
const AVN_BG_RETRY_CAP = 120;                // ~60 s @ 500 ms — safety bound; the async server capture lands far sooner

// Known failure messages and how to render them on the silhouette. Mirrors the server's
// failure-message strings; same keys, same positions, so the silhouette reads identically in
// both single-pane and split-pane modes.
const AVN_FAILURE_DEFS = {
  'LEFT ENGINE FIRE':  { text: 'L ENG FIRE', cx: 0.20, cy: 0.78 },
  'RIGHT ENGINE FIRE': { text: 'R ENG FIRE', cx: 0.80, cy: 0.78 },
};

// ── Renderer ───────────────────────────────────────────────────────────────────────
function renderAvn() {
  const type = avnData.name;
  if (!type) {
    avnHeaderEl.classList.remove('placed');
    avnFrame.style.display  = 'none';
    avnEmptyEl.style.display = '';
    avnFuelBar.classList.remove('placed');
    avnThrBar .classList.remove('placed');
    avnLayoutType = type;   // record that the empty state is shown, so returning to a plane
    return;                 // (even the SAME type — e.g. respawn) re-triggers a render
  }
  avnHeaderEl.classList.add('placed');
  avnFrame.style.display   = '';
  avnEmptyEl.style.display = 'none';
  avnNameEl.textContent = type;

  // full profile: anchor the header to the top bezel row the shell forwards (headerTop), with the
  // band height as a MIN so short content stays centred in the row but a wrapped 2-row status can
  // grow past it. compact (split pane) uses the CSS band. The frame then follows the header's
  // actual bottom (layoutAvnFrame) — so the silhouette starts below however tall the status ends up.
  if (layout === 'full' && avnFullGeom && typeof avnFullGeom.headerTop === 'number') {
    avnHeaderEl.style.top       = avnFullGeom.headerTop + 'px';
    avnHeaderEl.style.minHeight = avnFullGeom.headerHeight + 'px';
    avnHeaderEl.style.height    = 'auto';
  } else {
    avnHeaderEl.style.top       = '';
    avnHeaderEl.style.minHeight = '';
    avnHeaderEl.style.height    = '';
  }

  // Colour the status tiles here (alongside the name) so they update even while the silhouette
  // layout is still fetching (the early return below). Placement is handled by the header flexbox.
  paintAvnStatus();
  layoutAvnFrame();   // position the silhouette frame just below the (possibly wrapped) header

  avnBg.style.display = '';
  avnPartsEl.style.display = '';

  ensureAvnLayout(type);
  ensureAvnBg(type);   // request the silhouette independently of the layout cache (see avn-bg-policy)
  const layoutDef = avnLayoutCache[type];
  if (!layoutDef || typeof layoutDef === 'string') return;
  if (avnLayoutType !== type) buildAvnParts(type, layoutDef);

  fitAvnPartsToBg();
  sizeAvnFailures();
  paintAvnDamage();
  paintAvnFailures();
  layoutAvnBars();
  paintAvnBars();
}

// Position the silhouette frame directly below the header's actual bottom, so the status row can
// wrap to two lines (8 tiles on a narrow screen) without ever overlapping the silhouette. The
// frame's lower edge stays put: the forwarded bezel limit in full, or the CSS bottom in compact.
const AVN_HDR_GAP = 6;
function layoutAvnFrame() {
  const panelTop = avnPanel.getBoundingClientRect().top;
  const frameTop = (avnHeaderEl.getBoundingClientRect().bottom - panelTop) + AVN_HDR_GAP;
  avnFrame.style.top = frameTop + 'px';
  if (layout === 'full' && avnFullGeom && typeof avnFullGeom.frameTop === 'number') {
    const frameBottom = avnFullGeom.frameTop + avnFullGeom.frameHeight;   // fixed lower limit (last bezel sep)
    avnFrame.style.height = Math.max(0, frameBottom - frameTop) + 'px';
  } else {
    avnFrame.style.height = '';   // compact: CSS bottom:12px spans the rest
  }
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

function ensureAvnLayout(type) {
  const cached = avnLayoutCache[type];
  if (cached && typeof cached === 'object') return;   // already loaded
  if (cached === 'pending') return;                   // fetch in flight
  avnLayoutCache[type] = 'pending';
  fetch('/airframe-layout?type=' + encodeURIComponent(type))
    .then(function(r) { if (!r.ok) throw new Error('layout ' + r.status); return r.json(); })
    .then(function(j) { avnLayoutCache[type] = j; avnLayoutTries[type] = 0; renderAvn(); })
    .catch(function() {
      // The airframe is captured ~1 Hz AFTER the plane loads (and its images stream in async),
      // so right after a respawn / plane change the layout can 404 for a beat. Retry until it
      // lands rather than giving up on the first miss, which would leave AVN stuck black.
      const n = (avnLayoutTries[type] || 0) + 1;
      avnLayoutTries[type] = n;
      avnLayoutCache[type] = (n <= 20) ? undefined : 'fail';
      if (n <= 20) setTimeout(function() { if (avnData.name === type) ensureAvnLayout(type); }, 500);
    });
}

// (Re)request the silhouette iff the type we're showing differs from the wanted one. Decoupled
// from the layout cache (unlike before) so switching to an aircraft whose layout is already
// cached — or whose bg PNG lagged the async server capture — still refreshes the silhouette
// instead of leaving it stuck on the previous plane. See avn-bg-policy.js.
function ensureAvnBg(type) {
  if (AvnBgPolicy.shouldRequestBg(avnBgType, type)) setAvnBg(type);
}

// Set the background silhouette image. Retries on error because its capture is async, so it can
// 404 for a moment right after a plane change; cache-busts each retry so a prior 404 doesn't
// stick in the browser cache.
function setAvnBg(type) {
  avnBgType = type; avnBgTries = 0; avnBgLoaded = false;
  avnBg.src = '/airframe?type=' + encodeURIComponent(type) + '&part=__bg';
}
avnBg.onerror = function() {
  if (!AvnBgPolicy.shouldRetryBg(avnData.name, avnBgType, avnBgLoaded, avnBgTries, AVN_BG_RETRY_CAP)) return;
  avnBgTries++;
  const t = avnBgType, v = avnBgTries;
  setTimeout(function() {
    if (avnData.name === t && !avnBgLoaded) avnBg.src = '/airframe?type=' + encodeURIComponent(t) + '&part=__bg&v=' + v;
  }, 500);
};

// Point a part's CSS mask at its sprite, but preload via Image() first so a not-yet-ready
// (async) sprite is retried rather than sticking as an empty mask. Cache-busts each retry.
function setPartMask(el, type, partName) {
  let tries = 0;
  (function attempt() {
    const url = '/airframe?type=' + encodeURIComponent(type) + '&part=' + encodeURIComponent(partName) + (tries ? '&v=' + tries : '');
    const img = new Image();
    img.onload  = function() { el.style.webkitMaskImage = 'url("' + url + '")'; el.style.maskImage = 'url("' + url + '")'; };
    img.onerror = function() { if (tries < 20 && avnData.name === type) { tries++; setTimeout(attempt, 500); } };
    img.src = url;
  })();
}

function buildAvnParts(type, layoutDef) {
  avnPartsEl.innerHTML = '';
  avnPartEls = Object.create(null);
  if (!layoutDef || !Array.isArray(layoutDef.parts)) { avnLayoutType = type; return; }
  for (const p of layoutDef.parts) {
    const el = document.createElement('div');
    el.className = 'avn-part';
    el.dataset.rt = p.rt;
    el.style.left   = (p.cx * 100).toFixed(3) + '%';
    el.style.top    = (p.cy * 100).toFixed(3) + '%';
    el.style.width  = (p.w  * 100).toFixed(3) + '%';
    el.style.height = (p.h  * 100).toFixed(3) + '%';
    const sx = (p.sx === -1) ? -1 : 1;
    const sy = (p.sy === -1) ? -1 : 1;
    const parts = ['translate(-50%, -50%)'];
    if (sx !== 1 || sy !== 1) parts.push('scale(' + sx + ',' + sy + ')');
    if (p.r)                   parts.push('rotate(' + (-p.r).toFixed(1) + 'deg)');
    el.style.transform = parts.join(' ');
    setPartMask(el, type, p.n);
    avnPartsEl.appendChild(el);
    avnPartEls[p.n] = el;
  }
  avnFailureEls = Object.create(null);
  for (const key in AVN_FAILURE_DEFS) {
    const def = AVN_FAILURE_DEFS[key];
    const el = document.createElement('div');
    el.className = 'avn-failure';
    el.textContent = def.text;
    el.style.left = (def.cx * 100).toFixed(3) + '%';
    el.style.top  = (def.cy * 100).toFixed(3) + '%';
    avnPartsEl.appendChild(el);
    avnFailureEls[key] = el;
  }
  avnLayoutType = type;
}

function sizeAvnFailures() {
  const h = avnPartsEl.getBoundingClientRect().height;
  if (h <= 0) return;
  const px = Math.max(11, h * 0.045);
  for (const name in avnFailureEls) {
    avnFailureEls[name].style.fontSize = px.toFixed(1) + 'px';
  }
}

// Bar geometry shared by the placement (layoutAvnBars) and the portrait frame inset
// (applyAvnFrameInset) so they always agree on where the bars sit. .avn-vbar has a fixed
// 42px CSS width; edgeInset matches the clamp in layoutAvnBars. AVN_BAR_SILHOUETTE_GAP is
// the breathing room kept between a bar's inner edge and the silhouette in portrait.
const AVN_BAR_W = 42;
const AVN_BAR_SILHOUETTE_GAP = 15;
function avnBarGap() { return Math.max(8, Math.round(avnPanel.getBoundingClientRect().width * 0.012)); }
function avnBarEdgeInset() { return avnBarGap() + 7; }   // 7 ≈ tick gutter (5px) + 2px margin

// In portrait the silhouette would fill the full frame width and slide under the FUEL/
// THROTTLE bars pinned at the panel edges. Pull the frame in on each side by the bar zone
// plus AVN_BAR_SILHOUETTE_GAP so the silhouette (bg + part masks, both sized to the frame)
// stays clear of the bars. Cleared in landscape, which flanks a narrow silhouette.
function applyAvnFrameInset() {
  if (document.body.classList.contains('portrait')) {
    const inset = avnBarEdgeInset() + AVN_BAR_W + AVN_BAR_SILHOUETTE_GAP;
    avnFrame.style.left  = inset + 'px';
    avnFrame.style.right = inset + 'px';
  } else {
    avnFrame.style.left  = '';
    avnFrame.style.right = '';
  }
}

function fitAvnPartsToBg() {
  applyAvnFrameInset();
  const fr = avnFrame.getBoundingClientRect();
  if (!fr.width || !fr.height || !avnBg.naturalWidth || !avnBg.naturalHeight) {
    avnPartsEl.style.width = fr.width + 'px';
    avnPartsEl.style.height = fr.height + 'px';
    return;
  }
  const imgAspect = avnBg.naturalWidth / avnBg.naturalHeight;
  const frAspect  = fr.width / fr.height;
  let w, h;
  if (imgAspect > frAspect) { w = fr.width;  h = fr.width  / imgAspect; }
  else                      { h = fr.height; w = fr.height * imgAspect; }
  avnPartsEl.style.width  = w + 'px';
  avnPartsEl.style.height = h + 'px';
}
avnBg.addEventListener('load', function() {
  avnBgLoaded = true;   // silhouette for avnBgType is up — stop the retry loop
  fitAvnPartsToBg();
  sizeAvnFailures();
  layoutAvnBars();
  paintAvnBars();
});

function paintAvnDamage() {
  const map = Object.create(null);
  if (Array.isArray(avnData.parts)) {
    for (const p of avnData.parts) map[p.n] = p;
  }
  for (const name in avnPartEls) {
    const el = avnPartEls[name];
    const data = map[name];
    const rt = +el.dataset.rt || 30;
    if (data && data.d) {
      el.style.backgroundColor = 'rgb(178, 0, 64)';
      el.style.opacity = '1';
      continue;
    }
    const hp = data ? data.hp : 100;
    const cond = Math.max((hp - rt) / (100 - rt), 0);
    const g = Math.min(cond * 2, 1);
    el.style.backgroundColor = 'rgb(255,' + Math.round(g * 255) + ',0)';
    el.style.opacity = (1 - cond).toFixed(3);
  }
}

function paintAvnFailures() {
  const set = Object.create(null);
  if (Array.isArray(avnData.failures))
    for (const name of avnData.failures) set[name] = true;
  for (const name in avnFailureEls) {
    avnFailureEls[name].classList.toggle('active', !!set[name]);
  }
}

function layoutAvnBars() {
  const partsRect = avnPartsEl.getBoundingClientRect();
  const frameRect = avnFrame.getBoundingClientRect();
  if (!partsRect.width || !partsRect.height || !frameRect.height) {
    avnFuelBar.classList.remove('placed');
    avnThrBar .classList.remove('placed');
    return;
  }
  const panelRect = avnPanel.getBoundingClientRect();
  const gap = avnBarGap();
  const topInPanel = frameRect.top - panelRect.top;

  const barW = avnFuelBar.offsetWidth || AVN_BAR_W;
  const edgeInset = avnBarEdgeInset();             // 7 ≈ tick gutter width (5px) + its 2px margin
  const edgePos = panelRect.width - barW - edgeInset;   // flush against the panel edge

  // Portrait: the silhouette fills the width (and is inset to clear the bars — see
  // applyAvnFrameInset), so pin the bars to the panel edges. Landscape: flank the narrow
  // silhouette, anchoring to its measured edges, clamped so a bar can never spill outside.
  const portrait = document.body.classList.contains('portrait');
  let fuelRight, thrLeft;
  if (portrait) {
    fuelRight = edgePos;
    thrLeft   = edgePos;
  } else {
    fuelRight = Math.max(edgeInset, Math.min(panelRect.right - (partsRect.left - gap), edgePos));
    thrLeft   = Math.max(edgeInset, Math.min((partsRect.right + gap) - panelRect.left, edgePos));
  }

  // Portrait: shorten the bars to 80% of the frame height and re-center them vertically so
  // they read tighter against the aircraft. Landscape keeps the full silhouette height.
  const barH   = portrait ? frameRect.height * 0.8 : frameRect.height;
  const barTop = topInPanel + (frameRect.height - barH) / 2;

  avnFuelBar.style.right  = fuelRight + 'px';
  avnFuelBar.style.top    = barTop + 'px';
  avnFuelBar.style.height = barH + 'px';
  avnFuelBar.classList.add('placed');

  avnThrBar.style.left   = thrLeft + 'px';
  avnThrBar.style.top    = barTop + 'px';
  avnThrBar.style.height = barH + 'px';
  avnThrBar.classList.add('placed');
}

function paintAvnBars() {
  paintAvnBar(avnFuelBar, avnFuelFill, avnFuelVal, avnData.fuel,     0.25, 0.10);
  paintAvnBar(avnThrBar,  avnThrFill,  avnThrVal,  avnData.throttle, null, null);
}

function paintAvnBar(barEl, fillEl, valEl, value01, cautionAt, criticalAt) {
  barEl.classList.remove('na', 'caution', 'critical');
  if (typeof value01 !== 'number' || value01 < 0) {
    barEl.classList.add('na');
    fillEl.style.height = '0%';
    valEl.textContent = '--';
    positionAvnBarValue(barEl, valEl, 0);
    return;
  }
  const v = Math.max(0, Math.min(1, value01));
  if      (criticalAt !== null && v <= criticalAt) barEl.classList.add('critical');
  else if (cautionAt  !== null && v <= cautionAt)  barEl.classList.add('caution');
  fillEl.style.height = (v * 100).toFixed(1) + '%';
  valEl.textContent = Math.round(v * 100) + '%';
  positionAvnBarValue(barEl, valEl, v);
}

// Slide the % readout so its vertical centre sits on the fill's top tip. Derived from the
// tube's box (not the fill's animated rect) so it tracks the target level immediately; the
// CSS `top` transition then carries it in step with the fill's height animation.
function positionAvnBarValue(barEl, valEl, v) {
  const tube = barEl.querySelector('.avn-vbar-tube');
  if (!tube) return;
  const tubeRect = tube.getBoundingClientRect();
  if (!tubeRect.height) return;
  const PAD = 6, BORDER = 2;                          // mirror .avn-vbar-tube padding + border
  const fillBottomY = tubeRect.bottom - BORDER - PAD; // viewport y of the fill's base
  const innerH      = tubeRect.height - 2 * BORDER;   // padding-box height the fill % spans
  const tipY        = fillBottomY - v * innerH;       // viewport y of the fill's top tip
  valEl.style.top = (tipY - barEl.getBoundingClientRect().top) + 'px';
}

// ── Shell → page forwarding ──────────────────────────────────────────────────────────
window.addEventListener('message', function(e) {
  const m = e.data;
  if (!m || m.mfd !== true) return;
  if (m.type === 'avn') {
    avnData = {
      name: m.name || null,
      parts: Array.isArray(m.parts) ? m.parts : null,
      failures: Array.isArray(m.failures) ? m.failures : null,
      fuel:     typeof m.fuel     === 'number' ? m.fuel     : -1,
      throttle: typeof m.throttle === 'number' ? m.throttle : -1,
      gearDown: m.gearDown === true,
      radar:    m.radar    === true,
      guns:     m.guns     === true,
      ignition: m.ignition === true,
      assist:   m.assist   === true,
      turret:   m.turret   === true,
      nvg:      m.nvg      === true,
      navLights: m.navLights === true,
    };
    // Full render on aircraft change, or whenever there's no aircraft — the empty-state hide lives
    // in renderAvn and must run even if a silhouette layout never cached (avnLayoutType stays null).
    if (avnLayoutType !== avnData.name || !avnData.name) renderAvn();
    else { paintAvnDamage(); paintAvnFailures(); paintAvnBars(); paintAvnStatus(); }
  } else if (m.type === 'avn-layout') {
    // Geometry profile from the shell. full forwards the bezel-anchored name/frame placement;
    // compact omits geom and the page falls back to the CSS fixed offsets.
    layout = (m.layout === 'full') ? 'full' : 'compact';
    document.body.classList.toggle('full', layout === 'full');
    avnFullGeom = m.geom || null;
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
