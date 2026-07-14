// TGT page — a reactive replica of the game's TARGET SELECTION panel, driven by the shell over
// postMessage and POSTing the tgt.* commands itself. State is telemetry-driven: a tap fires a
// command and the next 'tgt' frame (~100 ms) reflects the game's real toggle state, so the buttons
// never lie even if a tap is dropped. See tgt.html for the message contract + docs/tgt-page.md.

const panel = document.getElementById('tgt-panel');
const rows = {
  faction:  document.getElementById('row-faction'),
  category: document.getElementById('row-category'),
  vehicle:  document.getElementById('row-vehicle'),
};
const modeEls = { laser: document.getElementById('mode-laser'), hud: document.getElementById('mode-hud') };

let state = { present: false, laser: false, hud: false, faction: [], category: [], vehicle: [] };
// Cache of the built row signatures (names) so we only rebuild DOM when the set of toggles changes,
// not on every 10 Hz frame — the per-frame work is just flipping the .on class.
const builtKey = { faction: '', category: '', vehicle: '' };

function label(n) { return (n || '').replace(/_/g, ' '); }

function send(cmd, args) {
  if (typeof sendCommand === 'function') sendCommand(cmd, args).catch(function () {});
}

function isOn(group, index) {
  const list = state[group] || [];
  return !!(list[index] && list[index].on);
}

// Build a text-toggle row (faction / category) only when its names change.
function buildRow(group) {
  const list = state[group] || [];
  const key = list.map(function (t) { return t.n; }).join('|');
  if (key === builtKey[group]) return;
  builtKey[group] = key;
  const row = rows[group];
  row.innerHTML = '';
  list.forEach(function (t, i) {
    const b = document.createElement('div');
    b.className = 'tgt-cell';
    b.dataset.group = group; b.dataset.index = i;
    b.textContent = label(t.n);
    row.appendChild(b);
  });
}

// Build the vehicle-type grid (icon over label) only when its names change. Icons come from the
// mod's /tgt-icon capture; if one isn't captured yet the label still carries the meaning.
function buildVehicles() {
  const list = state.vehicle || [];
  const key = list.map(function (t) { return t.n; }).join('|');
  if (key === builtKey.vehicle) return;
  builtKey.vehicle = key;
  const row = rows.vehicle;
  row.innerHTML = '';
  list.forEach(function (t, i) {
    const cell = document.createElement('div');
    cell.className = 'tgt-veh'; cell.dataset.group = 'vehicle'; cell.dataset.index = i;
    const img = document.createElement('img');
    img.className = 'veh-icon'; img.alt = t.n;
    const iconUrl = '/tgt-icon?type=' + encodeURIComponent(t.n);
    // The mod captures these sprites over the first few mission scans, so a request can 404 if the
    // page is opened early. Retry a handful of times (hide meanwhile; the label carries it) and show
    // once it lands — otherwise an early open would leave the icon hidden for the whole session.
    let tries = 0;
    img.addEventListener('error', function () {
      img.style.visibility = 'hidden';
      if (++tries <= 6) setTimeout(function () { img.src = iconUrl + '&r=' + tries; }, 1200);
    });
    img.addEventListener('load', function () { img.style.visibility = ''; });
    img.src = iconUrl;
    const lbl = document.createElement('div');
    lbl.className = 'veh-label'; lbl.textContent = label(t.n);
    cell.appendChild(img); cell.appendChild(lbl);
    row.appendChild(cell);
  });
}

function paint() {
  document.body.classList.toggle('unavailable', !state.present);
  if (!state.present) return;
  buildRow('faction'); buildRow('category'); buildVehicles();
  ['faction', 'category', 'vehicle'].forEach(function (group) {
    const cells = rows[group].children;
    const list = state[group] || [];
    for (let i = 0; i < cells.length && i < list.length; i++)
      cells[i].classList.toggle('on', !!list[i].on);
  });
  modeEls.laser.classList.toggle('on', !!state.laser);
  modeEls.hud.classList.toggle('on', !!state.hud);
}

// ── Interaction: tap = toggle, long-press = "only this" (filter cells only) ───────────
const LONG_MS = 500;
let press = null;   // { group, index, longFired, timer }

function clearPress() { if (press) { clearTimeout(press.timer); press = null; } }

panel.addEventListener('pointerdown', function (e) {
  const cell = e.target.closest('.tgt-cell, .tgt-veh');
  if (!cell) return;
  press = { group: cell.dataset.group, index: +cell.dataset.index, longFired: false };
  press.timer = setTimeout(function () {
    if (!press) return;
    press.longFired = true;
    send('tgt.only', { group: press.group, index: press.index });   // isolate this one in its group
  }, LONG_MS);
});

panel.addEventListener('pointerup', function (e) {
  if (!press) return;
  const cell = e.target.closest('.tgt-cell, .tgt-veh');
  // Fire the tap only if released on the same cell and the long-press hasn't already fired.
  if (cell && cell.dataset.group === press.group && +cell.dataset.index === press.index && !press.longFired) {
    send('tgt.set', { group: press.group, index: press.index, on: !isOn(press.group, press.index) });
  }
  clearPress();
});

panel.addEventListener('pointercancel', clearPress);
panel.addEventListener('pointerleave', clearPress);
window.addEventListener('contextmenu', function (e) { e.preventDefault(); });   // long-press must not pop a menu

// Action buttons + mode toggles — plain taps (no long-press).
document.querySelectorAll('.tgt-action').forEach(function (b) {
  b.addEventListener('click', function () { send(b.dataset.cmd === 'reset' ? 'tgt.reset' : 'tgt.clear'); });
});
modeEls.laser.addEventListener('click', function () { send('tgt.laser', { on: !state.laser }); });
modeEls.hud.addEventListener('click', function () { send('tgt.hud', { on: !state.hud }); });

// ── Shell → page ─────────────────────────────────────────────────────────────────────
window.addEventListener('message', function (e) {
  const m = e.data;
  if (!m || m.mfd !== true || m.type !== 'tgt') return;
  state = {
    present:  !!m.present,
    laser:    !!m.laser,
    hud:      !!m.hud,
    faction:  Array.isArray(m.faction)  ? m.faction  : [],
    category: Array.isArray(m.category) ? m.category : [],
    vehicle:  Array.isArray(m.vehicle)  ? m.vehicle  : [],
  };
  paint();
});

paint();   // initial paint — UNAVAILABLE until the first frame arrives
