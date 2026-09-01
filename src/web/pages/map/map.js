// MAP page — the VIEW half: canvas rendering, the HUD readout, and pan/zoom/follow/select
// interactions. The telemetry transport + the derive-and-broadcast "provider" role live in
// TelemetrySource (telemetry-source.js); this file instantiates it and renders the frames it
// hands back. See src/web/README.md for why MAP is the telemetry tap.
import { TelemetrySource, gridLabel } from '/assets/services/telemetry-source.js';
import { createPadCursor } from '/assets/services/pad-cursor.js';

// ── State (declared first so callbacks never hit a temporal dead zone) ──────────
let   lastData  = null;        // last rendered frame (the source hands it to renderFrame)
let   mapMeta   = null;        // { w, h, ox, oy, rw, rh } — the view's copy, for worldToBase /
                                // gridLabel; rw/rh (issue #65) is the mission's real reachable
                                // extent, w/h the smaller square the minimap image itself covers

// Map-icon sizes switch with zoom: larger when zoomed in, smaller when zoomed out — so icons
// stay legible up close without cluttering the full-extent view. Picked by iconBase() /
// fallbackSize() against the zoom threshold defined below.
const ICON_BASE_IN  = 20, ICON_BASE_OUT  = 15;   // player + unit base size (px), scaled by iconScale
const FALLBACK_IN   = 10, FALLBACK_OUT   = 7;    // icon-less square size (px)
const HIT_PAD = 4;             // extra px around an icon that still counts as a hover hit
let   hitTargets = [];         // [{cx, cy, r, label}] rebuilt every drawOverlay() for hover
let   view = { zoom: 1, panX: 0, panY: 0 };   // map view: pan in screen px, zoom about canvas centre
const MIN_ZOOM = 1, MAX_ZOOM = 8;
// How far past the map image's own edge pan/cursor may reach, as a fraction of the image's dw/dh
// (see MapTransform.clampPan) — issue #65: a mission's real reachable extent (mapMeta.rw/rh, the
// server's MapReachW/H) can run past the square the minimap IMAGE covers (mapMeta.w/h), so a zero
// margin clamps the camera/cursor short of real content. Derived per mission, not a flat guess:
// zero whenever the server has no reach data beyond the image (rw/rh missing or no bigger than
// w/h), so a mission without it keeps the old exact-image clamp.
function edgeMarginFrac(w, reach) {
  return (reach && w > 0 && reach > w) ? (reach - w) / (2 * w) : 0;
}
// Icons grow once the map is zoomed in to 4x or more (zoom range is MIN..MAX = 1..8): zoom
// 1–3 uses the small OUT sizes, 4–8 the larger IN sizes.
const ICON_ZOOM_THRESHOLD = 4;
function zoomedIn()     { return view.zoom >= ICON_ZOOM_THRESHOLD; }
function iconBase()     { return zoomedIn() ? ICON_BASE_IN : ICON_BASE_OUT; }
function fallbackSize() { return zoomedIn() ? FALLBACK_IN  : FALLBACK_OUT; }
let   followPlayer = false;    // when on (and zoomed in), keep the player icon centred
let   gridOn       = false;    // coordinate grid overlay (issue #41), default off

// ── Waypoints/routes (issue #38) ──────────────────────────────────────────────────
let   waypointRoute = null;    // WaypointsStore's active route, cached for drawWaypoints()
let   steerPoints = [];
let   activeSteerPointId = null;
const WPT_LINE_COLOR = '#39d0ff';       // dashed route line + non-next markers
const WPT_NEXT_COLOR = '#ffaa00';       // the waypoint the WPT readout is currently tracking — matches
                                         // theme.css's --no-amber (WPT's own compass needle) and the
                                         // in-game HUD cue's amber, one color scheme across all three
const WPT_REACHED_COLOR = '#a0a0a0';    // waypoints/segments already flown past — lighter than the
                                         // theme's --no-gray (#5a5a5a), which nearly vanished against dark terrain
const STEER_POINT_COLOR = '#39ff14';    // a non-active steer point — theme's --no-green, distinct from
                                         // WPT_LINE_COLOR's route-line teal since steer points aren't routes
function refreshWaypointRoute() {
  const data = WaypointsStore.load();
  waypointRoute = WaypointsStore.getActiveRoute();
  steerPoints = data.steerPoints || [];
  activeSteerPointId = data.activeSteerPointId || null;
  updateRouteChip();
}
// ROUTE chip (bottom-right status row) — the active route's name, same show/hide-when-empty
// pattern as CURSOR (updateCursorChip). No active route: hidden, same as CURSOR with nothing hovered.
function updateRouteChip() {
  if (waypointRoute) { routeBar.textContent = 'ROUTE: ' + waypointRoute.name; routeBar.className = 'mfd-chip'; }
  else { routeBar.className = 'mfd-chip empty'; }
}
// The plugin is the single source of truth for routes (docs/hud-waypoint-indicator.md) — the
// WPT page (a separate iframe), another device's browser, or this map's own edits all converge
// through WaypointsStore's poll of /wpt-options, which fires this event on any real change.
window.addEventListener('wptroutes:changed', function() { refreshWaypointRoute(); drawOverlay(); });

// ── PAD cursor (docs/page-cursor.md, docs/map-cursor.md) ──────────────────────────
// A crosshair standing in for the mouse/touch while this MAP is the HOTAS-driven SOI focus. The
// shell alone decides whether that's true — cursor/cursor-select/cursor-focus messages only ever
// arrive here while this surface is the SOI's focused MAP — so this file just answers "where is the
// cursor, and what's under it," the same way it already does for a mouse click. The crosshair
// element/position/integrator live in the shared pad-cursor.js module (also used by TGT/HUD);
// this page only supplies the clamp rect and the select callback.
const CURSOR_HIT_PAD = 16;   // extra reach around an icon — coarser than a mouse, finer than a fat touch tap

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
const DEFAULT_GRID   = false; // coordinate grid overlay (issue #41) — off by default, toggleable
function loadPersistedView() {
  let saved = null;
  try { saved = JSON.parse(sessionStorage.getItem(VIEW_STORE_KEY) || 'null'); } catch (_) {}
  const z = saved && typeof saved.zoom === 'number' ? saved.zoom : DEFAULT_ZOOM;
  view.zoom = Math.max(MIN_ZOOM, Math.min(MAX_ZOOM, z));
  followPlayer = saved && typeof saved.follow === 'boolean' ? saved.follow : DEFAULT_FOLLOW;
  gridOn = saved && typeof saved.grid === 'boolean' ? saved.grid : DEFAULT_GRID;
}
function savePersistedView() {
  try { sessionStorage.setItem(VIEW_STORE_KEY, JSON.stringify({ zoom: view.zoom, follow: followPlayer, grid: gridOn })); } catch (_) {}
}
const PLAYER_COLOR = '#39ff14';                     // player stays HUD green — matches --no-green;
                                                     // canvas strokeStyle can't use CSS var()
const TARGET_COLOR = '#ff8000';                     // orange ring on the player's targeted unit(s)
const STALE_ALPHA  = 0.5;                           // faded icon opacity for a stale contact (F2)
// A squadmate's aircraft (issue #48, docs/squadron-transport.md) — matches --no-squad-rgb
// (78,201,201, theme.css); canvas strokeStyle can't use CSS var() so this is its own literal, same
// reasoning as PLAYER_COLOR above. Takes priority over the plain faction color, but never applies
// to the viewer's own plane — that's a separate draw call below that never reads factionColors at
// all, so there's nothing here for it to override.
const SQUAD_COLOR = '#4ec9c9';
let   factionColors = { 0: '#9aa0a6', 1: '#39ff14', 2: '#ff4040' };  // updated from the game's HUD colors —
                                                     // 1/2 default to --no-green/--no-red until then
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
const cursorBar = document.getElementById('cursor-bar');
const routeBar  = document.getElementById('route-bar');
const jamBar    = document.getElementById('jam-bar');
const unitLabel = document.getElementById('unit-label');
const cursorEl  = document.getElementById('soi-cursor');   // SOI crosshair — see pad-cursor.js

// The PAD cursor instance: clamps to the map image's rect (same footprint pan is clamped to, so it
// can't wander into the letterboxed margin), and Select picks the nearest unselected contact
// within CURSOR_HIT_PAD, same body as a mouse click / touch tap (selectAt, defined below).
const cursor = createPadCursor({
  el: cursorEl,
  clampRect: imgRect,
  onSelect: (x, y) => selectAt(x, y, CURSOR_HIT_PAD),
  onHold: (x, y) => placeWaypointAt(x, y),   // Cursor Select held past holdMs = waypoint placement (issue #38)
  onEdge: onCursorEdge,
  onMove: updateCursorChip,   // CURSOR chip tracks the PAD cursor too, not just the mouse
});

// Edge-panning (docs/page-cursor.md #3): the cursor lives in screen space and never leaves
// imgRect(), so pushing it against a border while more map exists past it (zoomed in) instead
// shifts the map that way, revealing what's past the edge — a 4-axis scroll, like an RTS map at
// the screen's border. Only while NOT following: under FLW the view is already re-centring on the
// player every frame (drawOverlay), and panning here would just fight that. ex/ey are how far past
// the rect (in screen px) the cursor's raw position landed this tick; clampPan already keeps the
// pan within the zoomed footprint, so this is a no-op at zoom=1 (nothing to reveal) with no extra
// gating needed.
const EDGE_PAN_SPEED = 700;   // screen px/second at full push, matching pad-cursor.js's own SPEED
function onCursorEdge(ex, ey, dt) {
  if (!mapMeta || followPlayer) return;
  if (ex) view.panX -= Math.sign(ex) * EDGE_PAN_SPEED * dt;
  if (ey) view.panY -= Math.sign(ey) * EDGE_PAN_SPEED * dt;
  clampPan();
  drawOverlay();
}

// ── Canvas geometry ──────────────────────────────────────────────────────────────
function resizeOverlay() {
  const panel = document.getElementById('map-panel');
  overlay.width  = panel.clientWidth;
  overlay.height = panel.clientHeight;
  clampPan();       // pan limits depend on canvas size; keep the view valid after a resize
  cursor.resize();  // same — the cursor's clamp rect shrank/grew too
  drawOverlay();
}

// Where the contain-fitted map image actually renders inside the overlay (letterbox-aware).
// The coordinate maths itself lives in map-transform.js, pure and unit-checked (its round trip is
// what the CURSOR chip's grid label rides on). This binds it to the live canvas/image/view/meta —
// the geometry the page owns and the module deliberately doesn't.
function geom() {
  return {
    canvas: { w: overlay.width, h: overlay.height },
    img: { w: mapImg.naturalWidth || overlay.width, h: mapImg.naturalHeight || overlay.height },
    view: view,
    meta: mapMeta,
  };
}

function imgRect() { return MapTransform.imgRect(geom()); }
function viewTransform(px, py) { return MapTransform.viewTransform(geom(), px, py); }
function worldToBase(wx, wz) { return MapTransform.worldToBase(geom(), wx, wz); }
function worldToOverlay(wx, wz) { return MapTransform.worldToOverlay(geom(), wx, wz); }
function overlayToWorld(sx, sy) { return MapTransform.overlayToWorld(geom(), sx, sy); }

function onScreen(p, pad) {
  return p && p.cx != null && p.cy != null &&
         p.cx >= -pad && p.cx <= overlay.width + pad &&
         p.cy >= -pad && p.cy <= overlay.height + pad;
}

// The module returns the clamped pair rather than mutating; the live view state stays owned here.
function clampPan() {
  const fx = mapMeta ? edgeMarginFrac(mapMeta.w, mapMeta.rw) : 0;
  const fy = mapMeta ? edgeMarginFrac(mapMeta.h, mapMeta.rh) : 0;
  const c = MapTransform.clampPan(geom(), view.panX, view.panY, fx, fy);
  view.panX = c.panX;
  view.panY = c.panY;
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
  oc.lineCap     = 'round';
  const s = half;
  const k = Math.max(3, s * 0.5);   // corner arm length
  oc.beginPath();
  oc.moveTo(-s, -s + k); oc.lineTo(-s, -s); oc.lineTo(-s + k, -s);   // top-left
  oc.moveTo( s - k, -s); oc.lineTo( s, -s); oc.lineTo( s, -s + k);   // top-right
  oc.moveTo( s,  s - k); oc.lineTo( s,  s); oc.lineTo( s - k,  s);   // bottom-right
  oc.moveTo(-s + k,  s); oc.lineTo(-s,  s); oc.lineTo(-s,  s - k);   // bottom-left
  // Cheap glow: a wide translucent underlay plus the bright core. This keeps the lock brackets
  // legible without paying canvas' live shadowBlur cost on every target redraw.
  oc.strokeStyle = TARGET_COLOR;
  oc.globalAlpha = 0.35;
  oc.lineWidth   = 5;
  oc.stroke();
  oc.globalAlpha = 1;
  oc.lineWidth   = 1.5;
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
    if (!onScreen(mp, 48)) continue;
    ensureIconImage(MISSILE_ICON);
    // Orient to the missile's travel heading (like the game's map icon); 1.2× flash boost.
    const r = drawIcon(MISSILE_ICON, hex, mp.cx, mp.cy, m.h || 0, typeof m.h === 'number', base, 1.2);
    hitTargets.push({ cx: mp.cx, cy: mp.cy, r: r + HIT_PAD,
                      label: (m.st ? m.st + ' MISSILE' : 'MISSILE'), color: '#ff3b30' });
  }
}

// Radar jamming (replicates the game's MAP JammedMarker): a yellow line from a jammed unit's
// icon to whoever is jamming it, plus a small lightning glyph on the jammed icon itself. The
// glyph always shows once jm is true (matching the game keeping its jammedImage on regardless
// of the line); the line only draws when the jammer's own position can be resolved — either
// another tracked contact (jb matched by id) or the player (jb === playerId).
function jamTargetPos(id) {
  if (!lastData) return null;
  if (lastData.playerId && id === lastData.playerId) return worldToOverlay(lastData.world.x, lastData.world.z);
  if (Array.isArray(lastData.contacts)) {
    for (const c of lastData.contacts) if (c.id === id) return worldToOverlay(c.x, c.z);
  }
  return null;
}
function drawJamLines() {
  if (!lastData) return;
  oc.save();
  oc.strokeStyle = 'rgba(255,221,0,0.85)';
  oc.lineWidth = 1.5;
  oc.lineCap = 'round';
  if (Array.isArray(lastData.contacts)) {
    for (const c of lastData.contacts) {
      if (!c.jm || !c.jb) continue;
      const from = worldToOverlay(c.x, c.z), to = jamTargetPos(c.jb);
      if (!from || !to) continue;
      oc.beginPath(); oc.moveTo(from.cx, from.cy); oc.lineTo(to.cx, to.cy); oc.stroke();
    }
  }
  if (lastData.pjm && lastData.pjb && lastData.world) {
    const from = worldToOverlay(lastData.world.x, lastData.world.z), to = jamTargetPos(lastData.pjb);
    if (from && to) { oc.beginPath(); oc.moveTo(from.cx, from.cy); oc.lineTo(to.cx, to.cy); oc.stroke(); }
  }
  oc.restore();
}
// A hand-drawn bolt, not the '⚡' glyph: that emoji is a multi-color font glyph (its own baked-in
// yellow-to-black shading), so fillStyle never actually touches its fill — only the shadow halo
// took our color, which is why it read as a gradient instead of solid orange.
// Centered directly on the jammed icon (same footprint, r matching drawIcon's own half-extent),
// fully opaque, ringed by a dotted circle so the marker reads as "this unit" even overlapping
// the icon underneath.
function drawJamGlyph(cx, cy, r) {
  const s = r * 2.4;
  oc.save();
  oc.strokeStyle = TARGET_COLOR;
  oc.lineWidth = 1.5;
  oc.setLineDash([3, 3]);
  oc.beginPath();
  oc.arc(cx, cy, r + 5, 0, Math.PI * 2);
  oc.stroke();
  oc.setLineDash([]);
  oc.translate(cx, cy);
  oc.fillStyle = TARGET_COLOR;
  oc.beginPath();
  oc.moveTo(s * 0.15, -s * 0.5);
  oc.lineTo(-s * 0.15, s * 0.05);
  oc.lineTo(s * 0.05, s * 0.05);
  oc.lineTo(-s * 0.1, s * 0.5);
  oc.lineTo(s * 0.25, -s * 0.02);
  oc.lineTo(s * 0.05, -s * 0.02);
  oc.closePath();
  oc.fill();
  oc.restore();
}

// ── Coordinate grid overlay (issue #41) ─────────────────────────────────────────────
// Redraws the game's own major/minor grid-square scheme — the same math gridLabel() uses to name
// a point (e.g. "Li36"), run in reverse to find which lines cross the map: minor lines every 1 km,
// bolder major lines + edge labels (numbers along the top, letters down the left, matching the
// native in-game map's placement) every 10 km. Toggleable via the GRID key, off by default.
// Iterated by integer grid-line index rather than accumulating world-unit floats, so "is this a
// major line" (index % 10 === 0) never drifts off after many additions.
const GRID_MINOR_UNIT  = 1000;   // world units per minor line (1 km)
const GRID_LINES_PER_MAJOR = 10; // minor lines between major lines (10 km majors)
const GRID_MINOR_COLOR = 'rgba(57,255,20,0.10)';    // matches --no-green-rgb; canvas can't use var()
const GRID_MAJOR_COLOR = 'rgba(57,255,20,0.30)';
const GRID_LABEL_COLOR = 'rgba(196,255,176,0.75)';
function drawGrid() {
  if (!gridOn || !mapMeta || mapMeta.w <= 0 || mapMeta.h <= 0) return;
  // Drawn out to the mission's real reachable extent (rw/rh, issue #65), not just the smaller w/h
  // square the minimap image covers — worldToOverlay projects correctly past the image either way.
  const rw = mapMeta.rw || mapMeta.w, rh = mapMeta.rh || mapMeta.h;
  const wMinX = -rw / 2, wMaxX = rw / 2;
  const wMinZ = -rh / 2, wMaxZ = rh / 2;
  // Grid-space (vx,vz) bounds the map spans — gridLabel's vx = ox+wx, vz = oy-wz — clamped to 0
  // since gridLabel itself treats negative grid-space as "off the labelled grid".
  const vMinX = Math.max(0, mapMeta.ox + wMinX), vMaxX = mapMeta.ox + wMaxX;
  const vMinZ = Math.max(0, mapMeta.oy - wMaxZ), vMaxZ = mapMeta.oy - wMinZ;
  const iMinX = Math.ceil(vMinX / GRID_MINOR_UNIT), iMaxX = Math.floor(vMaxX / GRID_MINOR_UNIT);
  const iMinZ = Math.ceil(vMinZ / GRID_MINOR_UNIT), iMaxZ = Math.floor(vMaxZ / GRID_MINOR_UNIT);

  oc.save();
  oc.lineWidth = 1;
  // Vertical lines (constant world X).
  for (let i = iMinX; i <= iMaxX; i++) {
    const wx = i * GRID_MINOR_UNIT - mapMeta.ox;
    const top = worldToOverlay(wx, wMaxZ), bot = worldToOverlay(wx, wMinZ);
    if (!top || !bot) continue;
    oc.strokeStyle = (i % GRID_LINES_PER_MAJOR === 0) ? GRID_MAJOR_COLOR : GRID_MINOR_COLOR;
    oc.beginPath(); oc.moveTo(top.cx, top.cy); oc.lineTo(bot.cx, bot.cy); oc.stroke();
  }
  // Horizontal lines (constant world Z).
  for (let i = iMinZ; i <= iMaxZ; i++) {
    const wz = mapMeta.oy - i * GRID_MINOR_UNIT;
    const left = worldToOverlay(wMinX, wz), right = worldToOverlay(wMaxX, wz);
    if (!left || !right) continue;
    oc.strokeStyle = (i % GRID_LINES_PER_MAJOR === 0) ? GRID_MAJOR_COLOR : GRID_MINOR_COLOR;
    oc.beginPath(); oc.moveTo(left.cx, left.cy); oc.lineTo(right.cx, right.cy); oc.stroke();
  }

  // Major-line labels — the same digits/letter gridLabel() would emit for a point on that line.
  // Pinned to the CANVAS edge (not r.dx/r.dy, imgRect()'s zoom=1 letterbox offset): that offset
  // only matches the map's actual rendered edge at zoom=1/pan=0, so anchoring labels to it made
  // them drift inward off the true panel edge at any other zoom/pan — worst at the default zoom
  // (4x) and most visible in a split pane, where letterboxing differs more from full view's.
  // Pinning to the canvas edge keeps them glued to the pane's true top/left regardless.
  oc.fillStyle = GRID_LABEL_COLOR;
  oc.font = '22px "Courier New", monospace';
  oc.textBaseline = 'top';
  for (let i = iMinX - (iMinX % GRID_LINES_PER_MAJOR); i <= iMaxX; i += GRID_LINES_PER_MAJOR) {
    if (i < iMinX) continue;
    const p = worldToOverlay(i * GRID_MINOR_UNIT - mapMeta.ox, wMaxZ);
    if (p) oc.fillText(String(i / GRID_LINES_PER_MAJOR), p.cx + 2, 4);
  }
  // 'top' baseline + a few px below the line itself (not 'middle' centred ON it) — a letter
  // straddling its own gridline reads ambiguously as to which row it's naming.
  oc.textBaseline = 'top';
  for (let i = iMinZ - (iMinZ % GRID_LINES_PER_MAJOR); i <= iMaxZ; i += GRID_LINES_PER_MAJOR) {
    if (i < iMinZ) continue;
    const p = worldToOverlay(wMinX, mapMeta.oy - i * GRID_MINOR_UNIT);
    if (p) oc.fillText(String.fromCharCode(65 + i / GRID_LINES_PER_MAJOR), 4, p.cy + 2);
  }
  oc.restore();
}

// Pilot-placed waypoints/route (issue #38) — a dashed line through the active route in order, plus
// a small numbered marker per waypoint. Drawn right after the grid: navigational chrome the pilot
// plans against, same "under icons, above map image" layer. The current "next" waypoint (the one
// the WPT page's readout is tracking) is drawn brighter so the map alone shows which one it is;
// everything before it (index < nextIndex) is already flown, drawn dim gray — a waypoint carries no
// "reached" flag of its own (WptRoute's nextIndex is a plain progress COUNT, not a per-waypoint
// state — same reasoning the reorder/delete fix relies on), so "reached" here is just index < nextIndex.
function drawWaypoints() {
  if (!waypointRoute || !waypointRoute.waypoints.length) return;
  const pts = waypointRoute.waypoints.map(w => Object.assign({}, w, worldToOverlay(w.x, w.z)));
  oc.save();
  oc.setLineDash([6, 5]);
  oc.lineWidth = 1.5;
  // Segment coloring is WptRoute.segmentReached (wpt-route.js, pure + tested) — the off-by-one this
  // pins down (grabbing the wrong end of the segment leading INTO "next") was a real shipped bug.
  for (let i = 0; i < pts.length - 1; i++) {
    const a = pts[i], b = pts[i + 1];
    if (a.cx == null || b.cx == null) continue;
    oc.strokeStyle = WptRoute.segmentReached(i, waypointRoute.nextIndex) ? WPT_REACHED_COLOR : WPT_LINE_COLOR;
    oc.beginPath();
    oc.moveTo(a.cx, a.cy);
    oc.lineTo(b.cx, b.cy);
    oc.stroke();
  }
  oc.setLineDash([]);
  pts.forEach((p, i) => {
    if (!onScreen(p, 48)) return;
    const state = WptRoute.waypointMarkerState(i, waypointRoute.nextIndex);   // pure + tested, wpt-route.js
    const next = state === 'next';
    const color = next ? WPT_NEXT_COLOR : state === 'reached' ? WPT_REACHED_COLOR : WPT_LINE_COLOR;
    const r = next ? 6 : 4;
    oc.strokeStyle = color;
    oc.beginPath();
    oc.arc(p.cx, p.cy, r, 0, Math.PI * 2);
    oc.globalAlpha = next ? 0.45 : 0.3;
    oc.lineWidth = next ? 5 : 3;
    oc.stroke();
    oc.globalAlpha = 1;
    oc.fillStyle = color;
    oc.beginPath();
    oc.arc(p.cx, p.cy, r, 0, Math.PI * 2);
    oc.fill();
    oc.font = '13px "Courier New", monospace';
    oc.textBaseline = 'bottom';
    oc.fillText(p.name ? (i + 1) + ' ' + p.name : String(i + 1), p.cx + 8, p.cy - 4);
  });
  oc.restore();
}

function drawSteerPoints() {
  if (!steerPoints.length) return;
  oc.save();
  steerPoints.forEach(function (point, i) {
    const p = worldToOverlay(point.x, point.z);
    if (!p || !onScreen(p, 48)) return;
    const active = !waypointRoute && point.id === activeSteerPointId;
    const color = active ? WPT_NEXT_COLOR : STEER_POINT_COLOR;
    const r = active ? 7 : 5;
    oc.strokeStyle = color;
    oc.lineWidth = active ? 3 : 2;
    oc.beginPath();
    oc.moveTo(p.cx, p.cy - r);
    oc.lineTo(p.cx + r, p.cy);
    oc.lineTo(p.cx, p.cy + r);
    oc.lineTo(p.cx - r, p.cy);
    oc.closePath();
    oc.stroke();
    oc.fillStyle = color;
    oc.font = '13px "Courier New", monospace';
    oc.textBaseline = 'bottom';
    oc.fillText(point.name ? 'S' + (i + 1) + ' ' + point.name : 'S' + (i + 1), p.cx + 10, p.cy - 4);
  });
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

  // Coordinate grid under the icons, same layer as the RWR spokes.
  drawGrid();

  // Pilot-placed waypoints/route (issue #38) — same layer as the grid.
  drawWaypoints();
  drawSteerPoints();

  // Radar-warning spokes under the icons (icons stay readable on top).
  drawRwrLines();
  drawJamLines();

  // Other units first, so the player's icon and label sit on top.
  if (lastData.contacts) {
    for (const u of lastData.contacts) {
      const p = worldToOverlay(u.x, u.z);
      if (!onScreen(p, 48)) continue;
      ensureIconImage(u.t);
      const hex = u.sq ? SQUAD_COLOR : (factionColors[u.f] || factionColors[0]);
      if (u.st) oc.globalAlpha = STALE_ALPHA;
      const r = drawIcon(u.t, hex, p.cx, p.cy, u.h, u.o, iconBase(), u.s);
      if (u.st) oc.globalAlpha = 1;
      if (u.tg) { drawTargetBox(p.cx, p.cy, r + 4); pendingSel.delete(u.id); }   // telemetry confirms selection
      if (u.jm) drawJamGlyph(p.cx, p.cy, r);
      hitTargets.push({ cx: p.cx, cy: p.cy, r: r + HIT_PAD, label: u.t, color: hex, id: u.id, tg: !!u.tg });
    }
  }

  // Player plane (kept green regardless of faction colors), drawn and hit-tested last = on top.
  // Never culled (issue #65): pinned to the nearest canvas edge instead of vanishing when the
  // aircraft/carrier sits past what EDGE_MARGIN_FRAC's pan/cursor slack can bring back on screen.
  const rawPos = worldToOverlay(lastData.world.x, lastData.world.z);
  if (rawPos) {
    const edgePad = iconBase() / 2 + 4;
    const pos = {
      cx: Math.max(edgePad, Math.min(overlay.width  - edgePad, rawPos.cx)),
      cy: Math.max(edgePad, Math.min(overlay.height - edgePad, rawPos.cy)),
    };
    const pr = drawIcon(lastData.name, PLAYER_COLOR, pos.cx, pos.cy, lastData.hdg, lastData.iconOrient, iconBase(), lastData.iconScale);
    if (lastData.pjm) drawJamGlyph(pos.cx, pos.cy, pr);
    hitTargets.push({ cx: pos.cx, cy: pos.cy, r: pr + HIT_PAD, label: lastData.name, color: PLAYER_COLOR });
  }

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

  // Waypoint placement feedback: a brief fading ring where the pilot just long-pressed. Screen-px
  // anchored (not id-anchored like clickFlash) since a fresh waypoint has no hitTargets entry.
  if (wptFlash) {
    const now = performance.now();
    if (now >= wptFlash.until) { wptFlash = null; }
    else {
      oc.save();
      oc.globalAlpha = Math.max(0, (wptFlash.until - now) / 450);
      oc.strokeStyle = WPT_NEXT_COLOR;
      oc.lineWidth   = 2;
      oc.beginPath();
      oc.arc(wptFlash.cx, wptFlash.cy, 14, 0, Math.PI * 2);
      oc.stroke();
      oc.restore();
    }
  }

  // The PAD cursor is NOT drawn here — it's #soi-cursor, its own element (pad-cursor.js). Slewing it
  // would otherwise force this whole function per frame just to shift a 24px mark.
}

// Drives the click-flash fade between telemetry frames (which only arrive ~10 Hz).
let clickFlash = null;
function pumpFlash() { if (!clickFlash) return; requestDraw(); requestAnimationFrame(pumpFlash); }
function flashSelect(id) { clickFlash = { id: id, until: performance.now() + 450 }; requestAnimationFrame(pumpFlash); }

// Missiles flash faster than the data rate, so while any are inbound we redraw on a ~20 fps
// timer (the sine reads performance.now(), so it stays smooth); it self-stops once the feed
// clears or the mission ends. Timer-driven (like RwrPage) rather than a perpetual rAF loop.
let threatTimer = null;
function ensureThreatAnimation() {
  const active = lastData && Array.isArray(lastData.mw) && lastData.mw.length;
  if (active && !threatTimer) {
    threatTimer = setInterval(function() {
      if (lastData && Array.isArray(lastData.mw) && lastData.mw.length) requestDraw();
      else { clearInterval(threatTimer); threatTimer = null; }
    }, 50);
  } else if (!active && threatTimer) {
    clearInterval(threatTimer); threatTimer = null;
  }
}

// ── Image load / error ─────────────────────────────────────────────────────────
// The captured map image is produced asynchronously on the server, so it can lag the first
// telemetry frame that reports map.valid (and a mission/map change re-captures it). So on error we
// retry — while a mission is active — until the image loads, cache-busting each attempt. While
// mapMeta is set (telemetry has already confirmed a real mission/map), an early 404 retries
// silently in the background instead of flashing NO SIGNAL back on — renderFrame already cleared
// it the moment telemetry confirmed the mission, and the image itself is a slower, separate
// asset lagging behind that same confirmed-valid state, not a sign the connection dropped.
let mapRetryTimer = null, mapRetries = 0;
const MAP_MAX_RETRIES = 30;   // ~24 s at 800 ms — covers a slow capture, then gives up
function setNoSignal(on) { document.getElementById('map-missing').style.display = on ? 'block' : 'none'; }
mapImg.onerror = function() {
  mapImg.classList.add('missing');
  if (!mapMeta) setNoSignal(true);   // no confirmed mission yet — a real "nothing to show" state
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
let drawPending = false;
function requestDraw() {
  if (drawPending) return;
  drawPending = true;
  requestAnimationFrame(function() {
    drawPending = false;
    drawOverlay();
  });
}

// A real telemetry frame arrived — render the map + HUD. The provider slices were already derived
// and posted up to the shell by the source; this is purely the local render.
function renderFrame(d) {
  lastData = d;
  ensureIconImage(d.name);
  if (d.colors) factionColors = { 0: d.colors.n, 1: d.colors.f, 2: d.colors.e };

  if (d.map && d.map.valid) {
    // rw/rh (issue #65): the mission's real reachable extent, which can run past w/h, the smaller
    // square the minimap IMAGE was captured at (see server-side MapReachW/H). Falls back to w/h
    // when the server sends no mapReach block (older frame, dev harness mock).
    const reach = d.mapReach || {};
    mapMeta = { w: d.map.w, h: d.map.h, ox: d.map.ox, oy: d.map.oy,
                rw: reach.w || d.map.w, rh: reach.h || d.map.h };
    // A valid frame means a real mission/map exists — clear NO SIGNAL now rather than waiting on
    // mapImg's own onload. That image is a slower, separate asset (captured async server-side,
    // see the retry loop below); gating the placeholder on it instead of on telemetry left NO
    // SIGNAL showing for a few extra seconds after a normal boot even once real data had arrived.
    setNoSignal(false);
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
      setGrid(gridOn);
    }
  }

  updateHUD(d);
  requestDraw();
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
  cursor.reset();   // no cursor across a mission boundary
  mapWasValid = false;
  view.zoom = 1; view.panX = 0; view.panY = 0;   // next mission starts at full extent
  followPlayer = false;                           // follow resets for the next mission
  oc.clearRect(0, 0, overlay.width, overlay.height);
  document.getElementById('map-panel').classList.remove('has-map');
  mapImg.src = '/map?t=' + Date.now();   // 404 now → falls back to the placeholder

  document.getElementById('grid-bar').className = 'mfd-chip empty';
  cursorBar.className = 'mfd-chip empty';
  jamBar.className = 'mfd-chip mfd-chip-jammed empty';
}

// ── HUD ──────────────────────────────────────────────────────────────────────────
// MAP's own on-map chrome: the GRID chip (bottom-right — the current grid square). Every other
// telemetry slice (status / loadout / cm / tgp / targets / rwr / mw / avn) is derived and
// broadcast to the shell by TelemetrySource._emit — the dedicated MFD pages render those. This one
// is the exception both ways: it is drawn here from the raw frame, AND emitted (as part of
// 'mapinfo') for shell chrome that shows no map — the F-35's master strip. The two derive the same
// value independently; neither reads the other.
function updateHUD(d) {
  const gridText = gridLabel(d.world.x, d.world.z, mapMeta);
  gridBar.textContent = 'GRID: ' + gridText;
  gridBar.className = 'mfd-chip';
  jamBar.className = 'mfd-chip mfd-chip-jammed' + (d.pjam ? '' : ' empty');
}

// CURSOR chip (below GRID) — the grid square under whichever pointer is currently active: the
// mouse (mapPanel's mousemove, below) or the PAD cursor (pad-cursor.js's onMove, wired at the
// bottom of this file). Both funnel through here so there's one place that shows/hides it, rather
// than each source tracking its own visibility. sx===null hides it (no active pointer this tick).
function updateCursorChip(sx, sy) {
  if (sx == null || !mapMeta) { cursorBar.className = 'mfd-chip empty'; return; }
  const w = overlayToWorld(sx, sy);
  if (!w) { cursorBar.className = 'mfd-chip empty'; return; }
  cursorBar.textContent = 'CURSOR: ' + gridLabel(w.x, w.z, mapMeta);
  cursorBar.className = 'mfd-chip';
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

// Toggle the coordinate grid overlay (the MFD's GRID key). Twin of setFollow above (issue #41).
function setGrid(on) {
  gridOn = on;
  savePersistedView();
  drawOverlay();
  source.emitGrid(on);   // mirror the grid state up to the shell, which lights the GRID label
}

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

// ── Waypoint placement (long-press, issue #38) ────────────────────────────────────
// Long-press (mouse hold, touch hold, or the PAD cursor's onHold above) drops a waypoint at that
// world position into the active route — same LONG_MS/longFired arbitration tgt.js's filter cells
// use, so a fired long-press never also reads as target-select (the click handler below guards on
// it). Armed on every single-pointer pointerdown, cancelled the moment gestureMoved flips true
// (>4px real movement) or a second pointer joins (a pinch is starting, not a hold).
const WPT_LONG_MS = 500;
let longPress = null;   // { pointerId, fired, timer }
function clearLongPress() { if (longPress) { clearTimeout(longPress.timer); longPress = null; } }
function armLongPress(pointerId, clientX, clientY) {
  clearLongPress();
  longPress = { pointerId: pointerId, fired: false, timer: null };
  longPress.timer = setTimeout(function() {
    if (!longPress || longPress.pointerId !== pointerId || gestureMoved || pointers.size !== 1) { longPress = null; return; }
    longPress.fired = true;
    const rect = overlay.getBoundingClientRect();
    placeNavigationPointAt(clientX - rect.left, clientY - rect.top);
  }, WPT_LONG_MS);
}
let wptFlash = null;   // brief confirmation ring at a just-placed waypoint (screen px, not unit id)
function pumpWptFlash() { if (!wptFlash) return; requestDraw(); requestAnimationFrame(pumpWptFlash); }
function flashWaypoint(cx, cy) { wptFlash = { cx: cx, cy: cy, until: performance.now() + 450 }; requestAnimationFrame(pumpWptFlash); }
function placeNavigationPointAt(sx, sy) {
  if (!mapMeta) return;
  const w = overlayToWorld(sx, sy);
  if (!w) return;
  flashWaypoint(sx, sy);   // immediate — purely cosmetic, no server round trip needed
  drawOverlay();
  WaypointsStore.addNavigationPoint(w.x, w.z).then(function () { refreshWaypointRoute(); drawOverlay(); });
}
window.addEventListener('contextmenu', function(e) { e.preventDefault(); });   // long-press must not pop a menu

function pinchGeom() {
  const p = [...pointers.values()];
  return { dist: Math.hypot(p[0].x - p[1].x, p[0].y - p[1].y),
           mx: (p[0].x + p[1].x) / 2, my: (p[0].y + p[1].y) / 2 };
}

// Zoom to z1 while holding the world point currently at screen (sx, sy) fixed — the shared math
// behind wheel-zoom, pinch-zoom, and zoomStep (bezel/keybind zoom, issue #64). While following, the
// player (not sx/sy) is what stays fixed — drawOverlay's own re-centring handles that every frame.
function zoomAbout(z1, sx, sy) {
  const z0 = view.zoom;
  if (z1 === z0) return;
  if (followPlayer) { view.zoom = z1; savePersistedView(); clampPan(); drawOverlay(); return; }
  const ox = overlay.width / 2, oy = overlay.height / 2;
  // pan1 = d - (z1/z0)(d - pan0), with d = anchor − centre — holds the anchor's world point in place.
  view.panX = (sx - ox) - (z1 / z0) * ((sx - ox) - view.panX);
  view.panY = (sy - oy) - (z1 / z0) * ((sy - oy) - view.panY);
  view.zoom = z1;
  savePersistedView();
  clampPan();
  drawOverlay();
}

// Scroll to zoom toward the mouse pointer.
overlay.addEventListener('wheel', function(e) {
  if (!mapMeta) return;
  e.preventDefault();
  const rect = overlay.getBoundingClientRect();
  const sx = e.clientX - rect.left, sy = e.clientY - rect.top;   // cursor in canvas px
  const z1 = Math.max(MIN_ZOOM, Math.min(MAX_ZOOM, view.zoom * Math.exp(-e.deltaY * 0.0015)));
  zoomAbout(z1, sx, sy);
}, { passive: false });

// Pan (single pointer) and pinch-zoom (two pointers). Follow mode stays LOCKED: neither a
// pan-drag nor a pinch disengages it, matching the in-game followed map — only the FLW button /
// 'f' key toggles it. Arming pan no longer requires zoom > MIN_ZOOM (issue #65): EDGE_MARGIN_FRAC
// gives clampPan slack even at zoom 1, and clampPan itself is what actually enforces the limit —
// this only decides whether to listen for the drag at all.
overlay.addEventListener('pointerdown', function(e) {
  pointers.set(e.pointerId, { x: e.clientX, y: e.clientY });
  downX = e.clientX; downY = e.clientY; gestureMoved = false;
  lastPointerType = e.pointerType || 'mouse';   // drives the tap-select reach (touch = fat finger)
  if (pointers.size === 2 && mapMeta) {                 // second finger → start a pinch
    clearLongPress();   // a pinch is starting, not a hold
    if (panId !== null) { try { overlay.releasePointerCapture(panId); } catch (_) {} panId = null; }
    const g = pinchGeom();
    pinching = true; pinchStartDist = g.dist; pinchStartZoom = view.zoom;
    return;
  }
  if (mapMeta) armLongPress(e.pointerId, e.clientX, e.clientY);
  if (mapMeta && !followPlayer) {
    panId = e.pointerId; lastX = e.clientX; lastY = e.clientY;
    overlay.setPointerCapture(e.pointerId);
  }
});
overlay.addEventListener('pointermove', function(e) {
  if (!pointers.has(e.pointerId)) return;
  pointers.set(e.pointerId, { x: e.clientX, y: e.clientY });
  if (Math.abs(e.clientX - downX) > 4 || Math.abs(e.clientY - downY) > 4) gestureMoved = true;
  if (gestureMoved) clearLongPress();   // a real pan/pinch cancels a pending hold

  if (pinching && pointers.size >= 2 && mapMeta) {       // pinch-zoom about the finger midpoint
    e.preventDefault();
    if (pinchStartDist <= 0) return;
    const g = pinchGeom();
    const z1 = Math.max(MIN_ZOOM, Math.min(MAX_ZOOM, pinchStartZoom * (g.dist / pinchStartDist)));
    const rect = overlay.getBoundingClientRect();
    zoomAbout(z1, g.mx - rect.left, g.my - rect.top);
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
  // Only clear an UNFIRED long-press here — a fired one must survive into the 'click' handler below
  // (touch fires a synthetic click after pointerup) so it can suppress the tap-select outcome.
  if (longPress && longPress.pointerId === e.pointerId && !longPress.fired) clearLongPress();
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
  if (lastPointerType === 'touch') { unitLabel.style.display = 'none'; updateCursorChip(null); return; }
  if (panId !== null) { unitLabel.style.display = 'none'; updateCursorChip(null); return; }   // don't flicker while panning
  const rect = overlay.getBoundingClientRect();
  const mx = e.clientX - rect.left, my = e.clientY - rect.top;
  updateCursorChip(mx, my);
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
mapPanel.addEventListener('mouseleave', function() { unitLabel.style.display = 'none'; updateCursorChip(null); });

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
// Picks the NEAREST unselected contact within reach of (px,py) and selects it — the shared body
// behind a mouse click, a touch tap, and (docs/map-cursor.md) a Cursor Select press. "Within reach"
// scales with pad so a fat touch tap or a coarse HOTAS cursor need not be pixel-precise.
function selectAt(px, py, pad) {
  let hit = null, bestD2 = Infinity;
  for (let i = hitTargets.length - 1; i >= 0; i--) {
    const t = hitTargets[i];
    if (t.id == null || isSelected(t)) continue;
    const dx = px - t.cx, dy = py - t.cy, d2 = dx * dx + dy * dy;
    const reach = t.r + pad;
    if (d2 <= reach * reach && d2 < bestD2) { bestD2 = d2; hit = t; }
  }
  if (!hit) return;   // nothing in reach, or everything in reach already selected → no-op (never deselects)
  pendingSel.set(hit.id, performance.now() + 1500);
  sendCommand('target.select', { id: hit.id })
    .then(function(r) { if (r.ok) flashSelect(hit.id); else pendingSel.delete(hit.id); })
    .catch(function() { pendingSel.delete(hit.id); });
}

overlay.addEventListener('click', function(e) {
  if (longPress && longPress.fired) { longPress = null; return; }   // that was a waypoint placement, not a select
  if (gestureMoved) return;   // that was a pan/pinch, not a select
  const rect = overlay.getBoundingClientRect();
  const mx = e.clientX - rect.left, my = e.clientY - rect.top;
  // Touch taps reach past the icon (fat finger); a mouse stays precise.
  const pad = lastPointerType === 'touch' ? TOUCH_HIT_PAD : 0;
  selectAt(mx, my, pad);
});

// ── Remote control ────────────────────────────────────────────────────────────────
// Lets an embedder (the MFD frame) drive the map without reaching into it directly, so
// the map stays a self-contained component. Works same-origin and cross-origin (file://).
// Bezel Z+/Z- and zoom keybinds (issue #64): zoom about the PAD cursor when it's on screen, the
// same way the mouse wheel zooms about the pointer — falling back to the canvas centre (i.e. the
// CURRENT view's own centre point, not the map's absolute centre, since the anchor formula in
// zoomAbout scales the existing pan proportionally rather than resetting it) when there's no
// cursor to anchor on.
function zoomStep(factor) {
  if (!mapMeta) return;
  const z1 = Math.max(MIN_ZOOM, Math.min(MAX_ZOOM, view.zoom * factor));
  const p = cursor.getPos();
  const ox = overlay.width / 2, oy = overlay.height / 2;
  zoomAbout(z1, p ? p.x : ox, p ? p.y : oy);
}
window.addEventListener('message', function(e) {
  const m = e.data;
  if (!m || m.mfd !== true) return;
  switch (m.action) {
    case 'toggle-follow': if (mapMeta) setFollow(!followPlayer); break;
    case 'toggle-grid':   setGrid(!gridOn); break;
    case 'zoom-in':       zoomStep(1.5);   break;
    case 'zoom-out':      zoomStep(1 / 1.5); break;
    case 'status-request': source.rebroadcastStatus(); break;   // shell asked for the current status
    // R+/R- (issue #38) — switch the active waypoint route; re-pull it and repaint so the map's
    // rendered line/markers switch immediately, same as a local long-press placement does.
    case 'route-next': WaypointsStore.cycleActiveRoute(1).then(function () { refreshWaypointRoute(); drawOverlay(); });  break;
    case 'route-prev': WaypointsStore.cycleActiveRoute(-1).then(function () { refreshWaypointRoute(); drawOverlay(); }); break;
    // W+/W- step route progress; with no active route the same actions arrive under S+/S- labels
    // and cycle the selected steer point. The plugin owns that context switch.
    case 'waypoint-next': WaypointsStore.stepNavigation(1).then(function () { refreshWaypointRoute(); drawOverlay(); });  break;
    case 'waypoint-prev': WaypointsStore.stepNavigation(-1).then(function () { refreshWaypointRoute(); drawOverlay(); }); break;
    // PAD cursor (docs/page-cursor.md, docs/map-cursor.md) — the shell only ever sends these while
    // THIS map is the SOI's focused surface, so no further gating is needed here.
    case 'cursor-focus':  cursor.setFocus(!!m.on, overlay.width / 2, overlay.height / 2); break;
    case 'cursor':        cursor.setVector(m.x, m.y); break;
    // MAP registers onHold (waypoint placement), so it needs the LIVE held state, not the plain
    // edge-driven 'cursor-select' every other page uses — that fires onSelect() straight away and
    // would make Cursor Select's hold arbitration (pad-cursor.js's setSelectHeld) unreachable, same
    // as a mouse click short-circuiting tgt.js's own long-press timer.
    case 'cursor-held':   cursor.setSelectHeld(!!m.held); break;
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
loadPersistedView();       // adopt the persisted FLW + ZOOM + GRID (or the defaults) before the first paint
refreshWaypointRoute();    // load the active route (issue #38) before the first paint
syncSizeWhenReady();
setFollow(followPlayer);    // report the restored follow up to the shell (paints the FOLLOW chip)
setGrid(gridOn);            // report the restored grid state up to the shell (paints the GRID label)
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
