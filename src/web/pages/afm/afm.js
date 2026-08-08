// AFM page — airframe status. A pure reactive renderer driven by the shell over postMessage;
// single source of truth for BOTH layouts (full-screen iframe + split pane). See afm.html for
// the message contract.
//
// compact (default) places name + frame with fixed CSS offsets. full (body.full) overrides the
// name/frame placement from the bezel geometry the shell forwards in 'afm-layout'.

// ── DOM refs ───────────────────────────────────────────────────────────────────────
const afmPanel   = document.getElementById('afm-panel');
const afmHeaderEl = document.getElementById('afm-header');
const afmNameEl  = document.getElementById('afm-name');
const afmFrame   = document.getElementById('afm-frame');
const afmBg      = document.getElementById('afm-bg');
const afmPartsEl = document.getElementById('afm-parts');
const afmEmptyEl = document.getElementById('afm-empty');

// ── State ──────────────────────────────────────────────────────────────────────────
let afmData        = { name: null, parts: null, failures: null };
let layout          = 'compact';   // 'compact' (split pane) | 'full' (full-screen iframe)
let afmFullGeom     = null;        // {headerTop, headerHeight, frameTop, frameHeight} forwarded by the shell in full
let afmLayoutType   = null;
let afmLayoutCache  = Object.create(null);
let afmLayoutTries  = Object.create(null);   // per-type layout-fetch retry counts
let afmPartEls      = Object.create(null);
let afmFailureEls   = [];   // current failure-label DOM nodes, rebuilt each paint (see paintAfmFailures)
let afmBgType = null, afmBgTries = 0, afmBgLoaded = false;   // background-image request/retry state
const AFM_BG_RETRY_CAP = 120;                // ~60 s @ 500 ms — safety bound; the async server capture lands far sooner

// Failure-label placement on the silhouette. The strings themselves vary per aircraft and are
// parsed by afm-failure-policy (side + display text); here we just decide where each column sits.
// Sided failures cluster over their engine (left/right); side-less ones stack in a centre column.
const AFM_FAIL_COL = { L: 0.20, R: 0.80, C: 0.50 };   // silhouette x per column (0..1)
const AFM_FAIL_BASE_CY = 0.78;                        // first row y — over the engines / lower body
const AFM_FAIL_ROW_DY  = 0.07;                        // vertical step when a column stacks
// ponytail: naive upward stack — with many simultaneous failures in one column the labels could
// climb off the top of the silhouette. Fine for the handful the game ever raises at once.

// ── Renderer ───────────────────────────────────────────────────────────────────────
function renderAfm() {
  const type = afmData.name;
  if (!type) {
    afmHeaderEl.classList.remove('placed');
    afmFrame.style.display  = 'none';
    afmEmptyEl.style.display = '';
    afmLayoutType = type;   // record that the empty state is shown, so returning to a plane
    return;                 // (even the SAME type — e.g. respawn) re-triggers a render
  }
  afmHeaderEl.classList.add('placed');
  afmFrame.style.display   = '';
  afmEmptyEl.style.display = 'none';
  afmNameEl.textContent = type;

  // full profile: anchor the header to the top bezel row the shell forwards (headerTop). compact
  // (split pane) uses the CSS band. The frame then follows the header's actual bottom
  // (layoutAfmFrame) — so the silhouette starts below however tall the name band ends up.
  if (layout === 'full' && afmFullGeom && typeof afmFullGeom.headerTop === 'number') {
    afmHeaderEl.style.top       = afmFullGeom.headerTop + 'px';
    afmHeaderEl.style.minHeight = afmFullGeom.headerHeight + 'px';
    afmHeaderEl.style.height    = 'auto';
  } else {
    afmHeaderEl.style.top       = '';
    afmHeaderEl.style.minHeight = '';
    afmHeaderEl.style.height    = '';
  }

  layoutAfmFrame();   // position the silhouette frame just below the header

  afmBg.style.display = '';
  afmPartsEl.style.display = '';

  ensureAfmLayout(type);
  ensureAfmBg(type);   // request the silhouette independently of the layout cache (see afm-bg-policy)
  const layoutDef = afmLayoutCache[type];
  if (!layoutDef || typeof layoutDef === 'string') return;
  if (afmLayoutType !== type) buildAfmParts(type, layoutDef);

  fitAfmPartsToBg();
  paintAfmDamage();
  paintAfmFailures();   // rebuilds + sizes the failure labels
}

// Position the silhouette frame directly below the header's actual bottom. The frame's lower
// edge stays put: the forwarded bezel limit in full, or the CSS bottom in compact.
const AFM_HDR_GAP = 6;
function layoutAfmFrame() {
  const panelTop = afmPanel.getBoundingClientRect().top;
  const frameTop = (afmHeaderEl.getBoundingClientRect().bottom - panelTop) + AFM_HDR_GAP;
  afmFrame.style.top = frameTop + 'px';
  if (layout === 'full' && afmFullGeom && typeof afmFullGeom.frameTop === 'number') {
    const frameBottom = afmFullGeom.frameTop + afmFullGeom.frameHeight;   // fixed lower limit (last bezel sep)
    afmFrame.style.height = Math.max(0, frameBottom - frameTop) + 'px';
  } else {
    afmFrame.style.height = '';   // compact: CSS bottom:12px spans the rest
  }
}

function ensureAfmLayout(type) {
  const cached = afmLayoutCache[type];
  if (cached && typeof cached === 'object') return;   // already loaded
  if (cached === 'pending') return;                   // fetch in flight
  afmLayoutCache[type] = 'pending';
  fetch('/airframe-layout?type=' + encodeURIComponent(type))
    .then(function(r) { if (!r.ok) throw new Error('layout ' + r.status); return r.json(); })
    .then(function(j) { afmLayoutCache[type] = j; afmLayoutTries[type] = 0; renderAfm(); })
    .catch(function() {
      // The airframe is captured ~1 Hz AFTER the plane loads (and its images stream in async),
      // so right after a respawn / plane change the layout can 404 for a beat. Retry until it
      // lands rather than giving up on the first miss, which would leave AFM stuck black.
      const n = (afmLayoutTries[type] || 0) + 1;
      afmLayoutTries[type] = n;
      afmLayoutCache[type] = (n <= 20) ? undefined : 'fail';
      if (n <= 20) setTimeout(function() { if (afmData.name === type) ensureAfmLayout(type); }, 500);
    });
}

// (Re)request the silhouette iff the type we're showing differs from the wanted one. Decoupled
// from the layout cache so switching to an aircraft whose layout is already cached — or whose bg
// PNG lagged the async server capture — still refreshes the silhouette instead of leaving it
// stuck on the previous plane. See afm-bg-policy.js.
function ensureAfmBg(type) {
  if (AfmBgPolicy.shouldRequestBg(afmBgType, type)) setAfmBg(type);
}

// Set the background silhouette image. Retries on error because its capture is async, so it can
// 404 for a moment right after a plane change; cache-busts each retry so a prior 404 doesn't
// stick in the browser cache.
function setAfmBg(type) {
  afmBgType = type; afmBgTries = 0; afmBgLoaded = false;
  afmBg.src = '/airframe?type=' + encodeURIComponent(type) + '&part=__bg';
}
afmBg.onerror = function() {
  if (!AfmBgPolicy.shouldRetryBg(afmData.name, afmBgType, afmBgLoaded, afmBgTries, AFM_BG_RETRY_CAP)) return;
  afmBgTries++;
  const t = afmBgType, v = afmBgTries;
  setTimeout(function() {
    if (afmData.name === t && !afmBgLoaded) afmBg.src = '/airframe?type=' + encodeURIComponent(t) + '&part=__bg&v=' + v;
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
    img.onerror = function() { if (tries < 20 && afmData.name === type) { tries++; setTimeout(attempt, 500); } };
    img.src = url;
  })();
}

function buildAfmParts(type, layoutDef) {
  afmPartsEl.innerHTML = '';
  afmPartEls = Object.create(null);
  if (!layoutDef || !Array.isArray(layoutDef.parts)) { afmLayoutType = type; return; }
  for (const p of layoutDef.parts) {
    const el = document.createElement('div');
    el.className = 'afm-part';
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
    afmPartsEl.appendChild(el);
    afmPartEls[p.n] = el;
  }
  // Failure labels are (re)built by paintAfmFailures from the live failure list — buildAfmParts
  // just cleared them along with the parts (innerHTML = ''), so drop our stale references.
  afmFailureEls = [];
  afmLayoutType = type;
}

function sizeAfmFailures() {
  const h = afmPartsEl.getBoundingClientRect().height;
  if (h <= 0) return;
  const px = Math.max(11, h * 0.045);
  for (const el of afmFailureEls) el.style.fontSize = px.toFixed(1) + 'px';
}

function fitAfmPartsToBg() {
  const fr = afmFrame.getBoundingClientRect();
  if (!fr.width || !fr.height || !afmBg.naturalWidth || !afmBg.naturalHeight) {
    afmPartsEl.style.width = fr.width + 'px';
    afmPartsEl.style.height = fr.height + 'px';
    return;
  }
  const imgAspect = afmBg.naturalWidth / afmBg.naturalHeight;
  const frAspect  = fr.width / fr.height;
  let w, h;
  if (imgAspect > frAspect) { w = fr.width;  h = fr.width  / imgAspect; }
  else                      { h = fr.height; w = fr.height * imgAspect; }
  afmPartsEl.style.width  = w + 'px';
  afmPartsEl.style.height = h + 'px';
}
afmBg.addEventListener('load', function() {
  afmBgLoaded = true;   // silhouette for afmBgType is up — stop the retry loop
  fitAfmPartsToBg();
  sizeAfmFailures();
});

function paintAfmDamage() {
  const map = Object.create(null);
  if (Array.isArray(afmData.parts)) {
    for (const p of afmData.parts) map[p.n] = p;
  }
  for (const name in afmPartEls) {
    const el = afmPartEls[name];
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

function paintAfmFailures() {
  // Failures are arbitrary per-aircraft strings, so render whatever is active rather than
  // matching a fixed table. Rebuild the labels each paint: side-column + stacked row per column.
  for (const el of afmFailureEls) el.remove();
  afmFailureEls = [];
  const active = Array.isArray(afmData.failures) ? afmData.failures : null;
  if (!active || !active.length) return;
  const rowInCol = { L: 0, R: 0, C: 0 };
  for (const name of active) {
    const side = AfmFailurePolicy.failureSide(name) || 'C';
    const row  = rowInCol[side]++;
    const el = document.createElement('div');
    el.className = 'afm-failure active';
    el.textContent = AfmFailurePolicy.failureText(name);
    el.style.left = (AFM_FAIL_COL[side] * 100).toFixed(3) + '%';
    el.style.top  = ((AFM_FAIL_BASE_CY - row * AFM_FAIL_ROW_DY) * 100).toFixed(3) + '%';
    afmPartsEl.appendChild(el);
    afmFailureEls.push(el);
  }
  sizeAfmFailures();
}

// ── Shell → page forwarding ──────────────────────────────────────────────────────────
window.addEventListener('message', function(e) {
  const m = e.data;
  if (!m || m.mfd !== true) return;
  if (m.type === 'afm') {
    afmData = {
      name: m.name || null,
      parts: Array.isArray(m.parts) ? m.parts : null,
      failures: Array.isArray(m.failures) ? m.failures : null,
    };
    // Full render on aircraft change, or whenever there's no aircraft — the empty-state hide lives
    // in renderAfm and must run even if a silhouette layout never cached (afmLayoutType stays null).
    if (afmLayoutType !== afmData.name || !afmData.name) renderAfm();
    else { paintAfmDamage(); paintAfmFailures(); }
  } else if (m.type === 'afm-layout') {
    // Geometry profile from the shell. full forwards the bezel-anchored name/frame placement;
    // compact omits geom and the page falls back to the CSS fixed offsets.
    layout = (m.layout === 'full') ? 'full' : 'compact';
    document.body.classList.toggle('full', layout === 'full');
    afmFullGeom = m.geom || null;
    renderAfm();
  } else if (m.type === 'orient') {
    // App-wide orientation forwarded by the shell (see body.portrait rules in the CSS).
    document.body.classList.toggle('portrait',  m.orientation === 'portrait');
    document.body.classList.toggle('landscape', m.orientation !== 'portrait');
    renderAfm();   // re-layout in case orientation-dependent sizing changed
  }
});

window.addEventListener('resize', renderAfm);
renderAfm();   // initial empty-state paint
