// TGT page — a reactive replica of the game's TARGET SELECTION panel, driven by the shell over
// postMessage and POSTing the tgt.* commands itself. State is telemetry-driven: a tap fires a
// command and the next 'tgt' frame (~100 ms) reflects the game's real toggle state, so the buttons
// never lie even if a tap is dropped. See tgt.html for the message contract + docs/tgt-page.md.
import { createPadCursor } from '/assets/services/pad-cursor.js';

const panel = document.getElementById('tgt-panel');
const rows = {
  faction:  document.getElementById('row-faction'),
  category: document.getElementById('row-category'),
  vehicle:  document.getElementById('row-vehicle'),
};
const modeEls = { laser: document.getElementById('mode-laser'), hud: document.getElementById('mode-hud') };
const listRows = document.getElementById('tgt-list-rows');
const datalinkBtn = document.getElementById('datalink-btn');
const staleBtn = document.getElementById('stale-btn');

let state = { present: false, laser: false, hud: false, faction: [], category: [], vehicle: [] };
let targets = [];        // selected-target list (from 'tgt-targets'): [{ id, n, g, r, f, dl }]
let targetsKey = '';     // id-set signature; rebuild rows only when it changes
// Next/Previous Target's row-stepper (docs/tgt-keybind-nav.md) — an index into `targets`, -1 when
// nothing's highlighted. Mutually exclusive with the PAD cursor: entering this mode hides the free
// crosshair, and moving the crosshair clears this back to -1 (see the 'cursor' message handler).
let highlightIndex = -1;
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
    b.className = 'tgt-cell pad-hoverable';
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
    cell.className = 'tgt-veh pad-hoverable'; cell.dataset.group = 'vehicle'; cell.dataset.index = i;
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

// ── Selected-target list ──────────────────────────────────────────────────────────────
// Range as "8,4 km" (European decimal comma); non-numbers pass through.
function fmtRng(r) {
  return (typeof r === 'number' && isFinite(r)) ? r.toFixed(1).replace('.', ',') + ' km' : '—';
}

function renderTargets() {
  const list = targets;
  // Rebuild the rows only when the set of target ids changes; otherwise just refresh the text
  // (name/grid/range drift as targets move) so we don't thrash the DOM at 10 Hz.
  const key = list.map(function (t) { return t.id; }).join(',');
  // A deselect elsewhere (crosshair, DATALINK/STALE, in-game) can drop the list out from under the
  // highlight — reclamp rather than leave it pointing past the end or lingering at -1's "none".
  if (highlightIndex >= list.length) highlightIndex = list.length - 1;
  if (key !== targetsKey) {
    targetsKey = key;
    listRows.innerHTML = '';
    list.forEach(function (t) {
      const row = document.createElement('div');
      row.className = 'tl-row pad-hoverable ' + (t.f === 1 ? 'f-friendly' : t.f === 0 ? 'f-neutral' : 'f-enemy');
      row.dataset.id = t.id;
      row.setAttribute('role', 'checkbox'); row.setAttribute('aria-checked', 'true');
      row.setAttribute('aria-label', 'deselect'); row.tabIndex = 0;
      const name = document.createElement('span'); name.className = 'tl-name';
      const src  = document.createElement('span'); src.className = 'tl-src';
      const dist = document.createElement('span'); dist.className = 'tl-dist';
      const grid = document.createElement('span'); grid.className = 'tl-grid';
      row.appendChild(name); row.appendChild(src); row.appendChild(dist); row.appendChild(grid);
      listRows.appendChild(row);
    });
  }
  const rowEls = listRows.children;
  for (let i = 0; i < rowEls.length && i < list.length; i++) {
    const t = list[i], el = rowEls[i];
    el.querySelector('.tl-name').textContent = t.n || '—';
    el.querySelector('.tl-grid').textContent = t.g != null ? String(t.g) : '—';
    el.querySelector('.tl-dist').textContent = fmtRng(t.r);
    el.classList.toggle('datalink', !!t.dl && !t.st);
    el.classList.toggle('stale', !!t.st);
    el.querySelector('.tl-src').textContent = t.st ? 'STALE' : t.dl ? 'DATALINK' : 'SENSOR';
    el.classList.toggle('nav-highlight', i === highlightIndex);
  }
}

// Tap anywhere on a row → deselect that target (the whole row is the target, not just the small
// checkbox — a bigger, easier touch/cursor hit area). The game drops it and the next 'tgt-targets'
// frame no longer carries it, so the row disappears — telemetry-driven, same as the filter toggles.
listRows.addEventListener('click', function (e) {
  const row = e.target.closest('.tl-row');
  const id = row && row.dataset.id;
  if (id) send('target.deselect', { id: Number(id) });
});

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

// DATALINK / STALE buttons (docs/tgt-datalink-cancel.md, docs/tgt-stale-lock.md): tap deselects the
// datalink-only / stale-locked targets — a bulk server-side deselect, no client-side filtering. Not
// folded into the .tgt-cell/.tgt-veh handling above, since these aren't game filter cells (no
// group/index, no tgt.set/tgt.only) — this is the real mouse/touch path; the PAD cursor mirrors the
// same tap below (padCursorSelectAt).
datalinkBtn.addEventListener('click', function () { send('tgt.clear-datalink'); });
staleBtn.addEventListener('click', function () { send('tgt.clear-stale'); });

// ── PAD cursor (docs/page-cursor.md) ──────────────────────────────────────────────────
// Same crosshair/transport MAP uses (pad-cursor.js), driven here only while this TGT is the SOI's
// focused surface. Clamped to the panel's own box (panel-local px, matching the crosshair's
// positioned ancestor — see tgt.css's .tgt-panel { position: relative }).
const CURSORABLE = '.tgt-cell, .tgt-veh, .tl-row, .tgt-action, .tgt-mode, .tgt-datalink-btn, .tgt-stale-btn';
const padCursorEl = document.getElementById('pad-cursor');
const cursor = createPadCursor({
  el: padCursorEl,
  clampRect: () => ({ dx: 0, dy: 0, dw: panel.clientWidth, dh: panel.clientHeight }),
  onSelect: padCursorSelectAt,
  onHold: padCursorHoldAt,
  onMove: padCursorMoveAt,
  holdMs: LONG_MS,
});

function elAt(px, py) {
  const rect = panel.getBoundingClientRect();
  const raw = document.elementFromPoint(rect.left + px, rect.top + py);
  return raw && raw.closest(CURSORABLE);
}

// Next/Previous Target (docs/tgt-keybind-nav.md): steps highlightIndex through `targets`, wrapping
// at both ends, and hides the free crosshair for the duration — the two selection modes are mutually
// exclusive, so entering this one puts the crosshair away rather than leaving both visible at once.
function navHighlight(dir) {
  if (!targets.length) return;
  highlightIndex = highlightIndex < 0 ? 0 : (highlightIndex + dir + targets.length) % targets.length;
  cursor.setHidden(true);
  renderTargets();
}

// Moving the crosshair (Cursor Up/Down/Left/Right or its axis) hands Select back to it — called from
// the 'cursor' message handler on an actual deflection, not the zero it reports on release.
function clearNavHighlight() {
  if (highlightIndex < 0) return;
  highlightIndex = -1;
  cursor.setHidden(false);
  renderTargets();
}

// Cursor Select's outcome while a row is highlighted: deselect it, same as tapping the row itself.
// highlightIndex is left as-is — the target drops from `targets` on the next telemetry frame and
// renderTargets()'s reclamp above settles it onto whatever slid into that slot, same as a deselect
// via any other path already does to the list itself.
function deselectHighlighted() {
  const t = targets[highlightIndex];
  if (t) send('target.deselect', { id: t.id });
}

// Select's TAP outcome (release before LONG_MS, or any control with no hold behaviour to mirror).
function padCursorSelectAt(px, py) {
  if (highlightIndex >= 0) { deselectHighlighted(); return; }
  const el = elAt(px, py);
  if (!el) return;
  if (el.classList.contains('tgt-cell') || el.classList.contains('tgt-veh')) {
    send('tgt.set', { group: el.dataset.group, index: +el.dataset.index, on: !isOn(el.dataset.group, +el.dataset.index) });
  } else if (el.classList.contains('tgt-datalink-btn')) {
    send('tgt.clear-datalink');   // mirrors datalinkBtn's own click outcome
  } else if (el.classList.contains('tgt-stale-btn')) {
    send('tgt.clear-stale');   // mirrors staleBtn's own click outcome
  } else {
    el.click();   // .tl-row / .tgt-action / .tgt-mode already have plain click handlers
  }
}

// Select's HOLD outcome — only filter cells (.tgt-cell/.tgt-veh) have a long-press meaning ("only
// this"); everything else, DATALINK included, has no hold behaviour, so holding over it is simply a
// no-op (same as holding the pointer down over a plain button already is today).
function padCursorHoldAt(px, py) {
  const el = elAt(px, py);
  if (!el) return;
  if (el.classList.contains('tgt-cell') || el.classList.contains('tgt-veh')) {
    send('tgt.only', { group: el.dataset.group, index: +el.dataset.index });
  }
}

// Hover feedback (docs/page-cursor.md #2): mark whatever's currently under the crosshair with the
// shared .pad-hover class (shared/theme.css), clearing it from whatever had it before.
let hoveredEl = null;
function padCursorMoveAt(px, py) {
  const el = px == null ? null : elAt(px, py);
  if (el === hoveredEl) return;
  if (hoveredEl) hoveredEl.classList.remove('pad-hover');
  hoveredEl = el;
  if (hoveredEl) hoveredEl.classList.add('pad-hover');
}

// Zoom In/Out (map-act's zoom-in/zoom-out) are repurposed here to scroll the target list — nothing
// on this page to zoom, and the binds already exist end-to-end (docs/page-cursor.md).
const SCROLL_STEP = 60;   // flat constant tuned by feel, like pad-cursor.js's own SPEED

// ── Shell → page ─────────────────────────────────────────────────────────────────────
window.addEventListener('message', function (e) {
  const m = e.data;
  if (!m || m.mfd !== true) return;
  if (m.type === 'tgt') {
    state = {
      present:  !!m.present,
      laser:    !!m.laser,
      hud:      !!m.hud,
      faction:  Array.isArray(m.faction)  ? m.faction  : [],
      category: Array.isArray(m.category) ? m.category : [],
      vehicle:  Array.isArray(m.vehicle)  ? m.vehicle  : [],
    };
    paint();
  } else if (m.type === 'tgt-targets') {
    targets = Array.isArray(m.items) ? m.items : [];
    renderTargets();
  } else if (m.action === 'cursor-focus') {
    cursor.setFocus(!!m.on, panel.clientWidth / 2, panel.clientHeight / 2);
  } else if (m.action === 'cursor') {
    if (m.x || m.y) clearNavHighlight();   // an actual deflection, not the (0,0) a key release reports
    cursor.setVector(m.x, m.y);
  } else if (m.action === 'cursor-held') {
    cursor.setSelectHeld(!!m.held);
  } else if (m.action === 'zoom-in') {
    listRows.scrollBy({ top: SCROLL_STEP });
  } else if (m.action === 'zoom-out') {
    listRows.scrollBy({ top: -SCROLL_STEP });
  } else if (m.action === 'tgt-next') {
    navHighlight(1);
  } else if (m.action === 'tgt-prev') {
    navHighlight(-1);
  } else if (m.action === 'tgt-datalink') {
    send('tgt.clear-datalink');
  } else if (m.action === 'tgt-stale') {
    send('tgt.clear-stale');
  }
});

paint();          // initial paint — UNAVAILABLE until the first frame arrives
renderTargets();  // initial empty list
