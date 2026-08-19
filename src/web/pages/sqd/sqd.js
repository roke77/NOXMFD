// SQD page (docs/squadron-transport.md) — squad membership over Steam P2P. Polls GET /squad and
// GET /server-players directly (no shell relay — see sqd.html's header comment) and drives every
// action through POST /command's sqd.* handlers. All protocol logic (who can invite whom, single-
// squad enforcement, succession) lives plugin-side (Squad.cs); this page only renders state and
// dispatches commands.

if (window.parent !== window) {
  const back = document.querySelector('.sqd-back');
  if (back) back.remove();
}

const unavailableEl   = document.getElementById('sqd-unavailable');
const noticeEl        = document.getElementById('sqd-notice');
const inviteSection   = document.getElementById('sqd-invite-section');
const inviteLeaderEl  = document.getElementById('sqd-invite-leader');
const inviteCountEl   = document.getElementById('sqd-invite-count');
const invitePluralEl  = document.getElementById('sqd-invite-plural');
const inviteAccept    = document.getElementById('sqd-invite-accept');
const inviteDecline   = document.getElementById('sqd-invite-decline');
const callsignSection = document.getElementById('sqd-callsign-section');
const callsignInput   = document.getElementById('sqd-callsign-input');
const callsignSet     = document.getElementById('sqd-callsign-set');
const rosterSection   = document.getElementById('sqd-roster-section');
const rosterRows      = document.getElementById('sqd-roster-rows');
const pendingSection  = document.getElementById('sqd-pending-section');
const pendingRows     = document.getElementById('sqd-pending-rows');
const squadSection    = document.getElementById('sqd-squad-section');
const squadHead       = document.getElementById('sqd-squad-head');
const squadRows       = document.getElementById('sqd-squad-rows');
const leaveBtn        = document.getElementById('sqd-leave');
const disbandBtn      = document.getElementById('sqd-disband');

let lastNoticeSeq = -1;
let noticeTimer = null;
let state = null;   // last-known Squad.StateJson payload (null until the first successful poll)
let players = [];   // last-known /server-players list

inviteAccept.onclick  = function () { sendCommand('sqd.accept', {}).catch(function () {}); };
inviteDecline.onclick = function () { sendCommand('sqd.decline', {}).catch(function () {}); };
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

callsignSet.onclick = function () {
  const name = callsignInput.value.trim();
  if (!name) return;
  sendCommand('sqd.set-callsign', { name: name }).catch(function () {});
};

function relinquishTo(id) {
  sendCommand('sqd.relinquish', { peer: id }).catch(function () {});
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

  const hasPending = !!state.pendingInvite;
  inviteSection.style.display = hasPending ? '' : 'none';
  if (hasPending) {
    const inv = state.pendingInvite;
    inviteLeaderEl.textContent = inv.leaderName || inv.leaderId;
    inviteCountEl.textContent = inv.members.length;
    invitePluralEl.textContent = inv.members.length === 1 ? '' : 's';
  }

  // Roster picker and callsign naming: only meaningful while we're not already deciding on, or
  // part of, someone else's squad — same condition for both, a plain member owns neither.
  const canInvite = state.role !== 'member' && !hasPending;
  rosterSection.style.display = canInvite ? '' : 'none';
  if (canInvite) renderRoster();

  callsignSection.style.display = canInvite ? '' : 'none';
  // Don't clobber the input while the pilot is actively typing a new one.
  if (canInvite && document.activeElement !== callsignInput) {
    callsignInput.value = state.callsign || '';
  }

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

function renderRoster() {
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
    const btn = document.createElement('button');
    btn.className = 'sqd-row-btn'; btn.textContent = 'INVITE';
    btn.onclick = function () { invite(p.id, p.name); };
    row.appendChild(name); row.appendChild(btn);
    rosterRows.appendChild(row);
  });
}

// One row of the roster table: [callsign+number] [player name] [LEADER badge / MAKE LEADER btn].
function addSquadRow(number, name, isLeaderRow, isSelf, memberId) {
  const row = document.createElement('div');
  row.className = 'sqd-row' + (isSelf ? ' self' : '');

  const tag = document.createElement('span');
  tag.className = 'sqd-row-tag';
  tag.textContent = (state.callsign || 'SQD') + ' ' + number;

  const nameEl = document.createElement('span');
  nameEl.className = 'sqd-row-name';
  nameEl.textContent = name;

  row.appendChild(tag); row.appendChild(nameEl);

  if (isLeaderRow) {
    const mark = document.createElement('span');
    mark.className = 'sqd-row-mark'; mark.textContent = 'LEADER';
    row.appendChild(mark);
  } else if (state.role === 'leader') {
    const btn = document.createElement('button');
    btn.className = 'sqd-row-btn'; btn.textContent = 'MAKE LEADER';
    btn.onclick = function () { relinquishTo(memberId); };
    row.appendChild(btn);
  }
  squadRows.appendChild(row);
}

function renderSquad() {
  squadRows.innerHTML = '';
  const isLeader = state.role === 'leader';
  const callsignLabel = state.callsign ? state.callsign + ' SQUAD' : 'YOUR SQUAD';
  squadHead.textContent = callsignLabel + (isLeader ? ' — LEADER' : '');
  disbandBtn.style.display = isLeader ? '' : 'none';

  // Number 1 is always the leader — this pilot themselves when leading (state.selfName, since a
  // leader has no reason to appear in their own state.members list), or state.leaderName when a
  // member. Every entry in state.members is numbered from there in join order (Squad.cs's own
  // _members list only ever appends, per its own header comment, so index IS join order) —
  // members[0] becomes 2, members[1] becomes 3, and so on.
  const leaderName = isLeader ? (state.selfName || '—') : (state.leaderName || state.leaderId);
  addSquadRow(1, leaderName, true, isLeader, null);

  state.members.forEach(function (m, i) {
    addSquadRow(i + 2, m.name || m.id, false, m.id === state.self, m.id);
  });
}

function refreshSquad() {
  return fetch('/squad')
    .then(function (r) { return r.ok ? r.json() : null; })
    .then(function (s) {
      if (!s) return;
      unavailableEl.style.display = s.ready ? 'none' : '';
      if (!s.ready) return;
      state = s.state;
      render();
    })
    .catch(function () {});
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

refreshSquad();
refreshPlayers();
setInterval(refreshSquad, 1000);
setInterval(refreshPlayers, 2000);
