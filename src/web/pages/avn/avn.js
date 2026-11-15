// ENG page (id avn) — engine cards, throttle/heat pedestal and fuel band (docs/eng-page.md). A
// pure reactive renderer driven by the shell over postMessage; single source of truth for BOTH
// layouts (full-screen iframe + split pane). See avn.html for the message contract.

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
// Engine cards and tank bars are built once per shape (engine/prop kinds / tank names) and then only repainted,
// so a steady stream of frames never rebuilds the DOM.
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
  paintAvnEngines(eng && Array.isArray(eng.engines) ? eng.engines : []);
  paintAvnPedestal();
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
      label: labels[i],
    });
  }
}

function paintAvnEngines(engines) {
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
    c.hpVal.textContent = hp >= 0 ? Math.round(hp * 100) + '%' : '--';

    paintAvnGauge(c.rpm, typeof e.rpm === 'number' ? e.rpm : -1, null, null);
    const out = AvnEngPolicy.engineOutput(e);
    c.outLabel.textContent = out.label;
    paintAvnGauge(c.out, out.value, null, null, out.text);
    c.out.classList.toggle('readout', out.readout === true);
    if (out.readout) {
      c.out.classList.remove('na');
      c.out.querySelector('.avn-gauge-val').textContent = out.text;
    }
    c.rpm.setAttribute('aria-label', c.label + ' RPM ' + c.rpm.querySelector('.avn-gauge-val').textContent);
    c.out.setAttribute('aria-label', c.label + ' ' + out.label + ' ' + out.text);
  }
}

// ── Pedestal: throttle lever + heat tape ───────────────────────────────────────────
function paintAvnPedestal() {
  const r = AvnThrottlePolicy.throttleReadout(avnData.throttle, avnData.hasAb, avnData.abStart);
  avnLever.classList.toggle('na', r.na);
  avnLever.classList.toggle('has-ab', r.boundary !== null);
  avnLever.style.setProperty('--thr', String(1 - r.fill));
  avnLever.style.setProperty('--ab', String(r.boundary !== null ? 1 - r.boundary : 0));
  avnLeverTagMil.hidden = r.boundary === null;
  avnThrVal.textContent = r.text;
  avnThrZone.textContent = r.zone === 'ab' ? 'AB' : r.zone === 'mil' ? 'MIL' : '';
  avnThrZone.classList.toggle('ab', r.zone === 'ab');
  avnLever.setAttribute('aria-label', 'Throttle ' + r.text + (r.zone === 'ab' ? ' afterburner' : ''));

  const heat = avnData.heat;
  const h = typeof heat === 'number' && heat >= 0 ? Math.min(1, heat) : 0;
  avnHeatTape.style.setProperty('--heat', String(h));
  // The lit segment takes the colour the game's own cockpit IR gauge shows at this value.
  avnHeatLit.style.background = avnData.heatColor || '';
  avnHeatVal.textContent = heat >= 0 ? Math.round(h * 100) + '%' : '--';
  avnHeatVal.style.color = avnData.heatColor || '';
}

// ── Fuel band ──────────────────────────────────────────────────────────────────────
function buildAvnTanks(tanks) {
  avnTanksEl.textContent = '';
  avnTankRows = tanks.map(function (t) {
    const el = avnTankTpl.content.firstElementChild.cloneNode(true);
    el.querySelector('.avn-tank-name').textContent = AvnEngPolicy.tankLabel(t.n);
    avnTanksEl.appendChild(el);
    return { el: el, fill: el.querySelector('.avn-tank-fill'), kg: el.querySelector('.avn-tank-kg'), flag: el.querySelector('.avn-tank-flag') };
  });
}

function paintAvnFuel(eng) {
  paintAvnGauge(avnGaugeFuel, avnData.fuel, 0.25, 0.10);
  const metric = avnData.metric !== false;
  const f = AvnEngPolicy.fuelReadouts(eng, metric);
  avnFuelMass.textContent = f.fuel === '--' ? '' : f.fuel + ' ' + f.fuelUnit;
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
// The needle is drawn pointing at the dial's zero position (-135deg); rotating it v*270deg
// clockwise covers the same 270deg sweep as the shared tick ring.
function avnNeedleAngle(v) { return (v * 270).toFixed(1) + 'deg'; }

// The fill arc from zero to v: dasharray = the path's own length L, dashoffset = L*(1-v), so only
// the first v*L of the path stays visible. getTotalLength() is cached per element.
const avnGaugeFillLengths = new WeakMap();
function setAvnGaugeFill(fillEl, v) {
  let L = avnGaugeFillLengths.get(fillEl);
  if (L === undefined) { L = fillEl.getTotalLength(); avnGaugeFillLengths.set(fillEl, L); }
  // Unitless: user-space units of the viewBox, matching getTotalLength().
  fillEl.style.strokeDasharray = L;
  fillEl.style.strokeDashoffset = L * (1 - v);
}
function paintAvnGauge(gaugeEl, value01, cautionAt, criticalAt, text) {
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
  valEl.textContent = text !== undefined ? text : Math.round(v * 100) + '%';
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
