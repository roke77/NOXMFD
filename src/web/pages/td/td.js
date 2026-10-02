// TD page (issue #47, docs/target-designator.md) — the squad leader's assignment matrix: targets
// from the leader's own TGT list down the side, squad members across the top, one tap per
// assignment (td.html's header has the full interaction model). Members never use this page; a
// designation reaches them on TGT (tgt.js's dock).
//
// Squad/assignment state has no polling of its own: one bootstrap GET /squad + GET /td-state on
// load (docs/sse-push-refactor.md), then the shell's SSE-relayed 'sqd-state'/'td-state-push'
// messages keep it current. The target ROWS come from the shell's own 'tgt-targets' broadcast
// (same message TGT itself mirrors), but unlike TGT this page deliberately does NOT redraw on
// every one of those messages — see buildRows' own header comment.
import { createPadCursor } from '/assets/services/pad-cursor.js';
import { fmtRng } from '/assets/services/range-format.js';
import { idsKey, tgtTargetsRedraw } from '/assets/pages/td/td-redraw-gate.js';
import { has, toggleCell, toggleRow, toggleColumn, memberIds, memberStatus } from '/assets/pages/td/td-matrix.js';

if (window.parent !== window) {
  const back = document.querySelector('.td-back');
  if (back) back.remove();
}

const unavailableEl = document.getElementById('td-unavailable');
const leaderSection = document.getElementById('td-leader-section');
const summaryEl     = document.getElementById('td-summary');
const listEl        = document.getElementById('td-list');
const headEl        = document.getElementById('td-matrix-head');
const rowsEl        = document.getElementById('td-rows');
const flashEl       = document.getElementById('td-flash');
const designateBtn  = document.getElementById('td-designate');
const designateSub  = document.getElementById('td-designate-sub');
const refreshBtn    = document.getElementById('td-refresh');
const clearBtn      = document.getElementById('td-clear');

const HINT = 'cell = one target to one pilot · name = every pilot · column head = every target';

let squad = null;      // last-known GET /squad {ready, state}
let td = null;          // last-known GET /td-state {ready, state}
let liveTargets = [];   // last-known live target rows from the shell's 'tgt-targets' message
let liveTargetsMetric = false;   // player's Metric/Imperial preference, carried on the same message
let tableTargets = [];  // the rows the table currently shows — liveTargets as of the last buildRows
let flash = '';

// Optimistic copy of the assignments, dropped as soon as a fresh td-state lands (the SSE-pushed
// 'td-state-push' below, or a REFRESH fetch). Without it a tap only shows once that round trip is
// back; set synchronously in the tap handler so the result is immediate.
let assignmentsOverride = null;
function assignments() { return assignmentsOverride || (td && td.state.assignments) || {}; }

function send(cmd, args) { sendCommand(cmd, args).catch(function () {}); }

function factionClass(f) { return f === 1 ? 'f-friendly' : f === 0 ? 'f-neutral' : 'f-enemy'; }

// Squad members in column order — the leader (this pilot) first, then everyone else in the order
// they joined (state.members). Columns are keyed by SteamID (issue #107): squads no longer number
// their members, and each flies under their own callsign (state.pilots[id].d, "VIPER 1-2").
function squadMembers(state) {
  const out = [{ id: state.self, name: state.selfName || '', leader: true }];
  (state.members || []).forEach(function (m) { out.push({ id: m.id, name: m.name || '', leader: false }); });
  return out;
}
function memberIdList() { return squadMembers(squad.state).map(function (m) { return m.id; }); }
function tableIds() { return tableTargets.map(function (t) { return t.id; }); }
function isLeaderId(state, id) { return id === state.self; }
function callsignOf(state, id) { const p = (state.pilots || {})[id]; return p ? p.d : ''; }
// "VIPER 1-2" → ["VIPER", "1-2"]; a pilot without a callsign shows their Steam name over a blank.
function splitCallsign(d, fallback) {
  const i = d.lastIndexOf(' ');
  return i > 0 ? [d.slice(0, i), d.slice(i + 1)] : [fallback, ''];
}
function memberLabel(state, id) {
  if (isLeaderId(state, id)) return 'YOU';
  const m = squadMembers(state).find(function (x) { return x.id === id; });
  return callsignOf(state, id) || (m && m.name) || 'pilot';
}

function render() {
  if (!squad || !squad.ready || !td || !td.ready) { showUnavailable('— UNAVAILABLE —'); return; }
  const role = squad.state.role;
  if (role !== 'leader') {
    showUnavailable(role === 'member'
      ? 'Target designation is the squad leader\'s. Designations from your leader arrive on your TGT page.'
      : 'Target Designator requires leading a squad. Create one on the SQD page first.');
    return;
  }
  unavailableEl.style.display = 'none';
  leaderSection.style.display = '';
  buildHeads();
  // Seed the table once on first entry — NOT an ongoing refresh; see buildRows' own header.
  if (lastAppliedIdsKey === null && liveTargets.length) buildRows();
  applyState();
}
function showUnavailable(text) {
  unavailableEl.textContent = text;
  unavailableEl.style.display = '';
  leaderSection.style.display = 'none';
}

// ── Column heads ────────────────────────────────────────────────────────────────────────
// Rebuilt only when the roster/callsign changes (memoized by signature); applyState fills in the
// per-member count/status.
let lastSquadSig = null;
function buildHeads() {
  const state = squad.state;
  const members = squadMembers(state);
  const sig = state.callsign + '|' + state.flight + '|' + members.map(function (m) {
    return m.id + ':' + m.name + ':' + callsignOf(state, m.id);
  }).join(',');
  if (sig === lastSquadSig) return;
  lastSquadSig = sig;
  // Member tracks stay at their full width up to three members, then share what the name column leaves.
  listEl.style.setProperty('--td-cols',
    'minmax(clamp(120px, 30vw, 240px), 1fr) repeat(' + members.length + ', minmax(clamp(40px, 6vw, 56px), clamp(64px, 13vw, 120px)))');
  headEl.querySelectorAll('.td-col').forEach(function (el) { el.remove(); });
  members.forEach(function (m) {
    const btn = document.createElement('button');
    btn.type = 'button';
    btn.className = 'td-col pad-hoverable';
    btn.dataset.member = m.id;
    btn.setAttribute('aria-label', 'Every target to ' + memberLabel(state, m.id) + ', or empty that column');
    btn.innerHTML = '<span class="td-col-lamp"></span><span class="td-col-top"></span><span class="td-col-num"></span><span class="td-col-st"></span>';
    const parts = splitCallsign(callsignOf(state, m.id), m.name || 'PILOT');
    btn.querySelector('.td-col-top').textContent = m.leader ? 'YOU' : parts[0];
    btn.querySelector('.td-col-num').textContent = parts[1];
    btn.addEventListener('click', function () { tapColumn(m.id); });
    headEl.appendChild(btn);
  });
  // A roster change adds or drops a column in every row too.
  if (lastAppliedIdsKey !== null) buildRows();
}

// ── Rows ────────────────────────────────────────────────────────────────────────────────
// The table does NOT mirror the live telemetry feed the way TGT does: it stays static between
// deliberate changes, so a tap's mousedown-then-mouseup is never split by a same-moment repaint.
// buildRows runs only on (1) a real select/deselect in-game — the SET of locked ids changed,
// td-redraw-gate.js; (2) a Metric/Imperial flip; (3) REFRESH; (4) a roster change (buildHeads).
// Range drifting on an already-locked target never redraws.
let lastAppliedIdsKey = null;   // idsKey() of whichever snapshot the rows currently reflect
let lastAppliedMetric = null;   // liveTargetsMetric as of the last redraw
function buildRows() {
  if (!squad || squad.state.role !== 'leader') return;
  tableTargets = liveTargets.slice();
  lastAppliedIdsKey = idsKey(tableTargets);
  const members = memberIdList();
  rowsEl.innerHTML = '';
  tableTargets.forEach(function (t) {
    const row = document.createElement('div');
    row.className = 'td-grid-row td-row ' + factionClass(t.f);
    row.dataset.id = t.id;
    const name = document.createElement('button');
    name.type = 'button';
    name.className = 'td-name-btn pad-hoverable';
    name.setAttribute('aria-label', (t.n || 'Target') + ' to every pilot');
    const n = document.createElement('span'); n.className = 'td-name'; n.textContent = t.n || '—';
    const r = document.createElement('span'); r.className = 'td-dist'; r.textContent = fmtRng(t.r, liveTargetsMetric);
    name.appendChild(n); name.appendChild(r);
    name.addEventListener('click', function () { tapRow(t); });
    row.appendChild(name);
    members.forEach(function (member) {
      const cell = document.createElement('button');
      cell.type = 'button';
      cell.className = 'td-cell pad-hoverable';
      cell.dataset.member = member;
      cell.addEventListener('click', function () { tapCell(t, member); });
      row.appendChild(cell);
    });
    rowsEl.appendChild(row);
  });
  applyState();
}

// Cell marks, column-head status, name borders and DESIGNATE — never touches row identity/order.
function applyState() {
  if (!squad || squad.state.role !== 'leader' || !td) return;
  const state = squad.state;
  const a = assignments();
  const sent = td.state.sent || {};
  const ids = tableIds();
  const members = memberIdList();
  rowsEl.querySelectorAll('.td-row').forEach(function (row) {
    const id = Number(row.dataset.id);
    const t = tableTargets.find(function (x) { return x.id === id; });
    row.querySelector('.td-name-btn').classList.toggle('full', members.every(function (m) { return has(a, id, m); }));
    row.querySelectorAll('.td-cell').forEach(function (cell) {
      const member = cell.dataset.member;
      const on = has(a, id, member);
      cell.classList.toggle('on', on);
      cell.textContent = on ? '●' : '';
      cell.setAttribute('aria-pressed', on ? 'true' : 'false');
      cell.setAttribute('aria-label', (on ? 'Unassign ' : 'Assign ') + ((t && t.n) || 'target') + (on ? ' from ' : ' to ') + memberLabel(state, member));
    });
  });
  let waiting = 0;
  headEl.querySelectorAll('.td-col').forEach(function (btn) {
    const st = memberStatus(a, sent, btn.dataset.member, ids, isLeaderId(state, btn.dataset.member));
    if (st.waiting) waiting++;
    const lamp = btn.querySelector('.td-col-lamp');
    lamp.classList.toggle('waiting', st.waiting);
    lamp.classList.toggle('sent', st.status === 'SENT');
    lamp.classList.toggle('marker', st.status === 'MARKER' && st.n > 0);
    const label = btn.querySelector('.td-col-st');
    label.textContent = st.n + ' · ' + st.status;
    label.classList.toggle('waiting', st.waiting);
    label.classList.toggle('some', !st.waiting && st.n > 0);
  });
  summaryEl.textContent = (state.callsign || 'SQD') + ' ' + (state.flight || 1) + ' · ' + tableTargets.length + ' TGT';
  designateBtn.classList.toggle('waiting', waiting > 0);
  designateSub.textContent = waiting ? waiting + (waiting > 1 ? ' LISTS' : ' LIST') + ' WAITING' : 'ALL SENT';
  flashEl.textContent = flash || HINT;
}

// ── Taps: applied locally at once (td-matrix.js mirrors TdStore's rules), then sent ──────
function tapCell(t, member) {
  assignmentsOverride = toggleCell(assignments(), t.id, member);
  flash = '';
  applyState();
  send('td.cell', { id: t.id, peer: member });
}
function tapRow(t) {
  const members = memberIdList();
  const full = members.every(function (m) { return has(assignments(), t.id, m); });
  assignmentsOverride = toggleRow(assignments(), t.id, members);
  flash = full ? (t.n || 'Target') + ' removed from every pilot' : (t.n || 'Target') + ' → every pilot';
  applyState();
  send('td.row', { id: t.id });
}
function tapColumn(member) {
  const ids = tableIds();
  if (!ids.length) return;
  const full = ids.every(function (id) { return has(assignments(), id, member); });
  assignmentsOverride = toggleColumn(assignments(), member, ids);
  const label = memberLabel(squad.state, member);
  flash = full ? label + ' emptied' : 'every target → ' + label;
  applyState();
  send('td.column', { peer: member, text: JSON.stringify(ids) });
}

// DESIGNATE sends every member whose list is waiting (UNSENT or CHANGED) — an emptied list too, so
// a member's pending designation is withdrawn rather than left stale. Each send replaces that
// member's whole designation. The plugin records what went out (TdStore.MarkSent) and its pushed
// td-state turns those columns SENT.
designateBtn.addEventListener('click', function () {
  if (!squad || !td || squad.state.role !== 'leader') return;
  const a = assignments();
  const ids = tableIds();
  const sent = td.state.sent || {};
  const byId = {};
  tableTargets.forEach(function (t) { byId[t.id] = t; });
  let count = 0;
  squadMembers(squad.state).forEach(function (m) {
    if (m.leader || !memberStatus(a, sent, m.id, ids, false).waiting) return;
    const rows = memberIds(a, m.id, ids).map(function (id) {
      const t = byId[id];
      return { id: t.id, n: t.n, g: t.g, r: t.r, f: t.f, dl: !!t.dl };
    });
    send('td.designate', { peer: m.id, text: JSON.stringify(rows) });
    count++;
  });
  if (!count) { flash = 'nothing new to send'; applyState(); return; }
  flash = 'sent · each member\'s list replaced';
  applyState();
});
clearBtn.addEventListener('click', function () {
  assignmentsOverride = {};
  flash = 'work discarded · nothing already sent changed';
  applyState();
  send('td.clear', {});
});
refreshBtn.addEventListener('click', function () {
  // The one manual sync point: re-pull squad roster + assignment state AND re-apply whatever the
  // shell's latest target snapshot is. No automatic timer does any of this.
  flash = 'table refreshed from TGT';
  refreshSquad();
  refreshTd();
  buildRows();
});

// ── Fetches: ONLY on initial load (below) and from REFRESH — never a timer. Everything else
// reaches this page through the SSE-pushed 'sqd-state'/'td-state-push' messages below.
function applySquad(s) { squad = s; render(); }
function applyTdState(s) {
  td = s;
  // The plugin has applied whatever set the override by now — trust the freshly-landed truth.
  assignmentsOverride = null;
  render();
}
function refreshSquad() {
  return fetch('/squad').then(function (r) { return r.ok ? r.json() : null; })
    .then(function (s) { if (s) applySquad(s); }).catch(function () {});
}
function refreshTd() {
  return fetch('/td-state').then(function (r) { return r.ok ? r.json() : null; })
    .then(function (s) { if (s) applyTdState(s); }).catch(function () {});
}
refreshSquad(); refreshTd();

// ── Shell -> page ─────────────────────────────────────────────────────────────────────────
// liveTargets is always kept current (cheap — no DOM) so REFRESH has an up-to-date snapshot; the
// rows only redraw when the id SET changed or the Metric/Imperial preference flipped.
window.addEventListener('message', function (e) {
  const m = e.data;
  if (!m || m.mfd !== true) return;
  if (m.type === 'tgt-targets') {
    liveTargets = Array.isArray(m.items) ? m.items : [];
    liveTargetsMetric = !!m.metric;
    const gate = tgtTargetsRedraw(liveTargets, lastAppliedIdsKey, liveTargetsMetric, lastAppliedMetric);
    if (gate.leaderShouldRedraw) buildRows();
    lastAppliedMetric = liveTargetsMetric;
  } else if (m.type === 'sqd-state') {
    applySquad(m.data);
  } else if (m.type === 'td-state-push') {
    applyTdState(m.data);
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

// Zoom In/Out (map-act's zoom-in/zoom-out) repurposed to scroll the matrix, same as SQD/WPT/TGT/
// HUD — nothing on this page to zoom, and the binds already exist end-to-end (docs/page-cursor.md).
const SCROLL_STEP = 60;   // Flat constant tuned by feel, like pad-cursor.js's own SPEED.

// The shell only forwards these once this page is in PAD_CURSOR_PAGES (mfd.js/f35.js).
window.addEventListener('message', function (e) {
  const m = e.data;
  if (!m || m.mfd !== true) return;
  if (m.action === 'cursor-focus') cursor.setFocus(!!m.on, window.innerWidth / 2, window.innerHeight / 2);
  else if (m.action === 'cursor') cursor.setVector(m.x, m.y);
  else if (m.action === 'cursor-select') cursor.select();
  else if (m.action === 'zoom-in') listEl.scrollBy({ top: SCROLL_STEP });
  else if (m.action === 'zoom-out') listEl.scrollBy({ top: -SCROLL_STEP });
});
