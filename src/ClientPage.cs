namespace NOXMFD
{
    internal static class ClientPage
    {
        internal static readonly string Html = """
<!DOCTYPE html>
<html lang="en">
<head>
<meta charset="UTF-8">
<title>NO XMFD</title>
<link rel="icon" type="image/svg+xml" href="data:image/svg+xml;utf8,<svg xmlns='http://www.w3.org/2000/svg' viewBox='0 0 32 32'><rect x='1' y='1' width='30' height='30' rx='4' fill='%233b3f45'/><rect x='6' y='6' width='20' height='20' rx='1' fill='%23050a05'/><g fill='%23c8ccd0'><rect x='2.5' y='8' width='2' height='2.5'/><rect x='2.5' y='14.75' width='2' height='2.5'/><rect x='2.5' y='21.5' width='2' height='2.5'/><rect x='27.5' y='8' width='2' height='2.5'/><rect x='27.5' y='14.75' width='2' height='2.5'/><rect x='27.5' y='21.5' width='2' height='2.5'/></g><path d='M11 16h10' stroke='%2339ff14' stroke-width='2' stroke-linecap='square'/></svg>">
<style>
  /* Self-hosted Share Tech Mono (embedded woff2) so the MFD needs no internet. */
  @font-face {
    font-family: 'Share Tech Mono';
    font-style: normal;
    font-weight: 400;
    font-display: swap;
    src: url(data:font/woff2;base64,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) format('woff2');
  }
  * { box-sizing: border-box; margin: 0; padding: 0; }

  body {
    background: #060a06;
    color: #39ff14;
    font-family: 'Courier New', monospace;
    font-size: 13px;
    height: 100vh;
    display: flex;
    flex-direction: column;
    overflow: hidden;
  }

  header {
    padding: 5px 12px;
    border-bottom: 1px solid #1a3a1a;
    display: flex;
    justify-content: space-between;
    align-items: center;
    font-size: 11px;
    color: #4aaa4a;
    flex-shrink: 0;
  }
  #status { font-weight: bold; }
  #status.connected    { color: #39ff14; }
  #status.disconnected { color: #ff4040; }
  #status.waiting      { color: #ffaa00; }

  main { flex: 1; display: flex; overflow: hidden; }

  /* "bare" mode (e.g. embedded in the MFD frame at /map-view?bare): map only, no chrome. */
  body.bare > header,
  body.bare #hud { display: none; }
  body.bare #map-panel { border-right: none; }

  #map-panel {
    flex: 1;
    position: relative;
    background: #000;
    border-right: 1px solid #1a3a1a;
    overflow: hidden;
    /* Custom HUD-green crosshair, constant regardless of zoom/drag state. */
    cursor: url(data:image/svg+xml;base64,PHN2ZyB4bWxucz0iaHR0cDovL3d3dy53My5vcmcvMjAwMC9zdmciIHdpZHRoPSIyNCIgaGVpZ2h0PSIyNCIgdmlld0JveD0iMCAwIDI0IDI0Ij48ZyBmaWxsPSJub25lIiBzdHJva2U9IiMwMDAiIHN0cm9rZS1vcGFjaXR5PSIuNTUiIHN0cm9rZS13aWR0aD0iMy40IiBzdHJva2UtbGluZWNhcD0icm91bmQiPjxsaW5lIHgxPSIxMiIgeTE9IjIiIHgyPSIxMiIgeTI9IjgiLz48bGluZSB4MT0iMTIiIHkxPSIxNiIgeDI9IjEyIiB5Mj0iMjIiLz48bGluZSB4MT0iMiIgeTE9IjEyIiB4Mj0iOCIgeTI9IjEyIi8+PGxpbmUgeDE9IjE2IiB5MT0iMTIiIHgyPSIyMiIgeTI9IjEyIi8+PC9nPjxnIGZpbGw9Im5vbmUiIHN0cm9rZT0iIzM5ZmYxNCIgc3Ryb2tlLXdpZHRoPSIxLjciIHN0cm9rZS1saW5lY2FwPSJyb3VuZCI+PGxpbmUgeDE9IjEyIiB5MT0iMiIgeDI9IjEyIiB5Mj0iOCIvPjxsaW5lIHgxPSIxMiIgeTE9IjE2IiB4Mj0iMTIiIHkyPSIyMiIvPjxsaW5lIHgxPSIyIiB5MT0iMTIiIHgyPSI4IiB5Mj0iMTIiLz48bGluZSB4MT0iMTYiIHkxPSIxMiIgeDI9IjIyIiB5Mj0iMTIiLz48L2c+PC9zdmc+) 12 12, crosshair;
  }
  #map-panel.has-map { background: #000; }   /* black letterbox once a map is loaded */
  /* Source sprite only — the map is blitted into #overlay so it shares the icons' transform. */
  #map-img { display: none; }
  #map-missing {
    display: none;
    position: absolute;
    top: 50%; left: 50%;
    transform: translate(-50%,-50%);
    color: #1a4a1a;
    font-family: 'Share Tech Mono', 'Courier New', monospace;
    font-size: 22px;
    letter-spacing: 3px;
    white-space: nowrap;
  }
  #overlay { position: absolute; top: 0; left: 0; width: 100%; height: 100%; }

  #mission-bar {
    position: absolute;
    bottom: 10px; left: 12px;
    background: rgba(6,10,6,0.78);
    border: 1px solid #1a3a1a;
    padding: 6px 11px;
    line-height: 1.5;
    pointer-events: none;
  }
  #mission-bar .mission-name { font-size: 11px; color: #4aaa4a; }
  #mission-bar.empty { display: none; }

  /* Bottom-right twin of the mission bar — current grid square (e.g. "GRID: Kg48"). */
  #grid-bar {
    position: absolute;
    bottom: 10px; right: 12px;
    background: rgba(6,10,6,0.78);
    border: 1px solid #1a3a1a;
    padding: 5px 9px;
    font-size: 11px;
    letter-spacing: 1px;
    color: #4aaa4a;
    pointer-events: none;
    user-select: none;
  }
  #grid-bar.empty { display: none; }

  /* FOLLOW indicator — only rendered when follow mode is ON (orange box).
     OFF state is hidden entirely so the corner stays clean. */
  #follow-btn {
    position: absolute;
    top: 10px; right: 12px;
    background: rgba(6,10,6,0.78);
    border: 1px solid #ffaa00;
    padding: 5px 9px;
    font-size: 11px;
    letter-spacing: 1px;
    color: #ffaa00;
    user-select: none;
    pointer-events: none;
  }
  #follow-btn.off { display: none; }
  /* Embedded inside the MFD frame: the MFD shell renders its own FOLLOW indicator
     (so it can stack alongside the PINNED indicator), so hide the iframe's local one. */
  body.bare #follow-btn { display: none; }

  #unit-label {
    position: absolute;
    display: none;
    transform: translate(12px, 12px);   /* sit just off the cursor */
    background: rgba(6,10,6,0.78);
    border: 1px solid #1a3a1a;
    color: #39ff14;
    font-size: 11px;
    padding: 2px 6px;
    white-space: nowrap;
    pointer-events: none;
    z-index: 50;
  }

  #hud { width: 210px; display: flex; flex-direction: column; flex-shrink: 0; }
  #loadout { font-size: 12px; color: #39ff14; overflow-y: auto; height: 100%; }
  #loadout .none { color: #1a4a1a; }
  .witem { margin-bottom: 9px; }
  .wname { font-size: 16px; white-space: nowrap; overflow: hidden; text-overflow: ellipsis; }
  .wammo { font-size: 11px; color: #4aaa4a; }
  .wammo span { color: #39ff14; }
  .witem.sel .wname, .witem.sel .wammo, .witem.sel .wammo span { color: #ffaa00; }  /* selected weapon */
  .wicon { height: 80px; max-width: 100%; margin-top: 2px; display: block; }
  .panel  { border-bottom: 1px solid #1a3a1a; padding: 9px 12px; }
  .label  { font-size: 9px; letter-spacing: 2px; color: #4aaa4a; margin-bottom: 3px; }
  .big    { font-size: 26px; font-weight: bold; letter-spacing: 1px; }
  .unit   { font-size: 10px; color: #4aaa4a; margin-left: 3px; }
  #plane-name { font-size: 14px; font-weight: bold; word-break: break-all; }
  #grid   { font-size: 22px; font-weight: bold; letter-spacing: 2px; }
  #gear.down  { color: #ffaa00; }
  #gear.up    { color: #39ff14; }
  #cm { font-size: 13px; }
  .cm-row { display: flex; justify-content: space-between; line-height: 1.9; color: #4aaa4a; }
  .cm-row .cm-val { color: #39ff14; font-weight: bold; }
  .cm-row .cm-val.dim { color: #1a4a1a; font-weight: normal; }
  .cm-row.cm-sel, .cm-row.cm-sel .cm-val { color: #ffaa00; }   /* currently selected */
  .dim { color: #1a4a1a; }
</style>
</head>
<body>

<header>
  <span>NO TELEMETRY &mdash; http://localhost:5005</span>
  <span id="status" class="disconnected">&#9679; DISCONNECTED</span>
</header>

<main>
  <div id="map-panel">
    <!-- src is intentionally empty: the HTML parser fetches static src= attributes
         directly (bypassing the preview-mock's <img>.src setter override), which
         would 404 against the static preview server. The img is display:none and
         only used as a canvas source — the first telemetry frame assigns the real
         /map URL via JS, which both the game server and the mock can satisfy. -->
    <img id="map-img" alt="">
    <div id="map-missing">&mdash; NO SIGNAL &mdash;</div>
    <canvas id="overlay"></canvas>
    <div id="mission-bar" class="empty">
      <div class="mission-name" id="mission-name">—</div>
    </div>
    <div id="follow-btn" class="off">FOLLOW</div>
    <div id="grid-bar" class="empty">GRID: &mdash;</div>
    <div id="unit-label"></div>
  </div>

  <div id="hud">
    <div class="panel">
      <div class="label">AIRCRAFT</div>
      <div id="plane-name" class="dim">—</div>
    </div>
    <div class="panel">
      <div class="label">GRID</div>
      <div id="grid" class="dim">—</div>
    </div>
    <div class="panel">
      <div class="label">SPEED</div>
      <div class="big"><span id="tas" class="dim">—</span><span class="unit">km/h</span></div>
    </div>
    <div class="panel">
      <div class="label">AGL ALTITUDE</div>
      <div class="big"><span id="agl" class="dim">—</span><span class="unit">m</span></div>
    </div>
    <div class="panel">
      <div class="label">HEADING / GEAR</div>
      <div class="big"><span id="hdg" class="dim">—</span><span class="unit">°</span>
        &nbsp; <span id="gear" style="font-size:16px">—</span></div>
    </div>
    <div class="panel">
      <div class="label">COUNTERMEASURES</div>
      <div id="cm">
        <div class="cm-row" id="cm-row-flares"><span>IR Flares</span><span id="cm-flares" class="cm-val dim">—</span></div>
        <div class="cm-row" id="cm-row-ew"><span>EW Capacitor</span><span id="cm-ew" class="cm-val dim">—</span></div>
      </div>
    </div>
    <div class="panel" style="flex:1; min-height:0; display:flex; flex-direction:column">
      <div class="label">LOADOUT</div>
      <div id="loadout"><span class="none">—</span></div>
    </div>
  </div>
</main>

<script>
// ── State (declared first so callbacks never hit a temporal dead zone) ──────────
let   lastData  = null;
let   mapMeta   = null;        // { w, h, ox, oy }
let   lastMsgAt = 0;
let   hadData   = false;       // true once a mission has delivered telemetry
let   loadoutNames = null;     // weapon-name signature; rebuild DOM only when it changes
let   ammoEls = [];            // ammo text elements, aligned with loadout order
let   witemEls = [];           // weapon item containers, aligned with loadout order

// Map-icon sizes switch with zoom: larger when zoomed in, smaller when zoomed out — so icons
// stay legible up close without cluttering the full-extent view. Picked by iconBase() /
// fallbackSize() against the zoom threshold defined below.
const ICON_BASE_IN  = 20, ICON_BASE_OUT  = 15;   // player + unit base size (px), scaled by iconScale
const FALLBACK_IN   = 10, FALLBACK_OUT   = 7;    // icon-less square size (px)
const HIT_PAD = 4;             // extra px around an icon that still counts as a hover hit
let   hitTargets = [];         // [{cx, cy, r, label}] rebuilt every drawOverlay() for hover
let   view = { zoom: 1, panX: 0, panY: 0 };   // map view: pan in screen px, zoom about canvas centre
const MIN_ZOOM = 1, MAX_ZOOM = 8;
// Icons grow once the map is zoomed in to 4x or more (zoom range is MIN..MAX = 1..8): zoom
// 1–3 uses the small OUT sizes, 4–8 the larger IN sizes.
const ICON_ZOOM_THRESHOLD = 4;
function zoomedIn()     { return view.zoom >= ICON_ZOOM_THRESHOLD; }
function iconBase()     { return zoomedIn() ? ICON_BASE_IN : ICON_BASE_OUT; }
function fallbackSize() { return zoomedIn() ? FALLBACK_IN  : FALLBACK_OUT; }
let   followPlayer = false;    // when on (and zoomed in), keep the player icon centred
const PLAYER_COLOR = '#39ff14';                     // player stays HUD green
const TARGET_COLOR = '#ff8000';                     // orange ring on the player's targeted unit(s)
let   factionColors = { 0: '#9aa0a6', 1: '#39ff14', 2: '#ff4040' };  // updated from the game's HUD colors
const iconImages = {};         // unitName -> { img, ready }   (raw sprite, fetched once)
const iconTints  = {};         // "unitName|#hex" -> canvas    (pre-tinted variant)

// ── DOM refs ────────────────────────────────────────────────────────────────────
const mapImg   = document.getElementById('map-img');
const overlay  = document.getElementById('overlay');
const oc       = overlay.getContext('2d');
const statusEl = document.getElementById('status');
const followBtn = document.getElementById('follow-btn');
const gridBar   = document.getElementById('grid-bar');
const unitLabel = document.getElementById('unit-label');

// ── Canvas geometry ──────────────────────────────────────────────────────────────
function resizeOverlay() {
  const panel = document.getElementById('map-panel');
  overlay.width  = panel.clientWidth;
  overlay.height = panel.clientHeight;
  clampPan();          // pan limits depend on canvas size; keep the view valid after a resize
  drawOverlay();
}

// Where the contain-fitted map image actually renders inside the overlay (letterbox-aware).
function imgRect() {
  const iw = mapImg.naturalWidth  || overlay.width;
  const ih = mapImg.naturalHeight || overlay.height;
  const cw = overlay.width, ch = overlay.height;
  const ia = iw / ih, ca = cw / ch;
  let dw, dh, dx, dy;
  if (ia > ca) { dw = cw; dh = cw / ia; dx = 0;             dy = (ch - dh) / 2; }
  else         { dh = ch; dw = ch * ia; dx = (cw - dw) / 2; dy = 0; }
  return { dx, dy, dw, dh };
}

// Apply the zoom/pan view transform to a base (zoom=1) overlay pixel. Zoom is about the
// canvas centre, so pan=0 reproduces today's centred framing exactly.
function viewTransform(px, py) {
  const ox = overlay.width / 2, oy = overlay.height / 2;
  return { x: ox + (px - ox) * view.zoom + view.panX,
           y: oy + (py - oy) * view.zoom + view.panY };
}

// Keep the scaled map covering its zoom=1 footprint: pan can't expose blank background, and
// at zoom=1 this pins pan to 0 (framing unchanged from before zoom existed).
function clampPan() {
  const r = imgRect();
  const maxX = r.dw * (view.zoom - 1) / 2;
  const maxY = r.dh * (view.zoom - 1) / 2;
  view.panX = Math.max(-maxX, Math.min(maxX, view.panX));
  view.panY = Math.max(-maxY, Math.min(maxY, view.panY));
}

// World (X east, Z north) → overlay pixel. The map is a square centered on the world
// origin spanning mapMeta.w × mapMeta.h, so this is a direct mapping — no calibration.
// The extracted map image is north-up, so screen Y is inverted relative to Z.
// World coord → base (zoom=1) overlay pixel, before the view transform.
function worldToBase(wx, wz) {
  if (!mapMeta || mapMeta.w <= 0 || mapMeta.h <= 0) return null;
  const relX = (wx + mapMeta.w * 0.5) / mapMeta.w;   // 0 = west,  1 = east
  const relY = (wz + mapMeta.h * 0.5) / mapMeta.h;   // 0 = south, 1 = north
  const r = imgRect();
  return { x: r.dx + relX * r.dw, y: r.dy + (1 - relY) * r.dh };
}

function worldToOverlay(wx, wz) {
  const b = worldToBase(wx, wz);
  if (!b) return null;
  const v = viewTransform(b.x, b.y);
  return { cx: v.x, cy: v.y };
}

// Reproduces the game's grid label (e.g. "Hc87") from world coords + map offsets.
function gridLabel(wx, wz) {
  if (!mapMeta) return '—';
  const vx = mapMeta.ox + wx;
  const vz = mapMeta.oy - wz;
  const majX = Math.floor(vx / 10000), minX = Math.floor((vx - 10000 * majX) / 1000);
  const majZ = Math.floor(vz / 10000), minZ = Math.floor((vz - 10000 * majZ) / 1000);
  if (majX < 0 || majZ < 0) return '—';
  const vert  = String.fromCharCode(65 + majZ) + String.fromCharCode(97 + minZ);
  return vert + `${majX}${minX}`;
}

// Fetches a unit type's map icon. The mod extracts icons gradually, so a type's icon may
// 404 the first time we ask — retry with backoff until it's ready (or give up after a while
// for types that genuinely have no icon, leaving the square fallback).
function ensureIconImage(type) {
  if (!type) return;
  let e = iconImages[type];
  if (!e) e = iconImages[type] = { img: null, ready: false, pending: false, none: false, tries: 0, lastTry: 0 };
  if (e.ready || e.pending || e.none || e.tries >= 8) return;
  const now = performance.now();
  if (e.tries > 0 && now - e.lastTry < 1500) return;   // back off between retries

  e.pending = true; e.tries++; e.lastTry = now;
  const img = new Image();
  img.onload  = function() {
    // 1×1 = the server's "no icon" sentinel (buildings etc.): stop asking, keep the square fallback.
    if (img.naturalWidth <= 1 && img.naturalHeight <= 1) { e.none = true; e.pending = false; return; }
    e.img = img; e.ready = true; e.pending = false; drawOverlay();
  };
  img.onerror = function() { e.pending = false; };      // not captured yet — retry on a later frame
  img.src = '/icon?type=' + encodeURIComponent(type) + '&v=' + e.tries;
}

// Returns the icon for a type pre-tinted to a faction color (cached), or null if not loaded.
function tintedIcon(type, hex) {
  const base = iconImages[type];
  if (!base || !base.ready) return null;
  const key = type + '|' + hex;
  let c = iconTints[key];
  if (!c) {
    c = document.createElement('canvas');
    c.width = base.img.naturalWidth; c.height = base.img.naturalHeight;
    const cx = c.getContext('2d');
    cx.drawImage(base.img, 0, 0);
    cx.globalCompositeOperation = 'source-in';   // tint opaque pixels, keep alpha
    cx.fillStyle = hex;
    cx.fillRect(0, 0, c.width, c.height);
    iconTints[key] = c;
  }
  return c;
}

// Draws one icon at a screen position. When no game icon is available, falls back to a
// square symbol — the same generic marker the game uses for units without a specific icon.
// Returns the icon's on-screen half-extent (in px) so callers can record a hover hotspot.
function drawIcon(type, hex, cx, cy, hdg, orient, basePx, scale) {
  const cv = tintedIcon(type, hex);
  oc.save();
  oc.translate(cx, cy);
  oc.shadowColor = hex;
  oc.shadowBlur  = 8;
  let r;
  if (cv) {
    if (orient) oc.rotate(hdg * Math.PI / 180);
    const h = basePx * (scale || 1);
    const w = h * (cv.width / cv.height);
    oc.drawImage(cv, -w / 2, -h / 2, w, h);
    r = Math.max(w, h) / 2;
  } else {
    const s = fallbackSize();
    oc.fillStyle = hex;
    oc.fillRect(-s / 2, -s / 2, s, s);
    r = s / 2;
  }
  oc.restore();
  return r;
}

// Draws a square target box (corner brackets) around an icon to mark one of the player's
// locked targets. Faction colour stays on the icon underneath; the box conveys "targeted".
function drawTargetBox(cx, cy, half) {
  oc.save();
  oc.translate(cx, cy);
  oc.strokeStyle = TARGET_COLOR;
  oc.shadowColor = TARGET_COLOR;
  oc.shadowBlur  = 8;
  oc.lineWidth   = 1.5;
  oc.lineCap     = 'round';
  const s = half;
  const k = Math.max(3, s * 0.5);   // corner arm length
  oc.beginPath();
  oc.moveTo(-s, -s + k); oc.lineTo(-s, -s); oc.lineTo(-s + k, -s);   // top-left
  oc.moveTo( s - k, -s); oc.lineTo( s, -s); oc.lineTo( s, -s + k);   // top-right
  oc.moveTo( s,  s - k); oc.lineTo( s,  s); oc.lineTo( s - k,  s);   // bottom-right
  oc.moveTo(-s + k,  s); oc.lineTo(-s,  s); oc.lineTo(-s,  s - k);   // bottom-left
  oc.stroke();
  oc.restore();
}

// ── Drawing ──────────────────────────────────────────────────────────────────────
function drawOverlay() {
  oc.clearRect(0, 0, overlay.width, overlay.height);
  hitTargets.length = 0;
  if (!lastData || !mapMeta) return;

  // Follow mode: re-derive pan each frame so the player icon stays centred. clampPan then keeps
  // the map edges honest, so near a border the player drifts off-centre instead of exposing blank
  // background — same as the in-game map.
  if (followPlayer && view.zoom > MIN_ZOOM && lastData.world) {
    const b = worldToBase(lastData.world.x, lastData.world.z);
    if (b) {
      view.panX = -(b.x - overlay.width  / 2) * view.zoom;
      view.panY = -(b.y - overlay.height / 2) * view.zoom;
      clampPan();
    }
  }

  // Blit the map sprite into the canvas under the same transform the icons use, so the map and
  // icons share one coordinate system and can never drift apart when zoomed or panned.
  if (mapImg.complete && mapImg.naturalWidth > 0) {
    const r = imgRect();
    const tl = viewTransform(r.dx, r.dy);
    oc.save();
    oc.globalAlpha = 0.92;   // preserves the map's former CSS opacity
    oc.drawImage(mapImg, tl.x, tl.y, r.dw * view.zoom, r.dh * view.zoom);
    oc.restore();
  }

  // Other units first, so the player's icon and label sit on top.
  if (lastData.contacts) {
    for (const u of lastData.contacts) {
      const p = worldToOverlay(u.x, u.z);
      if (!p) continue;
      ensureIconImage(u.t);
      const hex = factionColors[u.f] || factionColors[0];
      const r = drawIcon(u.t, hex, p.cx, p.cy, u.h, u.o, iconBase(), u.s);
      if (u.tg) drawTargetBox(p.cx, p.cy, r + 4);
      hitTargets.push({ cx: p.cx, cy: p.cy, r: r + HIT_PAD, label: u.t, color: hex });
    }
  }

  // Player plane (kept green regardless of faction colors), drawn and hit-tested last = on top.
  const pos = worldToOverlay(lastData.world.x, lastData.world.z);
  if (!pos) return;
  const pr = drawIcon(lastData.name, PLAYER_COLOR, pos.cx, pos.cy, lastData.hdg, lastData.iconOrient, iconBase(), lastData.iconScale);
  hitTargets.push({ cx: pos.cx, cy: pos.cy, r: pr + HIT_PAD, label: lastData.name, color: PLAYER_COLOR });
}

// ── Image load / error ─────────────────────────────────────────────────────────
mapImg.onerror = function() {
  mapImg.classList.add('missing');
  document.getElementById('map-missing').style.display = 'block';
};
mapImg.onload = function() {
  mapImg.classList.remove('missing');
  document.getElementById('map-missing').style.display = 'none';
  resizeOverlay();
};

// ── SSE ────────────────────────────────────────────────────────────────────────
let mapWasValid = false;

const es = new EventSource('/stream');

es.onmessage = function(e) {
  lastMsgAt = performance.now();
  const d = JSON.parse(e.data);

  if (d.ping) {
    setStatus('waiting', '● CONNECTED — no mission');
    if (hadData) clearMission();   // mission ended — wipe the display once
    return;
  }

  setStatus('connected', '● CONNECTED');
  lastData = d;
  hadData  = true;
  ensureIconImage(d.name);
  if (d.colors) factionColors = { 0: d.colors.n, 1: d.colors.f, 2: d.colors.e };

  if (d.map && d.map.valid) {
    mapMeta = { w: d.map.w, h: d.map.h, ox: d.map.ox, oy: d.map.oy };
    // The game's map image becomes available shortly after the mission loads; refresh once.
    if (!mapWasValid) {
      mapWasValid = true;
      mapImg.src = '/map?t=' + Date.now();
      document.getElementById('map-panel').classList.add('has-map');
    }
  }

  updateHUD(d);
  drawOverlay();
};

// Wipe everything when a mission/map exits, so stale data never lingers on screen.
function clearMission() {
  hadData = false;
  lastData = null;
  mapMeta = null;
  mapWasValid = false;
  view.zoom = 1; view.panX = 0; view.panY = 0;   // next mission starts at full extent
  followPlayer = false; followBtn.className = 'off'; followBtn.textContent = 'FOLLOW';
  oc.clearRect(0, 0, overlay.width, overlay.height);
  document.getElementById('map-panel').classList.remove('has-map');
  mapImg.src = '/map?t=' + Date.now();   // 404 now → falls back to the placeholder

  document.getElementById('mission-bar').className = 'empty';
  document.getElementById('grid-bar').className = 'empty';
  dim('plane-name'); dim('grid'); dim('tas'); dim('agl'); dim('hdg');
  const gEl = document.getElementById('gear'); gEl.textContent = '—'; gEl.className = '';
  const fEl = document.getElementById('cm-flares'); fEl.textContent = '—'; fEl.className = 'cm-val dim';
  const eEl = document.getElementById('cm-ew'); eEl.textContent = '—'; eEl.className = 'cm-val dim';
  document.getElementById('cm-row-flares').classList.remove('cm-sel');
  document.getElementById('cm-row-ew').classList.remove('cm-sel');
  document.getElementById('loadout').innerHTML = '<span class="none">—</span>';
  loadoutNames = null;
  ammoEls = [];
  witemEls = [];
  if (window.parent !== window) {
    window.parent.postMessage({ mfd: true, type: 'loadout', items: [], selWeapon: null }, '*');
    window.parent.postMessage({ mfd: true, type: 'cm', flares: -1, flaresMax: -1, ewKJ: -1, ewKJMax: -1, cmCat: 0 }, '*');
    window.parent.postMessage({ mfd: true, type: 'tgp', active: false }, '*');
    window.parent.postMessage({ mfd: true, type: 'targets', items: [] }, '*');
    window.parent.postMessage({ mfd: true, type: 'rwr', items: [] }, '*');
    window.parent.postMessage({ mfd: true, type: 'mw', items: [] }, '*');
    window.parent.postMessage({ mfd: true, type: 'avn', name: null, parts: null, failures: null, fuel: -1, throttle: -1 }, '*');
    window.parent.postMessage({ mfd: true, type: 'follow', on: false }, '*');
  }
}

function dim(id) {
  const el = document.getElementById(id);
  el.textContent = '—';
  if (!el.className.includes('dim')) el.className = (el.className + ' dim').trim();
}

es.onerror = function() {
  // EventSource auto-reconnects; the watchdog decides when to actually show DISCONNECTED.
};

function setStatus(cls, text) {
  statusEl.className   = cls;
  statusEl.textContent = text;
  // Mirror state to an embedder (e.g. the MFD), so it can show the connection
  // status on its MAIN page without opening its own /stream.
  if (window.parent !== window) {
    window.parent.postMessage({ mfd: true, type: 'status', cls: cls, text: text }, '*');
  }
}

// Watchdog — tolerate transient SSE blips, only flag disconnect after a real gap.
setInterval(function() {
  if (performance.now() - lastMsgAt > 2500)
    setStatus('disconnected', '● DISCONNECTED — retrying…');
}, 700);

// ── HUD ──────────────────────────────────────────────────────────────────────────
function updateHUD(d) {
  // Mission / map name bar
  const bar = document.getElementById('mission-bar');
  if (d.mission) {
    bar.className = '';
    document.getElementById('mission-name').textContent = d.mission;
  } else {
    bar.className = 'empty';
  }

  set('plane-name', d.name);
  const gridText = gridLabel(d.world.x, d.world.z);
  set('grid', gridText);
  gridBar.textContent = 'GRID: ' + gridText;
  gridBar.className = '';
  set('tas', (d.tas * 3.6).toFixed(0));   // m/s → km/h
  set('agl', d.agl.toFixed(0));
  set('hdg', d.hdg.toFixed(0));

  updateLoadout(d);

  // Mirror countermeasure state to an embedder (e.g. the MFD WPN page) so it can show the
  // flares / radar-jammer panel without opening its own /stream.
  if (window.parent !== window) {
    window.parent.postMessage({
      mfd: true, type: 'cm',
      flares:    typeof d.flares    === 'number' ? d.flares    : -1,
      flaresMax: typeof d.flaresMax === 'number' ? d.flaresMax : -1,
      ewKJ:      typeof d.ewKJ      === 'number' ? d.ewKJ      : -1,
      ewKJMax:   typeof d.ewKJMax   === 'number' ? d.ewKJMax   : -1,
      cmCat:     d.cmCat || 0
    }, '*');
    // Mirror the TGP feed state so the MFD's TGP page can swap to NO TARGET when the feed
    // stops (after the in-game 3-second post-loss hold expires).
    window.parent.postMessage({ mfd: true, type: 'tgp', active: !!d.tgpActive }, '*');
    // Mirror the player's selected target list so the MFD's TGL page can render it.
    // The mod doesn't emit a dedicated `targets` field — each targeted unit is flagged on
    // its contact entry (same `tg` flag that draws the orange target box on the map). So
    // derive the list from contacts; preview mocks can still supply an explicit `d.targets`
    // to override (used for showing 12+ entries without spawning 12 contacts).
    let targets;
    if (Array.isArray(d.targets)) {
      targets = d.targets;
    } else if (Array.isArray(d.contacts) && d.world) {
      targets = [];
      for (const u of d.contacts) {
        if (!u.tg) continue;
        const dx = u.x - d.world.x;
        const dz = u.z - d.world.z;
        targets.push({
          n: u.t,
          g: gridLabel(u.x, u.z),
          r: Math.hypot(dx, dz) / 1000,
          f: u.f,
        });
      }
    } else {
      targets = [];
    }
    window.parent.postMessage({ mfd: true, type: 'targets', items: targets }, '*');
    // Mirror the radar-warning emitters so the MFD's RWR page can render its scope. The wire
    // shape carries each emitter's world position (x,z) + tier (tr) + power (pw); we turn that
    // into a nose-up plot here, where the data + ownship state live: az = bearing relative to
    // heading (clockwise from the nose), dist = radius from centre 0..1 (higher power = closer,
    // so 1 - pw). The shell + bare RWR pane just plot {az, d, tr, n, k}.
    let rwr = [];
    if (Array.isArray(d.rwr) && d.world) {
      const hdg = d.hdg || 0;
      for (const c of d.rwr) {
        const dx = c.x - d.world.x;
        const dz = c.z - d.world.z;
        let az = Math.atan2(dx, dz) * 180 / Math.PI - hdg;
        az = ((az % 360) + 360) % 360;
        const pw = Math.max(0, Math.min(1, typeof c.pw === 'number' ? c.pw : 0));
        const fr = typeof c.fr === 'number' ? Math.max(0, Math.min(1, c.fr)) : 1;
        rwr.push({ az: az, d: Math.max(0.06, Math.min(1, 1 - pw)), tr: c.tr || 0, fr: fr, n: c.n || '', k: c.k || 0 });
      }
    }
    window.parent.postMessage({ mfd: true, type: 'rwr', items: rwr }, '*');
    // Mirror incoming missiles for the RWR's missile-launch indicator. Same idea as rwr: turn
    // each missile's world position into a nose-up bearing (az) + range in km (rng) for the
    // label; the scope draws a flickering line from that bearing in toward the player.
    let mw = [];
    if (Array.isArray(d.mw) && d.world) {
      const hdg = d.hdg || 0;
      for (const m of d.mw) {
        const dx = m.x - d.world.x;
        const dz = m.z - d.world.z;
        let az = Math.atan2(dx, dz) * 180 / Math.PI - hdg;
        az = ((az % 360) + 360) % 360;
        const item = { az: az, rng: Math.hypot(dx, dz) / 1000, st: m.st || '' };
        // Beam-notch line (radar seekers only): nb is a world heading; rotate it nose-up too.
        if (typeof m.nb === 'number' && m.nb >= 0) {
          item.nb = (((m.nb - hdg) % 360) + 360) % 360;
        }
        mw.push(item);
      }
    }
    window.parent.postMessage({ mfd: true, type: 'mw', items: mw }, '*');
    // Mirror the player's aircraft name + per-part HP so the MFD's AVN page can render
    // the live damage silhouette. The silhouette assets (background PNG, per-part PNGs,
    // layout JSON) live behind /airframe and /airframe-layout — the MFD fetches them on demand.
    window.parent.postMessage({
      mfd: true, type: 'avn',
      name: d.name || null,
      parts: Array.isArray(d.parts) ? d.parts : null,
      failures: Array.isArray(d.failures) ? d.failures : null,
      fuel:     typeof d.fuel === 'number' ? d.fuel : -1,
      throttle: typeof d.thr  === 'number' ? d.thr  : -1,
    }, '*');
  }

  const gEl = document.getElementById('gear');
  gEl.textContent = d.gear.toUpperCase();
  gEl.className   = d.gear;

  // Countermeasures (-1 = the aircraft has no such system)
  const fEl = document.getElementById('cm-flares');
  fEl.textContent = (d.flares >= 0) ? d.flares : '—';
  fEl.className   = 'cm-val' + (d.flares >= 0 ? '' : ' dim');
  const eEl = document.getElementById('cm-ew');
  eEl.textContent = (d.ewKJ >= 0) ? (Math.round(d.ewKJ) + ' kJ') : '—';
  eEl.className   = 'cm-val' + (d.ewKJ >= 0 ? '' : ' dim');

  // Highlight the currently selected countermeasure line (1 = flares, 2 = EW)
  document.getElementById('cm-row-flares').classList.toggle('cm-sel', d.cmCat === 1);
  document.getElementById('cm-row-ew').classList.toggle('cm-sel', d.cmCat === 2);
}

function set(id, text) {
  const el = document.getElementById(id);
  el.textContent = text;
  el.className   = el.className.replace('dim', '').trim();
}

// Renders the loadout: each weapon's name, remaining/total ammo, and its game icon.
// The DOM (and icon fetches) are rebuilt only when the set of weapons changes; ammo text
// is refreshed in place every frame so firing doesn't re-fetch the icons.
function updateLoadout(d) {
  const list = d.loadout;
  const loEl = document.getElementById('loadout');

  // Mirror loadout to an embedder (e.g. the MFD's WPN page) so it doesn't open its own /stream.
  if (window.parent !== window) {
    window.parent.postMessage({
      mfd: true, type: 'loadout',
      items: list || [],
      selWeapon: d.selWeapon || null
    }, '*');
  }

  if (!list || !list.length) {
    if (loadoutNames !== '') { loadoutNames = ''; ammoEls = []; witemEls = []; loEl.innerHTML = '<span class="none">— none —</span>'; }
    return;
  }

  const key = list.map(function(w) { return w.n; }).join('|');
  if (key !== loadoutNames) {
    loadoutNames = key;
    ammoEls = [];
    witemEls = [];
    loEl.innerHTML = '';
    for (const w of list) {
      const item = document.createElement('div');
      item.className = 'witem';
      witemEls.push(item);

      const name = document.createElement('div');
      name.className = 'wname';
      name.textContent = w.n;
      item.appendChild(name);

      const ammo = document.createElement('div');
      ammo.className = 'wammo';
      item.appendChild(ammo);
      ammoEls.push(ammo);

      const img = document.createElement('img');
      img.className = 'wicon';
      img.alt = '';
      img.onerror = function() { img.remove(); };   // no icon for this weapon
      img.src = '/weapon?name=' + encodeURIComponent(w.n);
      item.appendChild(img);

      loEl.appendChild(item);
    }
  }

  // Refresh ammo text and the selected-weapon highlight in place (cheap, no DOM rebuild).
  for (let i = 0; i < list.length && i < ammoEls.length; i++) {
    const w = list[i];
    ammoEls[i].innerHTML = (w.f > 0) ? ('<span>' + w.a + '</span> / ' + w.f) : '';
    witemEls[i].classList.toggle('sel', w.n === d.selWeapon);
  }
}

// ── Map zoom / pan ───────────────────────────────────────────────────────────────
function resetView() { view.zoom = 1; view.panX = 0; view.panY = 0; setFollow(false); }

// Toggle follow mode (keyboard F or the MFD's FLW key). The on-screen badge is a status
// indicator only — drawOverlay does the centring.
function setFollow(on) {
  followPlayer = on;
  followBtn.className   = on ? 'on' : 'off';
  followBtn.textContent = 'FOLLOW';
  drawOverlay();
  if (window.parent !== window) {
    window.parent.postMessage({ mfd: true, type: 'follow', on: !!on }, '*');
  }
}
window.addEventListener('keydown', function(e) {
  if ((e.key === 'f' || e.key === 'F') && mapMeta) setFollow(!followPlayer);
});

let dragging = false, lastX = 0, lastY = 0;

// Scroll to zoom toward the cursor: keep the world point under the pointer fixed while scaling.
overlay.addEventListener('wheel', function(e) {
  if (!mapMeta) return;
  e.preventDefault();
  const rect = overlay.getBoundingClientRect();
  const sx = e.clientX - rect.left, sy = e.clientY - rect.top;   // cursor in canvas px
  const ox = overlay.width / 2, oy = overlay.height / 2;
  const z0 = view.zoom;
  const z1 = Math.max(MIN_ZOOM, Math.min(MAX_ZOOM, z0 * Math.exp(-e.deltaY * 0.0015)));
  if (z1 === z0) return;
  // While following, zoom about the player (drawOverlay re-centres) rather than the cursor.
  if (followPlayer) { view.zoom = z1; clampPan(); drawOverlay(); return; }
  // pan1 = d - (z1/z0)(d - pan0), with d = cursor − centre — holds the cursor's point in place.
  view.panX = (sx - ox) - (z1 / z0) * ((sx - ox) - view.panX);
  view.panY = (sy - oy) - (z1 / z0) * ((sy - oy) - view.panY);
  view.zoom = z1;
  clampPan();
  drawOverlay();
}, { passive: false });

// Drag to pan (only meaningful once zoomed in).
overlay.addEventListener('pointerdown', function(e) {
  if (!mapMeta || view.zoom <= MIN_ZOOM) return;
  if (followPlayer) setFollow(false);   // dragging hands control to free-look
  dragging = true; lastX = e.clientX; lastY = e.clientY;
  overlay.setPointerCapture(e.pointerId);
});
overlay.addEventListener('pointermove', function(e) {
  if (!dragging) return;
  view.panX += e.clientX - lastX;
  view.panY += e.clientY - lastY;
  lastX = e.clientX; lastY = e.clientY;
  clampPan();
  drawOverlay();
});
function endDrag(e) {
  if (!dragging) return;
  dragging = false;
  try { overlay.releasePointerCapture(e.pointerId); } catch (_) {}
}
overlay.addEventListener('pointerup', endDrag);
overlay.addEventListener('pointercancel', endDrag);
overlay.addEventListener('dblclick', function() { if (mapMeta) resetView(); });   // reset to full map

// ── Hover-to-label ───────────────────────────────────────────────────────────────
// Icons are canvas pixels, so we hit-test the cursor against the per-frame hitTargets
// (positions are post-zoom/pan, so this stays correct at any view). Cursor-anchored.
const mapPanel = document.getElementById('map-panel');
mapPanel.addEventListener('mousemove', function(e) {
  if (dragging) { unitLabel.style.display = 'none'; return; }   // don't flicker while panning
  const rect = overlay.getBoundingClientRect();
  const mx = e.clientX - rect.left, my = e.clientY - rect.top;
  let hit = null;
  for (let i = hitTargets.length - 1; i >= 0; i--) {   // topmost (last-drawn) first
    const t = hitTargets[i];
    const dx = mx - t.cx, dy = my - t.cy;
    if (dx * dx + dy * dy <= t.r * t.r) { hit = t; break; }
  }
  if (hit) {
    unitLabel.textContent   = hit.label;
    unitLabel.style.color   = hit.color;   // match the hovered unit's icon color
    unitLabel.style.left    = mx + 'px';
    unitLabel.style.top     = my + 'px';
    unitLabel.style.display = 'block';
  } else {
    unitLabel.style.display = 'none';
  }
});
mapPanel.addEventListener('mouseleave', function() { unitLabel.style.display = 'none'; });

// ── Remote control ────────────────────────────────────────────────────────────────
// Lets an embedder (the MFD frame) drive the map without reaching into it directly, so
// the map stays a self-contained component. Works same-origin and cross-origin (file://).
function zoomStep(factor) {   // zoom about the canvas centre (the wheel zooms at the cursor)
  if (!mapMeta) return;
  const z = Math.max(MIN_ZOOM, Math.min(MAX_ZOOM, view.zoom * factor));
  if (z === view.zoom) return;
  view.zoom = z;
  clampPan();
  drawOverlay();
}
window.addEventListener('message', function(e) {
  const m = e.data;
  if (!m || m.mfd !== true) return;
  switch (m.action) {
    case 'toggle-follow': if (mapMeta) setFollow(!followPlayer); break;
    case 'zoom-in':       zoomStep(1.5);   break;
    case 'zoom-out':      zoomStep(1 / 1.5); break;
    case 'status-request':                                      // re-broadcast current status
      if (window.parent !== window) {
        window.parent.postMessage({ mfd: true, type: 'status', cls: statusEl.className, text: statusEl.textContent }, '*');
      }
      break;
  }
});

// ── Init ──────────────────────────────────────────────────────────────────────────
// "bare" mode: hide header + HUD sidebar so just the map shows (used by the MFD frame).
if (location.search.indexOf('bare') >= 0) document.body.classList.add('bare');
window.addEventListener('resize', resizeOverlay);
resizeOverlay();
</script>
</body>
</html>
""";
    }
}
