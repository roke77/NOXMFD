// AKF page — advanced kill feed (issue #34), a reactive replica of the game's own HUD kill-feed
// ticker plus session stats, driven by the shell over postMessage (docs/akf-page.md). No
// interaction, no commands — pure render of the 'akf' block. See akf.html for the message contract.

const allEl    = document.getElementById('akf-all');
const playerEl = document.getElementById('akf-player');
const kAircraftEl = document.getElementById('akf-k-aircraft');
const kShipEl      = document.getElementById('akf-k-ship');
const kVehicleEl   = document.getElementById('akf-k-vehicle');
const kBuildingEl  = document.getElementById('akf-k-building');
const fundsGainedEl = document.getElementById('akf-funds-gained');
const fundsSpentEl  = document.getElementById('akf-funds-spent');
const fundsNetEl    = document.getElementById('akf-funds-net');
const rankEl         = document.getElementById('akf-rank');

function span(cls, text) {
  const el = document.createElement('span');
  el.className = cls;
  el.textContent = text;
  return el;
}

// ALL feed: "Attacker verb Victim [with Weapon]" when there's a killer, "Victim verb" (e.g. "T-90
// was destroyed") when there isn't — matches the game's own MessageManager.RpcKillMessage string
// construction exactly (attacker-first only when an attacker exists).
function renderAllLine(e) {
  const line = document.createElement('div');
  line.className = 'akf-line';
  if (e.a) {
    line.appendChild(span(e.h ? 'akf-hostile' : 'akf-friendly', e.a));
    line.appendChild(document.createTextNode(' '));
    line.appendChild(span('akf-verb', e.verb));
    line.appendChild(document.createTextNode(' '));
    line.appendChild(span(e.vh ? 'akf-hostile' : 'akf-friendly', e.v));
  } else {
    line.appendChild(span(e.vh ? 'akf-hostile' : 'akf-friendly', e.v));
    line.appendChild(document.createTextNode(' '));
    line.appendChild(span('akf-verb', e.verb));
  }
  appendWeapon(line, e.w);
  return line;
}

// PLAYER feed: the attacker is normally the player's own aircraft, so naming it every line would be
// redundant — "Victim verb [with Weapon]" instead ("MiG-29 shot down with AIM-9X"). An "incoming"
// line (e.pv: the player was killed, or the player's own fired munition was intercepted) is the
// opposite — the player is the VICTIM, not the attacker — so it renders in full like the ALL feed,
// with an accent marking it as incoming rather than scored.
function renderPlayerLine(e) {
  if (e.pv) {
    const line = renderAllLine(e);
    line.classList.add('akf-incoming');
    return line;
  }
  const line = document.createElement('div');
  line.className = 'akf-line';
  line.appendChild(span(e.vh ? 'akf-hostile' : 'akf-friendly', e.v));
  line.appendChild(document.createTextNode(' '));
  line.appendChild(span('akf-verb', e.verb));
  appendWeapon(line, e.w);
  return line;
}

function appendWeapon(line, weapon) {
  if (!weapon) return;
  line.appendChild(document.createTextNode(' '));
  line.appendChild(span('akf-with', 'with'));
  line.appendChild(document.createTextNode(' '));
  line.appendChild(span('akf-weapon', weapon));
}

// Rebuilds a feed column and pins the scroll to the bottom, so the newest (last) entry stays in
// view — the panel grows downward, matching a chat log or terminal, not the game's own ticker
// (which grows upward and ages lines out).
function renderFeed(el, items, renderLine) {
  el.textContent = '';
  for (const e of items) el.appendChild(renderLine(e));
  el.scrollTop = el.scrollHeight;
}

function fmtSigned(n) {
  const r = Math.round(n || 0);
  return (r >= 0 ? '+' : '') + r.toLocaleString();
}

function paint(state) {
  renderFeed(allEl, state.all || [], renderAllLine);
  renderFeed(playerEl, state.player || [], renderPlayerLine);

  const kills = state.kills || {};
  kAircraftEl.textContent = String(kills.aircraft || 0);
  kShipEl.textContent     = String(kills.ship || 0);
  kVehicleEl.textContent  = String(kills.vehicle || 0);
  kBuildingEl.textContent = String(kills.building || 0);

  const gained = state.fundsGained || 0, spent = state.fundsSpent || 0;
  fundsGainedEl.textContent = fmtSigned(gained);
  fundsSpentEl.textContent  = '-' + Math.round(spent).toLocaleString();
  fundsNetEl.textContent    = fmtSigned(gained - spent);

  rankEl.textContent = String(state.rank || 0);
}

window.addEventListener('message', function (e) {
  const m = e.data;
  if (!m || m.mfd !== true || m.type !== 'akf') return;
  paint(m);
});

paint({});   // initial paint — all-zero until the first frame arrives
