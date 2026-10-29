// SYS page (id afm) — airframe status, pylon weapons and the system toggles. Reactive renderer
// driven by the shell over postMessage.
// compact (default) places name + frame with fixed CSS offsets. full (body.full) overrides
// name/frame placement from the bezel geometry the shell forwards in 'afm-layout'.

// ── DOM refs ───────────────────────────────────────────────────────────────────────
const afmPanel   = document.getElementById('afm-panel');
const afmHeaderEl = document.getElementById('afm-header');
const afmNameEl  = document.getElementById('afm-name');
const afmFrontSection  = document.getElementById('afm-front-section');
const afmFrontEl       = document.getElementById('afm-front');
const afmFrontMarkersEl = document.getElementById('afm-front-markers');
const afmPylonRowEl    = document.getElementById('afm-pylon-row');
const afmPylonSideEls  = [document.getElementById('afm-pylon-left'), document.getElementById('afm-pylon-right')];
const afmPylonLinksEl  = document.getElementById('afm-pylon-links');
let afmPylonSides = false;   // the outer two pylons' boxes sit beside the front view
let afmPylonBoxEls  = Object.create(null);   // pylon name -> its box
let afmPylonLinkEls = Object.create(null);   // pylon name -> its <polyline>
const afmFrame   = document.getElementById('afm-frame');
const afmBg      = document.getElementById('afm-bg');
const afmPartsEl = document.getElementById('afm-parts');
const afmEmptyEl = document.getElementById('afm-empty');
const afmFlagCols = [document.getElementById('afm-flags-left'), document.getElementById('afm-flags-right')];
// [button, snapshot field]; each button's data-group is the avn.toggle group and the kind
// AvnStatusPolicy.tileClass expects.
const AFM_FLAGS = [['gear', 'gearDown'], ['radar', 'radar'], ['guns', 'guns'], ['eng', 'ignition'],
                   ['assist', 'assist'], ['nvg', 'nvg'], ['lights', 'navLights'], ['turret', 'turret']]
  .map(function(pair) { return { el: document.getElementById('afm-flag-' + pair[0]), kind: pair[0], field: pair[1] }; });
AFM_FLAGS.forEach(function(f) {
  f.el.addEventListener('click', function() {
    sendCommand('avn.toggle', { group: f.kind }).catch(function() {});
  });
});

// ── State ──────────────────────────────────────────────────────────────────────────
let afmData        = { name: null, parts: null, failures: null, pylons: null, flags: null };
let layout          = 'compact';   // 'compact' (split pane) | 'full' (full-screen iframe)
let afmFullGeom     = null;        // {headerTop, headerHeight, frameTop, frameHeight} from the shell in full
let afmLayoutType   = null;
let afmLayoutCache  = Object.create(null);
let afmLayoutTries  = Object.create(null);
let afmPartEls      = Object.create(null);
let afmFailureEls   = [];
let afmBgType = null, afmBgTries = 0, afmBgLoaded = false;
const AFM_BG_RETRY_CAP = 120;   // ~60s @ 500ms; safety bound, not the expected latency
let afmFrontType = null, afmFrontTries = 0, afmFrontLoaded = false;
let afmFrontLayoutType  = null;
let afmFrontLayoutCache = Object.create(null);
let afmFrontLayoutTries = Object.create(null);
let afmFrontMarkerEls   = Object.create(null);

// Failure label placement: sided failures cluster over their engine, side-less ones center-stack.
const AFM_FAIL_COL = { L: 0.20, R: 0.80, C: 0.50 };
const AFM_FAIL_BASE_CY = 0.78;
const AFM_FAIL_ROW_DY  = 0.07;
// ponytail: naive upward stack — many simultaneous failures in one column could climb off-frame.

// ── Renderer ───────────────────────────────────────────────────────────────────────
function renderAfm() {
  const type = afmData.name;
  if (!type) {
    afmHeaderEl.classList.remove('placed');
    afmFrontSection.style.display = 'none';
    afmFrame.style.display  = 'none';
    afmPylonRowEl.style.display = 'none';
    for (const el of afmPylonSideEls) el.style.display = 'none';
    afmPylonLinksEl.style.display = 'none';
    for (const col of afmFlagCols) col.classList.remove('placed');
    afmEmptyEl.style.display = '';
    afmLayoutType = type;   // record that the empty state is shown, so returning to a plane
    return;                 // (even the SAME type — e.g. respawn) re-triggers a render
  }
  afmHeaderEl.classList.add('placed');
  afmFrontSection.style.display = '';
  afmFrame.style.display   = '';
  afmPylonRowEl.style.display = '';
  for (const el of afmPylonSideEls) el.style.display = afmPylonSides ? '' : 'none';
  afmPylonLinksEl.style.display = '';
  for (const col of afmFlagCols) col.classList.add('placed');
  afmEmptyEl.style.display = 'none';
  afmNameEl.textContent = type;

  // full profile: anchor the header to the top of the bezel row the shell forwards (headerTop),
  // but only as tall as the CSS band, not the whole row, so the silhouettes get the rest. compact
  // (split pane) uses the CSS band as is. The silhouette stack then follows the header's actual
  // bottom (layoutAfmFrame).
  if (layout === 'full' && afmFullGeom && typeof afmFullGeom.headerTop === 'number') {
    afmHeaderEl.style.top       = afmFullGeom.headerTop + 'px';
    afmHeaderEl.style.minHeight = '';
    afmHeaderEl.style.height    = 'auto';
  } else {
    afmHeaderEl.style.top       = '';
    afmHeaderEl.style.minHeight = '';
    afmHeaderEl.style.height    = '';
  }

  layoutAfmFrame();   // position the silhouette frame + front-section below the header

  afmBg.style.display = '';
  afmPartsEl.style.display = '';

  ensureAfmLayout(type);
  ensureAfmBg(type);   // request the silhouette independently of the layout cache (see afm-bg-policy)
  ensureAfmFront(type);
  ensureAfmFrontLayout(type);
  const layoutDef = afmLayoutCache[type];
  if (layoutDef && typeof layoutDef === 'object' && afmLayoutType !== type) buildAfmParts(type, layoutDef);
  const frontLayoutDef = afmFrontLayoutCache[type];
  if (frontLayoutDef && typeof frontLayoutDef === 'object' && afmFrontLayoutType !== type) buildAfmFrontMarkers(type, frontLayoutDef);

  fitAfmPartsToBg();
  fitAfmFrontToImg();
  paintAfmDamage();
  paintAfmFailures(true);   // rebuilds + sizes the failure labels; forced -- see its own comment
  paintAfmPylons();
  paintAfmFlags();
  drawAfmPylonLinks();
}

// Position the top-down silhouette frame directly below the header's actual bottom, and the
// front-section band below it, ending at the stack's lower edge: the forwarded bezel limit in
// full, or the CSS bottom in compact, less the pylon row reserved at the very bottom. The front
// band and the row get modest shares of the vertical room (capped both ways) so the top-down
// silhouette — the primary content — keeps the majority of the space in both profiles.
const AFM_HDR_GAP  = 6;
const AFM_BAND_GAP = 6;
const AFM_ROW_GAP  = 36;   // the link lines' horizontal tracks run here, below the frontal image
function layoutAfmFrame() {
  const panelTop = afmPanel.getBoundingClientRect().top;
  const stackTop = (afmHeaderEl.getBoundingClientRect().bottom - panelTop) + AFM_HDR_GAP;

  let bottomLimit;
  if (layout === 'full' && afmFullGeom && typeof afmFullGeom.frameTop === 'number') {
    bottomLimit = afmFullGeom.frameTop + afmFullGeom.frameHeight;   // fixed lower limit (last bezel sep)
  } else {
    bottomLimit = afmPanel.getBoundingClientRect().height - 12;     // matches the compact CSS bottom:12px
  }
  const available   = Math.max(0, bottomLimit - stackTop);
  const rowHeight   = Math.min(56, Math.max(28, available * 0.06));
  const frontHeight = Math.min(200, Math.max(90, available * 0.3));
  const frameHeight = Math.max(0, available - rowHeight - AFM_ROW_GAP - frontHeight - AFM_BAND_GAP);

  afmFrame.style.top    = stackTop + 'px';
  afmFrame.style.bottom = 'auto';
  afmFrame.style.height = frameHeight + 'px';

  afmFrontSection.style.top    = (stackTop + frameHeight + AFM_BAND_GAP) + 'px';
  afmFrontSection.style.bottom = 'auto';
  afmFrontSection.style.height = frontHeight + 'px';

  afmPylonRowEl.style.top    = (bottomLimit - rowHeight) + 'px';
  afmPylonRowEl.style.height = rowHeight + 'px';
  for (const el of afmPylonSideEls) {
    el.style.top    = (stackTop + frameHeight + AFM_BAND_GAP + (frontHeight - rowHeight) / 2) + 'px';
    el.style.height = rowHeight + 'px';
  }
  sizeAfmPylonSides();

  // The toggle columns centre on the top-down silhouette's band.
  for (const col of afmFlagCols) {
    col.style.top    = stackTop + 'px';
    col.style.height = frameHeight + 'px';
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

// Front-section's small frontal silhouette — same request/retry shape as the big damage bg
// (AfmBgPolicy's predicates are generic, not tied to one image element), just a different part
// key and element.
function ensureAfmFront(type) {
  if (AfmBgPolicy.shouldRequestBg(afmFrontType, type)) setAfmFront(type);
}
function setAfmFront(type) {
  afmFrontType = type; afmFrontTries = 0; afmFrontLoaded = false;
  afmFrontEl.classList.remove('loaded');
  afmFrontEl.src = '/airframe?type=' + encodeURIComponent(type) + '&part=__front';
}
afmFrontEl.addEventListener('load', function() {
  afmFrontLoaded = true;
  afmFrontEl.classList.add('loaded');
  fitAfmFrontToImg();
  drawAfmPylonLinks();
});
afmFrontEl.onerror = function() {
  if (!AfmBgPolicy.shouldRetryBg(afmData.name, afmFrontType, afmFrontLoaded, afmFrontTries, AFM_BG_RETRY_CAP)) return;
  afmFrontTries++;
  const t = afmFrontType, v = afmFrontTries;
  setTimeout(function() {
    if (afmData.name === t && !afmFrontLoaded) afmFrontEl.src = '/airframe?type=' + encodeURIComponent(t) + '&part=__front&v=' + v;
  }, 500);
};

// Pylon-marker layout for the frontal silhouette (hardpoint_* positions, captured once — see
// AssetCapture.TryCaptureFrontalSilhouette). Requested under type+"__front" so it doesn't collide
// with the damage-silhouette layout cache key. Same retry shape as ensureAfmLayout.
function ensureAfmFrontLayout(type) {
  const cached = afmFrontLayoutCache[type];
  if (cached && typeof cached === 'object') return;   // already loaded
  if (cached === 'pending') return;                   // fetch in flight
  afmFrontLayoutCache[type] = 'pending';
  fetch('/airframe-layout?type=' + encodeURIComponent(type + '__front'))
    .then(function(r) { if (!r.ok) throw new Error('layout ' + r.status); return r.json(); })
    .then(function(j) { afmFrontLayoutCache[type] = j; afmFrontLayoutTries[type] = 0; renderAfm(); })
    .catch(function() {
      const n = (afmFrontLayoutTries[type] || 0) + 1;
      afmFrontLayoutTries[type] = n;
      afmFrontLayoutCache[type] = (n <= 20) ? undefined : 'fail';
      if (n <= 20) setTimeout(function() { if (afmData.name === type) ensureAfmFrontLayout(type); }, 500);
    });
}

function buildAfmFrontMarkers(type, layoutDef) {
  afmFrontMarkersEl.innerHTML = '';
  afmFrontMarkerEls = Object.create(null);
  afmPylonRowEl.innerHTML = '';
  afmPylonLinksEl.innerHTML = '';
  afmPylonBoxEls  = Object.create(null);
  afmPylonLinkEls = Object.create(null);
  if (!layoutDef || !Array.isArray(layoutDef.parts)) { afmFrontLayoutType = type; return; }
  // Boxes go left to right by cx; with 3+ pylons the outermost two move beside the front view.
  const sorted = layoutDef.parts.slice().sort(function(a, b) { return a.cx - b.cx; });
  const n = sorted.length;
  afmPylonSides = n >= 3;
  for (const el of afmPylonSideEls) el.style.display = afmPylonSides ? '' : 'none';
  sorted.forEach(function(p, i) {
    let box;
    if (afmPylonSides && i === 0)          box = afmPylonSideEls[0];
    else if (afmPylonSides && i === n - 1) box = afmPylonSideEls[1];
    else {
      box = document.createElement('div');
      box.className = 'afm-pylon-box';
      afmPylonRowEl.appendChild(box);
    }
    afmPylonBoxEls[p.n] = box;
    const line = document.createElementNS('http://www.w3.org/2000/svg', 'polyline');
    line.setAttribute('class', 'afm-pylon-link');
    afmPylonLinksEl.appendChild(line);
    afmPylonLinkEls[p.n] = line;
  });
  sizeAfmPylonSides();
  for (const p of layoutDef.parts) {
    const el = document.createElement('div');
    el.className = 'afm-front-marker';
    el.style.left   = (p.cx * 100).toFixed(3) + '%';
    el.style.top    = (p.cy * 100).toFixed(3) + '%';
    el.style.width  = (p.w  * 100).toFixed(3) + '%';
    el.style.height = (p.h  * 100).toFixed(3) + '%';
    afmFrontMarkersEl.appendChild(el);
    afmFrontMarkerEls[p.n] = el;
  }
  afmFrontLayoutType = type;
}

// The side boxes match a bottom-row box's width, which the row's flex layout decides.
function sizeAfmPylonSides() {
  const box = afmPylonRowEl.firstElementChild;
  if (!box) return;
  const w = box.getBoundingClientRect().width + 'px';
  for (const el of afmPylonSideEls) el.style.width = w;
}

// Circuit-style links, horizontal and vertical runs only. A marker over its own row box drops
// straight in. Any other row box's line drops from its marker
// to its own horizontal track between the frontal image and the row, runs across, then drops into
// the box's top centre. On each side the outermost pylon takes the highest track, so a side's
// tracks never cross each other's drops. A side box's line runs out level from its marker, then
// turns up or down into the box (or straight into its inner edge when level with it). Positions
// are read back from layout, so this runs after anything that moves the markers or boxes
// (renderAfm, the front image's load), not per tick.
function drawAfmPylonLinks() {
  const panel = afmPanel.getBoundingClientRect();
  const trackTop = afmFrontSection.getBoundingClientRect().bottom;
  const trackBottom = afmPylonRowEl.getBoundingClientRect().top;
  const pts = function(list) {
    return list.map(function(p) { return (p[0] - panel.left).toFixed(1) + ',' + (p[1] - panel.top).toFixed(1); }).join(' ');
  };
  const rowLinks = { L: [], R: [] };
  for (const name in afmPylonLinkEls) {
    const m = afmFrontMarkerEls[name], box = afmPylonBoxEls[name], line = afmPylonLinkEls[name];
    if (!m || !box) continue;
    const mr = m.getBoundingClientRect(), br = box.getBoundingClientRect();
    const x1 = mr.left + mr.width / 2, y1 = mr.top + mr.height / 2;
    const bx = br.left + br.width / 2;
    if (box === afmPylonSideEls[0] || box === afmPylonSideEls[1]) {
      const edge = (box === afmPylonSideEls[0]) ? br.right : br.left;
      if (y1 > br.bottom)   line.setAttribute('points', pts([[x1, y1], [bx, y1], [bx, br.bottom]]));
      else if (y1 < br.top) line.setAttribute('points', pts([[x1, y1], [bx, y1], [bx, br.top]]));
      else                  line.setAttribute('points', pts([[x1, y1], [edge, y1]]));
    } else if (x1 > br.left && x1 < br.right) {
      line.setAttribute('points', pts([[x1, y1], [x1, br.top]]));   // already over its box
    } else {
      rowLinks[bx < x1 ? 'L' : 'R'].push({ line: line, x1: x1, y1: y1, bx: bx, top: br.top });
    }
  }
  for (const side in rowLinks) {
    const links = rowLinks[side].sort(function(a, b) { return Math.abs(b.bx - b.x1) - Math.abs(a.bx - a.x1); });
    const step = (trackBottom - trackTop) / (links.length + 1);
    links.forEach(function(l, i) {
      const ty = trackTop + step * (i + 1);
      l.line.setAttribute('points', pts([[l.x1, l.y1], [l.x1, ty], [l.bx, ty], [l.bx, l.top]]));
    });
  }
}

// Size .afm-front-markers to match the frontal silhouette's actual rendered box (same
// letterboxing math as fitAfmPartsToBg, against the frontal image instead of the damage bg).
function fitAfmFrontToImg() {
  const fr = afmFrontSection.getBoundingClientRect();
  if (!fr.width || !fr.height || !afmFrontEl.naturalWidth || !afmFrontEl.naturalHeight) {
    afmFrontMarkersEl.style.width  = fr.width + 'px';
    afmFrontMarkersEl.style.height = fr.height + 'px';
    return;
  }
  const imgAspect = afmFrontEl.naturalWidth / afmFrontEl.naturalHeight;
  const frAspect  = fr.width / fr.height;
  let w, h;
  if (imgAspect > frAspect) { w = fr.width;  h = fr.width  / imgAspect; }
  else                      { h = fr.height; w = fr.height * imgAspect; }
  afmFrontMarkersEl.style.width  = w + 'px';
  afmFrontMarkersEl.style.height = h + 'px';
}

// Colors each pylon marker from the live snapshot — [{n, s}], s one of 'armed' / 'exhausted' /
// 'empty' (mirroring the cockpit's own weapon-station panel's three states — confirmed live: a gray
// station has nothing mounted at all, distinct from a red one that fired its load). Rendered in the
// page's own solid theme colors, not the game's raw (semi-transparent) hue. Positions are static
// (buildAfmFrontMarkers); only the color updates per tick. Each pylon's link line and the
// weapon name in its box take the same color as its marker.
const AFM_PYLON_COLOR = { armed: 'var(--no-green)', exhausted: 'var(--no-red)', empty: 'var(--no-gray)' };
function paintAfmPylons() {
  const pylons = Array.isArray(afmData.pylons) ? afmData.pylons : null;
  if (!pylons) return;
  for (const p of pylons) {
    const color = AFM_PYLON_COLOR[p.s] || AFM_PYLON_COLOR.empty;
    const el = afmFrontMarkerEls[p.n], box = afmPylonBoxEls[p.n], line = afmPylonLinkEls[p.n];
    if (el)   el.style.backgroundColor = color;
    if (line) line.style.stroke = color;
    if (box) {
      const w = p.w || '';
      if (box.textContent !== w) box.textContent = w;
      box.style.color = color;
    }
  }
}

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
      el.style.backgroundColor = 'var(--no-afm-destroyed)';
      el.style.opacity = '1';
      continue;
    }
    const hp = data ? data.hp : 100;
    const cond = Math.max((hp - rt) / (100 - rt), 0);
    const g = Math.min(cond * 2, 1);
    el.style.backgroundColor = 'color-mix(in srgb, var(--no-afm-damaged-light) ' + Math.round(g * 100) +
      '%, var(--no-afm-damaged-heavy))';
    el.style.opacity = (1 - cond).toFixed(3);
  }
}

// For as long as a failure lasts, the incremental update path (afm message handler below) called
// this every single 'afm' tick, tearing down and recreating its label element(s) the whole time
// instead of just once when the failure set actually changed (docs/web-efficiency-audit.md
// finding 05). `force` is for renderAfm's full-render path (aircraft change / no aircraft), which
// must always rebuild even if the new aircraft's failure set happens to match the old one — the
// existing elements are positioned for the OLD airframe's layout.
let afmFailuresKey = null;
function paintAfmFailures(force) {
  const active = Array.isArray(afmData.failures) ? afmData.failures : null;
  const key = JSON.stringify(active || []);
  if (!force && key === afmFailuresKey) return;
  afmFailuresKey = key;

  // Failures are arbitrary per-aircraft strings, so render whatever is active rather than
  // matching a fixed table. Rebuild the labels each paint: side-column + stacked row per column.
  for (const el of afmFailureEls) el.remove();
  afmFailureEls = [];
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

// Eight booleans that hold for seconds at a time but arrive every 'afm' tick, so repaint only on
// a change. aria-pressed mirrors the lamp for assistive tech.
let afmFlagsKey = null;
function paintAfmFlags() {
  const flags = afmData.flags;
  if (!flags) return;
  const key = AFM_FLAGS.map(function(f) { return !!flags[f.field]; }).join('|');
  if (key === afmFlagsKey) return;
  afmFlagsKey = key;
  for (const f of AFM_FLAGS) {
    const active = !!flags[f.field];
    f.el.classList.remove('on', 'off', 'gear-down');
    f.el.classList.add(AvnStatusPolicy.tileClass(f.kind, active));
    f.el.setAttribute('aria-pressed', active ? 'true' : 'false');
  }
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
      pylons: Array.isArray(m.pylons) ? m.pylons : null,
      flags: m,
    };
    // Full render on aircraft change, or whenever there's no aircraft — the empty-state hide lives
    // in renderAfm and must run even if a silhouette layout never cached (afmLayoutType stays null).
    if (afmLayoutType !== afmData.name || !afmData.name) renderAfm();
    else { paintAfmDamage(); paintAfmFailures(); paintAfmPylons(); paintAfmFlags(); }
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
