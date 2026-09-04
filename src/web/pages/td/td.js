// TD page (issue #47, docs/target-designator.md) — squad leader hand-assigns targets from their
// own TGT list to squad members; members get a read-only list of what was designated to them.
// Squad/assignment state has no polling of its own: one bootstrap GET /squad + GET /td-state on
// load (docs/sse-push-refactor.md), then the shell's SSE-relayed 'sqd-state'/'td-state-push'
// messages keep it current — a leader's DESIGNATE reaches an already-open member page as soon as
// the plugin's state changes. The target ROWS come from the shell's own 'tgt-targets' broadcast
// (same message TGT itself mirrors, mfd.js/f35.js forward it to this page too), but unlike TGT,
// this page deliberately does NOT redraw on every one of those messages — see applyLiveTargets'
// own header comment for why the table is static except on a real select/deselect or the REFRESH
// button.
import { createPadCursor } from '/assets/services/pad-cursor.js';

if (window.parent !== window) {
  const back = document.querySelector('.td-back');
  if (back) back.remove();
}

// Long-pressing a squad button (issue #47 follow-up) must not pop a context menu — same guard
// tgt.js's own tap/long-press cells use.
window.addEventListener('contextmenu', function (e) { e.preventDefault(); });

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
const memberRefreshBtn = document.getElementById('td-member-refresh');
const refreshBtn      = document.getElementById('td-refresh');
const selectAllBtn    = document.getElementById('td-select-all');

let squad = null;      // last-known GET /squad {ready, state}
let td = null;          // last-known GET /td-state {ready, state}
let liveTargets = [];   // last-known live target rows from the shell's 'tgt-targets' message

// Optimistic overlay for the leader's selected/assignments, cleared as soon as a fresh td-state
// lands (the SSE-pushed 'td-state-push' below, or a REFRESH/nudge fetch). Without this, a click
// only becomes visible once that arrives. Set synchronously in the click handler itself so the
// visual result is immediate, not waiting on the round trip.
let selectedOverride = null;      // Set<id> | null
let assignmentsOverride = null;   // {id: [slots]} | null
function effectiveSelected(tdState) { return selectedOverride || new Set(tdState.selected || []); }
function effectiveAssignments(tdState) { return assignmentsOverride || (tdState.assignments || {}); }

function send(cmd, args) { sendCommand(cmd, args).catch(function () {}); }

// Range as "8,4 km" (European decimal comma) — matches tgt.js's own fmtRng.
function fmtRng(r) {
  return (typeof r === 'number' && isFinite(r)) ? r.toFixed(1).replace('.', ',') + ' km' : '—';
}

function factionClass(f) { return f === 1 ? 'f-friendly' : f === 0 ? 'f-neutral' : 'f-enemy'; }

// Shared by applyLiveTargets/renderMember: both tables are the same NAME/GRID/RNG row shape, differing
// only in whether a trailing tags cell is appended and what a click does (toggle-select vs.
// immediate in-game select).
function makeRow(t, onClick) {
  const row = document.createElement('div');
  row.className = 'td-row-item pad-hoverable ' + factionClass(t.f);
  row.dataset.id = t.id;
  const name = document.createElement('span'); name.className = 'td-name'; name.textContent = t.n || '—';
  const grid = document.createElement('span'); grid.className = 'td-grid'; grid.textContent = t.g != null ? String(t.g) : '—';
  const dist = document.createElement('span'); dist.className = 'td-dist'; dist.textContent = fmtRng(t.r);
  row.appendChild(name); row.appendChild(grid); row.appendChild(dist);
  row.addEventListener('click', onClick);
  return row;
}

// Squad-slot numbering — 1 is the leader/self, member i (join order) is i+2. Same scheme sqd.js's
// own addSquadRow uses (Squad.cs's _members list only ever appends, so index IS join order).
function squadSlots(state) {
  const slots = [{ num: 1, name: 'SELF' }];
  (state.members || []).forEach(function (m, i) { slots.push({ num: i + 2, name: m.name || m.id }); });
  return slots;
}

// Squadron Callsign System (issue #42) — "<CALLSIGN> <FLIGHT>-<MEMBER>", e.g. "TALON 1-2". Same
// format sqd.js's own squadDesignation renders on the roster table.
function squadDesignation(state, memberNumber) {
  return (state.callsign || 'SQD') + ' ' + (state.flight || 1) + '-' + memberNumber;
}

// Structural render: which section is visible. Only ever called from the initial page-load fetch
// or the REFRESH button (refreshSquad/refreshTd below) — TD has no automatic timer of any kind,
// and never from the 'tgt-targets' feed, which has its own separate, also-not-automatic path.
function render() {
  if (!squad || !squad.ready || !td || !td.ready) { unavailableEl.style.display = ''; leaderSection.style.display = 'none'; memberSection.style.display = 'none'; return; }
  const state = squad.state;
  const role = state.role;
  unavailableEl.style.display = role === 'none' ? '' : 'none';
  leaderSection.style.display = role === 'leader' ? '' : 'none';
  memberSection.style.display = role === 'member' ? '' : 'none';
  if (role === 'leader') {
    renderSquadButtons(state);
    // Seed the table once on first entry (nothing to show yet otherwise) — NOT an ongoing
    // refresh; see applyLiveTargets' own header for the only three things that trigger it again.
    if (leaderRowEls.size === 0 && liveTargets.length) applyLiveTargets();
    applySelectionState();
  }
  else if (role === 'member') renderMember(td.state);
}

// ── Leader view ──────────────────────────────────────────────────────────────────────
// TD does NOT mirror the live telemetry feed the way TGT does: the table is static between actual
// designation activity, so a click's mousedown-then-mouseup gesture is never disturbed by a
// same-moment repaint. It refreshes only in three cases, all deliberate, all triggered by something
// the user (or the user's own game actions) actually did:
//   1. A real select/deselect in-game — i.e. the SET of locked target ids changed, checked below
//      via idsKey(). Range/grid drifting on an already-locked target does NOT trigger this.
//   2. The REFRESH button — pulls in whatever the shell's last 'tgt-targets' message was, on
//      demand, so grid/range can be brought current without needing to lock/unlock anything.
//   3. Squad roster changes (renderSquadButtons, memoized by signature) and a pushed 'sqd-state'/
//      'td-state-push' (selection/assignment state — applySelectionState below), neither of which
//      touches the target rows at all.
// squadButtons stays memoized by roster signature. leaderRowEls persists row elements by id so an
// existing row is only ever updated in place, never destroyed/recreated/repositioned even when
// case 1 or 2 above does run.
let lastSquadSig = null;
const leaderRowEls = new Map();   // target id -> row element, persists across updates
let lastAppliedIdsKey = null;     // idsKey() of whichever snapshot leaderRowEls currently reflects

function idsKey(list) { return list.map(function (t) { return t.id; }).sort(function (a, b) { return a - b; }).join(','); }

// Assign — tap vs. long-press (issue #47 follow-up), same LONG_MS/pointerdown-timer shape tgt.js's
// own tap/long-press cells use, no keybind or PAD-cursor-hold plumbing needed: a tap clears the
// selection afterward as always; a long-press keeps it lit, so the leader can designate the same
// selection to several slots in a row without re-selecting between each one. `on` tells the plugin
// to do the same server-side, so a REFRESH mid-sequence doesn't wipe the highlights being kept.
const LONG_MS = 500;
function doAssign(slot, retain) {
  const nextAssignments = Object.assign({}, effectiveAssignments(td.state));
  effectiveSelected(td.state).forEach(function (id) {
    const key = String(id);
    const memberSlots = new Set(nextAssignments[key] || []);
    if (memberSlots.has(slot)) memberSlots.delete(slot); else memberSlots.add(slot);
    if (memberSlots.size) nextAssignments[key] = Array.from(memberSlots); else delete nextAssignments[key];
  });
  assignmentsOverride = nextAssignments;
  if (!retain) selectedOverride = new Set();
  applySelectionState();
  send('td.assign', { index: slot, on: retain });
}

function renderSquadButtons(state) {
  const slots = squadSlots(state);
  const sig = state.callsign + '|' + state.flight + '|' + slots.map(function (s) { return s.num + ':' + s.name; }).join(',');
  if (sig === lastSquadSig) return;
  lastSquadSig = sig;
  squadButtons.innerHTML = '';
  slots.forEach(function (s) {
    const btn = document.createElement('button');
    btn.className = 'td-squad-btn pad-hoverable';
    btn.textContent = squadDesignation(state, s.num);
    btn.title = s.name;
    btn.dataset.slot = s.num;
    let longFired = false;
    let timer = null;
    btn.addEventListener('pointerdown', function () {
      longFired = false;
      timer = setTimeout(function () { longFired = true; doAssign(s.num, true); }, LONG_MS);
    });
    btn.addEventListener('pointerup', function () {
      clearTimeout(timer);
      if (!longFired) doAssign(s.num, false);
    });
    btn.addEventListener('pointerleave', function () { clearTimeout(timer); });
    btn.addEventListener('pointercancel', function () { clearTimeout(timer); });
    squadButtons.appendChild(btn);
  });
}

// Identity/text only — never touches .selected or tags. Called only from the three places listed
// above (initial seed, a real select/deselect, or the REFRESH button) — never on a timer.
function applyLiveTargets() {
  if (!squad || squad.state.role !== 'leader') return;
  lastAppliedIdsKey = idsKey(liveTargets);
  const seen = new Set();
  liveTargets.forEach(function (t) {
    seen.add(t.id);
    let row = leaderRowEls.get(t.id);
    if (!row) {
      row = makeRow(t, function () {
        const next = new Set(effectiveSelected(td.state));
        if (next.has(t.id)) next.delete(t.id); else next.add(t.id);
        selectedOverride = next;
        applySelectionState();
        send('td.select', { id: t.id });
      });
      const tags = document.createElement('span'); tags.className = 'td-tags';
      row.appendChild(tags);
      leaderRowEls.set(t.id, row);
    } else {
      row.querySelector('.td-name').textContent = t.n || '—';
      row.querySelector('.td-grid').textContent = t.g != null ? String(t.g) : '—';
      row.querySelector('.td-dist').textContent = fmtRng(t.r);
      row.classList.remove('f-friendly', 'f-neutral', 'f-enemy');
      row.classList.add(factionClass(t.f));
      return;   // EXISTING row: text updated above, but never reposition it — see below.
    }
    leaderRows.appendChild(row);   // brand-new row only: lands at the end.
  });
  leaderRowEls.forEach(function (row, id) {
    if (!seen.has(id)) { row.remove(); leaderRowEls.delete(id); }
  });
  leaderEmpty.style.display = liveTargets.length ? 'none' : '';
  // A newly-created row above has no selection/tags applied yet — catch up immediately.
  applySelectionState();
}

// .selected + tags only, on whichever rows currently exist — never touches identity/text/order.
function applySelectionState() {
  if (!td) return;
  const selected = effectiveSelected(td.state);
  const assignments = effectiveAssignments(td.state);
  leaderRowEls.forEach(function (row, id) {
    row.classList.toggle('selected', selected.has(id));
    const assigned = assignments[String(id)] || [];
    row.querySelector('.td-tags').textContent = assigned.length ? assigned.join(' ') : '';
  });
}

designateBtn.addEventListener('click', function () {
  if (!squad || !td) return;
  const state = squad.state;
  // effectiveAssignments, not td.state.assignments directly — doAssign() only updates the override
  // for instant UI feedback (no re-fetch; TD has no polling of its own), so the raw fetched state
  // stays stale until the next REFRESH/nudge. Reading it directly here meant DESIGNATE could see
  // stale (often empty) assignments right after a leader assigned and immediately hit DESIGNATE.
  const assignments = effectiveAssignments(td.state);
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
  // Return the leader to TGT (issue #47 follow-up) — DESIGNATE is the "I'm done here" action.
  // Handled by the shell (mfd.js/f35.js), not this page directly: TD can be the full-view page or
  // either split pane, and only the shell knows which one this iframe actually is.
  if (window.parent !== window) window.parent.postMessage({ mfd: true, type: 'td-designated' }, '*');
});
leaderClearBtn.addEventListener('click', function () {
  selectedOverride = new Set();
  assignmentsOverride = {};
  applySelectionState();
  send('td.clear', {});
});
selectAllBtn.addEventListener('click', function () {
  // Select every row currently in the table — applied locally first (see selectedOverride's own
  // comment) for instant feedback, then one td.select per row not already selected so the
  // server's own state agrees (ToggleSelect is a toggle, not a set-true, so an already-selected
  // row must not be re-sent or it would flip back off).
  const ids = Array.from(leaderRowEls.keys());
  const already = effectiveSelected(td.state);
  selectedOverride = new Set(ids);
  applySelectionState();
  ids.forEach(function (id) { if (!already.has(id)) send('td.select', { id: id }); });
});

// ── Member view ──────────────────────────────────────────────────────────────────────
function renderMember(tdState) {
  const rows = tdState.designated || [];
  memberRows.innerHTML = '';
  rows.forEach(function (t) {
    memberRows.appendChild(makeRow(t, function () { send('target.select', { id: t.id }); }));
  });
  memberEmpty.style.display = rows.length ? 'none' : '';
}

// Jumps to TGT right after acquiring — the whole point of AQUIRE is to lock those targets in the
// cockpit, and TGT is where the pilot actually sees/uses the result. Same 'td-designated' signal
// DESIGNATE sends below: TGT has no telemetry connection of its own (it only ever renders what the
// shell relays), so navigating via a bare location.href would strand it on a standalone page with
// no data — the shell (mfd.js/f35.js) has to be the one to actually switch this frame/pane to TGT.
acquireBtn.addEventListener('click', function () {
  send('td.acquire-all', {});
  if (window.parent !== window) window.parent.postMessage({ mfd: true, type: 'td-designated' }, '*');
});
memberClearBtn.addEventListener('click', function () { send('td.member-clear', {}); });
memberRefreshBtn.addEventListener('click', function () { refreshSquad(); refreshTd(); });
refreshBtn.addEventListener('click', function () {
  // The one manual sync point: re-pull squad roster + assignment state from the server (in case
  // anything drifted — a member leaving, etc.) AND re-apply whatever the shell's latest target
  // snapshot is. No automatic timer does any of this — see the fetch functions' own header.
  refreshSquad();
  refreshTd();
  applyLiveTargets();
});

// ── Fetches: ONLY on initial load (below) and from the REFRESH button above — never a timer of
// any kind. Everything else (a squad-role change, a designation landing) reaches this page through
// the SSE-pushed 'sqd-state'/'td-state-push' messages below instead.
function applySquad(s) { squad = s; render(); }
function applyTdState(s) {
  td = s;
  // Whatever set these overrides has had a full round trip to apply server-side by now — drop the
  // overlay and trust the freshly-landed truth instead.
  selectedOverride = null;
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

// ── Shell -> page: the live target-row mirror (same message TGT itself listens for) ────
// Always keep `liveTargets` current (cheap — just a variable, no DOM) so the REFRESH button always
// has an up-to-date snapshot ready. Only actually touch the DOM (applyLiveTargets) when the SET of
// ids changed — a real select/deselect — never for a pure value-only update (range/grid drifting
// on a target that was already locked). See applyLiveTargets' own header for the full reasoning.
window.addEventListener('message', function (e) {
  const m = e.data;
  if (!m || m.mfd !== true) return;
  if (m.type === 'tgt-targets') {
    liveTargets = Array.isArray(m.items) ? m.items : [];
    if (idsKey(liveTargets) !== lastAppliedIdsKey) {
      applyLiveTargets();
    }
  } else if (m.type === 'sqd-state') {
    // SSE-pushed (docs/sse-push-refactor.md) — same shell relay tgt.js's own TD column already
    // rides. Squad/role changes land as soon as the plugin's state changes, not on a timer.
    applySquad(m.data);
  } else if (m.type === 'td-state-push') {
    // SSE-pushed the instant TdStore.StateJson changes (SseHub.cs), including the leader's own
    // DESIGNATE (applied directly plugin-side, Squad.HandleData) — a member with TD already open
    // sees the new rows land on their own, no REFRESH or page-revisit needed.
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

// Zoom In/Out (map-act's zoom-in/zoom-out) repurposed to scroll the page, same as SQD/WPT/TGT/HUD —
// nothing on this page to zoom, and the binds already exist end-to-end (docs/page-cursor.md).
const SCROLL_STEP = 60;   // ponytail: flat constant tuned by feel, like pad-cursor.js's own SPEED

// The shell only forwards these once this page is in PAD_CURSOR_PAGES (mfd.js/f35.js) — without
// this listener the crosshair above is built but never shown or moved (docs/web-efficiency-audit.md
// correctness section: "TD's entire PAD-cursor block is dead code").
window.addEventListener('message', function (e) {
  const m = e.data;
  if (!m || m.mfd !== true) return;
  if (m.action === 'cursor-focus') cursor.setFocus(!!m.on, window.innerWidth / 2, window.innerHeight / 2);
  else if (m.action === 'cursor') cursor.setVector(m.x, m.y);
  else if (m.action === 'cursor-select') cursor.select();
  else if (m.action === 'zoom-in') window.scrollBy({ top: SCROLL_STEP });
  else if (m.action === 'zoom-out') window.scrollBy({ top: -SCROLL_STEP });
});
