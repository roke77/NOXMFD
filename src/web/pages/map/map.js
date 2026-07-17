// MAP page — the VIEW half: canvas rendering, the HUD readout, and pan/zoom/follow/select
// interactions. The telemetry transport + the derive-and-broadcast "provider" role live in
// TelemetrySource (telemetry-source.js); this file instantiates it and renders the frames it
// hands back. See src/web/README.md for why MAP is the telemetry tap.
import { TelemetrySource, gridLabel } from '/assets/services/telemetry-source.js';

// ── State (declared first so callbacks never hit a temporal dead zone) ──────────
let   lastData  = null;        // last rendered frame (the source hands it to renderFrame)
let   mapMeta   = null;        // { w, h, ox, oy } — the view's copy, for worldToBase / gridLabel

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

// ── Persisted view (FLW + ZOOM) ───────────────────────────────────────────────────
// FLW and ZOOM persist across navigation in sessionStorage, shared same-origin by the shell
// and every map iframe (full view, both split panes). In full view the map iframe stays alive
// behind the page frame, so its state already survives a page switch; this also covers the
// cases where the iframe DOES reload (a split pane, a shell reload) and the mission-exit reset —
// so coming back to MAP always restores the last FLW + ZOOM. First run (no stored value) seeds
// the defaults: follow ON and a medium zoom, so MAP opens centred on the player and zoomed in
// enough for follow to bite (it only re-centres while view.zoom > MIN_ZOOM).
const VIEW_STORE_KEY = 'noxmfd.map.view';
const DEFAULT_FOLLOW = true;
const DEFAULT_ZOOM   = 4;     // medium point of the MIN_ZOOM..MAX_ZOOM (1..8) range — tune here
function loadPersistedView() {
  let saved = null;
  try { saved = JSON.parse(sessionStorage.getItem(VIEW_STORE_KEY) || 'null'); } catch (_) {}
  const z = saved && typeof saved.zoom === 'number' ? saved.zoom : DEFAULT_ZOOM;
  view.zoom = Math.max(MIN_ZOOM, Math.min(MAX_ZOOM, z));
  followPlayer = saved && typeof saved.follow === 'boolean' ? saved.follow : DEFAULT_FOLLOW;
}
function savePersistedView() {
  try { sessionStorage.setItem(VIEW_STORE_KEY, JSON.stringify({ zoom: view.zoom, follow: followPlayer })); } catch (_) {}
}
const PLAYER_COLOR = '#39ff14';                     // player stays HUD green
const TARGET_COLOR = '#ff8000';                     // orange ring on the player's targeted unit(s)
let   factionColors = { 0: '#9aa0a6', 1: '#39ff14', 2: '#ff4040' };  // updated from the game's HUD colors
const iconImages = {};         // unitName -> { img, ready }   (raw sprite, fetched once)
const iconTints  = {};         // "unitName|#hex" -> { cv, iw, ih }  (pre-tinted + pre-glowed)

// Map threat overlay — replicates the game's DynamicMap radar pings (DynamicMap.ShowRadarPing):
// a spoke from each emitter toward the player, tier-coloured (white search / yellow track /
// red lock) with alpha fading as the ping ages (fr). And the incoming-missile cue
// (UnitMapIcon.SetMissileWarning): a triangle flashing red<->yellow that points at the player.
const RWR_LINE_RGB   = ['220,220,220', '255,210,30', '255,59,48'];  // search / track / lock
const RWR_LINE_ALPHA = [0.5, 0.7, 0.95];                            // base alpha per tier, scaled by fr

// ── DOM refs ────────────────────────────────────────────────────────────────────
const mapImg   = document.getElementById('map-img');
const overlay  = document.getElementById('overlay');
const oc       = overlay.getContext('2d');
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

// gridLabel(wx, wz, meta) is imported from telemetry-source.js (shared with the target derive).

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

// Pre-tinted + pre-glowed icon for a (type,color), cached. We bake the faction-colour glow
// into the canvas ONCE here instead of setting canvas shadowBlur on every draw — per-draw
// shadowBlur is the single most expensive 2D op, and with dozens of contacts redrawn at 10 Hz
// it would dominate MAP/RWR redraw cost. Returns { cv, iw, ih } or null if not loaded;
// cv is padded by GLOW_PAD on every side so the baked glow has room to bleed.
const GLOW_BLUR = 8;    // blur radius baked into the icon glow
const GLOW_PAD  = 12;   // canvas padding (source px) to contain the blur spread
function tintedIcon(type, hex) {
  const base = iconImages[type];
  if (!base || !base.ready) return null;
  const key = type + '|' + hex;
  let e = iconTints[key];
  if (!e) {
    const iw = base.img.naturalWidth, ih = base.img.naturalHeight;
    // Tint first (source-in recolours opaque pixels, keeps the icon's alpha).
    const tint = document.createElement('canvas');
    tint.width = iw; tint.height = ih;
    const tcx = tint.getContext('2d');
    tcx.drawImage(base.img, 0, 0);
    tcx.globalCompositeOperation = 'source-in';
    tcx.fillStyle = hex;
    tcx.fillRect(0, 0, iw, ih);
    // Bake the glow: one shadowed draw paints both the sharp icon and its blurred halo.
    const cv = document.createElement('canvas');
    cv.width = iw + GLOW_PAD * 2; cv.height = ih + GLOW_PAD * 2;
    const cx = cv.getContext('2d');
    cx.shadowColor = hex;
    cx.shadowBlur  = GLOW_BLUR;
    cx.drawImage(tint, GLOW_PAD, GLOW_PAD);
    e = iconTints[key] = { cv: cv, iw: iw, ih: ih };
  }
  return e;
}

// Draws one icon at a screen position. When no game icon is available, falls back to a
// square symbol — the same generic marker the game uses for units without a specific icon.
// Returns the icon's on-screen half-extent (in px) so callers can record a hover hotspot.
function drawIcon(type, hex, cx, cy, hdg, orient, basePx, scale) {
  const t = tintedIcon(type, hex);
  oc.save();
  oc.translate(cx, cy);
  let r;
  if (t) {
    if (orient) oc.rotate(hdg * Math.PI / 180);
    // Size the ICON to h (its on-screen size is unchanged); the padded glow canvas is drawn
    // larger by the pad ratio so the baked glow bleeds symmetrically around the icon.
    const h  = basePx * (scale || 1);
    const w  = h * (t.iw / t.ih);
    const pw = w * (t.cv.width  / t.iw);
    const ph = h * (t.cv.height / t.ih);
    oc.drawImage(t.cv, -pw / 2, -ph / 2, pw, ph);
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

// Radar-warning spokes: a line from each emitter toward the player, coloured by tier and
// fading with ping freshness — the same grey/yellow/red lines the game draws on its map.
// Drawn under the unit icons so the icons stay readable on top.
function drawRwrLines() {
  if (!lastData || !Array.isArray(lastData.rwr) || !lastData.world) return;
  const pp = worldToOverlay(lastData.world.x, lastData.world.z);
  if (!pp) return;
  oc.save();
  oc.lineCap = 'round';
  for (const c of lastData.rwr) {
    const ep = worldToOverlay(c.x, c.z);
    if (!ep) continue;
    const tr  = c.tr || 0;
    const fr  = (typeof c.fr === 'number') ? Math.max(0, Math.min(1, c.fr)) : 1;
    const rgb = RWR_LINE_RGB[tr]   || RWR_LINE_RGB[0];
    const a   = (RWR_LINE_ALPHA[tr] || RWR_LINE_ALPHA[0]) * Math.max(0.15, fr);
    const core = (tr === 2) ? 2.4 : 1.8;   // lock a touch bolder
    // Cheap glow: a wider, fainter underlay then the bright core — replaces a per-line
    // shadowBlur pass (two thin strokes are far cheaper than a blur).
    oc.beginPath(); oc.moveTo(ep.cx, ep.cy); oc.lineTo(pp.cx, pp.cy);
    oc.strokeStyle = 'rgba(' + rgb + ',' + (a * 0.35).toFixed(3) + ')';
    oc.lineWidth   = core + 3;
    oc.stroke();
    oc.beginPath(); oc.moveTo(ep.cx, ep.cy); oc.lineTo(pp.cx, pp.cy);
    oc.strokeStyle = 'rgba(' + rgb + ',' + a.toFixed(3) + ')';
    oc.lineWidth   = core;
    oc.stroke();
  }
  oc.restore();
}

// Incoming missiles: the game's actual missile-warning sprite (served at /icon?type=__missilewarn)
// at each missile's map position, oriented to its travel heading and flashing red<->yellow
// (color = (1, sin(t·20)·0.5+0.5, 0), matching UnitMapIcon.SetMissileWarning). Drawn last (on
// top) since it's the most urgent cue; self-animated via the threat timer.
const MISSILE_ICON = '__missilewarn';
const MISSILE_BASE_IN = 15, MISSILE_BASE_OUT = 11;   // full icon height (px) by zoom level
function drawMissiles() {
  if (!lastData || !Array.isArray(lastData.mw) || !lastData.world) return;
  const t = performance.now() / 1000;
  let g = Math.round((Math.sin(t * 20) * 0.5 + 0.5) * 255);   // game flash: red (0) <-> yellow (255)
  g = Math.min(255, Math.round(g / 32) * 32);                 // quantise so tintedIcon's cache stays small
  const hex = '#ff' + ('0' + g.toString(16)).slice(-2) + '00';
  const base = zoomedIn() ? MISSILE_BASE_IN : MISSILE_BASE_OUT;
  for (const m of lastData.mw) {
    const mp = worldToOverlay(m.x, m.z);
    if (!mp) continue;
    ensureIconImage(MISSILE_ICON);
    // Orient to the missile's travel heading (like the game's map icon); 1.2× flash boost.
    const r = drawIcon(MISSILE_ICON, hex, mp.cx, mp.cy, m.h || 0, typeof m.h === 'number', base, 1.2);
    hitTargets.push({ cx: mp.cx, cy: mp.cy, r: r + HIT_PAD,
                      label: (m.st ? m.st + ' MISSILE' : 'MISSILE'), color: '#ff3b30' });
  }
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

  // Radar-warning spokes under the icons (icons stay readable on top).
  drawRwrLines();

  // Other units first, so the player's icon and label sit on top.
  if (lastData.contacts) {
    for (const u of lastData.contacts) {
      const p = worldToOverlay(u.x, u.z);
      if (!p) continue;
      ensureIconImage(u.t);
      const hex = factionColors[u.f] || factionColors[0];
      const r = drawIcon(u.t, hex, p.cx, p.cy, u.h, u.o, iconBase(), u.s);
      if (u.tg) { drawTargetBox(p.cx, p.cy, r + 4); pendingSel.delete(u.id); }   // telemetry confirms selection
      hitTargets.push({ cx: p.cx, cy: p.cy, r: r + HIT_PAD, label: u.t, color: hex, id: u.id, tg: !!u.tg });
    }
  }

  // Player plane (kept green regardless of faction colors), drawn and hit-tested last = on top.
  const pos = worldToOverlay(lastData.world.x, lastData.world.z);
  if (!pos) return;
  const pr = drawIcon(lastData.name, PLAYER_COLOR, pos.cx, pos.cy, lastData.hdg, lastData.iconOrient, iconBase(), lastData.iconScale);
  hitTargets.push({ cx: pos.cx, cy: pos.cy, r: pr + HIT_PAD, label: lastData.name, color: PLAYER_COLOR });

  // Incoming-missile triangles last = on top of everything (most urgent cue).
  drawMissiles();

  // Click-to-select feedback: a brief fading ring on the just-selected unit. Anchored by id so
  // it stays on the contact as the view pans/follows. The persistent confirmation is the target
  // box, which appears once the game echoes the selection back in the next telemetry frame.
  if (clickFlash) {
    const now = performance.now();
    if (now >= clickFlash.until) { clickFlash = null; }
    else {
      for (let i = 0; i < hitTargets.length; i++) {
        if (hitTargets[i].id === clickFlash.id) {
          const t = hitTargets[i];
          oc.save();
          oc.globalAlpha = Math.max(0, (clickFlash.until - now) / 450);
          oc.strokeStyle = '#ffffff';
          oc.lineWidth   = 2;
          oc.beginPath();
          oc.arc(t.cx, t.cy, t.r + 6, 0, Math.PI * 2);
          oc.stroke();
          oc.restore();
          break;
        }
      }
    }
  }
}

// Drives the click-flash fade between telemetry frames (which only arrive ~10 Hz).
let clickFlash = null;
function pumpFlash() { if (!clickFlash) return; drawOverlay(); requestAnimationFrame(pumpFlash); }
function flashSelect(id) { clickFlash = { id: id, until: performance.now() + 450 }; requestAnimationFrame(pumpFlash); }

// Missiles flash faster than the data rate, so while any are inbound we redraw on a ~20 fps
// timer (the sine reads performance.now(), so it stays smooth); it self-stops once the feed
// clears or the mission ends. Timer-driven (like RwrPage) rather than a perpetual rAF loop.
let threatTimer = null;
function ensureThreatAnimation() {
  const active = lastData && Array.isArray(lastData.mw) && lastData.mw.length;
  if (active && !threatTimer) {
    threatTimer = setInterval(function() {
      if (lastData && Array.isArray(lastData.mw) && lastData.mw.length) drawOverlay();
      else { clearInterval(threatTimer); threatTimer = null; }
    }, 50);
  } else if (!active && threatTimer) {
    clearInterval(threatTimer); threatTimer = null;
  }
}

// ── Image load / error ─────────────────────────────────────────────────────────
// The captured map image is produced asynchronously on the server, so it can lag the first
// telemetry frame that reports map.valid (and a mission/map change re-captures it). A single
// early /map fetch then 404s and sticks as NO SIGNAL until a manual page reload. So on error we
// retry — while a mission is active — until the image loads, cache-busting each attempt.
let mapRetryTimer = null, mapRetries = 0;
const MAP_MAX_RETRIES = 30;   // ~24 s at 800 ms — covers a slow capture, then gives up
function setNoSignal(on) { document.getElementById('map-missing').style.display = on ? 'block' : 'none'; }
mapImg.onerror = function() {
  mapImg.classList.add('missing');
  setNoSignal(true);
  if (mapMeta && !mapRetryTimer && mapRetries < MAP_MAX_RETRIES) {
    mapRetryTimer = setTimeout(function() {
      mapRetryTimer = null;
      if (mapMeta) { mapRetries++; mapImg.src = '/map?t=' + Date.now(); }   // mission still active → try again
    }, 800);
  }
};
mapImg.onload = function() {
  if (mapRetryTimer) { clearTimeout(mapRetryTimer); mapRetryTimer = null; }
  mapRetries = 0;
  mapImg.classList.remove('missing');
  setNoSignal(false);
  resizeOverlay();
};

// ── Frame rendering (driven by TelemetrySource) ──────────────────────────────────
let mapWasValid = false;

// A real telemetry frame arrived — render the map + HUD. The provider slices were already derived
// and posted up to the shell by the source; this is purely the local render.
function renderFrame(d) {
  lastData = d;
  ensureIconImage(d.name);
  if (d.colors) factionColors = { 0: d.colors.n, 1: d.colors.f, 2: d.colors.e };

  if (d.map && d.map.valid) {
    mapMeta = { w: d.map.w, h: d.map.h, ox: d.map.ox, oy: d.map.oy };
    // The game's map image becomes available shortly after the mission loads; refresh once (the
    // onerror retry covers the case where the capture isn't ready yet at this first attempt).
    if (!mapWasValid) {
      mapWasValid = true;
      mapRetries = 0;
      if (mapRetryTimer) { clearTimeout(mapRetryTimer); mapRetryTimer = null; }
      mapImg.src = '/map?t=' + Date.now();
      document.getElementById('map-panel').classList.add('has-map');
      // A freshly-loaded map (split pane / reload) or a new mission after the no-signal reset adopts
      // the persisted FLW + ZOOM here, and setFollow reports it up so the shell paints the chip.
      loadPersistedView();
      setFollow(followPlayer);
    }
  }

  updateHUD(d);
  drawOverlay();
  ensureThreatAnimation();   // start/keep the missile-flash loop while any missile is inbound
}

// A no-mission ping. didEnd is true on the mission→no-mission transition, so wipe the view once;
// every ping shows NO SIGNAL (idempotent).
function handleNoMission(didEnd) {
  if (didEnd) clearViewState();
  setNoSignal(true);
}

// The single telemetry provider for the whole MFD: it owns /stream, derives the per-page slices,
// and broadcasts them up — including the connection status, which the shell renders on MAIN (MAP
// itself has no status readout). We just render the frames it hands back; connect() is
// called from init.
const source = new TelemetrySource({ onFrame: renderFrame, onNoMission: handleNoMission });

// Wipe the view when a mission/map exits, so stale data never lingers on screen. The matching
// "everything is empty" broadcast to the shell is the source's job (_emitEmpties); NO SIGNAL is
// set by handleNoMission, which calls this.
function clearViewState() {
  lastData = null;
  mapMeta = null;
  if (threatTimer) { clearInterval(threatTimer); threatTimer = null; }   // stop the missile-flash loop
  mapWasValid = false;
  view.zoom = 1; view.panX = 0; view.panY = 0;   // next mission starts at full extent
  followPlayer = false;                           // follow resets for the next mission
  oc.clearRect(0, 0, overlay.width, overlay.height);
  document.getElementById('map-panel').classList.remove('has-map');
  mapImg.src = '/map?t=' + Date.now();   // 404 now → falls back to the placeholder

  document.getElementById('mission-bar').className = 'empty';
  document.getElementById('grid-bar').className = 'mfd-chip empty';
}

// ── HUD ──────────────────────────────────────────────────────────────────────────
// MAP's own on-map chrome: the mission-name bar (top-left) and the GRID chip (bottom-right).
// Every other telemetry slice (status / loadout / cm / tgp / targets / rwr / mw / avn) is derived
// and broadcast to the shell by TelemetrySource._emit — the dedicated MFD pages render those.
// This pair is the exception both ways: it is drawn here from the raw frame, AND emitted (as
// 'mapinfo') for shell chrome that shows no map — the F-35's master strip. The two derive the
// same values independently; neither reads the other.
function updateHUD(d) {
  const bar = document.getElementById('mission-bar');
  if (d.mission) {
    bar.className = '';
    document.getElementById('mission-name').textContent = d.mission;
  } else {
    bar.className = 'empty';
  }

  const gridText = gridLabel(d.world.x, d.world.z, mapMeta);
  gridBar.textContent = 'GRID: ' + gridText;
  gridBar.className = 'mfd-chip';
}

// ── Map zoom / pan ───────────────────────────────────────────────────────────────
function resetView() { view.zoom = 1; view.panX = 0; view.panY = 0; setFollow(false); }

// Toggle follow mode (keyboard F or the MFD's FLW key). The on-screen badge is a status
// indicator only — drawOverlay does the centring.
function setFollow(on) {
  followPlayer = on;
  savePersistedView();
  drawOverlay();
  source.emitFollow(on);   // mirror the follow state up to the shell, which renders the FOLLOW chip
}
window.addEventListener('keydown', function(e) {
  if ((e.key === 'f' || e.key === 'F') && mapMeta) setFollow(!followPlayer);
});

// ── Map gestures (mouse + touch) ──────────────────────────────────────────────────
// One pointer set drives pan, pinch-zoom, and tap-select so single-finger and two-finger
// gestures never fight each other.
const pointers = new Map();          // active pointerId -> {x,y}
let panId = null, lastX = 0, lastY = 0;
let pinching = false, pinchStartDist = 0, pinchStartZoom = 1;
let gestureMoved = false, downX = 0, downY = 0;
// Touch taps are imprecise (fat finger), so a tap-select reaches this many extra px beyond an
// icon's hit circle and grabs the nearest contact in range. Mouse clicks stay pixel-precise.
const TOUCH_HIT_PAD = 22;
let lastPointerType = 'mouse';
function pinchGeom() {
  const p = [...pointers.values()];
  return { dist: Math.hypot(p[0].x - p[1].x, p[0].y - p[1].y),
           mx: (p[0].x + p[1].x) / 2, my: (p[0].y + p[1].y) / 2 };
}

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
  if (followPlayer) { view.zoom = z1; savePersistedView(); clampPan(); drawOverlay(); return; }
  // pan1 = d - (z1/z0)(d - pan0), with d = cursor − centre — holds the cursor's point in place.
  view.panX = (sx - ox) - (z1 / z0) * ((sx - ox) - view.panX);
  view.panY = (sy - oy) - (z1 / z0) * ((sy - oy) - view.panY);
  view.zoom = z1;
  savePersistedView();
  clampPan();
  drawOverlay();
}, { passive: false });

// Pan (single pointer, once zoomed in) and pinch-zoom (two pointers). Follow mode stays
// LOCKED: neither a pan-drag nor a pinch disengages it, matching the in-game followed map —
// only the FLW button / 'f' key toggles it.
overlay.addEventListener('pointerdown', function(e) {
  pointers.set(e.pointerId, { x: e.clientX, y: e.clientY });
  downX = e.clientX; downY = e.clientY; gestureMoved = false;
  lastPointerType = e.pointerType || 'mouse';   // drives the tap-select reach (touch = fat finger)
  if (pointers.size === 2 && mapMeta) {                 // second finger → start a pinch
    if (panId !== null) { try { overlay.releasePointerCapture(panId); } catch (_) {} panId = null; }
    const g = pinchGeom();
    pinching = true; pinchStartDist = g.dist; pinchStartZoom = view.zoom;
    return;
  }
  if (mapMeta && view.zoom > MIN_ZOOM && !followPlayer) {
    panId = e.pointerId; lastX = e.clientX; lastY = e.clientY;
    overlay.setPointerCapture(e.pointerId);
  }
});
overlay.addEventListener('pointermove', function(e) {
  if (!pointers.has(e.pointerId)) return;
  pointers.set(e.pointerId, { x: e.clientX, y: e.clientY });
  if (Math.abs(e.clientX - downX) > 4 || Math.abs(e.clientY - downY) > 4) gestureMoved = true;

  if (pinching && pointers.size >= 2 && mapMeta) {       // pinch-zoom about the finger midpoint
    e.preventDefault();
    if (pinchStartDist <= 0) return;
    const g = pinchGeom();
    const z0 = view.zoom;
    const z1 = Math.max(MIN_ZOOM, Math.min(MAX_ZOOM, pinchStartZoom * (g.dist / pinchStartDist)));
    if (z1 === z0) return;
    if (followPlayer) { view.zoom = z1; savePersistedView(); clampPan(); drawOverlay(); return; }   // follow re-centres
    const rect = overlay.getBoundingClientRect();
    const sx = g.mx - rect.left, sy = g.my - rect.top, ox = overlay.width / 2, oy = overlay.height / 2;
    view.panX = (sx - ox) - (z1 / z0) * ((sx - ox) - view.panX);
    view.panY = (sy - oy) - (z1 / z0) * ((sy - oy) - view.panY);
    view.zoom = z1; savePersistedView(); clampPan(); drawOverlay();
    return;
  }
  if (e.pointerId === panId) {                            // single-finger / mouse pan
    view.panX += e.clientX - lastX;
    view.panY += e.clientY - lastY;
    lastX = e.clientX; lastY = e.clientY;
    clampPan(); drawOverlay();
  }
}, { passive: false });
function dropPointer(e) {
  pointers.delete(e.pointerId);
  if (pointers.size < 2) pinching = false;
  if (e.pointerId === panId) { try { overlay.releasePointerCapture(e.pointerId); } catch (_) {} panId = null; }
}
overlay.addEventListener('pointerup', dropPointer);
overlay.addEventListener('pointercancel', dropPointer);
// Double-click empty map = reset to full view (a mouse affordance). Skip it entirely for touch:
// players tap rapidly to select stacked/nearby contacts, and a fat-finger double-tap on near-empty
// map would otherwise zoom all the way out mid-selection. Pinch still zooms out on touch. A
// double-click ON a contact is a selection gesture (two taps), so ignore it there too — otherwise
// selecting a unit would zoom out + drop FLW.
overlay.addEventListener('dblclick', function(e) {
  if (!mapMeta || lastPointerType === 'touch') return;
  const rect = overlay.getBoundingClientRect();
  const mx = e.clientX - rect.left, my = e.clientY - rect.top;
  for (let i = 0; i < hitTargets.length; i++) {
    const t = hitTargets[i];
    if (t.id == null) continue;
    const dx = mx - t.cx, dy = my - t.cy;
    if (dx * dx + dy * dy <= t.r * t.r) return;   // over a contact → not a reset
  }
  resetView();
});

// ── Hover-to-label ───────────────────────────────────────────────────────────────
// Icons are canvas pixels, so we hit-test the cursor against the per-frame hitTargets
// (positions are post-zoom/pan, so this stays correct at any view). Cursor-anchored.
const mapPanel = document.getElementById('map-panel');
mapPanel.addEventListener('mousemove', function(e) {
  // Touch has no hover: a tap emits a synthetic mousemove but never a mouseleave, so the label
  // would stick forever (even after the unit dies). Touch taps are select-only — mouse hovers label.
  if (lastPointerType === 'touch') { unitLabel.style.display = 'none'; return; }
  if (panId !== null) { unitLabel.style.display = 'none'; return; }   // don't flicker while panning
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

// ── Tap-to-select (POC write path) ──────────────────────────────────────────────────
// A tap on a contact POSTs its id to /select; the mod targets it in-game. Map-select only ever
// ADDS targets — it never deselects. So a tap picks the nearest NOT-yet-selected contact under
// the cursor: tapping an already-selected unit selects the next nearby one instead, and when
// every nearby contact is already selected the tap is a no-op. Taps that were really a pan/pinch
// (gestureMoved) are ignored, and the player icon has no id so it's never selectable.
//
// Selection state comes from each contact's tg flag (telemetry), but that lags a tap by ~100 ms.
// pendingSel optimistically marks a just-tapped id as selected until telemetry confirms it (the
// contact loop clears it on tg, and entries self-expire), so rapid taps advance through a stack
// instead of re-hitting the same unit.
// sendCommand(cmd, args) — POST /command — is provided by src/web/services/send-command.js (linked as
// a classic <script> before this module in map.html, so it's a plain global). Returns the raw
// fetch promise; the tap handler below reacts to r.ok and attaches its own .catch.

const pendingSel = new Map();   // id -> expiry ts
function isSelected(t) {
  if (t.tg) return true;
  const exp = pendingSel.get(t.id);
  if (exp === undefined) return false;
  if (performance.now() >= exp) { pendingSel.delete(t.id); return false; }
  return true;
}
overlay.addEventListener('click', function(e) {
  if (gestureMoved) return;   // that was a pan/pinch, not a select
  const rect = overlay.getBoundingClientRect();
  const mx = e.clientX - rect.left, my = e.clientY - rect.top;
  // Touch taps reach past the icon (fat finger); a mouse stays precise. Pick the NEAREST
  // unselected contact within reach so a fat tap grabs whatever's closest, not just the
  // topmost icon its centre happened to land in.
  const pad = lastPointerType === 'touch' ? TOUCH_HIT_PAD : 0;
  let hit = null, bestD2 = Infinity;
  for (let i = hitTargets.length - 1; i >= 0; i--) {
    const t = hitTargets[i];
    if (t.id == null || isSelected(t)) continue;
    const dx = mx - t.cx, dy = my - t.cy, d2 = dx * dx + dy * dy;
    const reach = t.r + pad;
    if (d2 <= reach * reach && d2 < bestD2) { bestD2 = d2; hit = t; }
  }
  if (!hit) return;   // nothing in reach, or everything in reach already selected → no-op (never deselects)
  pendingSel.set(hit.id, performance.now() + 1500);
  sendCommand('target.select', { id: hit.id })
    .then(function(r) { if (r.ok) flashSelect(hit.id); else pendingSel.delete(hit.id); })
    .catch(function() { pendingSel.delete(hit.id); });
});

// ── Remote control ────────────────────────────────────────────────────────────────
// Lets an embedder (the MFD frame) drive the map without reaching into it directly, so
// the map stays a self-contained component. Works same-origin and cross-origin (file://).
function zoomStep(factor) {   // zoom about the canvas centre (the wheel zooms at the cursor)
  if (!mapMeta) return;
  const z = Math.max(MIN_ZOOM, Math.min(MAX_ZOOM, view.zoom * factor));
  if (z === view.zoom) return;
  view.zoom = z;
  savePersistedView();
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
    case 'status-request': source.rebroadcastStatus(); break;   // shell asked for the current status
  }
});

// ── Init ──────────────────────────────────────────────────────────────────────────
// Size the canvas to its panel. This module is deferred (type="module"), so init can run while
// the shell's power-on boot still has the recess mid-layout (panel width 0). Retry on the next
// frame until the panel has a real width, so the first sizing isn't stuck on a transient 0 — the
// ResizeObserver below handles every later change.
function syncSizeWhenReady() {
  resizeOverlay();
  if (document.getElementById('map-panel').clientWidth === 0) requestAnimationFrame(syncSizeWhenReady);
}
loadPersistedView();       // adopt the persisted FLW + ZOOM (or the defaults) before the first paint
syncSizeWhenReady();
setFollow(followPlayer);    // report the restored follow up to the shell (paints the FOLLOW chip)
source.connect();   // open /stream now that the renderer + interaction handlers are wired

// Keep the canvas sized to its panel. A ResizeObserver — not just window 'resize' — is essential:
// in split mode the shell sets this map iframe to display:none, so #map-panel collapses to 0×0
// (and any stray resize while hidden zeroes the canvas). When the pane is shown again the panel
// grows 0→N but no window 'resize' fires inside the iframe, so the canvas would stay 0×0 and the
// map renders black until a manual reload. Observing the panel catches that 0→N transition and
// re-sizes + redraws. (resizeOverlay is idempotent; the observer subsumes the window listener.)
if (window.ResizeObserver) {
  new ResizeObserver(resizeOverlay).observe(document.getElementById('map-panel'));
} else {
  window.addEventListener('resize', resizeOverlay);
}
