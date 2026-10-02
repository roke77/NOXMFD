// TGT page — a reactive replica of the game's TARGET SELECTION panel, driven by the shell over
// postMessage and POSTing the tgt.* commands itself. State is telemetry-driven: a tap fires a
// command and the next 'tgt' frame (~100 ms) reflects the game's real toggle state, so the buttons
// never lie even if a tap is dropped. See tgt.html for the message contract + docs/tgt-page.md.
import { createPadCursor } from '/assets/services/pad-cursor.js';
import { fmtRng } from '/assets/services/range-format.js';
import { createPresetCards } from '/assets/services/preset-cards.js';

const panel = document.getElementById('tgt-panel');
const rows = {
  faction:  document.getElementById('row-faction'),
  category: document.getElementById('row-category'),
  vehicle:  document.getElementById('row-vehicle'),
};
const modeEls = { laser: document.getElementById('mode-laser'), hud: document.getElementById('mode-hud') };
const listRows = document.getElementById('tgt-list-rows');
const countNEl = document.getElementById('tgt-count-n');
const listScroll = document.querySelector('.tgt-list-scroll');
const datalinkCountEl = document.getElementById('datalink-count');
const staleCountEl = document.getElementById('stale-count');
const densityToggleEl = document.getElementById('tgt-density-toggle');

let state = { present: false, laser: false, hud: false, faction: [], category: [], vehicle: [], preset: { index: 1, name: '' } };
let targets = [];        // selected-target list (from 'tgt-targets'): [{ id, n, g, r, f, dl, hd, sp, al, h }]
let targetsKey = '';     // id-set signature; rebuild rows only when it changes
let targetsMetric = false;   // player's Metric/Imperial preference, carried on 'tgt-targets' too
// Shared column sort (TargetSort.cs) — owned by the plugin, so the rows already arrive in this order
// and Next/Previous steps through them in it; the page only draws the ▲/▼ and sends taps.
let sortKey = '', sortDir = 1;
const sortBtns = document.querySelectorAll('.tl-sort');
const SORT_NAMES = { n: 'name', src: 'source', r: 'range' };

// ── DETAILED/COMPACT flight-data toggle (issue #88) ────────────────────────────────────
// Purely a client-local display preference, same as AKF's identical toggle — nothing here reaches
// the shell, and it isn't persisted. Opposite default from AKF though: TGT starts COMPACT.
let compact = true;
densityToggleEl.classList.toggle('compact', compact);
panel.classList.toggle('has-flight-col', !compact);
densityToggleEl.addEventListener('click', function () {
  compact = !compact;
  densityToggleEl.classList.toggle('compact', compact);
  panel.classList.toggle('has-flight-col', !compact);
});

// Squad state (issue #47): the leader-only TD column needs the squad role, and the designation dock
// below needs the leader's callsign plus TdStore's pending/accepted lists — nothing else here tracks
// either. Rides the shell's relayed 'sqd-state'/'td-state-push' pushes
// (docs/sse-push-refactor.md) instead of its own poll — one bootstrap GET each on load for the
// brief gap before the first push (and for standalone/preview contexts with no shell), then just
// message listeners. assignments maps target id (string) -> [SteamID, ...] (issue #107: squad members
// are keyed by SteamID, not by a member number).
let squadRole = 'none';
let squadState = null;
let tdAssignments = {};
function applySquad(s) {
  squadState = (s && s.state) || null;
  squadRole = (squadState && squadState.role) || 'none';
  panel.classList.toggle('has-td-col', squadRole === 'leader');
  renderDock();
}
// A squad member's callsign ("VIPER 1-2", state.pilots), else their Steam name.
function memberLabel(id) {
  if (!squadState) return String(id);
  const p = (squadState.pilots || {})[id];
  if (p) return p.d;
  if (id === squadState.self) return squadState.selfName || 'YOU';
  const m = (squadState.members || []).find(function (x) { return x.id === id; });
  return (m && m.name) || (id === squadState.leaderId && squadState.leaderName) || String(id);
}
// Squad order for the TD column: the leader first, then members as they joined.
function squadOrder(id) {
  if (!squadState) return 0;
  if (id === squadState.self) return -1;
  return (squadState.members || []).findIndex(function (x) { return x.id === id; });
}
function applyTdState(s) {
  const st = (s && s.state) || {};
  tdAssignments = st.assignments || {};
  tdDesignated = st.designated || [];
  tdAccepted = new Set(st.accepted || []);
  renderDock();
  renderTargets();
}

// ── Squad designation dock (docs/target-designator.md) ─────────────────────────────────
// The leader's DESIGNATE lands here as TdStore's pending `designated` list; the dock floats over
// the bottom of the target list until ADD / REPLACE / DISMISS answers it (the plugin then clears
// `designated`, and the next td-state push hides it). `accepted` holds the ids that came in through
// an answered designation, so their rows carry a TD tag. The drawer's open/closed state is local.
let tdDesignated = [];
let tdAccepted = new Set();
let dockOpen = false;
const dockEl = document.getElementById('tgt-td-dock');
const dockDrawer = document.getElementById('tgt-td-drawer');
const dockToggle = document.getElementById('tgt-td-toggle');
const dockHead = document.getElementById('tgt-td-label');
const dockShow = document.getElementById('tgt-td-show');
const dockAdd = document.getElementById('tgt-td-add');
function renderDock() {
  const pending = tdDesignated.length > 0;
  dockEl.hidden = !pending;
  panel.classList.toggle('has-td-dock', pending);
  if (!pending) { dockOpen = false; return; }
  const listed = new Set(targets.map(function (t) { return t.id; }));
  const fresh = tdDesignated.filter(function (t) { return !listed.has(t.id); }).length;
  const leader = squadState ? (memberLabel(squadState.leaderId) || 'LEADER') : 'LEADER';
  dockHead.textContent = leader + ' DESIGNATED ' + tdDesignated.length;
  dockShow.textContent = dockOpen ? 'HIDE ▾' : 'SHOW ▴';
  dockToggle.setAttribute('aria-expanded', dockOpen ? 'true' : 'false');
  dockAdd.textContent = 'ADD ' + fresh;
  dockAdd.setAttribute('aria-label', 'Add ' + fresh + ' new target' + (fresh === 1 ? '' : 's') + ' to yours');
  dockDrawer.hidden = !dockOpen;
  if (!dockOpen) return;
  dockDrawer.innerHTML = '';
  tdDesignated.forEach(function (t) {
    const item = document.createElement('div');
    item.className = 'tgt-td-item ' + factionClass(t.f) + (listed.has(t.id) ? ' listed' : '');
    const n = document.createElement('span'); n.className = 'n'; n.textContent = t.n || '—';
    if (listed.has(t.id)) {
      const tag = document.createElement('span'); tag.className = 'tgt-td-listed'; tag.textContent = 'LISTED';
      n.appendChild(tag);
    }
    const r = document.createElement('span'); r.className = 'r'; r.textContent = fmtRng(t.r, targetsMetric);
    const g = document.createElement('span'); g.className = 'g'; g.textContent = t.g != null ? String(t.g) : '—';
    item.appendChild(n); item.appendChild(r); item.appendChild(g);
    dockDrawer.appendChild(item);
  });
}
dockToggle.addEventListener('click', function () { dockOpen = !dockOpen; renderDock(); });
dockAdd.addEventListener('click', function () { send('td.accept', { on: false }); });
document.getElementById('tgt-td-replace').addEventListener('click', function () { send('td.accept', { on: true }); });
document.getElementById('tgt-td-dismiss').addEventListener('click', function () { send('td.dismiss'); });
fetch('/squad').then(function (r) { return r.ok ? r.json() : null; }).then(applySquad).catch(function () {});
fetch('/td-state').then(function (r) { return r.ok ? r.json() : null; }).then(applyTdState).catch(function () {});
window.addEventListener('message', function (e) {
  const m = e.data;
  if (!m || m.mfd !== true) return;
  if (m.type === 'sqd-state') applySquad(m.data);
  else if (m.type === 'td-state-push') applyTdState(m.data);
});
// The single locked target Next/Previous currently focuses, shared with FCR/HSD (issue #62,
// docs/tgt-cycle-focus.md) — every row here is already locked, so this is a plain id match, unlike
// FCR/HSD which also carry unlocked contacts. 0 = none focused.
let focusedTargetId = 0;
// Which control Cursor Select acts on right now (docs/tgt-cycle-focus.md's "Select arbitration"):
// true = the free crosshair (hit-test whatever it's over); false = the focused lock (deselect it
// directly, no aiming needed). Next/Previous Target switches to false and hides the crosshair;
// moving the crosshair (an actual deflection) switches back to true and shows it again. Starts
// true so a page that's never touched Next/Prev this session behaves like a plain PAD-cursor page.
let crosshairActive = true;
// Cache of the built row signatures (names) so we only rebuild DOM when the set of toggles changes,
// not on every 10 Hz frame — the per-frame work is just flipping the .on class.
const builtKey = { faction: '', category: '', vehicle: '' };

function label(n) { return (n || '').replace(/_/g, ' '); }
function factionClass(f) { return f === 1 ? 'f-friendly' : f === 0 ? 'f-neutral' : 'f-enemy'; }

function send(cmd, args) {
  if (typeof sendCommand !== 'function') return Promise.resolve();
  return sendCommand(cmd, args).catch(function () {});
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
    b.className = 'lit tgt-cell pad-hoverable';
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
  presetUi.sync(state.preset);
}

// ── TGT filter presets (issue #78) ────────────────────────────────────────────────────
// The five cards and their SAVE PRESET dialog live in preset-cards.js; this page only routes taps and
// holds to them (below) and reports the current slot from each 'tgt' frame (paint).
const presetUi = createPresetCards({
  cardsEl: document.getElementById('preset-cards'),
  endpoint: '/tgt-presets',
  cmdPrefix: 'tgt-preset',
  dialog: {
    scrim: document.getElementById('preset-kp'),
    title: document.getElementById('preset-kp-title'),
    input: document.getElementById('preset-kp-input'),
    error: document.getElementById('preset-kp-error'),
    clear: document.getElementById('preset-kp-clear'),
    save: document.getElementById('preset-kp-save'),
    cancel: document.getElementById('preset-kp-cancel'),
  },
  send: send,
});

// ── Selected-target list ──────────────────────────────────────────────────────────────

// "M:SS" — mirrors HudTtiMath.FormatTti (src/plugin/Hud/HudTtiMath.cs) so the web readout matches
// the native HUD cue's own format exactly.
function fmtTti(seconds) {
  const total = Math.round(seconds);
  return Math.floor(total / 60) + ':' + String(total % 60).padStart(2, '0');
}

// Same rounding/normalization MAP's own hover tooltip uses for a contact's heading (map.js).
function fmtHdg(deg) {
  return Math.round(((deg % 360) + 360) % 360) + '°';
}

function renderSort() {
  sortBtns.forEach(function (b) {
    const active = b.dataset.sort === sortKey;
    if (active) b.dataset.dir = sortDir; else delete b.dataset.dir;
    b.setAttribute('aria-label', 'Sort by ' + SORT_NAMES[b.dataset.sort] +
      (active ? (sortDir > 0 ? ', ascending' : ', descending') : ''));
  });
}

// Tap = sort by this column (again = reverse); long-press (below) = back to lock order.
function tapSort(key) {
  send('tgt.sort', { key: key, index: key === sortKey ? -sortDir : 1 });
}
function clearSort() { send('tgt.sort', { key: '', index: 1 }); }

function renderTargets() {
  const list = targets;
  countNEl.textContent = list.length ? '(' + list.length + ')' : '';
  // What CLEAR DATALINK / CLEAR STALE would drop — STALE implies DATALINK, so the datalink count
  // includes the stale rows, same as the plugin's bulk deselect.
  const datalinkN = list.filter(function (t) { return t.dl; }).length;
  const staleN = list.filter(function (t) { return t.st; }).length;
  datalinkCountEl.textContent = datalinkN ? '· ' + datalinkN : '';
  staleCountEl.textContent = staleN ? '· ' + staleN : '';
  // Rebuild the rows only when the set of target ids changes; otherwise just refresh the text
  // (name/grid/range drift as targets move) so we don't thrash the DOM at 10 Hz.
  const key = list.map(function (t) { return t.id; }).join(',');
  if (key !== targetsKey) {
    targetsKey = key;
    listRows.innerHTML = '';
    list.forEach(function (t) {
      const row = document.createElement('div');
      row.className = 'tl-row pad-hoverable ' + factionClass(t.f);
      row.dataset.id = t.id;
      row.setAttribute('role', 'checkbox'); row.setAttribute('aria-checked', 'true');
      row.setAttribute('aria-label', 'deselect'); row.tabIndex = 0;
      // NAME cell holds the name and, after it, the TD tag — see .tl-name's flex layout in tgt.css.
      const name = document.createElement('span'); name.className = 'tl-name';
      const nameText = document.createElement('span'); nameText.className = 'tl-name-text';
      const tdTag = document.createElement('span'); tdTag.className = 'tl-td-tag';
      name.appendChild(nameText); name.appendChild(tdTag);
      const wpn  = document.createElement('span'); wpn.className = 'tl-wpn';
      const tti  = document.createElement('span'); tti.className = 'tl-tti';
      const td   = document.createElement('span'); td.className = 'tl-td';
      const src  = document.createElement('span'); src.className = 'tl-src';
      const dist = document.createElement('span'); dist.className = 'tl-dist';
      const grid = document.createElement('span'); grid.className = 'tl-grid';
      const spd  = document.createElement('span'); spd.className = 'tl-spd';
      const alt  = document.createElement('span'); alt.className = 'tl-alt';
      const hdg  = document.createElement('span'); hdg.className = 'tl-hdg';
      row.appendChild(name); row.appendChild(wpn); row.appendChild(tti); row.appendChild(td); row.appendChild(src); row.appendChild(dist); row.appendChild(grid);
      row.appendChild(spd); row.appendChild(alt); row.appendChild(hdg);
      listRows.appendChild(row);
    });
  }
  const rowEls = listRows.children;
  for (let i = 0; i < rowEls.length && i < list.length; i++) {
    const t = list[i], el = rowEls[i];
    el.querySelector('.tl-name-text').textContent = t.n || '—';
    // WPN / TTI (docs/hud-tti-estimate.md): only when this lock actually has one of the player's own
    // in-flight guided weapons tracking it (telemetry-source.js only sets t.tti/t.wpn in that case).
    el.querySelector('.tl-wpn').textContent = typeof t.tti === 'number' && t.wpn ? t.wpn : '';
    el.querySelector('.tl-tti').textContent = typeof t.tti === 'number' ? fmtTti(t.tti) : '';
    el.querySelector('.tl-td-tag').textContent = tdAccepted.has(t.id) ? 'TD' : '';
    el.querySelector('.tl-grid').textContent = t.g != null ? String(t.g) : '—';
    el.querySelector('.tl-dist').textContent = fmtRng(t.r, targetsMetric);
    // TD column (issue #47 follow-up) — blank when this target isn't currently assigned to anyone;
    // the column itself is hidden entirely for a non-leader (see .has-td-col in tgt.css), so an
    // empty cell here never shows for someone with no leader-side assignments to display anyway.
    const assigned = tdAssignments[String(t.id)] || [];
    const tdCell = el.querySelector('.tl-td');
    const tdText = assigned.slice().sort(function (a, b) { return squadOrder(a) - squadOrder(b); }).map(memberLabel).join(', ');
    tdCell.textContent = tdText;
    tdCell.title = tdText;
    // SPD/ALT/HDG (issue #88, only rendered while .has-flight-col is on): "—" when this target has
    // no HasDetail — a stale lock, or a non-aircraft/missile category — same gate/placeholder MAP's
    // hover tooltip already uses for the identical data.
    el.querySelector('.tl-spd').textContent = t.hd && t.sp ? t.sp : '—';
    el.querySelector('.tl-alt').textContent = t.hd && t.al ? t.al : '—';
    el.querySelector('.tl-hdg').textContent = t.hd && typeof t.h === 'number' ? fmtHdg(t.h) : '—';
    el.classList.toggle('datalink', !!t.dl && !t.st);
    el.classList.toggle('stale', !!t.st);
    el.querySelector('.tl-src').textContent = t.st ? 'STALE' : t.dl ? 'DATALINK' : 'SENSOR';
    const isFocused = focusedTargetId !== 0 && t.id === focusedTargetId;
    // Amber while Select would act on this lock (crosshair inactive); grey once the crosshair has
    // taken over Select, so it's clear at a glance that pressing Select now won't deselect it.
    el.classList.toggle('tgt-focused', isFocused && !crosshairActive);
    el.classList.toggle('tgt-focused-inactive', isFocused && crosshairActive);
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

// ── Interaction: tap = toggle, long-press = "only this" (filter cells) ────────────────
// Sort headers share the same tap/long-press split: tap = sort, long-press = lock order. Preset
// cards too: tap = recall, long-press = save.
const LONG_MS = 500;
let press = null;   // { group, index, sort, slot, longFired, timer }

const PRESSABLE = '.tgt-cell, .tgt-veh, .tl-sort, .preset-card';

function clearPress() { if (press) { clearTimeout(press.timer); press = null; } }

panel.addEventListener('pointerdown', function (e) {
  const cell = e.target.closest(PRESSABLE);
  if (!cell) return;
  press = { group: cell.dataset.group, index: +cell.dataset.index, sort: cell.dataset.sort, slot: +cell.dataset.slot, longFired: false };
  press.timer = setTimeout(function () {
    if (!press) return;
    press.longFired = true;
    if (press.slot) presetUi.save(press.slot);
    else if (press.sort) clearSort();
    else send('tgt.only', { group: press.group, index: press.index });   // isolate this one in its group
  }, LONG_MS);
});

panel.addEventListener('pointerup', function (e) {
  if (!press) return;
  const cell = e.target.closest(PRESSABLE);
  if (press.slot) {
    // A completed hold already opened the dialog and must not also recall. Its release re-focuses
    // the name entry: only a focus() inside a user gesture raises a touch keyboard.
    if (press.longFired) presetUi.focusEntry();
    else if (cell && +cell.dataset.slot === press.slot) presetUi.recall(press.slot);
  } else if (press.sort) {
    if (cell && cell.dataset.sort === press.sort && !press.longFired) tapSort(press.sort);
  } else if (cell && cell.dataset.group === press.group && +cell.dataset.index === press.index && !press.longFired) {
    // Fire the tap only if released on the same cell and the long-press hasn't already fired.
    send('tgt.set', { group: press.group, index: press.index, on: !isOn(press.group, press.index) });
  }
  clearPress();
});

// Keyboard activation of a sort header or preset card (Enter/Space) — pointer taps are handled above,
// and a click from those carries detail >= 1.
panel.addEventListener('click', function (e) {
  if (e.detail !== 0) return;
  const b = e.target.closest('.tl-sort');
  if (b) { tapSort(b.dataset.sort); return; }
  const card = e.target.closest('.preset-card');
  if (card) presetUi.recall(+card.dataset.slot);
});

panel.addEventListener('pointercancel', clearPress);
panel.addEventListener('pointerleave', clearPress);
window.addEventListener('contextmenu', function (e) { e.preventDefault(); });   // long-press must not pop a menu

// Action buttons + mode toggles — plain taps (no long-press). Each action's data-cmd is its tgt.*
// command: reset, clear, clear-datalink, clear-stale. CLEAR DATALINK / CLEAR STALE
// (docs/tgt-datalink-cancel.md, docs/tgt-stale-lock.md) are bulk server-side deselects, no
// client-side filtering; the PAD cursor mirrors the same tap by clicking the button.
document.querySelectorAll('.tgt-action').forEach(function (b) {
  b.addEventListener('click', function () { send('tgt.' + b.dataset.cmd); });
});
modeEls.laser.addEventListener('click', function () { send('tgt.laser', { on: !state.laser }); });
modeEls.hud.addEventListener('click', function () { send('tgt.hud', { on: !state.hud }); });

// ── PAD cursor (docs/page-cursor.md) ──────────────────────────────────────────────────
// Same crosshair/transport MAP uses (pad-cursor.js), driven here only while this TGT is the SOI's
// focused surface. Clamped to the panel's own box (panel-local px, matching the crosshair's
// positioned ancestor — see tgt.css's .tgt-panel { position: relative }).
const CURSORABLE = '.tgt-cell, .tgt-veh, .tl-sort, .tl-row, .tgt-action, .tgt-mode, .preset-card, .preset-kp-btn, .tgt-density-toggle, .tgt-td-toggle, .tgt-td-btn';
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

// Cursor Select's outcome while the crosshair is inactive (docs/tgt-cycle-focus.md): deselect the
// focused lock, same as tapping its row. TargetFocus.Id (focusedTargetId) is the only "which row is
// current" state TGT keeps, so this reads straight off it rather than an index tgt.js would have to
// keep in sync by hand.
function deselectFocused() {
  const t = targets.find(function (x) { return x.id === focusedTargetId; });
  if (t) send('target.deselect', { id: t.id });
}

// Select's TAP outcome (release before LONG_MS, or any control with no hold behaviour to mirror).
function padCursorSelectAt(px, py) {
  if (!crosshairActive && focusedTargetId !== 0) { deselectFocused(); return; }
  const el = elAt(px, py);
  if (!el) return;
  if (el.classList.contains('tgt-cell') || el.classList.contains('tgt-veh')) {
    send('tgt.set', { group: el.dataset.group, index: +el.dataset.index, on: !isOn(el.dataset.group, +el.dataset.index) });
  } else if (el.classList.contains('preset-card')) {
    presetUi.recall(+el.dataset.slot);   // mirrors the card's own pointer tap
  } else if (el.classList.contains('tl-sort')) {
    tapSort(el.dataset.sort);   // mirrors the header's own pointer tap
  } else {
    el.click();   // .tl-row / .tgt-action / .tgt-mode / the density toggle already have plain click handlers
  }
}

// Select's HOLD outcome — filter cells (.tgt-cell/.tgt-veh: "only this"), sort headers (lock order)
// and preset cards (save) have a long-press meaning; everything else has no hold behaviour, so
// holding over it is simply a no-op (same as holding the pointer down over a plain button already is).
function padCursorHoldAt(px, py) {
  const el = elAt(px, py);
  if (!el) return;
  if (el.classList.contains('tgt-cell') || el.classList.contains('tgt-veh')) {
    send('tgt.only', { group: el.dataset.group, index: +el.dataset.index });
  } else if (el.classList.contains('preset-card')) {
    presetUi.save(+el.dataset.slot);
  } else if (el.classList.contains('tl-sort')) {
    clearSort();
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
      preset:   m.preset || { index: 1, name: '' },
    };
    paint();
  } else if (m.type === 'tgt-targets') {
    targets = Array.isArray(m.items) ? m.items : [];
    focusedTargetId = m.focusedTargetId || 0;
    targetsMetric = !!m.metric;
    sortKey = m.sortKey || '';
    sortDir = m.sortDir < 0 ? -1 : 1;
    renderSort();
    renderTargets();
    renderDock();   // ADD's count and the LISTED marks follow the live selection
  } else if (m.action === 'cursor-focus') {
    // A fresh SOI grant always starts crosshair-active, regardless of whatever mode a previous
    // grant left behind — there's no stale mode worth carrying across a focus loss.
    if (m.on) { crosshairActive = true; cursor.setHidden(false); renderTargets(); }
    cursor.setFocus(!!m.on, panel.clientWidth / 2, panel.clientHeight / 2);
  } else if (m.action === 'cursor') {
    // An actual deflection (not the (0,0) a key release reports) hands Select back to the
    // crosshair and shows it again.
    if (m.x || m.y) { crosshairActive = true; cursor.setHidden(false); renderTargets(); }
    cursor.setVector(m.x, m.y);
  } else if (m.action === 'cursor-held') {
    cursor.setSelectHeld(!!m.held);
  } else if (m.action === 'zoom-in') {
    listScroll.scrollBy({ top: SCROLL_STEP });
  } else if (m.action === 'zoom-out') {
    listScroll.scrollBy({ top: -SCROLL_STEP });
  } else if (m.action === 'tgt-next' || m.action === 'tgt-prev') {
    // Next/Previous Target (docs/tgt-cycle-focus.md) hands Select to the focused lock and hides
    // the crosshair for as long as it's active — mutually exclusive with the PAD cursor above.
    // The actual cycling happens server-side (TargetFocus.Cycle); this just switches which control
    // Select acts on once the next 'tgt-targets' frame reflects the new focusedTargetId.
    crosshairActive = false;
    cursor.setHidden(true);
    renderTargets();
  } else if (m.action === 'tgt-datalink') {
    send('tgt.clear-datalink');
  } else if (m.action === 'tgt-stale') {
    send('tgt.clear-stale');
  }
});

paint();          // initial paint — UNAVAILABLE until the first frame arrives
presetUi.render();
presetUi.refresh();
renderSort();
renderTargets();  // initial empty list
