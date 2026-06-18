namespace NOTelemetryReader
{
    internal static class ClientPage
    {
        internal static readonly string Html = """
<!DOCTYPE html>
<html lang="en">
<head>
<meta charset="UTF-8">
<title>NO Telemetry</title>
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

  #map-panel {
    flex: 1;
    position: relative;
    background: #0a0f0a;
    border-right: 1px solid #1a3a1a;
    overflow: hidden;
  }
  #map-img {
    position: absolute;
    top: 0; left: 0;
    width: 100%; height: 100%;
    object-fit: contain;
    opacity: 0.92;
  }
  #map-img.missing { display: none; }
  #map-missing {
    display: none;
    position: absolute;
    top: 50%; left: 50%;
    transform: translate(-50%,-50%);
    text-align: center;
    color: #1a4a1a;
    font-size: 12px;
    line-height: 2;
  }
  #overlay { position: absolute; top: 0; left: 0; width: 100%; height: 100%; }

  #tools {
    position: absolute;
    bottom: 10px; left: 10px;
    background: rgba(6,10,6,0.85);
    border: 1px solid #1a3a1a;
    padding: 7px 11px;
    font-size: 10px;
    color: #4aaa4a;
    line-height: 1.7;
  }
  #tools label { cursor: pointer; user-select: none; margin-right: 10px; }
  #tools .src { color: #39ff14; }

  #hud { width: 210px; display: flex; flex-direction: column; flex-shrink: 0; }
  .panel  { border-bottom: 1px solid #1a3a1a; padding: 9px 12px; }
  .label  { font-size: 9px; letter-spacing: 2px; color: #4aaa4a; margin-bottom: 3px; }
  .big    { font-size: 26px; font-weight: bold; letter-spacing: 1px; }
  .unit   { font-size: 10px; color: #4aaa4a; margin-left: 3px; }
  #plane-name { font-size: 14px; font-weight: bold; word-break: break-all; }
  #grid   { font-size: 22px; font-weight: bold; letter-spacing: 2px; }
  #gear.down  { color: #ffaa00; }
  #gear.up    { color: #39ff14; }
  #world      { font-size: 12px; line-height: 1.9; color: #4aaa4a; }
  #world span { color: #39ff14; }
  #raw-pos    { font-size: 11px; line-height: 1.8; color: #4aaa4a; }
  #raw-pos span { color: #39ff14; }
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
    <img id="map-img" src="/map" alt="">
    <div id="map-missing">
      Map image not available yet<br>
      <small>The real map is pulled from the game when a mission loads.</small>
    </div>
    <canvas id="overlay"></canvas>
    <div id="tools">
      <div>MAP: <span id="map-src" class="src">in-game (auto)</span></div>
      <label><input type="checkbox" id="flip-ns"> flip N/S</label>
      <label><input type="checkbox" id="flip-ew"> flip E/W</label>
    </div>
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
      <div class="label">TRUE AIRSPEED</div>
      <div class="big"><span id="tas" class="dim">—</span><span class="unit">m/s</span></div>
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
      <div class="label">WORLD</div>
      <div id="world" class="dim">—</div>
    </div>
    <div class="panel" style="flex:1">
      <div class="label">POSITION (WORLD)</div>
      <div id="raw-pos" class="dim">—</div>
    </div>
  </div>
</main>

<script>
// ── State (declared first so callbacks never hit a temporal dead zone) ──────────
const TRAIL_MAX = 600;
const trail     = [];          // { x, z } world coords
let   lastData  = null;
let   mapMeta   = null;        // { w, h, ox, oy }
let   lastMsgAt = 0;

let flipNS = sessionStorage.getItem('noFlipNS') === '1';
let flipEW = sessionStorage.getItem('noFlipEW') === '1';

// ── DOM refs ────────────────────────────────────────────────────────────────────
const mapImg   = document.getElementById('map-img');
const overlay  = document.getElementById('overlay');
const oc       = overlay.getContext('2d');
const statusEl = document.getElementById('status');
const flipNSEl = document.getElementById('flip-ns');
const flipEWEl = document.getElementById('flip-ew');
flipNSEl.checked = flipNS;
flipEWEl.checked = flipEW;

// ── Canvas geometry ──────────────────────────────────────────────────────────────
function resizeOverlay() {
  const panel = document.getElementById('map-panel');
  overlay.width  = panel.clientWidth;
  overlay.height = panel.clientHeight;
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

// World (X east, Z north) → overlay pixel. The map is a square centered on the world
// origin spanning mapMeta.w × mapMeta.h, so this is a direct mapping — no calibration.
function worldToOverlay(wx, wz) {
  if (!mapMeta || mapMeta.w <= 0 || mapMeta.h <= 0) return null;
  let relX = (wx + mapMeta.w * 0.5) / mapMeta.w;   // 0 = west,  1 = east
  let relY = (wz + mapMeta.h * 0.5) / mapMeta.h;   // 0 = south, 1 = north
  const r = imgRect();
  const fx = flipEW ? (1 - relX) : relX;
  const fy = flipNS ? relY : (1 - relY);           // image is north-up by default
  return { cx: r.dx + fx * r.dw, cy: r.dy + fy * r.dh };
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

// ── Drawing ──────────────────────────────────────────────────────────────────────
function drawOverlay() {
  oc.clearRect(0, 0, overlay.width, overlay.height);
  if (!lastData || !mapMeta) return;

  // Trail
  for (let i = 1; i < trail.length; i++) {
    const a = worldToOverlay(trail[i-1].x, trail[i-1].z);
    const b = worldToOverlay(trail[i].x,   trail[i].z);
    if (!a || !b) continue;
    const alpha = 0.05 + 0.75 * (i / trail.length);
    oc.strokeStyle = `rgba(57,255,20,${alpha.toFixed(2)})`;
    oc.lineWidth   = 2;
    oc.beginPath(); oc.moveTo(a.cx, a.cy); oc.lineTo(b.cx, b.cy); oc.stroke();
  }

  // Plane icon — pointed by true heading from the game.
  const pos = worldToOverlay(lastData.world.x, lastData.world.z);
  if (!pos) return;

  let rot = lastData.hdg;
  if (flipEW) rot = -rot;
  if (flipNS) rot = 180 - rot;

  const s = 11;
  oc.save();
  oc.translate(pos.cx, pos.cy);
  oc.rotate(rot * Math.PI / 180);
  oc.shadowColor = '#39ff14';
  oc.shadowBlur  = 10;
  oc.fillStyle   = '#39ff14';
  oc.beginPath();
  oc.moveTo( 0,       -s * 1.7);
  oc.lineTo(-s * 0.7,  s * 0.9);
  oc.lineTo( 0,        s * 0.35);
  oc.lineTo( s * 0.7,  s * 0.9);
  oc.closePath();
  oc.fill();
  oc.restore();

  oc.shadowBlur   = 0;
  oc.font         = 'bold 11px Courier New';
  oc.fillStyle    = '#39ff14';
  oc.textAlign    = 'left';
  oc.textBaseline = 'middle';
  oc.fillText(lastData.name, pos.cx + 16, pos.cy - 4);
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

  if (d.ping) { setStatus('waiting', '● CONNECTED — no mission'); return; }

  setStatus('connected', '● CONNECTED');
  lastData = d;

  if (d.map && d.map.valid) {
    mapMeta = { w: d.map.w, h: d.map.h, ox: d.map.ox, oy: d.map.oy };
    // The game's map image becomes available shortly after the mission loads; refresh once.
    if (!mapWasValid) { mapWasValid = true; mapImg.src = '/map?t=' + Date.now(); }
  }

  trail.push({ x: d.world.x, z: d.world.z });
  if (trail.length > TRAIL_MAX) trail.shift();

  updateHUD(d);
  drawOverlay();
};

es.onerror = function() {
  // EventSource auto-reconnects; the watchdog decides when to actually show DISCONNECTED.
};

function setStatus(cls, text) {
  statusEl.className   = cls;
  statusEl.textContent = text;
}

// Watchdog — tolerate transient SSE blips, only flag disconnect after a real gap.
setInterval(function() {
  if (performance.now() - lastMsgAt > 2500)
    setStatus('disconnected', '● DISCONNECTED — retrying…');
}, 700);

// ── HUD ──────────────────────────────────────────────────────────────────────────
function updateHUD(d) {
  set('plane-name', d.name);
  set('grid', gridLabel(d.world.x, d.world.z));
  set('tas', d.tas.toFixed(1));
  set('agl', d.agl.toFixed(0));
  set('hdg', d.hdg.toFixed(0));

  const gEl = document.getElementById('gear');
  gEl.textContent = d.gear.toUpperCase();
  gEl.className   = d.gear;

  const wEl = document.getElementById('world');
  wEl.innerHTML  = '<span>' + d.units + '</span> units &nbsp;(<span>' + d.aircraft + '</span> a/c)';
  wEl.className  = '';

  const rEl = document.getElementById('raw-pos');
  rEl.innerHTML = 'X <span>' + d.world.x.toFixed(0) + '</span><br>' +
                  'Y <span>' + d.world.y.toFixed(0) + '</span><br>' +
                  'Z <span>' + d.world.z.toFixed(0) + '</span>';
  rEl.className = '';
}

function set(id, text) {
  const el = document.getElementById(id);
  el.textContent = text;
  el.className   = el.className.replace('dim', '').trim();
}

// ── Flip toggles (orientation fallback, persisted) ────────────────────────────────
flipNSEl.onchange = function() {
  flipNS = flipNSEl.checked;
  sessionStorage.setItem('noFlipNS', flipNS ? '1' : '0');
  drawOverlay();
};
flipEWEl.onchange = function() {
  flipEW = flipEWEl.checked;
  sessionStorage.setItem('noFlipEW', flipEW ? '1' : '0');
  drawOverlay();
};

// ── Init ──────────────────────────────────────────────────────────────────────────
window.addEventListener('resize', resizeOverlay);
resizeOverlay();
</script>
</body>
</html>
""";
    }
}
