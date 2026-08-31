// TD page (issue #47, docs/target-designator.md) — squad leader hand-assigns targets from their
// own live TGT list to squad members; members get a read-only list of what was designated to them.
// Squad/assignment state polls GET /squad and GET /td-state directly (same 1s-poll convention as
// SQD, docs/squadron-transport.md); the live target ROWS come from the shell's own 'tgt-targets'
// broadcast, same message TGT itself mirrors (mfd.js/f35.js forward it to this page too).
import { createPadCursor } from '/assets/services/pad-cursor.js';

if (window.parent !== window) {
  const back = document.querySelector('.td-back');
  if (back) back.remove();
}

const unavailableEl = document.getElementById('td-unavailable');
const leaderSection  = document.getElementById('td-leader-section');
const memberSection  = document.getElementById('td-member-section');
const squadButtons   = document.getElementById('td-squad-buttons');
const leaderRows     = document.getElementById('td-leader-rows');
const leaderEmpty    = document.getElementById('td-leader-empty');
const designateBtn   = document.getElementById('td-designate');
const leaderClearBtn = document.getElementById('td-leader-clear');
const memberRows     = document.getElementById('td-member-rows');
const memberEmpty    = document.getElementById('td-member-empty');
const acquireBtn     = document.getElementById('td-acquire');
const memberClearBtn = document.getElementById('td-member-clear');

let squad = null;      // last-known GET /squad {ready, state}
let td = null;          // last-known GET /td-state {ready, state}
let liveTargets = [];   // last-known live target rows from the shell's 'tgt-targets' message

function send(cmd, args) { sendCommand(cmd, args).catch(function () {}); }

// Range as "8,4 km" (European decimal comma) — matches tgt.js's own fmtRng.
function fmtRng(r) {
  return (typeof r === 'number' && isFinite(r)) ? r.toFixed(1).replace('.', ',') + ' km' : '—';
}

function factionClass(f) { return f === 1 ? 'f-friendly' : f === 0 ? 'f-neutral' : 'f-enemy'; }

// Squad-slot numbering — 1 is the leader/self, member i (join order) is i+2. Same scheme sqd.js's
// own addSquadRow uses (Squad.cs's _members list only ever appends, so index IS join order).
function squadSlots(state) {
  const slots = [{ num: 1, name: 'SELF' }];
  (state.members || []).forEach(function (m, i) { slots.push({ num: i + 2, name: m.name || m.id }); });
  return slots;
}

function render() {
  if (!squad || !squad.ready || !td || !td.ready) { unavailableEl.style.display = ''; leaderSection.style.display = 'none'; memberSection.style.display = 'none'; return; }
  const state = squad.state;
  const role = state.role;
  unavailableEl.style.display = role === 'none' ? '' : 'none';
  leaderSection.style.display = role === 'leader' ? '' : 'none';
  memberSection.style.display = role === 'member' ? '' : 'none';
  if (role === 'leader') renderLeader(state, td.state);
  else if (role === 'member') renderMember(td.state);
}

// ── Leader view ──────────────────────────────────────────────────────────────────────
function renderLeader(state, tdState) {
  const slots = squadSlots(state);
  squadButtons.innerHTML = '';
  slots.forEach(function (s) {
    const btn = document.createElement('button');
    btn.className = 'td-squad-btn pad-hoverable';
    btn.textContent = (state.callsign || 'SQD') + s.num;
    btn.title = s.name;
    btn.dataset.slot = s.num;
    btn.addEventListener('click', function () { send('td.assign', { index: s.num }); });
    squadButtons.appendChild(btn);
  });

  const selected = new Set(tdState.selected || []);
  const assignments = tdState.assignments || {};

  leaderRows.innerHTML = '';
  liveTargets.forEach(function (t) {
    const row = document.createElement('div');
    row.className = 'td-row-item pad-hoverable ' + factionClass(t.f);
    row.dataset.id = t.id;
    if (selected.has(t.id)) row.classList.add('selected');
    const name = document.createElement('span'); name.className = 'td-name'; name.textContent = t.n || '—';
    const grid = document.createElement('span'); grid.className = 'td-grid'; grid.textContent = t.g != null ? String(t.g) : '—';
    const dist = document.createElement('span'); dist.className = 'td-dist'; dist.textContent = fmtRng(t.r);
    const tags = document.createElement('span'); tags.className = 'td-tags';
    const assigned = assignments[String(t.id)] || [];
    tags.textContent = assigned.length ? assigned.map(function (n) { return '→' + n; }).join(' ') : '';
    row.appendChild(name); row.appendChild(grid); row.appendChild(dist); row.appendChild(tags);
    row.addEventListener('click', function () { send('td.select', { id: t.id }); });
    leaderRows.appendChild(row);
  });
  leaderEmpty.style.display = liveTargets.length ? 'none' : '';
}

designateBtn.addEventListener('click', function () {
  if (!squad || !td) return;
  const state = squad.state;
  const assignments = td.state.assignments || {};
  const byId = {};
  liveTargets.forEach(function (t) { byId[t.id] = t; });
  (state.members || []).forEach(function (m, i) {
    const slot = i + 2;
    const ids = Object.keys(assignments).filter(function (id) { return assignments[id].indexOf(slot) !== -1; });
    if (ids.length === 0) return;   // nothing assigned to this member — nothing to send
    const rows = ids.map(function (id) { return byId[id]; }).filter(Boolean)
      .map(function (t) { return { id: t.id, n: t.n, g: t.g, r: t.r, f: t.f, dl: !!t.dl }; });
    if (rows.length === 0) return;
    send('td.designate', { peer: m.id, text: JSON.stringify(rows) });
  });
});
leaderClearBtn.addEventListener('click', function () { send('td.clear', {}); });

// ── Member view ──────────────────────────────────────────────────────────────────────
function renderMember(tdState) {
  const rows = tdState.designated || [];
  memberRows.innerHTML = '';
  rows.forEach(function (t) {
    const row = document.createElement('div');
    row.className = 'td-row-item pad-hoverable ' + factionClass(t.f);
    row.dataset.id = t.id;
    const name = document.createElement('span'); name.className = 'td-name'; name.textContent = t.n || '—';
    const grid = document.createElement('span'); grid.className = 'td-grid'; grid.textContent = t.g != null ? String(t.g) : '—';
    const dist = document.createElement('span'); dist.className = 'td-dist'; dist.textContent = fmtRng(t.r);
    row.appendChild(name); row.appendChild(grid); row.appendChild(dist);
    row.addEventListener('click', function () { send('target.select', { id: t.id }); });
    memberRows.appendChild(row);
  });
  memberEmpty.style.display = rows.length ? 'none' : '';
}

acquireBtn.addEventListener('click', function () { send('td.acquire-all', {}); });
memberClearBtn.addEventListener('click', function () { send('td.member-clear', {}); });

// ── Polling (same 1s convention sqd.js uses) ────────────────────────────────────────
function refreshSquad() {
  return fetch('/squad').then(function (r) { return r.ok ? r.json() : null; })
    .then(function (s) { if (s) { squad = s; render(); } }).catch(function () {});
}
function refreshTd() {
  return fetch('/td-state').then(function (r) { return r.ok ? r.json() : null; })
    .then(function (s) { if (s) { td = s; render(); } }).catch(function () {});
}
refreshSquad(); refreshTd();
setInterval(refreshSquad, 1000);
setInterval(refreshTd, 1000);

// ── Shell -> page: the live target-row mirror (same message TGT itself listens for) ────
window.addEventListener('message', function (e) {
  const m = e.data;
  if (!m || m.mfd !== true) return;
  if (m.type === 'tgt-targets') {
    liveTargets = Array.isArray(m.items) ? m.items : [];
    render();
  }
});

// ── PAD cursor (docs/page-cursor.md) — same fixed-position crosshair SQD uses. ─────────
const CURSORABLE = '.pad-hoverable';
const padCursorEl = document.getElementById('pad-cursor');
const cursor = createPadCursor({
  el: padCursorEl,
  clampRect: () => ({ dx: 0, dy: 0, dw: window.innerWidth, dh: window.innerHeight }),
  onSelect: padCursorSelectAt,
  onMove: padCursorMoveAt,
});
function padCursorSelectAt(x, y) {
  const raw = document.elementFromPoint(x, y);
  const el = raw && raw.closest(CURSORABLE);
  if (el) el.click();
}
let hoveredEl = null;
function padCursorMoveAt(x, y) {
  const raw = x == null ? null : document.elementFromPoint(x, y);
  const el = raw && raw.closest(CURSORABLE);
  if (el === hoveredEl) return;
  if (hoveredEl) hoveredEl.classList.remove('pad-hover');
  hoveredEl = el;
  if (hoveredEl) hoveredEl.classList.add('pad-hover');
}
