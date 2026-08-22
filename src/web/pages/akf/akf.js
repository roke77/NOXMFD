// Driven by the shell over postMessage (docs/akf-page.md, akf.html for the message contract).
// Feed content is read-only; the one interaction is the ALL/PLAYER resizer below, reachable from
// a mouse/touch drag or the PAD cursor.
import { createPadCursor } from '/assets/services/pad-cursor.js';

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

// COMPACT truncates every unit name to its first word and collapses the verb and "with" to single
// glyphs; the weapon name is left in full either way since that's the one piece of information
// compacting would actually lose.
let compact = false;
const VERB_GLYPH = '▸';   // "acted on"
const WITH_GLYPH = '•';   // "carrying"

function firstWord(name) {
  const s = String(name || '').trim();
  const i = s.indexOf(' ');
  return i === -1 ? s : s.slice(0, i);
}
function nameText(raw) { return compact ? firstWord(raw) : raw; }
function verbText(raw) { return compact ? VERB_GLYPH : raw; }

// Matches the game's own MessageManager.RpcKillMessage string construction exactly (attacker-first
// only when an attacker exists).
function renderAllLine(e) {
  const line = document.createElement('div');
  line.className = 'akf-line';
  if (e.a) {
    line.appendChild(span(e.h ? 'akf-hostile' : 'akf-friendly', nameText(e.a)));
    line.appendChild(document.createTextNode(' '));
    line.appendChild(span('akf-verb', verbText(e.verb)));
    line.appendChild(document.createTextNode(' '));
    line.appendChild(span(e.vh ? 'akf-hostile' : 'akf-friendly', nameText(e.v)));
  } else {
    line.appendChild(span(e.vh ? 'akf-hostile' : 'akf-friendly', nameText(e.v)));
    line.appendChild(document.createTextNode(' '));
    line.appendChild(span('akf-verb', verbText(e.verb)));
  }
  appendWeapon(line, e.w);
  return line;
}

// The attacker is normally the player's own aircraft, so naming it every line would be redundant —
// victim-only phrasing instead. An "incoming" line (e.pv: the player was killed, or the player's
// own fired munition was intercepted) is the opposite — the player is the VICTIM, not the attacker —
// so it renders in full like the ALL feed, with an accent marking it as incoming rather than scored.
function renderPlayerLine(e) {
  if (e.pv) {
    const line = renderAllLine(e);
    line.classList.add('akf-incoming');
    return line;
  }
  const line = document.createElement('div');
  line.className = 'akf-line';
  line.appendChild(span(e.vh ? 'akf-hostile' : 'akf-friendly', nameText(e.v)));
  line.appendChild(document.createTextNode(' '));
  line.appendChild(span('akf-verb', verbText(e.verb)));
  appendWeapon(line, e.w);
  return line;
}

function appendWeapon(line, weapon) {
  if (!weapon) return;
  line.appendChild(document.createTextNode(' '));
  line.appendChild(span('akf-with', compact ? WITH_GLYPH : 'with'));
  line.appendChild(document.createTextNode(' '));
  line.appendChild(span('akf-weapon', weapon));
}

// Pins scroll to the bottom so the newest (last) entry stays in view — the panel grows downward,
// unlike the game's own ticker (which grows upward and ages lines out).
function renderFeed(el, items, renderLine) {
  el.textContent = '';
  for (const e of items) el.appendChild(renderLine(e));
  el.scrollTop = el.scrollHeight;
}

function fmtSigned(n) {
  const r = Math.round(n || 0);
  return (r >= 0 ? '+' : '') + r.toLocaleString();
}

let lastState = {};   // lets the density toggle re-render without waiting for the next 'akf' frame

function renderFeeds(state) {
  renderFeed(allEl, state.all || [], renderAllLine);
  renderFeed(playerEl, state.player || [], renderPlayerLine);
}

function paint(state) {
  lastState = state;
  renderFeeds(state);

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

paint({});

// ── DETAILED/COMPACT density toggle ────────────────────────────────────────────────────
// Purely a client-local display preference: nothing here reaches the shell, and it isn't persisted.
const densityToggleEl = document.getElementById('akf-density-toggle');
densityToggleEl.addEventListener('click', function () {
  compact = !compact;
  densityToggleEl.classList.toggle('compact', compact);
  renderFeeds(lastState);
});

// ── ALL/PLAYER split resizer ───────────────────────────────────────────────────────────
// Purely client-local UI state — nothing here reaches the shell or the plugin, and it isn't
// persisted across a reload (the grid reverts to akf-feeds' own CSS grid-template-columns).
const feedsEl    = document.getElementById('akf-feeds');
const resizerEl  = document.getElementById('akf-resizer');
const RESIZER_W  = 20;    // must match akf.css's middle grid track
const MIN_FEED_W = 60;    // px a feed can shrink to before it collapses instead

let dragging = false;
let tapArmed = false;   // pointerdown started on an already-collapsed resizer, so a plain release
                         // (a tap) should restore instead of being treated as a drag
let collapsed = null;      // null | 'all' | 'player' — which feed (if any) is currently collapsed
let customSplit = null;    // null = default CSS fr ratio; else ALL's share (0..1) of usable width

function setColumns(leftPx, rightPx) {
  feedsEl.style.gridTemplateColumns = leftPx + 'px ' + RESIZER_W + 'px ' + rightPx + 'px';
}

// Back to the CSS default split, both feeds visible. This is the only way out of a collapsed
// state — dragging back out never un-collapses.
function resetSplit() {
  feedsEl.style.gridTemplateColumns = '';
  collapsed = null;
  customSplit = null;
  resizerEl.classList.remove('collapsed-all', 'collapsed-player');
}

function collapseSide(side) {
  collapsed = side;
  resizerEl.classList.toggle('collapsed-all', side === 'all');
  resizerEl.classList.toggle('collapsed-player', side === 'player');
  applyForCurrentWidth();
}

// A drag/collapse fixes the split in px at that instant, but this page can live inside an F-35
// portal that later merges/splits (or a classic-shell pane that resizes) with no reload — so those
// px would go stale, leaving dead space instead of the split filling the new width. Re-derives the
// pixel columns from the resize-safe state (collapsed side, or customSplit's fraction) whenever the
// feeds box changes size.
function applyForCurrentWidth() {
  if (!collapsed && customSplit === null) return;   // untouched default: CSS fr columns handle it
  const usable = feedsEl.getBoundingClientRect().width - RESIZER_W;
  if (collapsed) setColumns(collapsed === 'all' ? 0 : usable, collapsed === 'player' ? 0 : usable);
  else setColumns(usable * customSplit, usable * (1 - customSplit));
}
new ResizeObserver(applyForCurrentWidth).observe(feedsEl);

// Shared by both drag sources (a real pointer drag and the PAD cursor's held-select drag below):
// clientX is viewport-space either way. The feeds rect is re-measured fresh each call rather than
// cached at drag-start, so it can't drift out of sync with either input path.
function applyDragX(clientX) {
  const rect = feedsEl.getBoundingClientRect();
  const x = clientX - rect.left - RESIZER_W / 2;
  const leftPx = Math.max(0, Math.min(rect.width - RESIZER_W, x));
  const rightPx = rect.width - RESIZER_W - leftPx;

  // Reaching the threshold collapses immediately, mid-drag, not only once the pointer is released.
  if (leftPx < MIN_FEED_W) { endDrag(); collapseSide('all'); return; }
  if (rightPx < MIN_FEED_W) { endDrag(); collapseSide('player'); return; }
  customSplit = leftPx / (rect.width - RESIZER_W);
  setColumns(leftPx, rightPx);
}

// Cleans up drag bookkeeping only. Tap-to-restore is decided in pointerup itself, not here, since
// this also runs mid-drag (the collapse branch above) where restoring would just undo the collapse
// that instant.
function endDrag(e) {
  dragging = false;
  padDragging = false;
  resizerEl.classList.remove('dragging');
  if (e && e.pointerId != null) { try { resizerEl.releasePointerCapture(e.pointerId); } catch (err) {} }
}

resizerEl.addEventListener('pointerdown', function (e) {
  if (collapsed) { tapArmed = true; return; }   // a collapsed resizer is a plain restore button
  dragging = true;
  resizerEl.classList.add('dragging');
  try { resizerEl.setPointerCapture(e.pointerId); } catch (err) {}
});
resizerEl.addEventListener('pointermove', function (e) {
  if (dragging) applyDragX(e.clientX);
});
// Tap-vs-drag is decided directly from pointerdown/pointerup, not a native 'click' listener: a
// browser's synthesized 'click' can arrive late/out of order under touch emulation, so a
// drag-to-collapse gesture's trailing click could land after a separate follow-up tap had already
// started and steal that tap's restore. Same tap/hold shape as TGT's pointerdown/pointerup pair,
// just simpler (no hold branch here).
resizerEl.addEventListener('pointerup', function (e) {
  const restore = tapArmed && collapsed;
  tapArmed = false;
  endDrag(e);
  if (restore) resetSplit();
});
resizerEl.addEventListener('pointercancel', function (e) {
  tapArmed = false;
  endDrag(e);
});

// ── PAD cursor (docs/page-cursor.md) ──────────────────────────────────────────────────
// Same crosshair/transport TGT/HUD use, driven here only while this AKF is the SOI's focused
// surface. .akf-panel doesn't scroll (unlike HUD's), so panel-local coordinates work directly —
// no viewport-coordinate workaround needed.
const CURSORABLE = '.akf-resizer, .akf-density-toggle';
const akfPanel = document.querySelector('.akf-panel');
const padCursorEl = document.getElementById('pad-cursor');
let hoveredEl = null;

// True only while Select is held down over the resizer (armed by padCursorHoldAt below); feeds the
// crosshair's x into the same applyDragX a mouse/touch drag uses, so a HOTAS pilot can resize the
// split the same way. A plain tap (release before the hold threshold) just selects whatever's under
// the point, same as every other PAD-cursor page.
let padDragging = false;

function elAt(px, py) {
  const rect = akfPanel.getBoundingClientRect();
  const raw = document.elementFromPoint(rect.left + px, rect.top + py);
  return raw && raw.closest(CURSORABLE);
}

// The resizer decides its own tap outcome from real pointerdown/pointerup, not a native 'click'
// (see resizerEl's pointerup listener above), so a tap here restores it directly rather than
// through el.click() — everything else on the page (the density toggle) is a plain button, where
// .click() is exactly right.
function padCursorSelectAt(x, y) {
  const el = elAt(x, y);
  if (el === resizerEl) { if (collapsed) resetSplit(); return; }
  if (el) el.click();
}

function padCursorHoldAt(x, y) {
  if (elAt(x, y) === resizerEl && !collapsed) padDragging = true;
}

function padCursorMoveAt(x, y) {
  if (padDragging) {
    if (x == null) { padDragging = false; return; }   // lost focus mid-drag: bail safely
    applyDragX(akfPanel.getBoundingClientRect().left + x);
    return;
  }
  const el = x == null ? null : elAt(x, y);
  if (el === hoveredEl) return;
  if (hoveredEl) hoveredEl.classList.remove('pad-hover');
  hoveredEl = el;
  if (hoveredEl) hoveredEl.classList.add('pad-hover');
}

const cursor = createPadCursor({
  el: padCursorEl,
  clampRect: () => ({ dx: 0, dy: 0, dw: akfPanel.clientWidth, dh: akfPanel.clientHeight }),
  onSelect: padCursorSelectAt,
  onHold: padCursorHoldAt,
  onMove: padCursorMoveAt,
  holdMs: 500,
});

window.addEventListener('message', function (e) {
  const m = e.data;
  if (!m || m.mfd !== true) return;
  if (m.action === 'cursor-focus') {
    cursor.setFocus(!!m.on, akfPanel.clientWidth / 2, akfPanel.clientHeight / 2);
  } else if (m.action === 'cursor') {
    cursor.setVector(m.x, m.y);
  } else if (m.action === 'cursor-held') {
    cursor.setSelectHeld(!!m.held);
    if (!m.held) padDragging = false;   // falling edge ends an in-progress drag too
  }
});
