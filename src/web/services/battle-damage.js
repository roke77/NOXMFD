// Battle damage (issue #110, docs/battle-damage.md): which page goes offline for the plugin's
// per-system status push ({on, radar, tgp, laser}, each 'ok' | 'fail' | 'none'; 'none' = not
// fitted, which never fails). Every page not listed here never fails. HSD needs no entry: the
// game itself stops reporting own-radar contacts once the radar is destroyed.
const PAGE_SYSTEM = { rdr: 'radar', tgp: 'tgp' };
const BANNER = { radar: 'RADAR FAIL', tgp: 'TGP FAIL' };

// The lost system behind `page`, or null while it works.
export function failedSystem(page, st) {
  const sys = PAGE_SYSTEM[page];
  return sys && st && st.on && st[sys] === 'fail' ? sys : null;
}

// TGP's partial case: the feed keeps running, its laser readouts don't.
export function laserFailed(st) {
  return !!st && !!st.on && st.laser === 'fail' && st.tgp !== 'fail';
}

// The FAIL screen over a page document: opaque, so it blanks the content and takes its clicks,
// with the `<SYSTEM> FAIL` banner. A loss seen while the page is up runs the one-shot flicker
// (theme.css .bd-fail); a page that opens already failed settles at once. Returns update(status).
export function createFailScreen(doc, page) {
  const el = doc.createElement('div');
  el.className = 'bd-fail';
  el.hidden = true;
  doc.body.appendChild(el);
  let seen = false, shown = null;
  return function update(st) {
    const sys = failedSystem(page, st);
    if (sys !== shown) {
      shown = sys;
      el.hidden = !sys;
      el.textContent = sys ? BANNER[sys] : '';
      el.classList.toggle('bd-flicker', !!sys && seen);
    }
    seen = true;
  };
}
