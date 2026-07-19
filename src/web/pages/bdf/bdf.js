// BDF page — a read-only reactive replica of the game's faction/HQ status panel, driven by the
// shell over postMessage (docs/bdf-page.md). No interaction, no commands — pure render of the
// 'bdf' block. See bdf.html for the message contract.
//
// This same script doubles as PAL (the enemy faction's panel) when the page is embedded with an
// `?enemy` URL flag — everything below is already driven purely by the incoming message, so the
// flag only has to pick which message type to listen for and swap the two hardcoded "BDF" strings
// (the title and the UNAVAILABLE label); the faction name/logo/counts are data, not identity.
const ENEMY    = new URLSearchParams(location.search).has('enemy');
const MSG_TYPE = ENEMY ? 'pal' : 'bdf';
if (ENEMY) {
  document.title = 'NO XMFD — PAL';
  document.getElementById('bdf-empty-title').textContent = 'PAL';
}

const factionEl  = document.getElementById('bdf-faction');
const logoEl     = document.getElementById('bdf-logo');
const warheadsEl = document.getElementById('bdf-warheads');
const scoreEl    = document.getElementById('bdf-score');
const fundsEl    = document.getElementById('bdf-funds');

// Section config: how each row's cells are built. `icon(name)` returns the sprite URL, or null for
// a text-only row (buildings/vehicles); `label` controls whether the type name renders under/over
// the count (aircraft is icon+count only, no name — matches the game's ungrouped aircraft grid).
const SECTIONS = {
  ships:     { grid: document.getElementById('grid-ships'),     total: null,
               icon: n => '/bdf-icon?type=' + encodeURIComponent(n), label: true },
  buildings: { grid: document.getElementById('grid-buildings'), total: document.getElementById('total-buildings'),
               icon: null, label: true },
  vehicles:  { grid: document.getElementById('grid-vehicles'),  total: document.getElementById('total-vehicles'),
               icon: null, label: true },
  aircraft:  { grid: document.getElementById('grid-aircraft'),  total: document.getElementById('total-aircraft'),
               icon: n => '/icon?type=' + encodeURIComponent(n), label: false },
};

let state = { present: false, faction: '', funds: 0, score: 0, warheads: 0,
              ships: [], buildings: [], vehicles: [], aircraft: [] };
// Cache of each row's built name signature, so we only rebuild DOM when the set of types changes —
// the per-frame work is just updating count text/colour (same pattern as TGT's builtKey).
const builtKey  = { ships: '', buildings: '', vehicles: '', aircraft: '' };
let loadedLogo  = '';   // faction name whose logo <img> src is currently set

function label(n) { return (n || '').replace(/_/g, ' '); }

// Builds one section's cell grid only when its set of type names changes.
function buildSection(key) {
  const cfg = SECTIONS[key];
  const list = state[key] || [];
  const sig = list.map(function (t) { return t.n; }).join('|');
  if (sig === builtKey[key]) return;
  builtKey[key] = sig;

  cfg.grid.innerHTML = '';
  list.forEach(function () {
    const cell = document.createElement('div');
    cell.className = 'bdf-cell';
    if (cfg.icon) {
      const img = document.createElement('img');
      img.className = 'bdf-icon';
      // The mod captures these sprites over the first few mission scans, so a request can 404 if
      // the page is opened early. Retry a handful of times and reveal once it lands (same pattern
      // as TGT's vehicle-icon loader) — otherwise an early open leaves it blank for the session.
      let tries = 0;
      img.addEventListener('error', function () {
        img.classList.remove('ready');
        if (++tries <= 6) setTimeout(function () { img.src = img.dataset.url + '&r=' + tries; }, 1200);
      });
      img.addEventListener('load', function () { img.classList.add('ready'); });
      cell.appendChild(img);
    }
    if (cfg.label) {
      const lbl = document.createElement('div');
      lbl.className = 'bdf-label';
      cell.appendChild(lbl);
    }
    const count = document.createElement('div');
    count.className = 'bdf-count';
    cell.appendChild(count);
    cfg.grid.appendChild(cell);
  });
}

function paintSection(key) {
  const cfg = SECTIONS[key];
  const list = state[key] || [];
  buildSection(key);
  const cells = cfg.grid.children;
  let total = 0;
  for (let i = 0; i < cells.length && i < list.length; i++) {
    const t = list[i], cell = cells[i];
    total += t.c || 0;
    cell.classList.toggle('zero', !t.c);
    if (cfg.icon) {
      const img = cell.querySelector('.bdf-icon');
      const url = cfg.icon(t.n);
      if (img.dataset.url !== url) { img.dataset.url = url; img.classList.remove('ready'); img.src = url; }
      img.alt = t.n;
    }
    if (cfg.label) cell.querySelector('.bdf-label').textContent = label(t.n);
    cell.querySelector('.bdf-count').textContent = t.c != null ? t.c : 0;
  }
  if (cfg.total) cfg.total.textContent = total;
}

// Mirrors UnitConverter.ValueReading's scale-by-magnitude format (docs/bdf-page.md — funds arrive
// in millions). Formatted with a period rather than the game's locale-dependent comma.
function fmtFunds(m) {
  if (typeof m !== 'number' || !isFinite(m)) return '$0';
  const raw = m * 1e6;
  if (raw * raw < 1e8) return '$' + Math.round(raw);
  if (m * m < 1) return '$' + (m * 1000).toFixed(1) + 'k';
  if (m * m < 100) return '$' + m.toFixed(2) + 'm';
  if (m * m < 1e6) return '$' + m.toFixed(1) + 'm';
  if (m * m < 1e12) return '$' + (m * 0.001).toFixed(2) + 'b';
  return '$' + (m * 1e-6).toFixed(3) + 't';
}

function paint() {
  document.body.classList.toggle('unavailable', !state.present);
  if (!state.present) return;

  factionEl.textContent = state.faction || '—';
  warheadsEl.textContent = state.warheads;
  scoreEl.textContent = (typeof state.score === 'number' ? state.score : 0).toFixed(1);
  fundsEl.textContent = fmtFunds(state.funds);
  fundsEl.classList.toggle('negative', state.funds < 0);

  if (state.faction && loadedLogo !== state.faction) {
    loadedLogo = state.faction;
    logoEl.classList.remove('ready');
    logoEl.onerror = function () { logoEl.classList.remove('ready'); };
    logoEl.onload  = function () { logoEl.classList.add('ready'); };
    logoEl.src = '/bdf-icon?type=' + encodeURIComponent(state.faction);
  }

  paintSection('ships'); paintSection('buildings'); paintSection('vehicles'); paintSection('aircraft');
}

window.addEventListener('message', function (e) {
  const m = e.data;
  if (!m || m.mfd !== true || m.type !== MSG_TYPE) return;
  state = {
    present:   !!m.present,
    faction:   m.faction || '',
    funds:     typeof m.funds === 'number' ? m.funds : 0,
    score:     typeof m.score === 'number' ? m.score : 0,
    warheads:  typeof m.warheads === 'number' ? m.warheads : 0,
    ships:     Array.isArray(m.ships) ? m.ships : [],
    buildings: Array.isArray(m.buildings) ? m.buildings : [],
    vehicles:  Array.isArray(m.vehicles) ? m.vehicles : [],
    aircraft:  Array.isArray(m.aircraft) ? m.aircraft : [],
  };
  paint();
});

paint();   // initial paint — UNAVAILABLE until the first frame arrives
