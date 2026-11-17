// ENG page (id avn) — engine cards, throttle/heat pedestal and fuel band (docs/eng-page.md). A
// pure reactive renderer driven by the shell over postMessage; single source of truth for BOTH
// layouts (full-screen iframe + split pane). See avn.html for the message contract.

// ── ECAM skin ──────────────────────────────────────────────────────────────────────
// ?ecam (the F-35 layout's ENG, layout-pages.js) swaps every dial's inner markup for an open 210deg
// ECAM-style arc before the DOM refs below are taken, so the first paint is already the skin's.
// Geometry: centre (62,58), r=46, zero at 165deg and full scale at 15deg (SVG angles, clockwise).
const AVN_ECAM = new URLSearchParams(location.search).has('ecam');
const AVN_SWEEP = AVN_ECAM ? 210 : 270;
document.body.classList.toggle('ecam', AVN_ECAM);
// ECAM boxes the bare number and prints the % beside the box (a tspan or avn.css ::after).
function avnPct(v) { return Math.round(v * 100) + (AVN_ECAM ? '' : '%'); }
function avnEcamGaugeHtml(label, unit, mid, fuel) {
  return '<div class="avn-gauge-dial-wrap"><svg class="avn-gauge-dial" viewBox="0 6 140 ' + (fuel ? 104 : 94) + '">'
    + '<path class="avn-gauge-track" d="M 17.57 69.91 A 46 46 0 1 1 106.43 69.91"/>'
    + '<g class="avn-gauge-ticks"><line x1="24.33" y1="68.09" x2="17.57" y2="69.91"/>'
    + '<line x1="62" y1="19" x2="62" y2="12"/><line x1="99.67" y1="68.09" x2="106.43" y2="69.91"/></g>'
    + (mid ? '<text class="avn-gauge-mid" x="62" y="30" text-anchor="middle">' + mid + '</text>' : '')
    // FUEL's caution mark sits at 25% (165 + 0.25*210 = 217.5deg); the engine RPM dial carries the
    // throttle-command index instead, drawn at zero and rotated like the needle.
    + (fuel ? '<line class="avn-gauge-caution" x1="31.06" y1="34.26" x2="25.5" y2="29.99"/>'
            : label === 'RPM' ? '<circle class="avn-gauge-cmd" cx="17.57" cy="69.91" r="3.5"/>' : '')
    + '<g class="avn-gauge-needle"><line x1="62" y1="58" x2="19.5" y2="69.39"/></g>'
    + '<rect class="avn-gauge-box" x="' + (fuel ? 66 : 76) + '" y="72" width="' + (fuel ? 68 : 58) + '" height="24"/>'
    + '<text class="avn-gauge-val" x="128" y="90" text-anchor="end">--</text>'
    + '<text x="4" y="95"><tspan class="avn-gauge-label">' + label + '</tspan> <tspan class="avn-gauge-unit">' + unit + '</tspan></text>'
    + (fuel ? '<text class="avn-gauge-sub" id="avn-fuel-mass" x="134" y="107" text-anchor="end"></text>' : '')
    + '</svg></div>';
}
if (AVN_ECAM) {
  const tpl = document.getElementById('avn-engine-tpl').content;
  tpl.querySelector('.avn-gauge.rpm').innerHTML = avnEcamGaugeHtml('RPM', '%', '50', false);
  tpl.querySelector('.avn-gauge.out').innerHTML = avnEcamGaugeHtml('THR', 'kN', '', false);
  document.getElementById('avn-gauge-fuel').innerHTML = avnEcamGaugeHtml('FUEL', 'KG', '1/2', true);
}

// ── DOM refs ───────────────────────────────────────────────────────────────────────
const avnPanel     = document.getElementById('avn-panel');
const avnEmptyEl   = document.getElementById('avn-empty');
const avnContentEl = document.getElementById('avn-content');
const avnSideL     = document.getElementById('avn-side-l');
const avnSideR     = document.getElementById('avn-side-r');
const avnLever     = document.getElementById('avn-lever');
const avnLeverTagMil = document.getElementById('avn-lever-tag-mil');
const avnThrVal    = document.getElementById('avn-thr-val');
const avnThrZone   = document.getElementById('avn-thr-zone');
const avnHeatTape  = document.querySelector('.avn-heat-tape');
const avnHeatLit   = document.getElementById('avn-heat-lit');
const avnHeatVal   = document.getElementById('avn-heat-val');
const avnGaugeFuel = document.getElementById('avn-gauge-fuel');
const avnFuelMass  = document.getElementById('avn-fuel-mass');
const avnFuelUnit  = avnGaugeFuel.querySelector('.avn-gauge-unit');   // ECAM only
const avnTanksEl   = document.getElementById('avn-tanks');
const avnBurn      = document.getElementById('avn-burn');
const avnBurnUnit  = document.getElementById('avn-burn-unit');
const avnEndur     = document.getElementById('avn-endur');
const avnRange     = document.getElementById('avn-range');
const avnRangeUnit = document.getElementById('avn-range-unit');
const avnGs        = document.getElementById('avn-gs');
const avnEngineTpl = document.getElementById('avn-engine-tpl');
const avnTankTpl   = document.getElementById('avn-tank-tpl');

// ── State ──────────────────────────────────────────────────────────────────────────
let avnData = { name: null, fuel: -1, throttle: -1, heat: -1, heatColor: null, hasAb: false, abStart: 1, eng: null, metric: true };
let layout         = 'compact';   // 'compact' (split pane) | 'full' (full-screen iframe)
let avnFullGeom    = null;        // {frameTop, frameHeight} forwarded by the shell in full
let avnLastType    = null;        // last aircraft type rendered, so a respawn on the SAME type still re-renders
// Engine cards and tank bars are built once per shape (engine/prop sequence, tank names) and then
// only repainted, so a steady stream of frames never rebuilds the DOM.
let avnEngineCards = [];
let avnEngineKey   = null;
let avnTankRows    = [];
let avnTankKey     = null;

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
  paintAvn();
  layoutAvnContent();
}

// The vertical band .avn-content fills: full uses the shell-forwarded bezel geometry (the band
// below the top bezel row); compact has no such geometry, so it is a small top and bottom margin.
const AVN_TOP_MARGIN = 12;
const AVN_BOTTOM_MARGIN = 12;
function avnContentVerticalExtent() {
  if (layout === 'full' && avnFullGeom && typeof avnFullGeom.frameTop === 'number') {
    return { top: avnFullGeom.frameTop, height: avnFullGeom.frameHeight };
  }
  const panelRect = avnPanel.getBoundingClientRect();
  return { top: AVN_TOP_MARGIN, height: Math.max(0, panelRect.height - AVN_TOP_MARGIN - AVN_BOTTOM_MARGIN) };
}

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

function paintAvn() {
  const eng = avnData.eng;
  const thr = AvnThrottlePolicy.throttleReadout(avnData.throttle, avnData.hasAb, avnData.abStart);
  paintAvnEngines(eng && Array.isArray(eng.engines) ? eng.engines : [], thr);
  paintAvnPedestal(thr);
  paintAvnFuel(eng);
}

// ── Engine cards ───────────────────────────────────────────────────────────────────
function buildAvnEngineCards(engines) {
  const n = engines.length;
  avnSideL.textContent = '';
  avnSideR.textContent = '';
  avnEngineCards = [];
  const labels = AvnEngPolicy.engineLabels(engines);
  const split = AvnEngPolicy.pedestalSplit(n);
  const cols = String(AvnEngPolicy.sideColumns(split));
  avnSideL.style.setProperty('--cols', cols);
  avnSideR.style.setProperty('--cols', cols);
  for (let i = 0; i < n; i++) {
    const el = avnEngineTpl.content.firstElementChild.cloneNode(true);
    el.querySelector('.avn-engine-name').textContent = labels[i];
    if (i >= split) el.classList.add('mirror');
    (i < split ? avnSideL : avnSideR).appendChild(el);
    avnEngineCards.push({
      el: el,
      chip: el.querySelector('.avn-chip'),
      hpVal: el.querySelector('.avn-hp-val'),
      rpm: el.querySelector('.avn-gauge.rpm'),
      out: el.querySelector('.avn-gauge.out'),
      outLabel: el.querySelector('.avn-gauge.out .avn-gauge-label'),
      outUnit: el.querySelector('.avn-gauge.out .avn-gauge-unit'),   // ECAM only
      label: labels[i],
    });
  }
}

function paintAvnEngines(engines, thr) {
  const key = engines.map(function (e) { return e.prop === true ? 'p' : 'e'; }).join('');
  if (avnEngineKey !== key) {
    avnEngineKey = key;
    buildAvnEngineCards(engines);
  }
  for (let i = 0; i < engines.length; i++) {
    const e = engines[i], c = avnEngineCards[i];
    const st = AvnEngPolicy.engineStatus(e.st);
    c.el.classList.remove('ok', 'fault', 'inop');
    c.el.classList.add(st.cls);
    c.chip.textContent = st.text;

    const hp = typeof e.hp === 'number' && e.hp >= 0 ? e.hp : -1;
    c.el.style.setProperty('--hp', String(hp >= 0 ? hp : 0));
    c.hpVal.textContent = hp >= 0 ? avnPct(hp) : '--';

    paintAvnGauge(c.rpm, typeof e.rpm === 'number' ? e.rpm : -1, null, null);
    // ECAM's throttle-command index on the RPM dial: where the lever asks the engine to be.
    const cmd = avnGaugeParts(c.rpm).cmd;
    if (cmd) {
      cmd.style.visibility = thr.na ? 'hidden' : '';
      cmd.style.transform = 'rotate(' + avnNeedleAngle(thr.fill) + ')';
    }
    const out = AvnEngPolicy.engineOutput(e);
    const outText = AVN_ECAM ? out.num : out.text;
    c.outLabel.textContent = out.label;
    if (c.outUnit) c.outUnit.textContent = out.unit;
    c.out.classList.toggle('readout', out.readout === true);
    if (out.readout) {
      c.out.classList.remove('na');
      avnGaugeParts(c.out).val.textContent = outText;
    } else {
      paintAvnGauge(c.out, out.value, null, null, outText);
    }
    c.rpm.setAttribute('aria-label', c.label + ' RPM ' + avnGaugeParts(c.rpm).val.textContent);
    c.out.setAttribute('aria-label', c.label + ' ' + out.label + ' ' + out.text);
  }
}

// ── Pedestal: throttle lever + heat tape ───────────────────────────────────────────
function paintAvnPedestal(r) {
  avnLever.classList.toggle('na', r.na);
  avnLever.classList.toggle('has-ab', r.boundary !== null);
  avnLever.style.setProperty('--thr', String(1 - r.fill));
  avnLever.style.setProperty('--ab', String(r.boundary !== null ? 1 - r.boundary : 0));
  avnLeverTagMil.hidden = r.boundary === null;
  // ECAM boxes the bare number; the sub-line under it carries the % (avn.css).
  avnThrVal.textContent = AVN_ECAM ? r.text.replace('%', '') : r.text;
  avnThrZone.textContent = r.zone === 'ab' ? 'AB' : r.zone === 'mil' ? 'MIL' : '';
  avnThrZone.classList.toggle('ab', r.zone === 'ab');
  avnLever.setAttribute('aria-label', 'Throttle ' + r.text + (r.zone === 'ab' ? ' afterburner' : ''));

  const heat = avnData.heat;
  const h = typeof heat === 'number' && heat >= 0 ? Math.min(1, heat) : 0;
  avnHeatTape.style.setProperty('--heat', String(h));
  // The lit segment takes the colour the game's own cockpit IR gauge shows at this value; ECAM
  // draws it as a translucent fill under a solid top line.
  const hc = avnData.heatColor || '';
  avnHeatLit.style.background = AVN_ECAM && hc ? 'color-mix(in srgb, ' + hc + ' 25%, transparent)' : hc;
  avnHeatLit.style.borderTopColor = hc;
  avnHeatVal.textContent = heat >= 0 ? avnPct(h) : '--';
  avnHeatVal.style.color = avnData.heatColor || '';
}

// ── Fuel band ──────────────────────────────────────────────────────────────────────
function buildAvnTanks(tanks) {
  avnTanksEl.textContent = '';
  const labels = tanks.map(function (t) { return AvnEngPolicy.tankLabel(t.n); });
  avnTanksEl.classList.toggle('two-line', labels.some(function (l) { return l.indexOf(' ') >= 0; }));
  avnTankRows = tanks.map(function (t, i) {
    const el = avnTankTpl.content.firstElementChild.cloneNode(true);
    el.querySelector('.avn-tank-name').textContent = labels[i];
    avnTanksEl.appendChild(el);
    return { el: el, fill: el.querySelector('.avn-tank-fill'), kg: el.querySelector('.avn-tank-kg'), flag: el.querySelector('.avn-tank-flag') };
  });
}

function paintAvnFuel(eng) {
  const metric = avnData.metric !== false;
  const f = AvnEngPolicy.fuelReadouts(eng, metric);
  if (AVN_ECAM) {
    // The mass is the boxed value; the fraction and the capacity move to the sub-line.
    paintAvnGauge(avnGaugeFuel, avnData.fuel, 0.25, 0.10, f.fuel);
    avnFuelUnit.textContent = f.fuelUnit.toUpperCase();
    avnFuelMass.textContent = avnData.fuel < 0 ? ''
      : Math.round(Math.min(1, avnData.fuel) * 100) + ' %' + (f.fuelCap !== '--' ? ' OF ' + f.fuelCap : '');
  } else {
    paintAvnGauge(avnGaugeFuel, avnData.fuel, 0.25, 0.10);
    avnFuelMass.textContent = f.fuel === '--' ? '' : f.fuel + ' ' + f.fuelUnit;
  }
  avnBurn.textContent = f.burn;
  avnBurnUnit.textContent = f.burnUnit;
  avnEndur.textContent = f.endurance;
  avnRange.textContent = f.range;
  avnRangeUnit.textContent = f.rangeUnit;
  avnGs.textContent = 'at GS ' + f.gs + ' ' + f.gsUnit;

  const tanks = eng && Array.isArray(eng.tanks) ? eng.tanks : [];
  const key = tanks.map(function (t) { return t.n; }).join('|');
  if (key !== avnTankKey) { avnTankKey = key; buildAvnTanks(tanks); }
  const massK = metric ? 1 : AvnEngPolicy.KG_TO_LB;
  for (let i = 0; i < tanks.length; i++) {
    const t = tanks[i], row = avnTankRows[i];
    const frac = t.cap > 0 ? Math.max(0, Math.min(1, t.kg / t.cap)) : 0;
    row.fill.style.height = (frac * 100) + '%';
    row.kg.textContent = Math.round(t.kg * massK);
    row.el.classList.toggle('leak', t.leak === true && t.fire !== true);
    row.el.classList.toggle('fire', t.fire === true);
    row.flag.textContent = t.fire === true ? 'FIRE' : t.leak === true ? 'LEAK' : '';
  }
}

// ── Dials ──────────────────────────────────────────────────────────────────────────
// The needle is drawn pointing at the dial's zero position; rotating it v*AVN_SWEEP clockwise covers
// the dial's sweep (270deg classic, 210deg ECAM).
function avnNeedleAngle(v) { return (v * AVN_SWEEP).toFixed(1) + 'deg'; }

// The fill arc from zero to v: dasharray = the path's own length L, dashoffset = L*(1-v), so only
// the first v*L of the path stays visible. getTotalLength() is cached per element.
const avnGaugeFillLengths = new WeakMap();
function setAvnGaugeFill(fillEl, v) {
  if (!fillEl) return;   // ECAM dials have no fill arc
  let L = avnGaugeFillLengths.get(fillEl);
  if (L === undefined) { L = fillEl.getTotalLength(); avnGaugeFillLengths.set(fillEl, L); }
  // Unitless: user-space units of the viewBox, matching getTotalLength().
  fillEl.style.strokeDasharray = L;
  fillEl.style.strokeDashoffset = L * (1 - v);
}
// A dial's moving parts, looked up once per dial element: every engine card repaints two dials
// each frame, so per-frame querySelector calls would scale with the engine count.
const avnGaugePartsCache = new WeakMap();
function avnGaugeParts(gaugeEl) {
  let p = avnGaugePartsCache.get(gaugeEl);
  if (!p) {
    p = {
      needle: gaugeEl.querySelector('.avn-gauge-needle'),
      fill:   gaugeEl.querySelector('.avn-gauge-fill'),
      val:    gaugeEl.querySelector('.avn-gauge-val'),
      cmd:    gaugeEl.querySelector('.avn-gauge-cmd'),
    };
    avnGaugePartsCache.set(gaugeEl, p);
  }
  return p;
}
function paintAvnGauge(gaugeEl, value01, cautionAt, criticalAt, text) {
  const parts  = avnGaugeParts(gaugeEl);
  const needle = parts.needle;
  const fill   = parts.fill;
  const valEl  = parts.val;
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
  valEl.textContent = text !== undefined ? text : avnPct(v);
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
      hasAb:    m.hasAb === true,
      abStart:  typeof m.abStart === 'number' ? m.abStart : 1,
      eng:      m.eng && typeof m.eng === 'object' ? m.eng : null,
      metric:   m.metric !== false,
    };
    // Full render on aircraft change, or whenever there's no aircraft.
    if (avnLastType !== avnData.name || !avnData.name) renderAvn();
    else paintAvn();
  } else if (m.type === 'avn-layout') {
    layout = (m.layout === 'full') ? 'full' : 'compact';
    document.body.classList.toggle('full', layout === 'full');
    avnFullGeom = (layout === 'full') ? (m.geom || null) : null;
    renderAvn();
  } else if (m.type === 'orient') {
    document.body.classList.toggle('portrait',  m.orientation === 'portrait');
    document.body.classList.toggle('landscape', m.orientation !== 'portrait');
    renderAvn();
  }
});

window.addEventListener('resize', renderAvn);
renderAvn();   // initial empty-state paint
