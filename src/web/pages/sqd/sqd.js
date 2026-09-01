// SQD page (docs/squadron-transport.md) — squad membership over Steam P2P. Polls GET /squad and
// GET /server-players directly (no shell relay — see sqd.html's header comment) and drives every
// action through POST /command's sqd.* handlers. All protocol logic (who can invite whom, single-
// squad enforcement, succession) lives plugin-side (Squad.cs); this page only renders state and
// dispatches commands.
import { createPadCursor } from '/assets/services/pad-cursor.js';
import { SQUAD_CALLSIGNS } from './callsigns.js';

if (window.parent !== window) {
  const back = document.querySelector('.sqd-back');
  if (back) back.remove();
}

const unavailableEl    = document.getElementById('sqd-unavailable');
const noticeEl         = document.getElementById('sqd-notice');
const createSection    = document.getElementById('sqd-create-section');
const createPrompt     = document.getElementById('sqd-create-prompt');
const createOpenBtn    = document.getElementById('sqd-create-open');
const createForm       = document.getElementById('sqd-create-form');
const createCallsign   = document.getElementById('sqd-create-callsign');
const createFlight     = document.getElementById('sqd-create-flight');
const createConfirmBtn = document.getElementById('sqd-create-confirm');
const createCancelBtn  = document.getElementById('sqd-create-cancel');
const inviteSection    = document.getElementById('sqd-invite-section');
const inviteCards      = document.getElementById('sqd-invite-cards');
const rosterSection    = document.getElementById('sqd-roster-section');
const rosterRows       = document.getElementById('sqd-roster-rows');
const pendingSection   = document.getElementById('sqd-pending-section');
const pendingRows      = document.getElementById('sqd-pending-rows');
const squadSection     = document.getElementById('sqd-squad-section');
const squadHead        = document.getElementById('sqd-squad-head');
const callsignEdit     = document.getElementById('sqd-callsign-edit');
const callsignSelect   = document.getElementById('sqd-callsign-select');
const callsignFlight   = document.getElementById('sqd-callsign-flight');
const callsignSet      = document.getElementById('sqd-callsign-set');
const callsignEditBtn  = document.getElementById('sqd-callsign-edit-btn');
const squadRows        = document.getElementById('sqd-squad-rows');
const leaveBtn         = document.getElementById('sqd-leave');
const disbandBtn       = document.getElementById('sqd-disband');

// Callsign/flight pickers (issue #42) — populated once at load, not rebuilt per render: the option
// list itself never changes, only which <select> shows which one (creatingSquad/editingCallsign).
function fillOptions(select, values) {
  values.forEach(function (v) {
    const opt = document.createElement('option');
    opt.value = v; opt.textContent = v;
    select.appendChild(opt);
  });
}
fillOptions(createCallsign, SQUAD_CALLSIGNS);
fillOptions(callsignSelect, SQUAD_CALLSIGNS);
for (let f = 1; f <= 9; f++) { fillOptions(createFlight, [String(f)]); fillOptions(callsignFlight, [String(f)]); }

let lastNoticeSeq = -1;
let noticeTimer = null;
let state = null;   // last-known Squad.StateJson payload (null until the first successful poll)
let players = [];   // last-known /server-players list
let editingCallsign = false;   // EDIT swaps the title span for the input, in place
let creatingSquad = false;   // CREATE SQUAD swaps the button for the callsign input, in place

function acceptInvite(leaderId) { sendCommand('sqd.accept', { peer: leaderId }).catch(function () {}); }
function declineInvite(leaderId) { sendCommand('sqd.decline', { peer: leaderId }).catch(function () {}); }

createOpenBtn.onclick = function () {
  if (createOpenBtn.disabled) return;
  creatingSquad = true;
  render();
};
createCancelBtn.onclick = function () { creatingSquad = false; render(); };
createConfirmBtn.onclick = function () {
  const name = createCallsign.value;
  const flight = parseInt(createFlight.value, 10);
  if (name) sendCommand('sqd.create', { name: name, index: flight }).catch(function () {});
  creatingSquad = false;
  render();
};

disbandBtn.onclick    = function () { sendCommand('sqd.disband', {}).catch(function () {}); };
leaveBtn.onclick = function () {
  if (!state) return;
  if (state.role === 'leader' && state.members.length > 0) {
    sendCommand('sqd.relinquish', { peer: '' }).catch(function () {});
  } else {
    sendCommand('sqd.leave', {}).catch(function () {});
  }
};

function invite(id, name) {
  sendCommand('sqd.invite', { peer: id, name: name || '' }).catch(function () {});
}

callsignEditBtn.onclick = function () {
  editingCallsign = !editingCallsign;
  if (editingCallsign) {
    callsignSelect.value = (state && state.callsign) || '';
    callsignFlight.value = String((state && state.flight) || 1);
  }
  render();
};
callsignSet.onclick = function () {
  const name = callsignSelect.value;
  const flight = parseInt(callsignFlight.value, 10);
  if (name) sendCommand('sqd.set-callsign', { name: name, index: flight }).catch(function () {});
  editingCallsign = false;
  render();
};

// Aircraft icon cache, keyed by unitName — same idea as MAP's own loadIcon (map.js), scaled down
// (no canvas tinting needed here, just an <img>). Without this, addSquadRow created a fresh <img>
// every 1s poll regardless of whether the type had already 404'd, which spammed the console with
// repeat failed requests AND caused the aircraft column to visibly jump every render (blank while
// the fresh image request was in flight, then collapse again the instant it failed) — a type is
// now only ever probed once per page load; a known result renders synchronously, no flash.
const iconStatus = {};   // type -> 'pending' | 'ok' | 'none'

function getIconStatus(type) {
  if (!type) return null;
  if (iconStatus[type]) return iconStatus[type];
  iconStatus[type] = 'pending';
  const img = new Image();
  img.onload = function () {
    // 1×1 = the server's "no icon" sentinel (real plugin only, not this static preview harness,
    // which 404s outright instead) — treat the same as a load failure: nothing worth drawing.
    iconStatus[type] = (img.naturalWidth <= 1 && img.naturalHeight <= 1) ? 'none' : 'ok';
    render();   // now that the type is resolved, re-render so it shows without waiting for the
                 // next 1s poll (state hasn't changed, but iconStatus has)
  };
  img.onerror = function () { iconStatus[type] = 'none'; render(); };
  img.src = '/icon?type=' + encodeURIComponent(type);
  return 'pending';
}

function relinquishTo(id) {
  sendCommand('sqd.relinquish', { peer: id }).catch(function () {});
}

function kick(id) {
  sendCommand('sqd.kick', { peer: id }).catch(function () {});
}

function render() {
  if (!state) return;

  // Notice toast — shown once per new sequence, auto-hides.
  if (state.noticeSeq > lastNoticeSeq && state.notice) {
    lastNoticeSeq = state.noticeSeq;
    noticeEl.textContent = state.notice;
    noticeEl.style.display = '';
    if (noticeTimer) clearTimeout(noticeTimer);
    noticeTimer = setTimeout(function () { noticeEl.style.display = 'none'; }, 6000);
  }

  const invites = state.pendingInvites || [];
  const hasPending = invites.length > 0;
  inviteSection.style.display = hasPending ? '' : 'none';
  if (hasPending) renderInviteCards(invites);

  // CREATE SQUAD — only meaningful while role is "none" (Squad.cs's CreateSquad requires it).
  // Disabled (with the same reasoning/tooltip as the old per-row disable) while our own incoming
  // invite(s) are still undecided — CreateSquad refuses until that's resolved.
  const showCreate = state.role === 'none';
  createSection.style.display = showCreate ? '' : 'none';
  if (showCreate) {
    createOpenBtn.disabled = hasPending;
    createOpenBtn.title = hasPending ? 'Decide your own pending invite(s) first' : '';
    createPrompt.style.display = creatingSquad ? 'none' : '';
    createForm.style.display = creatingSquad ? '' : 'none';
  } else {
    creatingSquad = false;
  }

  // Roster: visible unless we're already a plain MEMBER of someone else's squad — browsing/
  // deciding an incoming invite (above) doesn't hide it. Each row's own INVITE button only
  // appears once we're actually a LEADER (Squad.cs's Invite() requires CreateSquad first); while
  // role is "none" this is browse-only, which is why it's never disabled the way the create
  // button above is — there's nothing here that could fail, just nothing to click yet.
  const canInvite = state.role !== 'member';
  rosterSection.style.display = canInvite ? '' : 'none';
  if (canInvite) renderRoster(state.role === 'leader');

  const showPending = state.role === 'leader' && state.pendingSent.length > 0;
  pendingSection.style.display = showPending ? '' : 'none';
  if (showPending) {
    pendingRows.innerHTML = '';
    state.pendingSent.forEach(function (p) {
      const row = document.createElement('div');
      row.className = 'sqd-row';
      const name = document.createElement('span');
      name.className = 'sqd-row-name';
      name.textContent = p.name || p.id;
      row.appendChild(name);
      pendingRows.appendChild(row);
    });
  }

  const inSquad = state.role === 'leader' || state.role === 'member';
  squadSection.style.display = inSquad ? '' : 'none';
  if (inSquad) renderSquad();
}

// One card per queued incoming invite (state.pendingInvites, oldest first — Squad.cs's
// _pendingReceived), each independently accept/decline-able by its own leaderId. Accepting any one
// declines the rest server-side (Squad.cs's AcceptInvite), so this render doesn't need to do
// anything special about the others disappearing — the next poll just reflects the empty list.
function renderInviteCards(invites) {
  inviteCards.innerHTML = '';
  invites.forEach(function (inv) {
    const card = document.createElement('div');
    card.className = 'sqd-invite-card';

    const text = document.createElement('div');
    text.className = 'sqd-invite-text';
    const leaderEl = document.createElement('span');
    leaderEl.className = 'sqd-invite-leader';
    leaderEl.textContent = inv.leaderName || inv.leaderId;
    text.appendChild(leaderEl);
    const count = inv.members.length;
    text.appendChild(document.createTextNode(
      ' invites you to their squad (' + count + ' member' + (count === 1 ? '' : 's') + ')'));

    const actions = document.createElement('div');
    actions.className = 'sqd-invite-actions';
    const accept = document.createElement('button');
    accept.className = 'sqd-btn pad-hoverable'; accept.textContent = 'ACCEPT';
    accept.onclick = function () { acceptInvite(inv.leaderId); };
    const decline = document.createElement('button');
    decline.className = 'sqd-btn sqd-btn-ghost pad-hoverable'; decline.textContent = 'DECLINE';
    decline.onclick = function () { declineInvite(inv.leaderId); };
    actions.appendChild(accept); actions.appendChild(decline);

    card.appendChild(text); card.appendChild(actions);
    inviteCards.appendChild(card);
  });
}

// showInvite: only true once we're a LEADER (Squad.cs's Invite() requires CreateSquad to have
// already run) — while role is "none" this list is browse-only, pointing at the CREATE SQUAD
// prompt above instead. Never disabled the way the create button is: a leader can never have an
// undecided incoming invite of their own (HandleInvite refuses one while already in a squad), so
// there's no state where this button would be visible but blocked.
function renderRoster(showInvite) {
  rosterRows.innerHTML = '';
  const invited = {};
  if (state.role === 'leader') state.pendingSent.forEach(function (p) { invited[p.id] = true; });
  players.forEach(function (p) {
    if (invited[p.id]) return;   // already invited — don't offer it twice
    const row = document.createElement('div');
    row.className = 'sqd-row';
    const name = document.createElement('span');
    name.className = 'sqd-row-name';
    name.textContent = p.name || p.id;
    row.appendChild(name);
    if (showInvite) {
      const btn = document.createElement('button');
      btn.className = 'sqd-row-btn pad-hoverable'; btn.textContent = 'INVITE';
      btn.onclick = function () { invite(p.id, p.name); };
      row.appendChild(btn);
    }
    rosterRows.appendChild(row);
  });
}

// Squadron Callsign System (issue #42) — "<CALLSIGN> <FLIGHT>-<MEMBER>", e.g. "TALON 1-2". FLIGHT
// is Squad.cs's own fixed-at-creation number; MEMBER is the join-order number this function's own
// callers already compute (1 = leader). TD's own squad buttons render the identical format off the
// same state fields — see td.js's squadSlots/renderLeader.
function squadDesignation(memberNumber) {
  return (state.callsign || 'SQD') + ' ' + (state.flight || 1) + '-' + memberNumber;
}

// One row of the roster table: [callsign+number] [player name] [LEADER badge, or for the leader
// viewing a subordinate: a star to promote them (relinquishTo) and a x to kick them (sqd.kick,
// docs/squadron-transport.md)]. Plain Unicode symbols, not emoji — same rule the rest of the app's
// row icons already follow (WPT's ✎/↺/⇩/⇪/×): U+2605 BLACK STAR has no emoji presentation, unlike
// U+2B50 "star" emoji, which does.
// aircraft is the unitName (e.g. "F-16C") from Squad.cs's BuildStateJson — "" whenever this pilot
// has nothing to report (dead, ejected, not spawned yet) or isn't visible at all right now, which
// this renders as a blank 3rd column rather than any placeholder text/icon.
function addSquadRow(number, name, aircraft, isLeaderRow, isSelf, memberId) {
  const row = document.createElement('div');
  row.className = 'sqd-row' + (isSelf ? ' self' : '');

  const tag = document.createElement('span');
  tag.className = 'sqd-row-tag';
  tag.textContent = squadDesignation(number);

  const nameEl = document.createElement('span');
  nameEl.className = 'sqd-row-name';
  nameEl.textContent = name;

  const aircraftEl = document.createElement('span');
  aircraftEl.className = 'sqd-row-aircraft';
  if (aircraft) {
    // Reuses the same /icon?type= endpoint MAP already draws its blips from (TelemetryServer.cs).
    // getIconStatus (above) resolves each type at most once — 'ok' shows the icon, 'none' (or
    // still 'pending' this render) shows just the name, with no per-render flash either way.
    if (getIconStatus(aircraft) === 'ok') {
      const icon = document.createElement('img');
      icon.className = 'sqd-row-aircraft-icon';
      icon.src = '/icon?type=' + encodeURIComponent(aircraft);
      icon.alt = '';
      aircraftEl.appendChild(icon);
    }
    aircraftEl.appendChild(document.createTextNode(aircraft));
  }

  row.appendChild(tag); row.appendChild(nameEl); row.appendChild(aircraftEl);

  if (isLeaderRow) {
    // sqd-row-trailing: pushes this whole trailing group to the row's right edge explicitly
    // (margin-left: auto), rather than leaning on .sqd-row-name's flex:1 to soak up the leftover
    // space as a side effect — every other column stays left-aligned by default (no text-align
    // rule touches them), only this last one is ever meant to hug the right edge.
    const mark = document.createElement('span');
    mark.className = 'sqd-row-mark sqd-row-trailing'; mark.textContent = 'LEADER';
    row.appendChild(mark);
  } else if (state.role === 'leader') {
    const star = document.createElement('button');
    star.className = 'sqd-row-icon-btn sqd-row-trailing pad-hoverable'; star.textContent = '★'; star.title = 'Make leader';
    star.onclick = function () { relinquishTo(memberId); };
    row.appendChild(star);

    const kickBtn = document.createElement('button');
    kickBtn.className = 'sqd-row-icon-btn pad-hoverable'; kickBtn.textContent = '×'; kickBtn.title = 'Kick from squad';
    kickBtn.onclick = function () { kick(memberId); };
    row.appendChild(kickBtn);
  }
  squadRows.appendChild(row);
}

function renderSquad() {
  squadRows.innerHTML = '';
  const isLeader = state.role === 'leader';

  // The callsign itself in --no-squad (theme.css, the same token WPT's SQD label and this row
  // table's LEADER mark use) — a nested span rather than colouring the whole title, so "SQUAD"
  // stays this page's normal heading colour.
  squadHead.innerHTML = '';
  const callsignSpan = document.createElement('span');
  callsignSpan.className = 'sqd-squad-callsign';
  callsignSpan.textContent = state.callsign || 'YOUR';
  squadHead.appendChild(callsignSpan);
  squadHead.appendChild(document.createTextNode(' SQUAD'));

  callsignEditBtn.style.display = isLeader ? '' : 'none';
  const showEdit = isLeader && editingCallsign;
  squadHead.style.display = showEdit ? 'none' : '';
  callsignEdit.style.display = showEdit ? '' : 'none';
  callsignEditBtn.textContent = showEdit ? 'CANCEL' : 'EDIT';

  disbandBtn.style.display = isLeader ? '' : 'none';

  // Number 1 is always the leader — this pilot themselves when leading (state.selfName, since a
  // leader has no reason to appear in their own state.members list), or state.leaderName when a
  // member. Every entry in state.members is numbered from there in join order (Squad.cs's own
  // _members list only ever appends, per its own header comment, so index IS join order) —
  // members[0] becomes 2, members[1] becomes 3, and so on.
  const leaderName = isLeader ? (state.selfName || '—') : (state.leaderName || state.leaderId);
  const leaderAircraft = isLeader ? state.selfAircraft : state.leaderAircraft;
  addSquadRow(1, leaderName, leaderAircraft, true, isLeader, null);

  state.members.forEach(function (m, i) {
    addSquadRow(i + 2, m.name || m.id, m.aircraft, false, m.id === state.self, m.id);
  });
}

// `s` is /squad's own {ready, state} shape — identical whether it came from the one-time bootstrap
// fetch below or a later shell-forwarded 'sqd-state' push (SseHub.cs wraps both the same way on
// purpose, docs/sse-push-refactor.md).
function applySquad(s) {
  if (!s) return;
  unavailableEl.style.display = s.ready ? 'none' : '';
  if (!s.ready) return;
  state = s.state;
  render();
}

function refreshSquad() {
  return fetch('/squad').then(function (r) { return r.ok ? r.json() : null; }).then(applySquad).catch(function () {});
}

function refreshPlayers() {
  return fetch('/server-players')
    .then(function (r) { return r.ok ? r.json() : null; })
    .then(function (list) {
      if (!Array.isArray(list)) return;
      players = list;
      if (state) render();
    })
    .catch(function () {});
}

// One-time bootstrap fetch — covers the brief gap before the shell's first 'sqd-state' push arrives
// (docs/sse-push-refactor.md), and standalone/preview contexts with no shell at all (this page is
// polled directly there, same as before). Every update after this rides the push instead of a
// recurring poll — no shell relay was needed before because a plain 1s poll was "simple and plenty
// for a UI that isn't latency-sensitive" (sqd.html's own header comment), but the relay now exists
// for TGT/TD/WPT's own squad-state needs, so reusing it here is no longer extra machinery, just an
// extra listener.
refreshSquad();
window.addEventListener('message', function (e) {
  const m = e.data;
  if (!m || m.mfd !== true || m.type !== 'sqd-state') return;
  applySquad(m.data);
});
// No SSE equivalent for /server-players (who's currently in the match) — a light 2s poll remains.
refreshPlayers();
setInterval(refreshPlayers, 2000);

// ── PAD cursor (docs/page-cursor.md) ──────────────────────────────────────────────────
// Same crosshair/transport WPT uses (pad-cursor.js), driven here only while SQD is the SOI's
// focused surface (mfd.js/f35.js's PAD_CURSOR_PAGES). Every clickable control already has a real
// onclick, so Select is just a synthetic click at the crosshair's point. #pad-cursor is
// position:fixed (page-chrome.css's #pad-cursor override, shared with WPT) — this page's own body
// scrolls rather than a fixed-size panel, so (x, y) here are already plain viewport coordinates.
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

// Hover feedback (docs/page-cursor.md #2): the shared .pad-hoverable/.pad-hover pair (theme.css).
// Tolerates a row being destroyed/recreated out from under it (render() rebuilds the roster on
// every poll) — a stale hoveredEl just fails the `===` check and gets replaced next move.
let hoveredEl = null;
function padCursorMoveAt(x, y) {
  const raw = x == null ? null : document.elementFromPoint(x, y);
  const el = raw && raw.closest(CURSORABLE);
  if (el === hoveredEl) return;
  if (hoveredEl) hoveredEl.classList.remove('pad-hover');
  hoveredEl = el;
  if (hoveredEl) hoveredEl.classList.add('pad-hover');
}

// Zoom In/Out (map-act's zoom-in/zoom-out) repurposed to scroll the page, same as WPT/TGT/HUD —
// nothing on this page to zoom, and the binds already exist end-to-end (docs/page-cursor.md).
const SCROLL_STEP = 60;   // ponytail: flat constant tuned by feel, like pad-cursor.js's own SPEED

window.addEventListener('message', function (e) {
  const m = e.data;
  if (!m || m.mfd !== true) return;
  if (m.action === 'cursor-focus') cursor.setFocus(!!m.on, window.innerWidth / 2, window.innerHeight / 2);
  else if (m.action === 'cursor') cursor.setVector(m.x, m.y);
  else if (m.action === 'cursor-select') cursor.select();
  else if (m.action === 'zoom-in') window.scrollBy({ top: SCROLL_STEP });
  else if (m.action === 'zoom-out') window.scrollBy({ top: -SCROLL_STEP });
});
