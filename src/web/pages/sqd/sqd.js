// SQD page (docs/squadron-transport.md) — squad membership over Steam P2P. Bootstraps from GET
// /squad and GET /server-players, then receives later changes through the shell's SSE relay
// (docs/sse-push-refactor.md), and drives every action through POST /command's sqd.* handlers. All
// protocol logic (who can invite whom, single-squad enforcement, succession) lives plugin-side
// (Squad.cs); this page only renders state and dispatches commands. state.faction carries every other
// squad in the faction (docs/faction-broadcast.md), listed read-only below the pilot's own squad.
// Every pilot flies under their own callsign (docs/self-callsign.md): state.me is this pilot's, and
// state.pilots maps a SteamID to anyone's, so a squad's name and its members' callsigns are
// independent.
import { createPadCursor } from '/assets/services/pad-cursor.js';
import { SQUAD_CALLSIGNS } from './callsigns.js';
import { sharerIds, sharedWithText } from './sqd-pilots.js';

if (window.parent !== window) {
  const back = document.querySelector('.sqd-back');
  if (back) back.remove();
}

const unavailableEl    = document.getElementById('sqd-unavailable');
const noticeEl         = document.getElementById('sqd-notice');
const panelEl          = document.getElementById('sqd-panel');
const countEl          = document.getElementById('sqd-count');
const selfBox          = document.getElementById('sqd-self');
const selfView         = document.getElementById('sqd-self-view');
const selfValue        = document.getElementById('sqd-self-value');
const selfDup          = document.getElementById('sqd-self-dup');
const selfChange       = document.getElementById('sqd-self-change');
const selfEdit         = document.getElementById('sqd-self-edit');
const selfCallsign     = document.getElementById('sqd-self-callsign');
const selfFlight       = document.getElementById('sqd-self-flight');
const selfNumber       = document.getElementById('sqd-self-number');
const selfCancel       = document.getElementById('sqd-self-cancel');
const selfSet          = document.getElementById('sqd-self-set');
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
const ONE_TO_NINE = ['1', '2', '3', '4', '5', '6', '7', '8', '9'];
fillOptions(createCallsign, SQUAD_CALLSIGNS);
fillOptions(callsignSelect, SQUAD_CALLSIGNS);
fillOptions(selfCallsign, SQUAD_CALLSIGNS);
fillOptions(selfFlight, ONE_TO_NINE);
fillOptions(selfNumber, ONE_TO_NINE);

// Every OTHER squad in the faction (state.faction), never the pilot's own.
function factionSquads() { return (state && state.faction && state.faction.squads) || []; }

// This pilot's own callsign (state.me; empty callsign = none set), and anyone's by SteamID
// (state.pilots[id] = {d: "VIPER 1-2", dup: another pilot flies the same}).
function me() { return (state && state.me) || { callsign: '', flight: 1, number: 1 }; }
function pilot(id) { return (state && state.pilots && state.pilots[id]) || null; }
function pilotKey(id) { const p = pilot(id); return p ? p.d + (p.dup ? '!' : '') : ''; }
function meDesignation() { const m = me(); return m.callsign ? m.callsign + ' ' + m.flight + '-' + m.number : ''; }

// SteamID → Steam name for everyone this page has heard of, to name who else flies a callsign.
function nameOf(id) {
  if (state) {
    if (id === state.self) return state.selfName || id;
    if (id === state.leaderId && state.leaderName) return state.leaderName;
    for (const m of state.members || []) if (m.id === id) return m.name || id;
    for (const sq of factionSquads()) for (const m of sq.members) if (m.id === id) return m.name || id;
    for (const inv of state.pendingInvites || []) for (const m of inv.members || []) if (m.id === id) return m.name || id;
  }
  for (const p of players) if (p.id === id) return p.name || id;
  return id;
}
// The other pilots who fly the same callsign as `id`, by name (sqd-pilots.js).
function sharers(id) { return sharerIds(state && state.pilots, id).map(nameOf); }
function sharedWith(id) { return sharedWithText(sharers(id)); }

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
let editingCallsign = false;   // EDIT swaps the squad card for the squad-name editor, in place
let editingSelf = false;       // CHANGE swaps YOUR CALLSIGN for its editor; open from the start while none is set

function acceptInvite(leaderId) { sendCommand('sqd.accept', { peer: leaderId }).catch(function () {}); }
function declineInvite(leaderId) { sendCommand('sqd.decline', { peer: leaderId }).catch(function () {}); }

// Creating a squad declines every pending invite plugin-side (Squad.cs's CreateSquad).
createConfirmBtn.onclick = function () {
  const name = createCallsign.value;
  if (name && me().callsign) sendCommand('sqd.create', { name: name, index: flights.create }).catch(function () {});
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

// ── YOUR CALLSIGN ────────────────────────────────────────────────────────────────────
// "TALON 1-2": a callsign from the same list as squads, a flight and a number, each 1-9. Shown in
// every state; amber until one is set. The dropdowns are seeded once when the editor opens, so a
// state push never overwrites a pick in progress.
let selfSeeded = false;
function renderSelf() {
  const m = me();
  const has = !!m.callsign;
  const editing = !has || editingSelf;
  selfBox.classList.toggle('unset', !has);
  selfView.style.display = editing ? 'none' : '';
  selfEdit.style.display = editing ? '' : 'none';
  selfCancel.style.display = has ? '' : 'none';
  if (editing && !selfSeeded) {
    selfSeeded = true;
    if (has) selfCallsign.value = m.callsign;
    selfFlight.value = String(has ? m.flight : 1);
    selfNumber.value = String(has ? m.number : 1);
  }
  if (!editing) selfSeeded = false;
  if (has) {
    selfValue.textContent = meDesignation();
    const also = sharedWith(state.self);
    selfDup.style.display = also ? '' : 'none';
    selfDup.textContent = also ? 'ALSO FLOWN BY ' + also : '';
  }
}
selfChange.onclick = function () { editingSelf = true; render(); };
selfCancel.onclick = function () { editingSelf = false; render(); };
selfSet.onclick = function () {
  const next = { callsign: selfCallsign.value, flight: Number(selfFlight.value), number: Number(selfNumber.value) };
  if (!next.callsign) return;
  sendCommand('sqd.set-self-callsign', { name: next.callsign, index: next.flight, n: next.number }).catch(function () {});
  if (state) state.me = next;   // shown at once; the pushed state confirms it
  editingSelf = false;
  render();
};

// CREATE SQUAD's pickers start on the pilot's own callsign and flight, again whenever that changes;
// picks the pilot makes in between stay. With no callsign yet the row is dimmed and CREATE is off.
let createSeed = '';
function renderCreate() {
  const m = me();
  const seed = m.callsign ? m.callsign + '|' + m.flight : '';
  if (seed !== createSeed) {
    createSeed = seed;
    if (m.callsign) { createCallsign.value = m.callsign; flights.create = m.flight; }
  }
  const off = !m.callsign;
  createSection.classList.toggle('off', off);
  createCallsign.disabled = off;
  createConfirmBtn.disabled = off;
  createConfirmBtn.title = off ? 'Set your callsign first' : '';
  Array.prototype.forEach.call(createFlights.children, function (btn) { btn.disabled = off; });
}

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

  // Top to bottom: YOUR CALLSIGN, CREATE SQUAD (only while role is "none", Squad.cs's CreateSquad
  // requires it), incoming invites, the pilot's own squad, then every other squad in the faction.
  renderSelf();
  createSection.style.display = noSquad ? '' : 'none';
  renderCreate();
  renderInviteCards(noSquad ? (state.pendingInvites || []) : []);
  markFlights();

  squadSection.style.display = inSquad ? '' : 'none';
  if (inSquad) renderSquad(); else { editingCallsign = false; menuFor = null; }
  renderOthers();
  renderCount();

  // Player list: hidden for a plain member, who can't invite anyone. Each row's INVITE button
  // only appears once we're a LEADER (Squad.cs's Invite() requires CreateSquad first).
  const showRoster = state.role !== 'member';
  rosterSection.style.display = showRoster ? '' : 'none';
  if (showRoster) renderRoster(state.role === 'leader'); else rosterRole = null;
}

// "4 SQUADS · 10 PILOTS" over the whole faction: the pilot's own squad plus every other one.
function renderCount() {
  const others = factionSquads();
  let squads = others.length;
  let pilots = 0;
  others.forEach(function (sq) { pilots += sq.members.length; });
  if (state.role !== 'none') { squads++; pilots += 1 + state.members.length; }
  countEl.textContent = squads
    ? squads + (squads === 1 ? ' SQUAD' : ' SQUADS') + ' · ' + pilots + (pilots === 1 ? ' PILOT' : ' PILOTS')
    : '';
}

// One card per queued incoming invite (state.pendingInvites, oldest first — Squad.cs's
// _pendingReceived), each independently accept/decline-able by its own leaderId. Accepting any one
// declines the rest server-side (Squad.cs's AcceptInvite), as does creating a squad, so the next
// push just reflects the shorter list. ACCEPT waits for the pilot's own callsign.
function renderInviteCards(invites) {
  inviteCards.innerHTML = '';
  const hasCallsign = !!me().callsign;
  invites.forEach(function (inv) {
    const card = document.createElement('div');
    card.className = 'sqd-invite-card';

    const key = document.createElement('span');
    key.className = 'sqd-invite-key';
    key.textContent = inv.callsign ? inv.callsign + ' ' + (inv.flight || 1) : (inv.leaderName || inv.leaderId);

    const text = document.createElement('span');
    text.className = 'sqd-invite-text';
    const pilots = (inv.members || []).length + 1;   // its members plus the leader
    // The sender by callsign, with their Steam name, once their callsign is known.
    const sender = (pilot(inv.leaderId) ? pilot(inv.leaderId).d + ' (' + (inv.leaderName || inv.leaderId) + ')' : (inv.leaderName || inv.leaderId));
    text.textContent = 'from ' + sender + ' · ' + pilots + (pilots === 1 ? ' pilot' : ' pilots');

    const actions = document.createElement('div');
    actions.className = 'sqd-invite-actions';
    const accept = document.createElement('button');
    accept.className = 'sqd-btn sqd-btn-squad pad-hoverable'; accept.textContent = 'ACCEPT';
    accept.disabled = !hasCallsign;
    if (!hasCallsign) accept.title = 'Set your callsign first';
    accept.onclick = function () { acceptInvite(inv.leaderId); };
    const decline = document.createElement('button');
    decline.className = 'sqd-btn sqd-btn-dim pad-hoverable'; decline.textContent = 'DECLINE';
    decline.onclick = function () { declineInvite(inv.leaderId); };
    actions.appendChild(accept); actions.appendChild(decline);

    card.appendChild(key); card.appendChild(text); card.appendChild(actions);
    inviteCards.appendChild(card);
  });
}

// Which containers the pilot has opened or closed, kept for the browser session (sessionStorage,
// shared by every SQD frame of this tab) so a reload or a page switch doesn't undo it. `squads`
// holds the leader ids of the other squads the pilot opened — they start collapsed; `roster` is the
// unassigned list's state once the pilot has toggled it, null until then.
const FOLD_STORE_KEY = 'noxmfd.sqd.fold';
const fold = { squads: {}, roster: null };
try {
  const saved = JSON.parse(sessionStorage.getItem(FOLD_STORE_KEY) || 'null');
  if (saved && saved.squads && typeof saved.squads === 'object') fold.squads = saved.squads;
  if (saved && typeof saved.roster === 'boolean') fold.roster = saved.roster;
} catch (_) {}
function saveFold() {
  try { sessionStorage.setItem(FOLD_STORE_KEY, JSON.stringify(fold)); } catch (_) {}
}

// The player list (not shown to a plain member): in-match players who aren't in any squad, plus the players we've invited and are
// awaiting (tagged INVITED, no button — Squad.cs has no way to withdraw an invite). Until the pilot
// toggles it, it follows the role: open while not in a squad, collapsed once in one. A toggle holds
// for the rest of the browser session.
let rosterOpen = true;
let rosterRole = null;
function applyRosterOpen() {
  rosterSection.classList.toggle('sqd-collapsed', !rosterOpen);
  rosterHead.setAttribute('aria-expanded', rosterOpen ? 'true' : 'false');
}
function toggleRoster() { rosterOpen = !rosterOpen; fold.roster = rosterOpen; saveFold(); applyRosterOpen(); }
rosterHead.addEventListener('click', toggleRoster);
rosterHead.addEventListener('keydown', function (e) {
  if (e.key === 'Enter' || e.key === ' ') { e.preventDefault(); toggleRoster(); }
});

// Memoized by content signature — render() also runs on every squad-state push, so without this a
// state-only update (e.g. a member's aircraft changing) would rebuild the rows for nothing.
let lastRosterSig = null;
function renderRoster(showInvite) {
  if (state.role !== rosterRole) {
    rosterRole = state.role;
    rosterOpen = fold.roster !== null ? fold.roster : state.role === 'none';
    applyRosterOpen();
  }

  // Everyone already in a squad, ours or another (a leader's state.members never includes
  // themselves, and /server-players never lists self).
  const assigned = {};
  (state.members || []).forEach(function (m) { assigned[m.id] = true; });
  factionSquads().forEach(function (sq) { sq.members.forEach(function (m) { assigned[m.id] = true; }); });

  const invited = {};
  if (state.role === 'leader') state.pendingSent.forEach(function (p) { invited[p.id] = p.name; });

  const rows = [];
  const listed = {};
  // Not in a squad: this pilot is one of the unassigned, listed first.
  if (state.role === 'none') {
    rows.push({ id: state.self, name: state.selfName || '—', aircraft: state.selfAircraft || '', self: true });
  }
  players.forEach(function (p) {
    if (assigned[p.id]) return;
    listed[p.id] = true;
    rows.push({ id: p.id, name: p.name || p.id, aircraft: p.aircraft || '', invited: p.id in invited, update: !!p.update });
  });
  // An invitee who has since left the match's roster is still awaiting our invite.
  Object.keys(invited).forEach(function (id) {
    if (!listed[id]) rows.push({ id: id, name: invited[id] || id, aircraft: '', invited: true });
  });

  const sig = showInvite + '|' + iconStatusVersion + '|' + rows.map(function (r) {
    return r.id + ':' + r.name + ':' + r.aircraft + ':' + (r.invited ? 1 : 0) + ':' + (r.update ? 1 : 0) + ':' + pilotKey(r.id);
  }).join(',');
  if (sig === lastRosterSig) return;
  lastRosterSig = sig;
  rosterRows.innerHTML = '';
  rows.forEach(function (r) {
    addSquadRow(rosterRows, {
      callsign: r.self ? meDesignation() : (pilot(r.id) || {}).d,
      dupOf: r.id, name: r.name, aircraft: r.aircraft, self: r.self,
      invited: r.invited, update: r.update,
      invite: showInvite && !r.self && !r.invited && !r.update ? function () { invite(r.id, r.name); } : null,
    });
  });
  rosterTitle.textContent = 'UNASSIGNED PLAYERS · ' + rows.length;
}

// The ⋮ menu of a member row (the leader's view): which member's menu is open, if any. Closed by
// an outside click, Escape, or picking an item.
let menuFor = null;
const DOTS_SVG = '<svg width="4" height="16" viewBox="0 0 4 16" fill="currentColor" aria-hidden="true"><circle cx="2" cy="2" r="1.8"/><circle cx="2" cy="8" r="1.8"/><circle cx="2" cy="14" r="1.8"/></svg>';
function closeMenu() {
  if (menuFor === null) return;
  menuFor = null;
  lastSquadRowsSig = null;
  if (state && state.role !== 'none') renderSquad();
}
document.addEventListener('click', function (e) {
  if (menuFor !== null && !(e.target.closest && e.target.closest('.sqd-menu, .sqd-dots'))) closeMenu();
});
document.addEventListener('keydown', function (e) { if (e.key === 'Escape') closeMenu(); });

function noteSpan(text, title) {
  const el = document.createElement('span');
  el.className = 'sqd-note-amber';
  el.textContent = text;
  if (title) el.title = title;
  return el;
}

// One row of a squad (or of the unassigned list): [callsign] [pilot] [aircraft] [trailing]. The
// trailing cell holds, as they apply: an amber SAME CALLSIGN or UPDATE NOXMFD note, the LEADER label,
// an INVITE button or INVITED tag, or — the leader viewing a member of their own squad — the ⋮
// button and its menu (PROMOTE TO LEADER / KICK FROM SQUADRON).
// o: {callsign, dupOf, name, aircraft, leaderRow, self, controls, memberId, invited, update, invite}.
// callsign is "" when the pilot has none (NO CALLSIGN); dupOf is the SteamID to look the SAME
// CALLSIGN flag up for. aircraft is the unitName (e.g. "F-16C") — "" whenever this pilot has nothing
// to report (dead, ejected, not spawned yet) or isn't visible right now, which renders as a blank
// column rather than any placeholder.
function addSquadRow(container, o) {
  const row = document.createElement('div');
  row.className = 'sqd-squad-row' + (o.self ? ' self' : '');

  const tag = document.createElement('span');
  tag.className = 'sqd-row-tag' + (o.callsign ? '' : ' none');
  tag.textContent = o.callsign || 'NO CALLSIGN';

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

  const end = document.createElement('span');
  end.className = 'sqd-row-end';
  const p = o.dupOf ? pilot(o.dupOf) : null;
  if (p && p.dup) end.appendChild(noteSpan('SAME CALLSIGN', 'Also flown by ' + sharers(o.dupOf).join(', ')));
  if (o.update) end.appendChild(noteSpan('UPDATE NOXMFD', 'Running a NOXMFD too old to squad with this one'));
  if (o.invited) {
    const t = document.createElement('span');
    t.className = 'sqd-player-tag'; t.textContent = 'INVITED'; t.title = 'Awaiting response';
    end.appendChild(t);
  }
  if (o.invite) {
    const btn = document.createElement('button');
    btn.className = 'sqd-btn sqd-btn-squad pad-hoverable'; btn.textContent = 'INVITE';
    btn.onclick = o.invite;
    end.appendChild(btn);
  }
  if (o.leaderRow) {
    const mark = document.createElement('span');
    mark.className = 'sqd-row-mark'; mark.textContent = 'LEADER';
    end.appendChild(mark);
  } else if (o.controls) {
    const open = menuFor === o.memberId;
    const dots = document.createElement('button');
    dots.type = 'button';
    dots.className = 'sqd-dots pad-hoverable' + (open ? ' on' : '');
    dots.innerHTML = DOTS_SVG;
    dots.setAttribute('aria-label', 'Actions for ' + o.name);
    dots.setAttribute('aria-haspopup', 'menu');
    dots.setAttribute('aria-expanded', open ? 'true' : 'false');
    dots.onclick = function () {
      menuFor = open ? null : o.memberId;
      lastSquadRowsSig = null;
      renderSquad();
    };
    end.appendChild(dots);
    row.appendChild(end);
    if (open) row.appendChild(buildMenu(o.memberId));
    container.appendChild(row);
    return;
  }
  row.appendChild(end);
  container.appendChild(row);
}

function buildMenu(memberId) {
  const menu = document.createElement('div');
  menu.className = 'sqd-menu';
  menu.setAttribute('role', 'menu');
  [['PROMOTE TO LEADER', '', relinquishTo], ['KICK FROM SQUADRON', ' red', kick]].forEach(function (item) {
    const btn = document.createElement('button');
    btn.type = 'button';
    btn.className = 'sqd-menu-item pad-hoverable' + item[1];
    btn.setAttribute('role', 'menuitem');
    btn.textContent = item[0];
    btn.onclick = function () { item[2](memberId); menuFor = null; lastSquadRowsSig = null; renderSquad(); };
    menu.appendChild(btn);
  });
  return menu;
}

// Memoized by content signature, same reasoning as lastRosterSig above — renderSquad() runs on
// every render() call (including a roster-only push, which never touches squad state at all), so
// without this the row table would tear down and rebuild for nothing on every one of them.
let lastSquadRowsSig = null;
function renderSquad() {
  const isLeader = state.role === 'leader';

  // Card title: the squad's name (callsign + flight, "TALON 1") and the pilot count. Swapped for the
  // editor while EDIT is active (leader only). SAME DESIGNATION shows while another squad flies the
  // same pair.
  squadHead.textContent = (state.callsign || 'YOUR') + ' ' + (state.flight || 1);
  const pilots = 1 + state.members.length;
  squadNote.textContent = pilots + (pilots === 1 ? ' PILOT' : ' PILOTS');
  squadDup.style.display = state.faction && state.faction.selfDup ? '' : 'none';

  callsignEditBtn.style.display = isLeader ? '' : 'none';
  const showEdit = isLeader && editingCallsign;
  squadCard.style.display = showEdit ? 'none' : '';
  callsignEdit.style.display = showEdit ? '' : 'none';
  disbandBtn.style.display = isLeader ? '' : 'none';

  // The leader first: this pilot themselves when leading (state.selfName, since a leader has no
  // reason to appear in their own state.members list), or state.leaderName when a member. Then the
  // members, in the order they joined.
  const leaderId = isLeader ? state.self : state.leaderId;
  const leaderName = isLeader ? (state.selfName || '—') : (state.leaderName || state.leaderId);
  const leaderAircraft = isLeader ? state.selfAircraft : state.leaderAircraft;
  if (menuFor !== null && !state.members.some(function (m) { return m.id === menuFor; })) menuFor = null;
  const rowsSig = isLeader + '|' + leaderName + '|' + leaderAircraft + '|' + state.self + '|' + iconStatusVersion + '|' +
    menuFor + '|' + pilotKey(leaderId) + '|' +
    state.members.map(function (m) { return m.id + ':' + (m.name || '') + ':' + (m.aircraft || '') + ':' + pilotKey(m.id); }).join(',');
  if (rowsSig === lastSquadRowsSig) return;
  lastSquadRowsSig = rowsSig;
  squadRows.innerHTML = '';

  const calls = function (id) { return (pilot(id) || {}).d; };
  addSquadRow(squadRows, { callsign: calls(leaderId), dupOf: leaderId, name: leaderName, aircraft: leaderAircraft, leaderRow: true, self: isLeader });
  state.members.forEach(function (m) {
    addSquadRow(squadRows, {
      callsign: calls(m.id), dupOf: m.id, name: m.name || m.id, aircraft: m.aircraft,
      self: m.id === state.self, controls: isLeader, memberId: m.id,
    });
  });

  // A menu near the bottom of the pane opens upward instead of off the page.
  const menuEl = squadRows.querySelector('.sqd-menu');
  if (menuEl) {
    const r = menuEl.getBoundingClientRect();
    if (r.bottom > window.innerHeight - 4) menuEl.classList.add('up');
    const first = menuEl.querySelector('.sqd-menu-item');
    if (first && !menuEl.contains(document.activeElement)) first.focus({ preventScroll: true });
  }
}

// Every other squad in the faction (state.faction.squads), read-only: a collapsible container per
// squad, collapsed until the pilot opens it (fold.squads, by leader id). Memoized by content like the
// rows above.
function isOpen(leader) { return !!fold.squads[leader]; }
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
    return sq.leader + ':' + sq.callsign + ':' + sq.flight + ':' + sq.dup + ':' + (isOpen(sq.leader) ? 1 : 0) + ':' +
      sq.members.map(function (m) { return m.id + '.' + m.leader + '.' + m.name + '.' + m.aircraft + '.' + pilotKey(m.id); }).join(',');
  }).join(';');
  if (sig === lastOthersSig) return;
  lastOthersSig = sig;
  othersEl.innerHTML = '';
  squads.forEach(function (sq) {
    const box = document.createElement('div');
    box.className = 'sqd-squad' + (isOpen(sq.leader) ? '' : ' sqd-collapsed');

    const head = document.createElement('button');
    head.type = 'button';
    head.className = 'sqd-squad-head pad-hoverable';
    head.setAttribute('aria-expanded', isOpen(sq.leader) ? 'true' : 'false');
    const n = sq.members.length;
    head.appendChild(textSpan('sqd-squad-title', sq.callsign + ' ' + sq.flight));
    head.appendChild(textSpan('sqd-note', n + (n === 1 ? ' PILOT' : ' PILOTS')));
    if (sq.dup) head.appendChild(textSpan('sqd-dup', 'SAME DESIGNATION'));
    head.appendChild(textSpan('sqd-spacer', ''));
    head.appendChild(textSpan('sqd-chevron', ''));
    head.onclick = function () {
      if (isOpen(sq.leader)) delete fold.squads[sq.leader]; else fold.squads[sq.leader] = true;
      saveFold();
      lastOthersSig = null;
      renderOthers();
    };

    const rows = document.createElement('div');
    rows.className = 'sqd-rows';
    sq.members.forEach(function (m) {
      addSquadRow(rows, { callsign: (pilot(m.id) || {}).d, dupOf: m.id, name: m.name || m.id, aircraft: m.aircraft, leaderRow: !!m.leader });
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

// A select that lands outside any control also closes an open ⋮ menu: the PAD has no Escape.
function padCursorSelectAt(x, y) {
  const raw = document.elementFromPoint(x, y);
  const el = raw && raw.closest(CURSORABLE);
  if (el) el.click(); else closeMenu();
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
