namespace NORoksMFD
{
    internal static class ClientPage
    {
        internal static readonly string Html = """
<!DOCTYPE html>
<html lang="en">
<head>
<meta charset="UTF-8">
<title>NO Roks MFD</title>
<link rel="icon" type="image/svg+xml" href="data:image/svg+xml;utf8,<svg xmlns='http://www.w3.org/2000/svg' viewBox='0 0 32 32'><rect x='1' y='1' width='30' height='30' rx='4' fill='%233b3f45'/><rect x='6' y='6' width='20' height='20' rx='1' fill='%23050a05'/><g fill='%23c8ccd0'><rect x='2.5' y='8' width='2' height='2.5'/><rect x='2.5' y='14.75' width='2' height='2.5'/><rect x='2.5' y='21.5' width='2' height='2.5'/><rect x='27.5' y='8' width='2' height='2.5'/><rect x='27.5' y='14.75' width='2' height='2.5'/><rect x='27.5' y='21.5' width='2' height='2.5'/></g><path d='M11 16h10' stroke='%2339ff14' stroke-width='2' stroke-linecap='square'/></svg>">
<style>
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

  /* "bare" mode (e.g. embedded in the MFD frame at /?bare): map only, no chrome. */
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

const ICON_BASE = 15;          // base icon size in px for the player (scaled by iconScale)
const UNIT_BASE = 15;          // base icon size in px for other units
const FALLBACK_SIZE = 5;       // square symbol size in px for units without a game icon
const HIT_PAD = 4;             // extra px around an icon that still counts as a hover hit
let   hitTargets = [];         // [{cx, cy, r, label}] rebuilt every drawOverlay() for hover
let   view = { zoom: 1, panX: 0, panY: 0 };   // map view: pan in screen px, zoom about canvas centre
const MIN_ZOOM = 1, MAX_ZOOM = 8;
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
  if (!e) e = iconImages[type] = { img: null, ready: false, pending: false, tries: 0, lastTry: 0 };
  if (e.ready || e.pending || e.tries >= 8) return;
  const now = performance.now();
  if (e.tries > 0 && now - e.lastTry < 1500) return;   // back off between retries

  e.pending = true; e.tries++; e.lastTry = now;
  const img = new Image();
  img.onload  = function() { e.img = img; e.ready = true; e.pending = false; drawOverlay(); };
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
    const s = FALLBACK_SIZE;
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
      const r = drawIcon(u.t, hex, p.cx, p.cy, u.h, u.o, UNIT_BASE, u.s);
      if (u.tg) drawTargetBox(p.cx, p.cy, r + 4);
      hitTargets.push({ cx: p.cx, cy: p.cy, r: r + HIT_PAD, label: u.t, color: hex });
    }
  }

  // Player plane (kept green regardless of faction colors), drawn and hit-tested last = on top.
  const pos = worldToOverlay(lastData.world.x, lastData.world.z);
  if (!pos) return;
  const pr = drawIcon(lastData.name, PLAYER_COLOR, pos.cx, pos.cy, lastData.hdg, lastData.iconOrient, ICON_BASE, lastData.iconScale);
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
    window.parent.postMessage({ mfd: true, type: 'avn', name: null, parts: null, failures: null }, '*');
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
    // Mirror the player's aircraft name + per-part HP so the MFD's AVN page can render
    // the live damage silhouette. The silhouette assets (background PNG, per-part PNGs,
    // layout JSON) live behind /airframe and /airframe-layout — the MFD fetches them on demand.
    window.parent.postMessage({
      mfd: true, type: 'avn',
      name: d.name || null,
      parts: Array.isArray(d.parts) ? d.parts : null,
      failures: Array.isArray(d.failures) ? d.failures : null,
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
