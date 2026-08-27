// FCR page — F-16 fire-control-radar B-scope. A pure reactive renderer driven by the shell over postMessage;
// single source of truth for BOTH layouts. See rdr.html for the message contract, docs/rdr-page.md
// for the design.

// Scope geometry in the 520x600 viewBox: ownship at bottom-centre, bearing across, range up.
var L = 60, R = 460, TOP = 70, BOT = 510;      // scope rectangle
var MIDX = 260, HALFW = 200, HGT = BOT - TOP;  // (R-L)/2, height
var DEF_CONE = 60;                             // fallback azimuth half-angle when the radar reports none
// Unit conversions matching the game's own UnitConverter (PlayerSettings.unitSystem): Imperial is
// nm for range, feet for altitude; Metric is km/m. M_PER_NM/M_PER_KM convert world metres to the
// scope's range unit; M_TO_FT is the plain metres->feet factor UnitConverter.AltitudeReading uses.
var M_PER_NM = 1852, M_PER_KM = 1000, M_TO_FT = 3.28084;

// Mirror theme.css's --no-green/--no-red/--no-amber/--no-purple/--no-blue — SVG string-building
// here can't use CSS var(), so these are plain literals kept in sync by hand.
// GREEN is the PAD cursor gate's own color (drawCursor/bar below) — unrelated to contact source
// color, which used to also be GREEN for "own radar detected it" until enemy-air contacts moved
// to RED (dropping green there in case friendlies are ever added, at which point green would be
// free to mean "friendly" instead of being reused for two different things).
var GREEN = '#39ff14', RED = '#ff4040', AMBER = '#ffaa00', PURPLE = 'rgb(179, 136, 255)';
var BLUE = '#4d9fff';   // pitbull missile triangle fill (issue #40) — the "this is MY missile" cue,
                         // distinct from RWR's inbound-threat red/yellow
var state = { present: false, range: 0, cone: 0, metric: false, radarOn: false, levelTime: 0, items: [], pb: [] };

// The caret's one-way sweep time (rdr.css's animation-duration must match). 2s one-way / 4s round
// trip mirrors the game's own MFD radar sweep exactly (TacScreen.ScanRadar: needle angle =
// sin(t * 0.5*PI) * 26deg, a 4s period — see docs/rdr-page.md).
var SWEEP_ONE_WAY = 2, SWEEP_PERIOD = SWEEP_ONE_WAY * 2;

// Phase-locks the caret to that same sweep: our own local timeline (TAU, seconds since our
// animation notionally started fresh) reaches (position, direction) = (centre, "+") at TAU=1, 2,
// 3... one second AHEAD of the real needle's matching moments — so setting animation-delay to
// -((levelTime + 1) mod period) makes our caret cross centre/hit its extremes at the exact instants
// the real needle does, even though ours is linear and the real one sinusoidal. Applied once per
// "radar just turned on" transition (see render()), not every tick — repeatedly resetting
// animation-delay would restart/stutter the animation instead of letting it run smoothly.
function syncSweepPhase(levelTime) {
  var sweep = document.getElementById('rdr-sweep');
  if (!sweep) return;
  var phase = (((levelTime + 1) % SWEEP_PERIOD) + SWEEP_PERIOD) % SWEEP_PERIOD;
  sweep.style.animationDelay = (-phase) + 's';
}
var _wasRadarOn = false;

// Range in the display unit (nm or km, per state.metric); rounded, matching the corner scale.
function rangeUnits(meters) { return Math.round(meters / (state.metric ? M_PER_KM : M_PER_NM)); }
// Altitude in the display unit (m or ft, per state.metric), matching UnitConverter.AltitudeReading.
function altUnits(meters) { return Math.round(state.metric ? meters : meters * M_TO_FT); }

function coneHalf() { return state.cone > 0 ? state.cone : DEF_CONE; }

// Selectable display range (DCS-style range rings), as a fraction of the radar's true max range
// (state.range) rather than fixed absolute km/nm steps — the radar's real max varies per airframe,
// so a fixed "80nm" ring would be meaningless on a short-range set and a fixed "10nm" one would
// barely use the scope on a long-range one. A fraction always fills the scope at every step. R+
// ("zoom in", index down) narrows to a closer/more-detailed view; R- ("zoom out", index up) widens
// back toward the radar's true max. Persisted in sessionStorage, same pattern as MAP's zoom/follow
// (map.js's VIEW_STORE_KEY) — survives a page switch/reload so a chosen range sticks.
var RANGE_STEPS = [0.25, 0.5, 1];
var DEFAULT_RANGE_IDX = RANGE_STEPS.length - 1;   // 100% — matches the page's pre-existing behaviour
var RANGE_STORE_KEY = 'noxmfd.rdr.view';
var rangeIdx = DEFAULT_RANGE_IDX;
function loadPersistedRange() {
  var saved = null;
  try { saved = JSON.parse(sessionStorage.getItem(RANGE_STORE_KEY) || 'null'); } catch (_) {}
  var i = saved && typeof saved.rangeIdx === 'number' ? saved.rangeIdx : DEFAULT_RANGE_IDX;
  rangeIdx = Math.max(0, Math.min(RANGE_STEPS.length - 1, i));
}
function savePersistedRange() {
  try { sessionStorage.setItem(RANGE_STORE_KEY, JSON.stringify({ rangeIdx: rangeIdx })); } catch (_) {}
}
// The radar's true max range (state.range) scaled by the selected step — what the scope actually
// displays right now. Everything that projects or labels range reads THIS, not state.range
// directly, so a shorter selection genuinely zooms in rather than just relabeling the same scale.
function displayRange() { return state.range * RANGE_STEPS[rangeIdx]; }
function setRangeIdx(i) {
  var clamped = Math.max(0, Math.min(RANGE_STEPS.length - 1, i));
  if (clamped === rangeIdx) return;
  rangeIdx = clamped;
  savePersistedRange();
  render();
}

// Pure B-scope projection: bearing off nose (az, deg) × range (world units) → scope x,y, or null
// when the contact falls outside the cone half-angle or past max range (culled). Kept free of
// module state so it's unit-checkable (rdr.test.js). ch = cone half-angle (deg), range = max range.
function bscopeXY(az, rng, range, ch) {
  var fx = az / ch;                              // -1..1 across the cone
  var fy = range > 0 ? rng / range : 0;          // 0 at ownship, 1 at max range
  if (Math.abs(fx) > 1 || fy > 1) return null;
  return { x: MIDX + fx * HALFW, y: BOT - fy * HGT };
}

// A contact's scope position against the current scope scale, or null when culled. Uses
// displayRange(), not state.range — a contact beyond the selected range (even if still within the
// radar's true max) is correctly culled, the same way one beyond the true max always was.
function plot(c) { return bscopeXY(c.az, c.rng, displayRange(), coneHalf()); }

// ── Acquisition cursor (docs/rdr-page.md, step 4) ────────────────────────────────────────
// The PAD cursor slews the two-bar #rdr-cursor over the scope; Select toggles a lock on the
// nearest contact. pad-cursor.js works in the panel's px space, so we map px ↔ viewBox using the
// SVG's xMidYMid-meet transform (derived from the panel size), and hit-test in viewBox units.
var HIT_PAD = 42;             // grab radius in viewBox units (bricks are 16 wide)
var plotted = [];             // {id,x,y} in viewBox coords for the contacts currently on the scope
var hoveredId = null;         // contact nearest the cursor, highlighted

function send(cmd, args) { if (typeof sendCommand === 'function') sendCommand(cmd, args).catch(function () {}); }
function itemById(id) { return (state.items || []).find(function (c) { return c.id === id; }); }

// The SVG's rendered transform in panel px: uniform scale s (xMidYMid meet) + letterbox offsets.
function viewport() {
  var p = document.querySelector('.rdr-panel');
  var s = Math.min(p.clientWidth / 520, p.clientHeight / 600);
  return { s: s, ox: (p.clientWidth - 520 * s) / 2, oy: (p.clientHeight - 600 * s) / 2 };
}
// The scope rectangle in panel px — the cursor's clamp region.
function scopeRectPx() {
  var v = viewport();
  return { dx: v.ox + L * v.s, dy: v.oy + TOP * v.s, dw: (R - L) * v.s, dh: HGT * v.s };
}
// Nearest plotted contact to a panel-px point, within HIT_PAD (viewBox units); null if none close.
function nearestContact(px, py) {
  var v = viewport(), vx = (px - v.ox) / v.s, vy = (py - v.oy) / v.s;
  var best = null, bestD = HIT_PAD;
  plotted.forEach(function (pt) {
    var d = Math.hypot(pt.x - vx, pt.y - vy);
    if (d <= bestD) { bestD = d; best = pt; }
  });
  return best;
}
// Select over a contact toggles its lock — reusing TGT's target set (target.select/deselect by id).
function padSelect(px, py) {
  var pt = nearestContact(px, py);
  if (!pt) return;
  var c = itemById(pt.id);
  send(c && c.tg ? 'target.deselect' : 'target.select', { id: pt.id });
}
// Draw the F-16 two-bar acquisition gate at the cursor, in viewBox units so it scales with the
// contact bricks (a 16-unit brick sits inside the ~28-unit gap). CUR_GAP = each bar's offset from
// the aimpoint (gap encloses a brick with margin); CUR_H = half the bar height.
var CUR_GAP = 14, CUR_H = 9;
function drawCursor(px, py) {
  var g = document.getElementById('rdr-cursor-g');
  if (!g) return;
  if (px == null) { g.innerHTML = ''; return; }
  var v = viewport(), vx = (px - v.ox) / v.s, vy = (py - v.oy) / v.s;
  g.innerHTML =
    bar(vx - CUR_GAP, vy) + bar(vx + CUR_GAP, vy);
}
function bar(x, y) {
  return '<line x1="' + x.toFixed(1) + '" y1="' + (y - CUR_H).toFixed(1) +
         '" x2="' + x.toFixed(1) + '" y2="' + (y + CUR_H).toFixed(1) +
         '" stroke="' + GREEN + '" stroke-width="3"/>';
}

// Move the gate and highlight the contact under the cursor (or clear both when it leaves/hides).
function padMove(px, py) {
  drawCursor(px, py);
  var pt = px == null ? null : nearestContact(px, py);
  var id = pt ? pt.id : null;
  if (id === hoveredId) return;
  hoveredId = id;
  renderContacts();
}

function renderGrid() {
  var g = document.getElementById('rdr-grid');
  if (!g) return;
  // Scan-limit lines converging on ownship at the cone half-angle, + a mid-range reference line.
  var ch = coneHalf();
  var topHalf = Math.tan(ch * Math.PI / 180) * HGT;   // horizontal spread of the cone at scope top
  var lx = Math.max(L, MIDX - topHalf), rx = Math.min(R, MIDX + topHalf);
  var out = '';
  out += line(MIDX, BOT, lx, TOP, 'rgba(255,255,255,0.22)', 1.5);
  out += line(MIDX, BOT, rx, TOP, 'rgba(255,255,255,0.22)', 1.5);
  out += line(MIDX, BOT, MIDX, TOP, 'rgba(255,255,255,0.14)', 1);
  out += line(L, TOP + HGT / 2, R, TOP + HGT / 2, 'rgba(255,255,255,0.28)', 1.5, '6 12');
  g.innerHTML = out;
}

function line(x1, y1, x2, y2, col, w, dash) {
  return '<line x1="' + x1.toFixed(1) + '" y1="' + y1.toFixed(1) + '" x2="' + x2.toFixed(1) +
         '" y2="' + y2.toFixed(1) + '" stroke="' + col + '" stroke-width="' + w + '"' +
         (dash ? ' stroke-dasharray="' + dash + '"' : '') + '/>';
}

function short(n) {
  if (!n) return '';
  return String(n).toUpperCase();
}

function renderContacts() {
  var g = document.getElementById('rdr-contacts');
  if (!g) return;
  var out = '', first = null;
  plotted = [];
  (state.items || []).forEach(function (c) {
    var p = plot(c);
    if (!p) return;
    plotted.push({ id: c.id, x: p.x, y: p.y });
    var locked = !!c.tg;
    // The target set can hold more than one lock (a planned cycling-locked-targets follow-up,
    // docs/rdr-fcr-hsd.md); until a real "which lock is focused" field exists, the first locked
    // contact encountered is the one the bottom readout describes, same as before — just named
    // explicitly now so the color logic below can tell it apart from any other simultaneous lock.
    var focused = locked && !first;
    if (focused) first = c;
    // Source colour: the FOCUSED lock (readout's own target) is amber, same as its ring below. Any
    // other simultaneous lock reads the same red an unlocked own-radar contact would — still
    // locked (its ring stays amber either way), just not the one currently being read out.
    // Own-radar red (not green) frees green to mean "friendly" if that's ever added; datalink-only
    // (not currently painted by the player's own radar) = purple (matching TGT's DATALINK button).
    var col = focused ? AMBER : ((locked || c.radar) ? RED : PURPLE);
    // Hover highlight: a soft ring under whatever the cursor is nearest (docs/rdr-page.md).
    if (c.id === hoveredId)
      out += '<circle cx="' + p.x.toFixed(1) + '" cy="' + p.y.toFixed(1) +
             '" r="22" fill="none" stroke="rgba(255,255,255,0.55)" stroke-width="2"/>';
    // Brick.
    out += '<rect x="' + (p.x - 8).toFixed(1) + '" y="' + (p.y - 8).toFixed(1) +
           '" width="16" height="16" fill="' + col + '"/>';
    // Velocity-vector stub: travel heading relative to nose, 0 = up (away). Length ~75% of a brick+.
    var a = (c.rhdg || 0) * Math.PI / 180, LEN = 19;
    out += '<line x1="' + p.x.toFixed(1) + '" y1="' + p.y.toFixed(1) + '" x2="' +
           (p.x + Math.sin(a) * LEN).toFixed(1) + '" y2="' + (p.y - Math.cos(a) * LEN).toFixed(1) +
           '" stroke="' + col + '" stroke-width="3"/>';
    // Lock circle.
    if (locked)
      out += '<circle cx="' + p.x.toFixed(1) + '" cy="' + p.y.toFixed(1) +
             '" r="17" fill="none" stroke="' + AMBER + '" stroke-width="2"/>';
  });
  g.innerHTML = out;
  renderReadout(first);
}

// Pitbull missiles (issue #40): the player's own AA missiles with a locked active-radar seeker.
// Matches RWR's own missile-threat design (rwr.js renderThreats): a slender pointed dart (not a
// squat isoceles triangle) and a SOLID line, flickering yellow<->red on the same timer RWR uses
// (see pbFlip below) rather than a static dashed line. The dart stays blue-filled (own weapon, not
// a threat) while the line adopts RWR's flicker so "missile in flight, still tracking" reads the
// same way it does on RWR. Points at its target's PLOTTED position (not its own travel heading) so
// the dart visibly aims at what it's pursuing; falls back to travel heading (rhdg, 0 = up/away from
// ownship) when the target isn't resolvable on-scope, so the dart still has a sensible orientation.
// Line only draws when the target is ALSO plotted on the scope right now (renderContacts must run
// first — it fills `plotted`); tid=0 or an off-scope target just skip the line, the dart still shows.
function renderPitbull() {
  var g = document.getElementById('rdr-pitbull');
  if (!g) return;
  var out = '';
  (state.pb || []).forEach(function (m) {
    var p = plot(m);
    if (!p) return;
    var target = m.tid ? plotted.find(function (pt) { return pt.id === m.tid; }) : null;
    var rot = target
      ? (Math.atan2(target.x - p.x, -(target.y - p.y)) * 180 / Math.PI).toFixed(1)
      : (m.rhdg || 0).toFixed(1);
    // Slender dart (RWR's HL/HB/HW proportions, scaled to RDR's ~16px contact size): a long tip and
    // a narrow base.
    out += '<polygon points="' + p.x.toFixed(1) + ',' + (p.y - 13).toFixed(1) + ' ' +
           (p.x - 4).toFixed(1) + ',' + (p.y + 3).toFixed(1) + ' ' +
           (p.x + 4).toFixed(1) + ',' + (p.y + 3).toFixed(1) +
           '" fill="' + BLUE + '" transform="rotate(' + rot + ' ' + p.x.toFixed(1) + ' ' + p.y.toFixed(1) + ')"/>';
    if (target)
      out += '<line x1="' + p.x.toFixed(1) + '" y1="' + p.y.toFixed(1) + '" x2="' + target.x.toFixed(1) +
             '" y2="' + target.y.toFixed(1) + '" stroke="currentColor" stroke-width="1.5" stroke-linecap="round"/>';
  });
  g.innerHTML = out;
}

// Bottom readout: always the FIRST locked contact (or blank), plus the total locked count.
function renderReadout(first) {
  var r1 = document.getElementById('rdr-r1'), r2 = document.getElementById('rdr-r2'),
      lk = document.getElementById('rdr-lk');
  var locked = (state.items || []).filter(function (c) { return c.tg; }).length;
  if (first) {
    r1.classList.add('big');
    r1.textContent = short(first.n);
    r2.textContent = 'RNG ' + rangeUnits(first.rng) +
                     '   ALT ' + altUnits(first.alt) +
                     '   HDG ' + pad3(Math.round(((first.rhdg + heading()) % 360 + 360) % 360));
  } else {
    r1.classList.remove('big');
    r1.textContent = '';
    r2.textContent = '';
  }
  lk.textContent = locked ? 'LOCK ' + locked : '';
}

// The readout's HDG is a world compass heading; rhdg is relative to nose, so add the ownship
// heading back. Ownship heading isn't in the 'rdr' message, so we carry the last value the scale
// implies via _hdg (updated when we know it); default 0 keeps it defined.
var _hdg = 0;
function heading() { return _hdg; }
function pad3(n) { return ('00' + n).slice(-3); }

function renderScale() {
  var range = document.getElementById('rdr-range');
  var azl = document.getElementById('rdr-azl'), azr = document.getElementById('rdr-azr');
  // Corner scale carries its unit (NM/KM) so the bare number is self-explanatory. Shows the
  // SELECTED display range, not the radar's true max — matching a real range-selectable radar's
  // own corner readout (the number that changes as the pilot steps the range in/out).
  range.textContent = state.range > 0 ? rangeUnits(displayRange()) + (state.metric ? 'km' : 'nm') : '';
  var ch = Math.round(coneHalf());
  azl.textContent = '-' + ch;
  azr.textContent = '+' + ch;
}

function render() {
  document.body.classList.toggle('unavailable', !state.present);
  if (!state.present) return;
  var sweep = document.getElementById('rdr-sweep');
  if (sweep) sweep.classList.toggle('on', !!state.radarOn);
  if (state.radarOn && !_wasRadarOn) syncSweepPhase(state.levelTime);
  _wasRadarOn = state.radarOn;
  renderScale();
  renderGrid();
  renderContacts();
  renderPitbull();
}

// Browser-only bootstrap (skipped under Node so rdr.test.js can require the pure helpers).
if (typeof window !== 'undefined' && window.addEventListener) {
  loadPersistedRange();

  // Flickers the pitbull target line yellow<->red on RWR's own timer (renderThreats' mwFlip, same
  // 130ms period). Only the line uses currentColor, so this doesn't affect the dart's fixed blue
  // fill; a no-op tick while no line is drawn (target unresolved/off-scope) costs one querySelector.
  var pbFlip = false;
  setInterval(function () {
    var g = document.getElementById('rdr-pitbull');
    if (!g || !g.querySelector('line')) return;
    pbFlip = !pbFlip;
    g.style.color = pbFlip ? '#ffd21e' : '#ff3b30';
  }, 130);

  // The PAD acquisition cursor (two vertical bars) reuses the shared pad-cursor integrator. Loaded
  // via dynamic import so this file stays a classic script the Node self-check can require; any
  // cursor message that arrives before it resolves is parked and applied on creation.
  var cursor = null, pendingFocus = null, pendingVec = null;
  function centerFocus(on) {
    var r = scopeRectPx();
    cursor.setFocus(on, r.dx + r.dw / 2, r.dy + r.dh / 2);
  }
  import('/assets/services/pad-cursor.js').then(function (mod) {
    cursor = mod.createPadCursor({
      el: document.getElementById('rdr-cursor'),
      clampRect: scopeRectPx,
      onSelect: padSelect,
      onMove: padMove,
      onEdge: onCursorEdge
    });
    if (pendingFocus) { centerFocus(pendingFocus.on); pendingFocus = null; }
    if (pendingVec) { cursor.setVector(pendingVec.x, pendingVec.y); pendingVec = null; }
  });

  // Cursor overflow at the scope's top/bottom edge also steps range (RNG+/-): pushing past the top
  // (further than max displayed range) widens back out; pushing past the bottom (toward/through
  // ownship) narrows in. onEdge fires every animation frame while overshot (fine for MAP's
  // continuous pan, map.js:onCursorEdge) — a discrete range step needs a cooldown instead, or one
  // push would blow through every step in a single frame.
  var EDGE_STEP_COOLDOWN_MS = 400;
  var lastEdgeStepAt = 0;
  function onCursorEdge(ex, ey) {
    if (!ey) return;
    var now = performance.now();
    if (now - lastEdgeStepAt < EDGE_STEP_COOLDOWN_MS) return;
    lastEdgeStepAt = now;
    setRangeIdx(rangeIdx + (ey < 0 ? 1 : -1));
  }

  // A mouse/touch tap selects the same way the PAD cursor's Select does — same hit-test, same
  // toggle-lock (target.select/deselect). The panel's own CSS cursor already matches the PAD gate's
  // two-bar look, so both input paths read as the same "aim between the bars" gesture.
  document.querySelector('.rdr-panel').addEventListener('click', function (e) {
    var r = this.getBoundingClientRect();
    padSelect(e.clientX - r.left, e.clientY - r.top);
  });

  window.addEventListener('message', function (e) {
    var m = e.data;
    if (!m || !m.mfd) return;
    if (m.type === 'rdr') {
      state = {
        present: !!m.present,
        range: m.range || 0,
        cone: m.cone || 0,
        metric: !!m.metric,
        radarOn: !!m.radarOn,
        levelTime: m.levelTime || 0,
        items: Array.isArray(m.items) ? m.items : [],
        pb: Array.isArray(m.pb) ? m.pb : []
      };
      if (typeof m.hdg === 'number') _hdg = m.hdg;
      render();
    } else if (m.action === 'cursor-focus') {
      if (cursor) centerFocus(!!m.on); else pendingFocus = { on: !!m.on };
    } else if (m.action === 'cursor') {
      if (cursor) cursor.setVector(m.x, m.y); else pendingVec = { x: m.x, y: m.y };
    } else if (m.action === 'cursor-held') {
      if (cursor) cursor.setSelectHeld(!!m.held);
    } else if (m.action === 'zoom-in') {
      // The same Zoom In/Out physical keybind MAP uses (map-act, docs/page-cursor.md) — routed here
      // instead of to MAP whenever RDR is the SOI-focused surface, exactly the way TGT repurposes it
      // to scroll its list (tgt.js). Matches R+: steps the displayed range UP (a bigger range
      // number), not a camera-style "zoom in narrows the view" — R+/R- and Zoom In/Out agree.
      setRangeIdx(rangeIdx + 1);
    } else if (m.action === 'zoom-out') {
      setRangeIdx(rangeIdx - 1);
    }
  });
  render();
}

if (typeof module !== 'undefined' && module.exports)
  module.exports = { bscopeXY: bscopeXY, geom: { MIDX: MIDX, HALFW: HALFW, TOP: TOP, BOT: BOT, HGT: HGT } };
