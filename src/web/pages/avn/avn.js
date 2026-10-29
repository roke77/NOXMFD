// ENG page (id avn) — the FUEL/RPM/HEAT/THRL gauges. A pure reactive renderer driven by the shell
// over postMessage; single source of truth for BOTH layouts (full-screen iframe + split pane). The
// system toggles live on the SYS page (id afm). See avn.html for the message contract.

// ── DOM refs ───────────────────────────────────────────────────────────────────────
const avnPanel     = document.getElementById('avn-panel');
const avnEmptyEl   = document.getElementById('avn-empty');
const avnContentEl  = document.getElementById('avn-content');
const avnGaugeFuel = document.getElementById('avn-gauge-fuel');
const avnGaugeRpm  = document.getElementById('avn-gauge-rpm');
const avnGaugeHeat = document.getElementById('avn-gauge-heat');
const avnGaugeThr  = document.getElementById('avn-gauge-thr');

// ── State ──────────────────────────────────────────────────────────────────────────
let avnData = { name: null, fuel: -1, throttle: -1, heat: -1, heatColor: null, rpm: -1, hasAb: false, abStart: 1 };
let layout         = 'compact';   // 'compact' (split pane) | 'full' (full-screen iframe)
// {frameTop, frameHeight} forwarded by the shell in full — the vertical band .avn-content centres
// itself in (below the top bezel row). The gauge grid is a plain CSS grid, sized/positioned
// entirely by CSS once .avn-content itself is placed.
let avnFullGeom    = null;
let avnLastType    = null;   // last aircraft type rendered, so a respawn on the SAME type still re-renders

// ── Renderer ───────────────────────────────────────────────────────────────────────
function renderAvn() {
  const type = avnData.name;
  avnLastType = type;
  if (!type) {
    avnContentEl.classList.remove('placed');
    avnEmptyEl.style.display = '';
    return;
  }
  avnEmptyEl.style.display = 'none';

  paintAvnGauges();
  layoutAvnContent();
}

// The vertical band .avn-content centres itself in: full uses the shell-forwarded bezel geometry
// (the band below the top bezel row); compact has no such geometry, so it's just a small top
// margin down to a small bottom margin.
const AVN_TOP_MARGIN = 12;
const AVN_BOTTOM_MARGIN = 12;
function avnContentVerticalExtent() {
  if (layout === 'full' && avnFullGeom && typeof avnFullGeom.frameTop === 'number') {
    return { top: avnFullGeom.frameTop, height: avnFullGeom.frameHeight };
  }
  const panelRect = avnPanel.getBoundingClientRect();
  return { top: AVN_TOP_MARGIN, height: Math.max(0, panelRect.height - AVN_TOP_MARGIN - AVN_BOTTOM_MARGIN) };
}

// .avn-content spans the full panel width and the full vertical extent, so it reads as "as wide as
// possible, vertically centred" per that extent. The gauge grid fills it via flex-grow (avn.css) —
// no further JS sizing needed.
function layoutAvnContent() {
  const panelRect = avnPanel.getBoundingClientRect();
  if (!panelRect.width || !panelRect.height) {
    avnContentEl.classList.remove('placed');
    return;
  }
  const vert = avnContentVerticalExtent();
  avnContentEl.style.width  = panelRect.width + 'px';
  avnContentEl.style.height = vert.height + 'px';
  avnContentEl.style.left   = '0px';
  avnContentEl.style.top    = vert.top + 'px';
  avnContentEl.classList.add('placed');
}

function paintAvnGauges() {
  paintAvnGauge(avnGaugeFuel, avnData.fuel, 0.25, 0.10);
  paintAvnGauge(avnGaugeRpm,  avnData.rpm,  null, null);   // no caution/critical — low RPM at idle is normal, not a warning
  paintAvnGauge(avnGaugeHeat, avnData.heat, null, null);   // no caution/critical class — color comes from paintAvnHeatColor instead
  paintAvnHeatColor();
  paintAvnThrottle();
}

// HEAT's fill color mirrors the game's own cockpit IR gauge exactly: TelemetryReader computes it
// server-side off the same GameAssets.i.redGreenGradient asset the game reads, so this just applies
// that hex color straight to the fill stroke instead of guessing our own green/amber/red stops.
function paintAvnHeatColor() {
  avnGaugeHeat.querySelector('.avn-gauge-fill').style.stroke = avnData.heatColor || '';
}

// Needle rotation: the SVG needle is drawn pointing at the gauge's zero position (-135deg, see
// avn.html), so rotating it (v * 270)deg clockwise lands it at -135 + v*270 — the same 270deg
// sweep every dial's tick ring covers (avn.html's shared <defs>).
function avnNeedleAngle(v) { return (v * 270).toFixed(1) + 'deg'; }

// Absolute arc-position math for THRL's AFTERBURNER placard (the needle above only needs a
// relative CSS rotate(); this is for an absolute <path> instead). Clock-angle convention (0deg =
// 12 o'clock, increasing clockwise) matches the dial's hardcoded track path (avn.html: "M 23.13
// 23.13 A 38 38 0 1 1 23.13 76.87"), whose two endpoints solve to exactly 315deg (v=0) and
// 585deg==225deg (v=1) — the coordinate system every dial in this file agrees on.
function avnClockPoint(r, thetaDeg) {
  const t = thetaDeg * Math.PI / 180;
  return { x: 50 + r * Math.sin(t), y: 50 - r * Math.cos(t) };
}
function avnClockAngle(v) { return 315 + v * 270; }
// sweep follows the v0->v1 direction (not hardcoded to 1, like the needle-swept fills above)
// because text laid on a path reads upright only when the path runs with "outward from the dial
// centre" on the glyphs' up side — on the bottom half of the dial (where the reheat zone always
// lands) that means running from the HIGH-v end toward the LOW-v end. Getting the sweep flag
// wrong here draws the major (306deg) arc instead of the minor one, not just mis-oriented text.
function avnArcPath(r, v0, v1) {
  const p0 = avnClockPoint(r, avnClockAngle(v0));
  const p1 = avnClockPoint(r, avnClockAngle(v1));
  const span = (v1 - v0) * 270;
  const large = Math.abs(span) > 180 ? 1 : 0;
  const sweep = span >= 0 ? 1 : 0;
  return 'M ' + p0.x.toFixed(3) + ' ' + p0.y.toFixed(3) +
    ' A ' + r + ' ' + r + ' 0 ' + large + ' ' + sweep + ' ' + p1.x.toFixed(3) + ' ' + p1.y.toFixed(3);
}
// Fill inner edge is r33.5 (fill r35, stroke-width 3 -> 35 - 3/2); pulled in another 1.5 so the
// placard clears it with a small gap instead of touching, while still reading as a concentric
// band rather than a separate ring.
const AVN_AB_LABEL_R = 32;

// The amber fill arc from zero to v, over .avn-gauge-track's identical curve — the classic SVG
// "circular progress" trick: dasharray = the path's own length L turns it into one dash of length
// L then one gap of length L; dashoffset = L*(1-v) slides that dash so only the first v*L of the
// path (from the zero end, where the path's `d` in avn.html starts) stays visible.
// getTotalLength() is cached per element (a WeakMap, not a data attribute, since the value is a
// number, not markup) — every dial's path is geometrically identical, but reading it straight off
// each element is one line simpler than threading a shared constant through four call sites.
const avnGaugeFillLengths = new WeakMap();
function avnGaugeFillLength(fillEl) {
  let L = avnGaugeFillLengths.get(fillEl);
  if (L === undefined) { L = fillEl.getTotalLength(); avnGaugeFillLengths.set(fillEl, L); }
  return L;
}
function setAvnGaugeFill(fillEl, v) {
  const L = avnGaugeFillLength(fillEl);
  // No unit suffix: both resolve in the path's own user-space coordinate system (the viewBox's
  // 0-100 grid), matching getTotalLength()'s own units. 'px' here would mean CSS pixels of the
  // rendered (cqmin-scaled) box instead, which drifts from L as soon as a dial isn't rendered at
  // exactly 100x100 device px — every size except the reference.
  fillEl.style.strokeDasharray = L;
  fillEl.style.strokeDashoffset = L * (1 - v);
}
function paintAvnGauge(gaugeEl, value01, cautionAt, criticalAt) {
  const needle = gaugeEl.querySelector('.avn-gauge-needle');
  const fill   = gaugeEl.querySelector('.avn-gauge-fill');
  const valEl  = gaugeEl.querySelector('.avn-gauge-val');
  gaugeEl.classList.remove('na', 'caution', 'critical');
  if (typeof value01 !== 'number' || value01 < 0) {
    gaugeEl.classList.add('na');
    needle.style.transform = 'rotate(' + avnNeedleAngle(0) + ')';
    setAvnGaugeFill(fill, 0);
    valEl.textContent = '--';
    return;
  }
  const v = Math.max(0, Math.min(1, value01));
  if      (criticalAt !== null && v <= criticalAt) gaugeEl.classList.add('critical');
  else if (cautionAt  !== null && v <= cautionAt)  gaugeEl.classList.add('caution');
  needle.style.transform = 'rotate(' + avnNeedleAngle(v) + ')';
  setAvnGaugeFill(fill, v);
  valEl.textContent = Math.round(v * 100) + '%';
}

// THROTTLE is its own paint: AvnThrottlePolicy gives the needle position (`fill`, 0..1), the
// readout text ('MIL nn%' / red 'AB nn%'), and the zone. The green/red split is done via masking
// rather than a variable-length dash range: .avn-gauge-fill-hot is always revealed full-extent to
// the live value (the same fixed-dasharray reveal every other gauge uses, see setAvnGaugeFill),
// and the green .avn-gauge-fill paints on top of it (avn.html source order) capped at boundary,
// hiding red for the MIL portion — only the segment past boundary ever shows red. A fixed-pattern
// dashoffset transition tweens with CSS; a dasharray recomputed every frame would not.
// The AB placard's own arc only depends on abStart (a per-aircraft constant), not the live
// throttle — re-set it whenever abStart changes rather than every paint, so its span never
// tracks the needle even transiently.
let avnAbPathBoundary = null;
function paintAvnThrottle() {
  const r = AvnThrottlePolicy.throttleReadout(avnData.throttle, avnData.hasAb, avnData.abStart);
  const needle = avnGaugeThr.querySelector('.avn-gauge-needle');
  const fill   = avnGaugeThr.querySelector('.avn-gauge-fill');
  const fillHot = avnGaugeThr.querySelector('.avn-gauge-fill-hot');
  const valEl  = avnGaugeThr.querySelector('.avn-gauge-val');
  const abPath = avnGaugeThr.querySelector('#avn-gauge-thr-ab-path');
  avnGaugeThr.classList.remove('caution', 'critical');
  avnGaugeThr.classList.toggle('na', r.na);
  avnGaugeThr.classList.toggle('ab-active', r.zone === 'ab');
  needle.style.transform = 'rotate(' + avnNeedleAngle(r.fill) + ')';
  setAvnGaugeFill(fillHot, r.boundary !== null ? r.fill : 0);
  setAvnGaugeFill(fill, r.boundary !== null ? Math.min(r.fill, r.boundary) : r.fill);
  valEl.textContent = r.text;
  if (r.boundary !== null && r.boundary !== avnAbPathBoundary) {
    // v0=1, v1=boundary (high to low) — see avnArcPath: the reheat zone sits on the dial's bottom
    // half, where upright text runs opposite the fills' usual low-to-high direction.
    abPath.setAttribute('d', avnArcPath(AVN_AB_LABEL_R, 1, r.boundary));
    avnAbPathBoundary = r.boundary;
  }
}

// ── Shell → page forwarding ──────────────────────────────────────────────────────────
window.addEventListener('message', function(e) {
  const m = e.data;
  if (!m || m.mfd !== true) return;
  if (m.type === 'avn') {
    avnData = {
      name: m.name || null,   // presence-only — never displayed, just gates the empty state
      fuel:     typeof m.fuel     === 'number' ? m.fuel     : -1,
      throttle: typeof m.throttle === 'number' ? m.throttle : -1,
      heat:     typeof m.heat     === 'number' ? m.heat     : -1,
      heatColor: typeof m.heatColor === 'string' ? m.heatColor : null,
      rpm:      typeof m.rpm      === 'number' ? m.rpm      : -1,
      hasAb:    m.hasAb === true,
      abStart:  typeof m.abStart === 'number' ? m.abStart : 1,
    };
    // Full render on aircraft change, or whenever there's no aircraft.
    if (avnLastType !== avnData.name || !avnData.name) renderAvn();
    else paintAvnGauges();
  } else if (m.type === 'avn-layout') {
    // Geometry profile from the shell. full forwards the bezel-anchored vertical band
    // (geom.frameTop/frameHeight); compact carries no geometry.
    layout = (m.layout === 'full') ? 'full' : 'compact';
    document.body.classList.toggle('full', layout === 'full');
    avnFullGeom = (layout === 'full') ? (m.geom || null) : null;
    renderAvn();
  } else if (m.type === 'orient') {
    // App-wide orientation forwarded by the shell (see body.portrait rules in the CSS).
    document.body.classList.toggle('portrait',  m.orientation === 'portrait');
    document.body.classList.toggle('landscape', m.orientation !== 'portrait');
    renderAvn();   // re-layout in case orientation-dependent sizing changed
  }
});

window.addEventListener('resize', renderAvn);
renderAvn();   // initial empty-state paint
