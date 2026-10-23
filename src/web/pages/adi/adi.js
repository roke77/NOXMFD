// ADI page (docs/adi-hsi-pages.md). Renders the shell-relayed 'adi' slice. The SVG is built once;
// each frame only moves the ball, the tapes and the slip ball, easing toward the latest telemetry
// (AdiMath) so the 10 Hz feed doesn't step.
(function () {
  const NS = 'http://www.w3.org/2000/svg';
  const svg = document.getElementById('adi-svg');
  function el(tag, attrs, parent, text) {
    const e = document.createElementNS(NS, tag);
    for (const k in attrs) e.setAttribute(k, attrs[k]);
    if (text != null) e.textContent = text;
    if (parent) parent.appendChild(e);
    return e;
  }

  const CX = 445, CY = 410, R = 260;
  const K = 6;                 // px per degree of pitch
  const TAPE_STEP_PX = 56;     // px per labelled tape step
  // ponytail: 10 px per degree of sideslip, clamped to the tube; tune once flown in game.
  const SLIP_PX_PER_DEG = 10, SLIP_MAX_PX = 79;
  const D2R = Math.PI / 180;
  const polar = (r, a) => [CX + r * Math.sin(a * D2R), CY - r * Math.cos(a * D2R)];

  const defs = el('defs', {}, svg);
  el('circle', { cx: CX, cy: CY, r: R }, el('clipPath', { id: 'adi-ball-clip' }, defs));
  el('rect', { x: 64, y: 130, width: 100, height: 560 }, el('clipPath', { id: 'adi-spd-clip' }, defs));
  el('rect', { x: 726, y: 130, width: 100, height: 560 }, el('clipPath', { id: 'adi-alt-clip' }, defs));

  // ── attitude ball ─────────────────────────────────────────────────────────────────────────
  const ball = el('g', {}, el('g', { 'clip-path': 'url(#adi-ball-clip)' }, svg));
  el('rect', { x: -1000, y: CY - 3000, width: 3000, height: 3000, class: 'adi-sky' }, ball);
  el('rect', { x: -1000, y: CY, width: 3000, height: 3000, class: 'adi-ground' }, ball);
  el('line', { x1: -1000, y1: CY, x2: 2000, y2: CY, class: 'adi-horizon' }, ball);
  for (let p = -90; p <= 90; p += 5) {
    if (p === 0) continue;
    const y = CY - p * K, w = p % 10 === 0 ? 66 : 30;
    el('line', { x1: CX - w, y1: y, x2: CX + w, y2: y, class: 'adi-rung' }, ball);
    if (p % 10 === 0) for (const s of [-1, 1])
      el('text', { x: CX + s * 92, y: y + 8, class: 'adi-rung-label' }, ball, Math.abs(p));
  }
  el('circle', { cx: CX, cy: CY, r: R, class: 'adi-rim' }, svg);
  for (const a of [-60, -45, -30, -20, -10, 0, 10, 20, 30, 45, 60]) {
    const big = a % 30 === 0;
    const [x1, y1] = polar(R + 4, a), [x2, y2] = polar(R + (big ? 28 : 16), a);
    el('line', { x1, y1, x2, y2, class: 'adi-bank', 'stroke-width': big ? 3.6 : 2.2 }, svg);
  }
  const skyPointer = el('path', { d: `M${CX} ${CY - R + 3} l-14 26 h28 z`, class: 'adi-pointer' }, svg);
  el('path', { d: `M${CX - 134} ${CY} h86 l22 22 M${CX + 134} ${CY} h-86 l-22 22`, class: 'adi-symbol' }, svg);
  el('rect', { x: CX - 7.5, y: CY - 7.5, width: 15, height: 15, class: 'adi-symbol-dot' }, svg);
  const SY = CY + R + 16;
  el('rect', { x: CX - 92, y: SY, width: 184, height: 32, rx: 16, class: 'adi-slip-tube' }, svg);
  for (const s of [-1, 1]) el('line', { x1: CX + s * 20, y1: SY, x2: CX + s * 20, y2: SY + 32, class: 'adi-slip-mark' }, svg);
  const slipBall = el('circle', { cx: CX, cy: SY + 16, r: 13, class: 'adi-slip-ball' }, svg);

  // ── tapes: marks rebuilt only when the value crosses a minor step, otherwise just scrolled ──
  function tape(x, ticksRight, labelX, labelSize, clipId, boxPath, valueX, valueSize, headerX) {
    el('rect', { x, y: 130, width: 100, height: 560, class: 'adi-tape' }, svg);
    const scroll = el('g', {}, el('g', { 'clip-path': `url(#${clipId})` }, svg));
    el('path', { d: boxPath, class: 'adi-box' }, svg);
    const value = el('text', { x: valueX, y: CY + 14, class: 'adi-value mid', 'font-size': valueSize }, svg, '—');
    const header = el('text', { x: headerX, y: 120, class: 'adi-label mid' }, svg);
    let builtKey = null, base = 0;
    return function update(v, u, name) {
      header.textContent = name + ' ' + u.unit;
      if (v == null) { value.textContent = '—'; scroll.style.display = 'none'; return; }
      scroll.style.display = '';
      const key = Math.floor(v / u.minor) + u.unit;
      if (key !== builtKey) {
        builtKey = key;
        base = Math.floor(v / u.minor) * u.minor;
        scroll.textContent = '';
        for (const m of AdiMath.tapeMarks(base, u.step, u.minor, 6)) {
          const y = CY - m.off * TAPE_STEP_PX;
          const len = m.major ? 18 : 10;
          const x1 = ticksRight ? x + 100 - len : x;
          el('line', { x1, y1: y, x2: x1 + len, y2: y, class: m.major ? 'adi-tick' : 'adi-tick-minor' }, scroll);
          if (m.major) el('text', { x: labelX, y: y + 8, class: 'adi-tape-label', 'font-size': labelSize }, scroll, m.v);
        }
      }
      scroll.setAttribute('transform', `translate(0 ${((v - base) / u.step) * TAPE_STEP_PX})`);
      value.textContent = Math.round(v);
    };
  }
  const spdTape = tape(64, true, 104, 24, 'adi-spd-clip',
    `M58 ${CY - 28} h106 l14 28 l-14 28 h-106 z`, 112, 40, 114);
  const altTape = tape(726, false, 788, 22, 'adi-alt-clip',
    `M832 ${CY - 28} h-106 l-14 28 l14 28 h106 z`, 778, 36, 776);

  // ── readouts ───────────────────────────────────────────────────────────────────────────
  function readout(x, y, label, cls, anchor) {
    const lbl = el('text', { x, y, class: 'adi-label ' + anchor }, svg, label);
    return { label: lbl, value: el('text', { x, y: y + 48, class: 'adi-value ' + cls + ' ' + anchor }, svg, '—') };
  }
  const mach = readout(64, 40, 'MACH', '', '');
  const vs = readout(826, 40, 'V/S', '', 'end');
  el('line', { x1: 64, y1: 744, x2: 826, y2: 744, class: 'adi-rule' }, svg);
  for (const x of [254.5, 445, 635.5]) el('line', { x1: x, y1: 760, x2: x, y2: 864, class: 'adi-rule' }, svg);
  const g = readout(159.3, 788, 'G', '', 'mid');
  const gMax = readout(349.8, 788, 'MAX G', 'white', 'mid');
  const aoa = readout(540.3, 788, 'AOA', '', 'mid');
  const ralt = readout(730.8, 788, 'R-ALT', '', 'mid');

  // ── state ───────────────────────────────────────────────────────────────────────────────
  let units = AdiMath.units(false);
  let present = false, maxG = null;
  const target = { pitch: 0, roll: 0, slip: 0, spd: null, alt: null };
  const shown = { pitch: 0, roll: 0, slip: 0, spd: null, alt: null };
  let running = false, last = 0, first = true;

  function draw() {
    ball.setAttribute('transform', `rotate(${-shown.roll} ${CX} ${CY}) translate(0 ${shown.pitch * K})`);
    skyPointer.setAttribute('transform', `rotate(${-shown.roll} ${CX} ${CY})`);
    slipBall.setAttribute('cx', CX + Math.max(-SLIP_MAX_PX, Math.min(SLIP_MAX_PX, shown.slip * SLIP_PX_PER_DEG)));
    spdTape(shown.spd, units.spd, 'TAS');
    altTape(shown.alt, units.alt, 'ALT');
  }

  function settled() {
    const near = (a, b) => (a == null || b == null ? a === b : Math.abs(a - b) < 0.01);
    return near(shown.pitch, target.pitch) && Math.abs(AdiMath.angleDelta(shown.roll, target.roll)) < 0.01 &&
           near(shown.slip, target.slip) && near(shown.spd, target.spd) && near(shown.alt, target.alt);
  }

  function frame(now) {
    const dt = Math.min(0.25, (now - last) / 1000);
    last = now;
    shown.pitch = AdiMath.approach(shown.pitch, target.pitch, dt);
    shown.roll = AdiMath.approachAngle(shown.roll, target.roll, dt);
    shown.slip = AdiMath.approach(shown.slip, target.slip, dt);
    shown.spd = target.spd == null || shown.spd == null ? target.spd : AdiMath.approach(shown.spd, target.spd, dt);
    shown.alt = target.alt == null || shown.alt == null ? target.alt : AdiMath.approach(shown.alt, target.alt, dt);
    draw();
    if (settled()) { running = false; return; }
    requestAnimationFrame(frame);
  }

  function kick() {
    if (first) { Object.assign(shown, target); first = false; draw(); return; }
    if (running) return;
    running = true;
    last = performance.now();
    requestAnimationFrame(frame);
  }

  const fixed = (v, n, suffix) => (v == null ? '—' : v.toFixed(n) + (suffix || ''));

  function apply(m) {
    present = m.present === true;
    if (!present) {
      maxG = null;
      Object.assign(target, { pitch: 0, roll: 0, slip: 0, spd: null, alt: null });
      for (const r of [mach, vs, g, gMax, aoa, ralt]) r.value.textContent = '—';
      kick();
      return;
    }
    units = AdiMath.units(m.metric);
    target.pitch = m.pitch || 0;
    target.roll = m.roll || 0;
    target.slip = m.beta || 0;
    target.spd = m.tas == null ? null : m.tas * units.spd.k;
    target.alt = m.alt == null ? null : m.alt * units.alt.k;
    if (m.g != null) maxG = maxG == null ? m.g : Math.max(maxG, m.g);

    mach.value.textContent = fixed(m.mach, 2);
    vs.label.textContent = 'V/S ' + units.vs.unit;
    vs.value.textContent = m.vs == null ? '—'
      : AdiMath.signed(Math.round(m.vs * units.vs.k / units.vs.round) * units.vs.round);
    g.value.textContent = fixed(m.g, 1);
    gMax.value.textContent = fixed(maxG, 1);
    aoa.value.textContent = fixed(m.aoa, 1, '°');
    ralt.label.textContent = 'R-ALT ' + units.alt.unit;
    ralt.value.textContent = m.agl == null ? '—' : String(Math.round(m.agl * units.alt.k));
    kick();
  }

  window.addEventListener('message', function (e) {
    const m = e.data;
    if (m && m.mfd === true && m.type === 'adi') apply(m);
  });

  draw();
})();
