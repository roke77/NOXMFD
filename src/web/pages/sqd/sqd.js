// SQD page (docs/squadron-transport.md) — squad membership over Steam P2P. Bootstraps from GET
// /squad and GET /server-players, then receives later changes through the shell's SSE relay
// (docs/sse-push-refactor.md), and drives every action through POST /command's sqd.* handlers. All
// protocol logic (who can invite whom, single-squad enforcement, succession) lives plugin-side
// (Squad.cs); this page only renders state and dispatches commands. state.faction carries every other
// squad in the faction (docs/faction-broadcast.md), listed read-only below the pilot's own squad.
import { createPadCursor } from '/assets/services/pad-cursor.js';
import { SQUAD_CALLSIGNS } from './callsigns.js';

if (window.parent !== window) {
  const back = document.querySelector('.sqd-back');
  if (back) back.remove();
}

const unavailableEl    = document.getElementById('sqd-unavailable');
const noticeEl         = document.getElementById('sqd-notice');
const panelEl          = document.getElementById('sqd-panel');
const countEl          = document.getElementById('sqd-count');
const createSection    = document.getElementById('sqd-create-section');
const createCallsign   = document.getElementById('sqd-create-callsign');
const createFlights    = document.getElementById('sqd-create-flights');
const createDup        = document.getElementById('sqd-create-dup');
const createConfirmBtn = document.getElementById('sqd-create-confirm');
const inviteCards      = document.getElementById('sqd-invite-cards');
const rosterSection    = document.getElementById('sqd-roster-section');
const rosterRows       = document.getElementById('sqd-roster-rows');
const rosterHead       = document.getElementById('sqd-roster-head');
const rosterTitle      = document.getElementById('sqd-roster-title');
const rosterBody       = document.getElementById('sqd-roster-body');
const squadSection     = document.getElementById('sqd-squad-section');
const squadCard        = document.getElementById('sqd-squad-card');
const squadHead        = document.getElementById('sqd-squad-head');
const squadNote        = document.getElementById('sqd-squad-note');
const squadDup         = document.getElementById('sqd-squad-dup');
const callsignEdit     = document.getElementById('sqd-callsign-edit');
const callsignSelect   = document.getElementById('sqd-callsign-select');
const callsignFlights  = document.getElementById('sqd-callsign-flights');
const callsignSet      = document.getElementById('sqd-callsign-set');
const callsignCancel   = document.getElementById('sqd-callsign-cancel');
const callsignEditBtn  = document.getElementById('sqd-callsign-edit-btn');
const squadRows        = document.getElementById('sqd-squad-rows');
const othersEl         = document.getElementById('sqd-others');
const leaveBtn         = document.getElementById('sqd-leave');
const disbandBtn       = document.getElementById('sqd-disband');

let state = null;   // last-known Squad.StateJson payload (null until the first successful poll)

// Callsign picker (issue #42) — populated once at load, not rebuilt per render: the option list
// itself never changes, only which <option> is selected.
function fillOptions(select, values) {
  values.forEach(function (v) {
    const opt = document.createElement('option');
    opt.value = v; opt.textContent = v;
    select.appendChild(opt);
  });
}
fillOptions(createCallsign, SQUAD_CALLSIGNS);
fillOptions(callsignSelect, SQUAD_CALLSIGNS);

// Every OTHER squad in the faction (state.faction), never the pilot's own.
function factionSquads() { return (state && state.faction && state.faction.squads) || []; }

// Flight number 1-9 as a row of buttons, the chosen one lit. A flight that another squad already
// flies under the picked callsign is marked amber but stays selectable (duplicate designations are
// allowed, with warnings). The chosen value lives in `flights`, one entry per picker.
const flights = { create: 1, edit: 1 };
const flightPickers = [
  { box: createFlights, select: createCallsign, key: 'create' },
  { box: callsignFlights, select: callsignSelect, key: 'edit' },
];
function buildFlights(container, key) {
  for (let f = 1; f <= 9; f++) {
    const btn = document.createElement('button');
    btn.type = 'button';
    btn.className = 'sqd-flight-btn pad-hoverable';
    btn.textContent = String(f);
    btn.onclick = function () { flights[key] = f; markFlights(); };
    container.appendChild(btn);
  }
}
function markFlights() {
  const used = {};
  factionSquads().forEach(function (sq) { used[sq.callsign.toUpperCase() + '|' + sq.flight] = true; });
  flightPickers.forEach(function (p) {
    const callsign = (p.select.value || '').toUpperCase();
    Array.prototype.forEach.call(p.box.children, function (btn, i) {
      btn.classList.toggle('on', i + 1 === flights[p.key]);
      btn.classList.toggle('used', !!used[callsign + '|' + (i + 1)]);
    });
  });
  const picked = (createCallsign.value || '').toUpperCase();
  const clash = !!used[picked + '|' + flights.create];
  createDup.style.display = clash ? '' : 'none';
  createDup.textContent = picked + ' ' + flights.create + ' ALREADY FLYING';
}
buildFlights(createFlights, 'create');
buildFlights(callsignFlights, 'edit');
createCallsign.onchange = markFlights;
callsignSelect.onchange = markFlights;
markFlights();

let lastNoticeSeq = -1;
let noticeTimer = null;
let players = [];   // last-known /server-players list
let editingCallsign = false;   // EDIT swaps the squad card for the callsign editor, in place

function acceptInvite(leaderId) { sendCommand('sqd.accept', { peer: leaderId }).catch(function () {}); }
function declineInvite(leaderId) { sendCommand('sqd.decline', { peer: leaderId }).catch(function () {}); }

// Creating a squad declines every pending invite plugin-side (Squad.cs's CreateSquad).
createConfirmBtn.onclick = function () {
  const name = createCallsign.value;
  if (name) sendCommand('sqd.create', { name: name, index: flights.create }).catch(function () {});
};

disbandBtn.onclick = function () { sendCommand('sqd.disband', {}).catch(function () {}); };
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
  editingCallsign = true;
  callsignSelect.value = (state && state.callsign) || '';
  flights.edit = (state && state.flight) || 1;
  markFlights();
  render();
};
callsignCancel.onclick = function () { editingCallsign = false; render(); };
callsignSet.onclick = function () {
  const name = callsignSelect.value;
  if (name) sendCommand('sqd.set-callsign', { name: name, index: flights.edit }).catch(function () {});
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
// Bumped whenever a type resolves — renderSquad's row-rebuild signature includes this, since an
// icon resolving is a real reason to redraw even though nothing in `state`/`players` changed.
let iconStatusVersion = 0;

function getIconStatus(type) {
  if (!type) return null;
  if (iconStatus[type]) return iconStatus[type];
  iconStatus[type] = 'pending';
  const img = new Image();
  img.onload = function () {
    // 1×1 = the server's "no icon" sentinel (real plugin only, not this static preview harness,
    // which 404s outright instead) — treat the same as a load failure: nothing worth drawing.
    iconStatus[type] = (img.naturalWidth <= 1 && img.naturalHeight <= 1) ? 'none' : 'ok';
    iconStatusVersion++;
    render();   // now that the type is resolved, re-render so it shows without waiting for the
                 // next 1s poll (state hasn't changed, but iconStatus has)
  };
  img.onerror = function () { iconStatus[type] = 'none'; iconStatusVersion++; render(); };
  img.src = '/icon?type=' + encodeURIComponent(type);
  return 'pending';
}

function relinquishTo(id) {
  sendCommand('sqd.relinquish', { peer: id }).catch(function () {});
}

function kick(id) {
  sendCommand('sqd.kick', { peer: id }).catch(function () {});
}

// ▲/▼: move a member one number up (-1) or down (+1); the leader always stays 1
// (docs/squad-callsign-names.md).
function moveMember(id, dir) {
  sendCommand('sqd.move-member', { peer: id, index: dir }).catch(function () {});
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

  const noSquad = state.role === 'none';
  const inSquad = !noSquad;

  // Top to bottom: CREATE SQUAD (only while role is "none", Squad.cs's CreateSquad requires it),
  // incoming invites, the pilot's own squad, then every other squad in the faction.
  createSection.style.display = noSquad ? '' : 'none';
  renderInviteCards(noSquad ? (state.pendingInvites || []) : []);
  markFlights();

  squadSection.style.display = inSquad ? '' : 'none';
  if (inSquad) renderSquad(); else editingCallsign = false;
  renderOthers();
  renderCount();

  // Player list: hidden for a plain member, who can't invite anyone. Each row's INVITE button
  // only appears once we're a LEADER (Squad.cs's Invite() requires CreateSquad first).
  const showRoster = state.role !== 'member';
  rosterSection.style.display = showRoster ? '' : 'none';
  if (showRoster) renderRoster(state.role === 'leader'); else rosterRole = null;
}

// "4 SQUADS \u00b7 10 PILOTS" over the whole faction: the pilot's own squad plus every other one.
function renderCount() {
  const others = factionSquads();
  let squads = others.length;
  let pilots = 0;
  others.forEach(function (sq) { pilots += sq.members.length; });
  if (state.role !== 'none') { squads++; pilots += 1 + state.members.length; }
  countEl.textContent = squads
    ? squads + (squads === 1 ? ' SQUAD' : ' SQUADS') + ' \u00b7 ' + pilots + (pilots === 1 ? ' PILOT' : ' PILOTS')
    : '';
}

// The slot a pilot joining a squad with these members takes: the lowest free one from 2 up
// (SquadDesignations.FirstFreeSlot).
function firstFreeSlot(members) {
  const taken = {};
  members.forEach(function (m) { taken[m.slot] = true; });
  let n = 2;
  while (taken[n]) n++;
  return n;
}

// One card per queued incoming invite (state.pendingInvites, oldest first — Squad.cs's
// _pendingReceived), each independently accept/decline-able by its own leaderId. Accepting any one
// declines the rest server-side (Squad.cs's AcceptInvite), as does creating a squad, so the next
// push just reflects the shorter list.
function renderInviteCards(invites) {
  inviteCards.innerHTML = '';
  invites.forEach(function (inv) {
    const card = document.createElement('div');
    card.className = 'sqd-invite-card';

    const key = document.createElement('span');
    key.className = 'sqd-invite-key';
    key.textContent = inv.callsign ? inv.callsign + ' ' + (inv.flight || 1) : (inv.leaderName || inv.leaderId);

    const text = document.createElement('span');
    text.className = 'sqd-invite-text';
    const from = 'from ' + (inv.leaderName || inv.leaderId);
    text.textContent = inv.callsign
      ? from + ' \u00b7 as ' + inv.callsign + ' ' + (inv.flight || 1) + '-' + firstFreeSlot(inv.members)
      : from;

    const actions = document.createElement('div');
    actions.className = 'sqd-invite-actions';
    const accept = document.createElement('button');
    accept.className = 'sqd-btn sqd-btn-squad pad-hoverable'; accept.textContent = 'ACCEPT';
    accept.onclick = function () { acceptInvite(inv.leaderId); };
    const decline = document.createElement('button');
    decline.className = 'sqd-btn sqd-btn-dim pad-hoverable'; decline.textContent = 'DECLINE';
    decline.onclick = function () { declineInvite(inv.leaderId); };
    actions.appendChild(accept); actions.appendChild(decline);

    card.appendChild(key); card.appendChild(text); card.appendChild(actions);
    inviteCards.appendChild(card);
  });
}

// The player list (not shown to a plain member): in-match players who aren't in any squad, plus the players we've invited and are
// awaiting (tagged INVITED, no button — Squad.cs has no way to withdraw an invite). Expanded by
// default only while not in a squad; the default is re-applied whenever the role changes (creating
// or joining a squad collapses it, leaving expands it), and a manual toggle holds until then.
let rosterOpen = true;
let rosterRole = null;
function applyRosterOpen() {
  rosterSection.classList.toggle('sqd-collapsed', !rosterOpen);
  rosterHead.setAttribute('aria-expanded', rosterOpen ? 'true' : 'false');
}
function toggleRoster() { rosterOpen = !rosterOpen; applyRosterOpen(); }
rosterHead.addEventListener('click', toggleRoster);
rosterHead.addEventListener('keydown', function (e) {
  if (e.key === 'Enter' || e.key === ' ') { e.preventDefault(); toggleRoster(); }
});

// Memoized by content signature — render() also runs on every squad-state push, so without this a
// state-only update (e.g. a member's aircraft changing) would rebuild the rows for nothing.
let lastRosterSig = null;
function renderRoster(showInvite) {
  if (state.role !== rosterRole) { rosterRole = state.role; rosterOpen = state.role === 'none'; applyRosterOpen(); }

  // Everyone already in a squad, ours or another (a leader's state.members never includes
  // themselves, and /server-players never lists self).
  const assigned = {};
  (state.members || []).forEach(function (m) { assigned[m.id] = true; });
  factionSquads().forEach(function (sq) { sq.members.forEach(function (m) { assigned[m.id] = true; }); });

  const invited = {};
  if (state.role === 'leader') state.pendingSent.forEach(function (p) { invited[p.id] = p.name; });

  const rows = [];
  const listed = {};
  players.forEach(function (p) {
    if (assigned[p.id]) return;
    listed[p.id] = true;
    rows.push({ id: p.id, name: p.name || p.id, invited: p.id in invited });
  });
  // An invitee who has since left the match's roster is still awaiting our invite.
  Object.keys(invited).forEach(function (id) {
    if (!listed[id]) rows.push({ id: id, name: invited[id] || id, invited: true });
  });

  const sig = showInvite + '|' + rows.map(function (r) { return r.id + ':' + r.name + ':' + (r.invited ? 1 : 0); }).join(',');
  if (sig === lastRosterSig) return;
  lastRosterSig = sig;
  rosterRows.innerHTML = '';
  rows.forEach(function (r) {
    const row = document.createElement('div');
    row.className = 'sqd-player-row' + (r.invited ? ' invited' : '');
    const name = document.createElement('span');
    name.className = 'sqd-player-name';
    name.textContent = r.name;
    row.appendChild(name);
    if (r.invited) {
      const tag = document.createElement('span');
      tag.className = 'sqd-player-tag';
      tag.textContent = 'INVITED \u00b7 AWAITING RESPONSE';
      row.appendChild(tag);
    } else if (showInvite) {
      const btn = document.createElement('button');
      btn.className = 'sqd-btn sqd-btn-squad pad-hoverable'; btn.textContent = 'INVITE';
      btn.onclick = function () { invite(r.id, r.name); };
      row.appendChild(btn);
    }
    rosterRows.appendChild(row);
  });
  rosterTitle.textContent = 'UNASSIGNED PLAYERS (' + rows.length + ')';
}

// Squadron Callsign System (issue #42) — "<CALLSIGN> <FLIGHT>-<MEMBER>", e.g. "TALON 1-2". FLIGHT
// is Squad.cs's own number; MEMBER is the pilot's slot (1 = leader, each member's own `slot`
// otherwise). TD's own squad buttons render the identical format off the same state fields — see
// td.js's squadSlots/renderLeader. `designation` is the same format for any squad, the faction's
// other squads included.
function designation(callsign, flight, memberNumber) {
  return (callsign || 'SQD') + ' ' + (flight || 1) + '-' + memberNumber;
}
function squadDesignation(memberNumber) { return designation(state.callsign, state.flight, memberNumber); }

function iconBtn(cls, glyph, title, onclick) {
  const btn = document.createElement('button');
  btn.className = 'sqd-row-icon-btn pad-hoverable' + (cls ? ' ' + cls : '');
  btn.textContent = glyph; btn.title = title;
  btn.setAttribute('aria-label', title);
  btn.onclick = onclick;
  return btn;
}

// One row of a squad: [designation] [pilot] [aircraft] [LEADER label, or for the leader viewing
// a subordinate in their own squad: ▲/▼ to renumber them (moveMember), a star to promote them
// (relinquishTo) and a x to kick them (sqd.kick, docs/squadron-transport.md)]. Plain Unicode
// symbols, not emoji — same rule the rest of the app's row icons follow: U+2605 BLACK STAR has no
// emoji presentation, unlike U+2B50 "star" emoji, which does.
// o: {tag, name, aircraft, leaderRow, self, controls, memberId, number}. aircraft is the unitName
// (e.g. "F-16C") — "" whenever this pilot has nothing to report (dead, ejected, not spawned yet) or
// isn't visible right now, which renders as a blank column rather than any placeholder.
function addSquadRow(container, o) {
  const row = document.createElement('div');
  row.className = 'sqd-squad-row' + (o.self ? ' self' : '');

  const tag = document.createElement('span');
  tag.className = 'sqd-row-tag';
  tag.textContent = o.tag;

  const nameEl = document.createElement('span');
  nameEl.className = 'sqd-row-name';
  nameEl.textContent = o.name;

  const aircraftEl = document.createElement('span');
  aircraftEl.className = 'sqd-row-aircraft';
  if (o.aircraft) {
    // Reuses the same /icon?type= endpoint MAP already draws its blips from (TelemetryServer.cs).
    // getIconStatus (above) resolves each type at most once — 'ok' shows the icon, 'none' (or
    // still 'pending' this render) shows just the name, with no per-render flash either way.
    if (getIconStatus(o.aircraft) === 'ok') {
      const icon = document.createElement('img');
      icon.className = 'sqd-row-aircraft-icon';
      icon.src = '/icon?type=' + encodeURIComponent(o.aircraft);
      icon.alt = '';
      aircraftEl.appendChild(icon);
    } else {
      aircraftEl.classList.add('plain');   // no icon: keep the name aligned with rows that have one
    }
    aircraftEl.appendChild(document.createTextNode(o.aircraft));
  }

  row.appendChild(tag); row.appendChild(nameEl); row.appendChild(aircraftEl);

  const trailing = document.createElement('span');
  if (o.leaderRow) {
    trailing.className = 'sqd-row-mark'; trailing.textContent = 'LEADER';
  } else if (o.controls) {
    // Both arrows on every row, the unusable one hidden rather than left out, so the star and x
    // line up down the table.
    trailing.className = 'sqd-row-ctl';
    [[-1, '\u25b2', 'Move up', o.number > 2], [1, '\u25bc', 'Move down', o.number < lastSlot()]]
      .forEach(function (a) {
        const btn = iconBtn('', a[1], a[2], a[3] ? function () { moveMember(o.memberId, a[0]); } : null);
        if (!a[3]) { btn.disabled = true; btn.style.visibility = 'hidden'; }
        trailing.appendChild(btn);
      });
    trailing.appendChild(iconBtn('star', '\u2605', 'Make leader', function () { relinquishTo(o.memberId); }));
    trailing.appendChild(iconBtn('kick', '\u00d7', 'Kick from squad', function () { kick(o.memberId); }));
  }
  row.appendChild(trailing);
  container.appendChild(row);
}

// Highest held slot — state.members arrives sorted by slot (Squad.cs's SortMembers). ▼ stops here,
// and empty slots below it render as OPEN rows.
function lastSlot() {
  return state.members.length ? state.members[state.members.length - 1].slot : 1;
}

// A slot left empty by a departure (docs/squad-callsign-names.md): stays until someone joins into
// it or the leader moves a member there with ▲/▼.
function addOpenRow(number) {
  const row = document.createElement('div');
  row.className = 'sqd-squad-row open';
  const tag = document.createElement('span');
  tag.className = 'sqd-row-tag';
  tag.textContent = squadDesignation(number);
  const nameEl = document.createElement('span');
  nameEl.className = 'sqd-row-name';
  nameEl.textContent = 'OPEN';
  const hint = document.createElement('span');
  hint.className = 'sqd-row-aircraft';
  hint.textContent = 'next pilot to join takes this number';
  row.appendChild(tag); row.appendChild(nameEl); row.appendChild(hint);
  row.appendChild(document.createElement('span'));
  squadRows.appendChild(row);
}

// Memoized by content signature, same reasoning as lastRosterSig above — renderSquad() runs on
// every render() call (including a roster-only push, which never touches squad state at all), so
// without this the row table would tear down and rebuild for nothing on every one of them.
let lastSquadRowsSig = null;
function renderSquad() {
  const isLeader = state.role === 'leader';

  // Card title: callsign + flight ("TALON 1"), and the pilot count. Swapped for the editor while
  // EDIT is active (leader only). SAME DESIGNATION shows while another squad flies the same pair.
  squadHead.textContent = (state.callsign || 'YOUR') + ' ' + (state.flight || 1);
  const pilots = 1 + state.members.length;
  squadNote.textContent = pilots + (pilots === 1 ? ' PILOT' : ' PILOTS');
  squadDup.style.display = state.faction && state.faction.selfDup ? '' : 'none';

  callsignEditBtn.style.display = isLeader ? '' : 'none';
  const showEdit = isLeader && editingCallsign;
  squadCard.style.display = showEdit ? 'none' : '';
  callsignEdit.style.display = showEdit ? '' : 'none';
  disbandBtn.style.display = isLeader ? '' : 'none';

  // Number 1 is always the leader — this pilot themselves when leading (state.selfName, since a
  // leader has no reason to appear in their own state.members list), or state.leaderName when a
  // member. Every other row is a slot from 2 up to the highest held one: its member, or OPEN when
  // a departure left it empty.
  const leaderName = isLeader ? (state.selfName || '\u2014') : (state.leaderName || state.leaderId);
  const leaderAircraft = isLeader ? state.selfAircraft : state.leaderAircraft;
  const rowsSig = isLeader + '|' + leaderName + '|' + leaderAircraft + '|' + state.self + '|' + iconStatusVersion + '|' +
    state.callsign + '|' + state.flight + '|' +
    state.members.map(function (m) { return m.id + ':' + m.slot + ':' + (m.name || '') + ':' + (m.aircraft || ''); }).join(',');
  if (rowsSig === lastSquadRowsSig) return;
  lastSquadRowsSig = rowsSig;
  squadRows.innerHTML = '';

  addSquadRow(squadRows, { tag: squadDesignation(1), name: leaderName, aircraft: leaderAircraft, leaderRow: true, self: isLeader });

  const bySlot = {};
  state.members.forEach(function (m) { bySlot[m.slot] = m; });
  for (let n = 2; n <= lastSlot(); n++) {
    const m = bySlot[n];
    if (m) {
      addSquadRow(squadRows, {
        tag: squadDesignation(n), name: m.name || m.id, aircraft: m.aircraft,
        self: m.id === state.self, controls: isLeader, memberId: m.id, number: n,
      });
    } else addOpenRow(n);
  }
}

// Every other squad in the faction (state.faction.squads), read-only: a collapsible container per
// squad, folded per page load by its leader's id. Memoized by content like the rows above.
const folded = {};
let lastOthersSig = null;
function textSpan(cls, text) {
  const el = document.createElement('span');
  el.className = cls;
  el.textContent = text;
  return el;
}
function renderOthers() {
  const squads = factionSquads();
  const sig = iconStatusVersion + '|' + squads.map(function (sq) {
    return sq.leader + ':' + sq.callsign + ':' + sq.flight + ':' + sq.dup + ':' + (folded[sq.leader] ? 1 : 0) + ':' +
      sq.members.map(function (m) { return m.id + '.' + m.slot + '.' + m.name + '.' + m.aircraft; }).join(',');
  }).join(';');
  if (sig === lastOthersSig) return;
  lastOthersSig = sig;
  othersEl.innerHTML = '';
  squads.forEach(function (sq) {
    const box = document.createElement('div');
    box.className = 'sqd-squad' + (folded[sq.leader] ? ' sqd-collapsed' : '');

    const head = document.createElement('button');
    head.type = 'button';
    head.className = 'sqd-squad-head pad-hoverable';
    head.setAttribute('aria-expanded', folded[sq.leader] ? 'false' : 'true');
    const n = sq.members.length;
    head.appendChild(textSpan('sqd-squad-title', sq.callsign + ' ' + sq.flight));
    head.appendChild(textSpan('sqd-note', n + (n === 1 ? ' PILOT' : ' PILOTS')));
    if (sq.dup) head.appendChild(textSpan('sqd-dup', 'SAME DESIGNATION'));
    head.appendChild(textSpan('sqd-spacer', ''));
    head.appendChild(textSpan('sqd-chevron', ''));
    head.onclick = function () { folded[sq.leader] = !folded[sq.leader]; lastOthersSig = null; renderOthers(); };

    const rows = document.createElement('div');
    rows.className = 'sqd-rows';
    sq.members.forEach(function (m) {
      addSquadRow(rows, { tag: designation(sq.callsign, sq.flight, m.slot), name: m.name || m.id, aircraft: m.aircraft, leaderRow: m.slot === 1 });
    });
    box.appendChild(head); box.appendChild(rows);
    othersEl.appendChild(box);
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

function applyPlayers(list) {
  if (!Array.isArray(list)) return;
  players = list;
  if (state) render();
}

function refreshPlayers() {
  return fetch('/server-players').then(function (r) { return r.ok ? r.json() : null; })
    .then(applyPlayers).catch(function () {});
}

// One-time bootstrap fetch — covers the brief gap before the shell's first 'sqd-state'/'server-
// players-push' arrives (docs/sse-push-refactor.md), and standalone/preview contexts with no shell
// at all (this page is polled directly there instead). Every update after this rides the push
// instead of a recurring poll — TGT/TD/WPT already need the same shell relay for their own squad-
// state reads, so this page listening too costs nothing beyond one more message listener.
refreshSquad();
refreshPlayers();
window.addEventListener('message', function (e) {
  const m = e.data;
  if (!m || m.mfd !== true) return;
  if (m.type === 'sqd-state') applySquad(m.data);
  else if (m.type === 'server-players-push') applyPlayers(m.data);
});

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

// Zoom In/Out (map-act's zoom-in/zoom-out) repurposed to scroll, same as WPT/TGT/HUD —
// nothing on this page to zoom, and the binds already exist end-to-end (docs/page-cursor.md).
// The player list scrolls on its own; the panel only when the fixed sections outgrow the pane.
function scrollTarget() { return rosterBody.scrollHeight > rosterBody.clientHeight ? rosterBody : panelEl; }
const SCROLL_STEP = 60;   // ponytail: flat constant tuned by feel, like pad-cursor.js's own SPEED

window.addEventListener('message', function (e) {
  const m = e.data;
  if (!m || m.mfd !== true) return;
  if (m.action === 'cursor-focus') cursor.setFocus(!!m.on, window.innerWidth / 2, window.innerHeight / 2);
  else if (m.action === 'cursor') cursor.setVector(m.x, m.y);
  else if (m.action === 'cursor-select') cursor.select();
  else if (m.action === 'zoom-in') scrollTarget().scrollBy({ top: SCROLL_STEP });
  else if (m.action === 'zoom-out') scrollTarget().scrollBy({ top: -SCROLL_STEP });
});
