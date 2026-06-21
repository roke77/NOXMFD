namespace NORoksMFD
{
    // Bare AVN page served at /avn. Renders the live damage silhouette + failure labels
    // + FUEL/THROTTLE side bars, sized to fill the iframe. Used by the split-screen
    // layout when a pane shows AVN. The shell forwards the live avnData snapshot
    // (mirrored from the map iframe's SSE feed) via postMessage on every update,
    // so this page's render loop is purely reactive — no SSE listener of its own.
    //
    // Positioning differs from the shell's single-pane AVN renderer: there are no
    // bezel keys to anchor against in this iframe, so name + frame use fixed pixel
    // offsets from the iframe edges. layoutAvnBars / fitAvnPartsToBg / paintAvn*
    // are unchanged from the shell.
    internal static class AvnPage
    {
        public const string Html = """
<!DOCTYPE html>
<html>
<head>
<meta charset="utf-8">
<title>NO Roks MFD — AVN</title>
<style>
  html, body { margin: 0; height: 100%; background: #000; overflow: hidden; }
  body {
    color: #39ff14;
    font-family: 'Share Tech Mono', 'Courier New', monospace;
    position: relative;
  }
  .avn-panel {
    position: absolute;
    inset: 0;
    color: #39ff14;
    font-family: 'Share Tech Mono', 'Courier New', monospace;
  }
  .avn-name {
    position: absolute;
    left: 50%;
    top: 18px;
    transform: translateX(-50%);
    color: #39ff14;
    font-size: clamp(20px, 4vh, 32px);
    font-weight: 900;
    letter-spacing: 2px;
    white-space: nowrap;
    z-index: 2;
  }
  .avn-frame {
    position: absolute;
    left: 0; right: 0;
    top: 56px;
    bottom: 12px;
    overflow: hidden;
  }
  .avn-bg, .avn-parts {
    position: absolute;
    top: 50%; left: 50%;
    transform: translate(-50%, -50%);
    pointer-events: none;
  }
  .avn-bg     { max-width: 100%; max-height: 100%; display: block; }
  .avn-parts  { display: block; }
  .avn-part {
    position: absolute;
    transform: translate(-50%, -50%);
    background-color: #ffffff;
    -webkit-mask-repeat: no-repeat;
            mask-repeat: no-repeat;
    -webkit-mask-size: 100% 100%;
            mask-size: 100% 100%;
    -webkit-mask-position: center;
            mask-position: center;
    -webkit-mask-source-type: luminance;
            mask-mode: luminance;
    pointer-events: none;
  }
  .avn-failure {
    position: absolute;
    transform: translate(-50%, -50%);
    display: none;
    color: #ff4040;
    font-weight: 900;
    letter-spacing: 1px;
    white-space: nowrap;
    text-align: center;
    pointer-events: none;
    text-shadow: 0 0 4px rgba(255, 64, 64, 0.5);
  }
  .avn-failure.active { display: block; }

  /* FUEL + THROTTLE side bars. Mirrors the shell's bar styling exactly so the
     bare AVN looks identical to single-pane AVN apart from the surrounding chrome. */
  .avn-vbar {
    position: absolute;
    display: none;
    flex-direction: column;
    align-items: center;
    width: 42px;
    color: #39ff14;
    font-family: 'Share Tech Mono', 'Courier New', monospace;
    text-shadow: 0 0 4px rgba(57, 255, 20, 0.35);
    pointer-events: none;
    z-index: 3;
  }
  .avn-vbar.placed { display: flex; }
  .avn-vbar-label,
  .avn-vbar-value {
    font-size: 16px;
    font-weight: 900;
    letter-spacing: 2px;
    line-height: 1;
    white-space: nowrap;
  }
  .avn-vbar-value { padding: 0 0 20px 0; }
  .avn-vbar-label { padding: 20px 0 0 0; }
  /* Orientation driven by the shell (body.portrait), not @media — a pane iframe's box
     is wide+short and would wrongly read landscape in split mode. */
  body.portrait .avn-vbar-label,
  body.portrait .avn-vbar-value { font-size: clamp(16px, 1.8vh, 25.5px); }
  .avn-vbar-tube {
    position: relative;
    flex: 1 1 auto;
    width: 28px;
    background: #050a05;
    box-sizing: border-box;
    padding: 6px;
    overflow: hidden;
  }
  .avn-vbar.fuel .avn-vbar-tube { border: 2px solid #39ff14; border-right: none; }
  .avn-vbar.thr  .avn-vbar-tube { border: 2px solid #39ff14; border-left:  none; }
  .avn-vbar.thr  .avn-vbar-tube::after,
  .avn-vbar.thr  .avn-vbar-tube::before { content: none; }
  .avn-vbar-fill {
    position: absolute;
    left: 6px; right: 6px; bottom: 6px;
    height: 0%;
    background: #39ff14;
    transition: height 200ms linear, background-color 150ms linear;
  }
  .avn-vbar-tube::after {
    content: '';
    position: absolute;
    inset: 0;
    background-image: repeating-linear-gradient(
      to top,
      transparent 0,
      transparent calc(10% - 6px),
      #050a05 calc(10% - 6px),
      #050a05 10%
    );
    pointer-events: none;
  }
  .avn-vbar-tube::before {
    content: '';
    position: absolute;
    left: 0; right: 0;
    top: 50%;
    height: 1.5px;
    margin-top: 2.25px;
    background: #39ff14;
    z-index: 2;
    pointer-events: none;
  }
  .avn-vbar-ticks {
    position: absolute;
    top: 0; bottom: 0;
    width: 5px;
    pointer-events: none;
  }
  .avn-vbar.fuel .avn-vbar-ticks { right: 100%; margin-right: 2px; }
  .avn-vbar.thr  .avn-vbar-ticks { left:  100%; margin-left:  2px; }
  .avn-vbar-ticks::before {
    content: '';
    position: absolute;
    inset: 0;
    background-image:
      linear-gradient(#39ff14, #39ff14),
      linear-gradient(#39ff14, #39ff14),
      linear-gradient(#39ff14, #39ff14),
      linear-gradient(#39ff14, #39ff14),
      linear-gradient(#39ff14, #39ff14);
    background-repeat: no-repeat;
    background-size: 100% 1px;
    background-position: 0 0%, 0 25%, 0 50%, 0 75%, 0 100%;
  }
  .avn-vbar.caution  .avn-vbar-fill { background: #ffaa00; }
  .avn-vbar.critical .avn-vbar-fill { background: #ff4040; }
  .avn-vbar.caution  { color: #ffaa00; }
  .avn-vbar.critical { color: #ff4040; }
  .avn-vbar.na { color: #1a4a1a; text-shadow: none; }
  .avn-vbar.na .avn-vbar-tube { border-color: #1a4a1a; }
  .avn-vbar.na .avn-vbar-tube::before { background: #1a4a1a; }
  .avn-vbar.na .avn-vbar-ticks::before { background-image:
      linear-gradient(#1a4a1a, #1a4a1a),
      linear-gradient(#1a4a1a, #1a4a1a),
      linear-gradient(#1a4a1a, #1a4a1a),
      linear-gradient(#1a4a1a, #1a4a1a),
      linear-gradient(#1a4a1a, #1a4a1a); }
  .avn-vbar.na .avn-vbar-fill { display: none; }

  .avn-empty {
    position: absolute;
    top: 50%; left: 50%;
    transform: translate(-50%, -50%);
    color: #1a4a1a;
    font-family: 'Share Tech Mono', 'Courier New', monospace;
    font-size: 22px;
    letter-spacing: 3px;
    text-align: center;
    pointer-events: none;
  }
</style>
</head>
<body>
  <div class="avn-panel" id="avn-panel">
    <div class="avn-name" id="avn-name"></div>
    <div class="avn-frame" id="avn-frame">
      <img class="avn-bg" id="avn-bg" alt="">
      <div class="avn-parts" id="avn-parts"></div>
    </div>
    <div class="avn-empty" id="avn-empty">&mdash; NO DATA &mdash;</div>
    <div class="avn-vbar fuel" id="avn-fuel-bar">
      <div class="avn-vbar-value" id="avn-fuel-val">&mdash;</div>
      <div class="avn-vbar-tube">
        <div class="avn-vbar-fill" id="avn-fuel-fill"></div>
        <div class="avn-vbar-ticks"></div>
      </div>
      <div class="avn-vbar-label">FUEL</div>
    </div>
    <div class="avn-vbar thr" id="avn-thr-bar">
      <div class="avn-vbar-value" id="avn-thr-val">&mdash;</div>
      <div class="avn-vbar-tube">
        <div class="avn-vbar-fill" id="avn-thr-fill"></div>
        <div class="avn-vbar-ticks"></div>
      </div>
      <div class="avn-vbar-label">THRL</div>
    </div>
  </div>
<script>
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

// ── State ──────────────────────────────────────────────────────────────────────────
let avnData = { name: null, parts: null, failures: null, fuel: -1, throttle: -1 };
let avnLayoutType  = null;
let avnLayoutCache = Object.create(null);
let avnPartEls     = Object.create(null);
let avnFailureEls  = Object.create(null);

// Known failure messages and how to render them on the silhouette. Mirrors the shell's
// AVN_FAILURE_DEFS table — same keys, same positions, so the silhouette reads identically
// in both single-pane and split-pane modes.
const AVN_FAILURE_DEFS = {
  'LEFT ENGINE FIRE':  { text: 'L ENG FIRE', cx: 0.20, cy: 0.78 },
  'RIGHT ENGINE FIRE': { text: 'R ENG FIRE', cx: 0.80, cy: 0.78 },
};

// ── Renderer ───────────────────────────────────────────────────────────────────────
function renderAvn() {
  const type = avnData.name;
  if (!type) {
    avnNameEl.style.display = 'none';
    avnFrame.style.display  = 'none';
    avnEmptyEl.style.display = '';
    avnFuelBar.classList.remove('placed');
    avnThrBar .classList.remove('placed');
    return;
  }
  avnNameEl.style.display  = '';
  avnFrame.style.display   = '';
  avnEmptyEl.style.display = 'none';
  avnNameEl.textContent = type;

  avnBg.style.display = '';
  avnPartsEl.style.display = '';

  ensureAvnLayout(type);
  const layout = avnLayoutCache[type];
  if (!layout || typeof layout === 'string') return;
  if (avnLayoutType !== type) buildAvnParts(type, layout);

  fitAvnPartsToBg();
  sizeAvnFailures();
  paintAvnDamage();
  paintAvnFailures();
  layoutAvnBars();
  paintAvnBars();
}

function ensureAvnLayout(type) {
  if (avnLayoutCache[type] !== undefined) return;
  avnLayoutCache[type] = 'pending';
  avnBg.src = '/airframe?type=' + encodeURIComponent(type) + '&part=__bg';
  fetch('/airframe-layout?type=' + encodeURIComponent(type))
    .then(function(r) { if (!r.ok) throw new Error('layout ' + r.status); return r.json(); })
    .then(function(j) { avnLayoutCache[type] = j; renderAvn(); })
    .catch(function()  { avnLayoutCache[type] = 'fail'; });
}

function buildAvnParts(type, layout) {
  avnPartsEl.innerHTML = '';
  avnPartEls = Object.create(null);
  if (!layout || !Array.isArray(layout.parts)) { avnLayoutType = type; return; }
  for (const p of layout.parts) {
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
    const url = '/airframe?type=' + encodeURIComponent(type) + '&part=' + encodeURIComponent(p.n);
    el.style.webkitMaskImage = 'url("' + url + '")';
    el.style.maskImage       = 'url("' + url + '")';
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
    return;
  }
  const v = Math.max(0, Math.min(1, value01));
  if      (criticalAt !== null && v <= criticalAt) barEl.classList.add('critical');
  else if (cautionAt  !== null && v <= cautionAt)  barEl.classList.add('caution');
  fillEl.style.height = (v * 100).toFixed(1) + '%';
  valEl.textContent = Math.round(v * 100) + '%';
}

// ── Shell → pane forwarding ────────────────────────────────────────────────────────
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
    };
    if (avnLayoutType !== avnData.name) renderAvn();
    else { paintAvnDamage(); paintAvnFailures(); paintAvnBars(); }
  } else if (m.type === 'orient') {
    // App-wide orientation forwarded by the shell (see body.portrait rules above).
    document.body.classList.toggle('portrait',  m.orientation === 'portrait');
    document.body.classList.toggle('landscape', m.orientation !== 'portrait');
    renderAvn();   // re-layout in case orientation-dependent sizing changed
  }
});

window.addEventListener('resize', renderAvn);
renderAvn();   // initial empty-state paint
</script>
</body>
</html>
""";
    }
}
