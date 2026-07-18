// HUD page — a clickable replica of the game's HUD OPTIONS screen (HUDOptions). Fetch-driven, not
// telemetry-pushed: it GETs the current state from /hud-options and POSTs hud.* commands back. See
// hud.html for the control contract and docs/hud-page.md for the model.

// The seven categories in listCategories order. The game exposes no per-category display name, and
// this order is fixed in its inspector, so the labels live here (docs/hud-page.md). The count from
// /hud-options is checked against this so a game-side reorder surfaces rather than mislabels.
const CATEGORY_LABELS = ['FRIENDLY', 'ENEMY', 'AIRCRAFT', 'MISSILES', 'VEHICLES', 'BUILDINGS', 'SHIPS'];
// Which category rows own a sub-type chip grid, and the hud.set group that drives it.
const SUBTYPE_GROUP = { 4: 'vehicle', 5: 'building' };

const modesEl = document.getElementById('hud-modes');
const catsEl  = document.getElementById('hud-cats');
const emptyEl = document.getElementById('hud-empty');

let data = null;          // last /hud-options snapshot
let resyncTimer = null;   // pending reconcile fetch after a write

// The game builds the toggles from the Encyclopedia with underscored names (IR_SAM); the in-game
// screen shows them spaced. Match that.
function pretty(name) { return String(name).replace(/_/g, ' '); }

function load() {
  fetch('/hud-options', { cache: 'no-store' })
    .then(function (r) { return r.ok ? r.json() : {}; })
    .then(render)
    .catch(function () { render({}); });
}

// A write applied in game shows up in /hud-options only on the plugin's next 1 Hz refresh, so a
// reconcile fetch waits for it. Optimistic local flips (below) keep the UI instant meanwhile; this
// catches drift and the many toggles a mode press rewrites.
function resyncSoon() {
  if (resyncTimer) clearTimeout(resyncTimer);
  resyncTimer = setTimeout(load, 1200);
}

function send(cmd, body) {
  sendCommand(cmd, body).catch(function () {});
  resyncSoon();
}

function render(d) {
  data = d;
  const has = d && Array.isArray(d.categories) && d.categories.length > 0;
  emptyEl.style.display = has ? 'none' : '';
  modesEl.style.display = has ? '' : 'none';
  catsEl.style.display  = has ? '' : 'none';
  if (!has) return;
  renderModes();
  renderCats();
}

function renderModes() {
  modesEl.textContent = '';
  (data.modes || []).forEach(function (name, i) {
    const b = document.createElement('button');
    b.className = 'hud-mode' + (i === data.mode ? ' on' : '');
    b.textContent = name;
    b.addEventListener('click', function () {
      if (i === data.mode) return;
      data.mode = i;                        // optimistic; the resync brings the real preset back
      renderModes();
      send('hud.mode', { index: i });
    });
    modesEl.appendChild(b);
  });
}

function renderCats() {
  catsEl.textContent = '';
  // Label count vs. what the game sent — a mismatch means the category order drifted; show it.
  if (data.categories.length !== CATEGORY_LABELS.length) {
    console.warn('[hud] ' + data.categories.length + ' categories from the game, ' +
                 CATEGORY_LABELS.length + ' labels here — labels may be misaligned (docs/hud-page.md).');
  }
  data.categories.forEach(function (on, i) {
    catsEl.appendChild(catRow(CATEGORY_LABELS[i] || ('CAT ' + i), on, i));
    const group = SUBTYPE_GROUP[i];
    if (group) catsEl.appendChild(subGrid(group, data[group === 'vehicle' ? 'vehicles' : 'buildings'] || []));
  });
}

// One category row: name on the left, a MAXIMIZE toggle on the right.
function catRow(label, on, index) {
  const row = document.createElement('div');
  row.className = 'hud-cat';
  const name = document.createElement('span');
  name.className = 'hud-cat-name';
  name.textContent = label;
  const btn = document.createElement('button');
  btn.className = 'hud-max' + (on ? ' on' : '');
  btn.textContent = 'MAXIMIZE';
  btn.addEventListener('click', function () {
    const next = !btn.classList.contains('on');
    btn.classList.toggle('on', next);       // optimistic
    data.categories[index] = next;
    send('hud.set', { group: 'category', index: index, on: next });
  });
  row.appendChild(name);
  row.appendChild(btn);
  return row;
}

// The chip grid under VEHICLES / BUILDINGS — one toggle per unit type.
function subGrid(group, items) {
  const grid = document.createElement('div');
  grid.className = 'hud-subs';
  items.forEach(function (it, i) {
    const chip = document.createElement('button');
    chip.className = 'hud-sub' + (it.on ? ' on' : '');
    chip.textContent = pretty(it.n);
    chip.addEventListener('click', function () {
      const next = !chip.classList.contains('on');
      chip.classList.toggle('on', next);    // optimistic
      it.on = next;
      send('hud.set', { group: group, index: i, on: next });
    });
    grid.appendChild(chip);
  });
  return grid;
}

load();
