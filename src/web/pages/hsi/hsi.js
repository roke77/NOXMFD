// HSI page (docs/adi-hsi-pages.md). Heading, turn rate and TAS come from the 'adi' slice, own
// position from 'mapinfo', the target and course from the navigation library (WaypointsStore +
// WptRoute.navigationTarget / navCourse). The rose eases toward the latest heading (AdiMath).
(function () {
  const NS = 'http://www.w3.org/2000/svg';
  const svg = document.getElementById('hsi-svg');
  function el(tag, attrs, parent, text) {
    const e = document.createElementNS(NS, tag);
    for (const k in attrs) e.setAttribute(k, attrs[k]);
    if (text != null) e.textContent = text;
    if (parent) parent.appendChild(e);
    return e;
  }

  const CX = 445, CY = 470, R = 340;
  const DOT_PX = 56;          // deviation dot spacing
  const TREND_SECONDS = 6;
  const D2R = Math.PI / 180;
  const polar = (r, a) => [CX + r * Math.sin(a * D2R), CY - r * Math.cos(a * D2R)];
  const rotate = (deg) => `rotate(${deg} ${CX} ${CY})`;

  // ── heading box, lubber line, trend arc ───────────────────────────────────────────────────
  el('rect', { x: CX - 62, y: 44, width: 124, height: 58, class: 'hsi-box' }, svg);
  const headingText = el('text', { x: CX, y: 90, class: 'hsi-heading' }, svg, '—');
  const trend = el('path', { class: 'hsi-trend' }, svg);
  el('path', { d: `M${CX} ${CY - R - 3} l-15 -20 h30 z`, class: 'hsi-lubber' }, svg);

  // ── compass card (rotates with heading) ───────────────────────────────────────────────────
  const card = el('g', {}, svg);
  el('circle', { cx: CX, cy: CY, r: R, class: 'hsi-card' }, card);
  for (let d = 0; d < 360; d += 5) {
    const len = d % 30 === 0 ? 34 : d % 10 === 0 ? 24 : 14;
    const [x1, y1] = polar(R, d), [x2, y2] = polar(R - len, d);
    el('line', { x1, y1, x2, y2, class: 'hsi-tick' + (d % 10 === 0 ? ' major' : '') }, card);
  }
  const CARDINAL = { 0: 'N', 90: 'E', 180: 'S', 270: 'W' };
  for (let d = 0; d < 360; d += 30) {
    el('text', { x: CX, y: CY - R + 75, transform: rotate(d), class: 'hsi-card-label' + (CARDINAL[d] ? ' cardinal' : '') },
       card, CARDINAL[d] || String(d / 10));
  }
  // Bearing pointer and course arrow are drawn pointing up and rotated to their absolute bearing
  // inside the card, so they turn with it.
  const bearing = el('g', {}, card);
  const pl = 2 * R - 170;
  el('path', { d: `M${CX} ${CY - R + 30} l-22 40 h15 v${pl} h14 v-${pl} h15 z`, class: 'hsi-bearing' }, bearing);
  const course = el('g', {}, card);
  el('path', { d: `M${CX} ${CY - R + 100} l-20 34 h13 v66 h14 v-66 h13 z`, class: 'hsi-course' }, course);
  el('rect', { x: CX - 7, y: CY + 132, width: 14, height: R - 200, class: 'hsi-course' }, course);
  for (const s of [-2, -1, 1, 2]) el('circle', { cx: CX + s * DOT_PX, cy: CY, r: 9, class: 'hsi-dot' }, course);
  const cdi = el('rect', { x: CX - 7, y: CY - 112, width: 14, height: 224, class: 'hsi-course' }, course);
  el('path', { d: `M${CX - 36} ${CY + 7} h72 M${CX} ${CY - 29} v57 M${CX - 18} ${CY + 32} h36`, class: 'hsi-ownship' }, svg);
  const noNav = el('text', { x: CX, y: CY + 110, class: 'hsi-no-nav' }, svg, 'NO NAV TARGET');

  // ── corner readouts ───────────────────────────────────────────────────────────────────────
  function readout(x, y, label, cls, end) {
    const a = end ? ' end' : '';
    const lbl = el('text', { x, y, class: 'hsi-label' + a }, svg, label);
    return { label: lbl, value: el('text', { x, y: y + 48, class: 'hsi-value ' + cls + a }, svg, '—') };
  }
  const to = readout(64, 40, 'TO', 'nav');
  const brg = readout(64, 128, 'BRG', 'nav');
  const crs = readout(826, 40, 'CRS', 'course', true);
  const xtk = readout(826, 128, 'XTK', 'course', true);
  const dist = readout(64, 734, 'DIST', 'nav');
  const ete = readout(64, 816, 'ETE', 'nav');
  const turn = readout(826, 734, 'TURN °/S', '', true);
  const tas = readout(826, 816, 'TAS', '', true);

  // ── state ─────────────────────────────────────────────────────────────────────────────────
  let adi = { present: false };
  let mapinfo = { x: null, z: null, metric: false };
  let shownHdg = null, targetHdg = 0;
  let running = false, last = 0;

  const pad3 = (d) => String(Math.round(((d % 360) + 360) % 360) % 360).padStart(3, '0');
  function eteText(distM, tasMs) {
    if (!(tasMs > 1)) return '—';
    const s = Math.round(distM / tasMs);
    const h = Math.floor(s / 3600), m = Math.floor((s % 3600) / 60), sec = s % 60;
    const mm = String(m).padStart(2, '0'), ss = String(sec).padStart(2, '0');
    return h > 0 ? h + ':' + mm + ':' + ss : mm + ':' + ss;
  }

  function drawCard() {
    card.setAttribute('transform', rotate(-(shownHdg || 0)));
  }

  function frame(now) {
    const dt = Math.min(0.25, (now - last) / 1000);
    last = now;
    shownHdg = AdiMath.approachAngle(shownHdg, targetHdg, dt);
    drawCard();
    if (Math.abs(AdiMath.angleDelta(shownHdg, targetHdg)) < 0.01) { running = false; return; }
    requestAnimationFrame(frame);
  }

  function kickHeading() {
    if (shownHdg == null) { shownHdg = targetHdg; drawCard(); return; }
    if (running) return;
    running = true;
    last = performance.now();
    requestAnimationFrame(frame);
  }

  function render() {
    const metric = !!(adi.present ? adi.metric : mapinfo.metric);
    const u = AdiMath.units(metric);
    const distUnit = metric ? 'KM' : 'NM', distK = metric ? 1 / 1000 : 1 / 1852;

    if (adi.present && typeof adi.hdg === 'number') {
      targetHdg = adi.hdg;
      headingText.textContent = pad3(adi.hdg);
      kickHeading();
    } else headingText.textContent = '—';

    const span = adi.present && typeof adi.turn === 'number' ? WptRoute.turnTrend(adi.turn, TREND_SECONDS) : 0;
    if (Math.abs(span) > 0.5) {
      const tr = R + 14, [x1, y1] = polar(tr, 0), [x2, y2] = polar(tr, span);
      trend.setAttribute('d', `M${x1} ${y1} A${tr} ${tr} 0 0 ${span > 0 ? 1 : 0} ${x2} ${y2}`);
    } else trend.setAttribute('d', '');
    turn.value.textContent = adi.present && typeof adi.turn === 'number'
      ? (adi.turn >= 0 ? 'R ' : 'L ') + Math.abs(adi.turn).toFixed(1) : '—';
    tas.label.textContent = 'TAS ' + u.spd.unit;
    tas.value.textContent = adi.present && typeof adi.tas === 'number' ? String(Math.round(adi.tas * u.spd.k)) : '—';

    const data = WaypointsStore.load();
    const target = WptRoute.navigationTarget(data);
    const own = mapinfo.x != null && mapinfo.z != null;
    const hasTarget = !!target && own;
    bearing.style.display = hasTarget ? '' : 'none';
    noNav.style.display = target ? 'none' : '';
    dist.label.textContent = 'DIST ' + distUnit;
    xtk.label.textContent = 'XTK ' + distUnit;
    if (hasTarget) {
      const db = WptRoute.distanceBearing(mapinfo.x, mapinfo.z, target.point.x, target.point.z);
      bearing.setAttribute('transform', rotate(db.brgDeg));
      to.value.textContent = (target.kind === 'waypoint' ? 'WPT ' : 'STP ') + (target.index + 1);
      brg.value.textContent = pad3(db.brgDeg) + '°';
      dist.value.textContent = (db.distM * distK).toFixed(1);
      ete.value.textContent = adi.present ? eteText(db.distM, adi.tas) : '—';
    } else {
      to.value.textContent = target ? (target.kind === 'waypoint' ? 'WPT ' : 'STP ') + (target.index + 1) : '—';
      brg.value.textContent = dist.value.textContent = ete.value.textContent = '—';
    }

    const c = hasTarget ? WptRoute.navCourse(data) : null;
    course.style.display = c ? '' : 'none';
    if (c) {
      const xt = WptRoute.crossTrack(c, mapinfo.x, mapinfo.z);
      course.setAttribute('transform', rotate(c.crsDeg));
      cdi.setAttribute('x', CX - 7 + WptRoute.deviationDots(xt, metric) * DOT_PX);
      crs.value.textContent = pad3(c.crsDeg) + '°';
      xtk.value.textContent = (xt >= 0 ? 'R ' : 'L ') + Math.abs(xt * distK).toFixed(1);
    } else crs.value.textContent = xtk.value.textContent = '—';
  }

  window.addEventListener('message', function (e) {
    const m = e.data;
    if (!m || m.mfd !== true) return;
    if (m.type === 'adi') { adi = m; render(); }
    else if (m.type === 'mapinfo') { mapinfo = m; render(); }
  });
  window.addEventListener('wptroutes:changed', render);

  render();
})();
