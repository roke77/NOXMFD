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

  #mission-bar {
    position: absolute;
    top: 10px; left: 12px;
    background: rgba(6,10,6,0.78);
    border: 1px solid #1a3a1a;
    padding: 6px 11px;
    line-height: 1.5;
    pointer-events: none;
  }
  #mission-bar .map-name { font-size: 15px; font-weight: bold; color: #39ff14; }
  #mission-bar .mission-name { font-size: 11px; color: #4aaa4a; }
  #mission-bar.empty { display: none; }

  #hud { width: 210px; display: flex; flex-direction: column; flex-shrink: 0; }
  #loadout { font-size: 12px; color: #39ff14; overflow-y: auto; height: 100%; }
  #loadout .none { color: #1a4a1a; }
  .witem { margin-bottom: 9px; }
  .wname { white-space: nowrap; overflow: hidden; text-overflow: ellipsis; }
  .wammo { font-size: 11px; color: #4aaa4a; }
  .wammo span { color: #39ff14; }
  .witem.sel .wname, .witem.sel .wammo, .witem.sel .wammo span { color: #ffaa00; }  /* selected weapon */
  .wicon { height: 60px; max-width: 100%; margin-top: 2px; display: block; }
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
    <div id="mission-bar" class="empty">
      <div class="map-name" id="map-name">—</div>
      <div class="mission-name" id="mission-name">—</div>
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
    <div class="panel">
      <div class="label">POSITION (WORLD)</div>
      <div id="raw-pos" class="dim">—</div>
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
const PLAYER_COLOR = '#39ff14';                     // player stays HUD green
let   factionColors = { 0: '#9aa0a6', 1: '#39ff14', 2: '#ff4040' };  // updated from the game's HUD colors
const iconImages = {};         // unitName -> { img, ready }   (raw sprite, fetched once)
const iconTints  = {};         // "unitName|#hex" -> canvas    (pre-tinted variant)

// ── DOM refs ────────────────────────────────────────────────────────────────────
const mapImg   = document.getElementById('map-img');
const overlay  = document.getElementById('overlay');
const oc       = overlay.getContext('2d');
const statusEl = document.getElementById('status');

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
// The extracted map image is north-up, so screen Y is inverted relative to Z.
function worldToOverlay(wx, wz) {
  if (!mapMeta || mapMeta.w <= 0 || mapMeta.h <= 0) return null;
  const relX = (wx + mapMeta.w * 0.5) / mapMeta.w;   // 0 = west,  1 = east
  const relY = (wz + mapMeta.h * 0.5) / mapMeta.h;   // 0 = south, 1 = north
  const r = imgRect();
  return { cx: r.dx + relX * r.dw, cy: r.dy + (1 - relY) * r.dh };
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
function drawIcon(type, hex, cx, cy, hdg, orient, basePx, scale) {
  const cv = tintedIcon(type, hex);
  oc.save();
  oc.translate(cx, cy);
  oc.shadowColor = hex;
  oc.shadowBlur  = 8;
  if (cv) {
    if (orient) oc.rotate(hdg * Math.PI / 180);
    const h = basePx * (scale || 1);
    const w = h * (cv.width / cv.height);
    oc.drawImage(cv, -w / 2, -h / 2, w, h);
  } else {
    const s = FALLBACK_SIZE;
    oc.fillStyle = hex;
    oc.fillRect(-s / 2, -s / 2, s, s);
  }
  oc.restore();
}

// ── Drawing ──────────────────────────────────────────────────────────────────────
function drawOverlay() {
  oc.clearRect(0, 0, overlay.width, overlay.height);
  if (!lastData || !mapMeta) return;

  // Other units first, so the player's icon and label sit on top.
  if (lastData.contacts) {
    for (const u of lastData.contacts) {
      const p = worldToOverlay(u.x, u.z);
      if (!p) continue;
      ensureIconImage(u.t);
      drawIcon(u.t, factionColors[u.f] || factionColors[0], p.cx, p.cy, u.h, u.o, UNIT_BASE, u.s);
    }
  }

  // Player plane (kept green regardless of faction colors)
  const pos = worldToOverlay(lastData.world.x, lastData.world.z);
  if (!pos) return;
  drawIcon(lastData.name, PLAYER_COLOR, pos.cx, pos.cy, lastData.hdg, lastData.iconOrient, ICON_BASE, lastData.iconScale);
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
    if (!mapWasValid) { mapWasValid = true; mapImg.src = '/map?t=' + Date.now(); }
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
  oc.clearRect(0, 0, overlay.width, overlay.height);
  mapImg.src = '/map?t=' + Date.now();   // 404 now → falls back to the placeholder

  document.getElementById('mission-bar').className = 'empty';
  dim('plane-name'); dim('grid'); dim('tas'); dim('agl'); dim('hdg');
  const gEl = document.getElementById('gear'); gEl.textContent = '—'; gEl.className = '';
  const fEl = document.getElementById('cm-flares'); fEl.textContent = '—'; fEl.className = 'cm-val dim';
  const eEl = document.getElementById('cm-ew'); eEl.textContent = '—'; eEl.className = 'cm-val dim';
  document.getElementById('cm-row-flares').classList.remove('cm-sel');
  document.getElementById('cm-row-ew').classList.remove('cm-sel');
  const rEl = document.getElementById('raw-pos'); rEl.textContent = '—'; rEl.className = 'dim';
  document.getElementById('loadout').innerHTML = '<span class="none">—</span>';
  loadoutNames = null;
  ammoEls = [];
  witemEls = [];
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
  if (d.mapName || d.mission) {
    bar.className = '';
    document.getElementById('map-name').textContent     = d.mapName || '—';
    document.getElementById('mission-name').textContent = d.mission || '';
  } else {
    bar.className = 'empty';
  }

  set('plane-name', d.name);
  set('grid', gridLabel(d.world.x, d.world.z));
  set('tas', (d.tas * 3.6).toFixed(0));   // m/s → km/h
  set('agl', d.agl.toFixed(0));
  set('hdg', d.hdg.toFixed(0));

  updateLoadout(d);

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

// Renders the loadout: each weapon's name, remaining/total ammo, and its game icon.
// The DOM (and icon fetches) are rebuilt only when the set of weapons changes; ammo text
// is refreshed in place every frame so firing doesn't re-fetch the icons.
function updateLoadout(d) {
  const list = d.loadout;
  const loEl = document.getElementById('loadout');

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

// ── Init ──────────────────────────────────────────────────────────────────────────
window.addEventListener('resize', resizeOverlay);
resizeOverlay();
</script>
</body>
</html>
""";
    }
}
