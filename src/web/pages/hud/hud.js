// HUD page — a clickable replica of the game's HUD OPTIONS screen (HUDOptions). It bootstraps from
// /hud-options, receives later changes through the shell's SSE relay, and POSTs hud.* commands back.
// See hud.html for the control contract and docs/hud-rework.md for the layout.
import { createPadCursor } from '/assets/services/pad-cursor.js';
import { createPresetCards } from '/assets/services/preset-cards.js';

// The game exposes no per-category display name; this order is fixed in its inspector (docs/hud-page.md).
// The count from /hud-options is checked against this so a game-side reorder surfaces rather than mislabels.
const CATEGORY_LABELS = ['FRIENDLY', 'ENEMY', 'AIRCRAFT', 'MISSILES', 'VEHICLES', 'BUILDINGS', 'SHIPS'];
// Vehicle names reuse the TGT page's capture; buildings have their own (a name like RDR is in both,
// so they can't share).
const ICON_BASE = { vehicle: '/tgt-icon', building: '/building-icon' };
// Index into CATEGORY_LABELS. The two faction categories are coloured apart from the green type
// categories, as in game — see hud.css.
const FACTION_CLASS = { 0: 'friendly', 1: 'enemy' };

// HUDOptions exposes no per-category icon field; the glyph is captured from a plain child Image
// ("TopContainer/Icon") on each row, keyed by this label (docs/hud-page.md,
// AssetCapture.TryCaptureHudCategoryIcons). FRIENDLY/ENEMY (0, 1) have no entry and show a dot.
const CAT_ICON_INDICES = new Set([2, 3, 4, 5, 6]);

// Same retry-on-404 approach as typeIcon below: the mod extracts these over the mission's first few
// scans, so an early request can 404.
function catIcon(index) {
  if (!CAT_ICON_INDICES.has(index)) return null;
  const img = document.createElement('img');
  img.alt = '';
  const url = '/hud-cat-icon?cat=' + encodeURIComponent(CATEGORY_LABELS[index]);
  let tries = 0;
  img.addEventListener('error', function () {
    img.style.visibility = 'hidden';
    if (++tries <= 6) setTimeout(function () { img.src = url + '&r=' + tries; }, 1200);
  });
  img.addEventListener('load', function () { img.style.visibility = ''; });
  img.src = url;
  return img;
}

// The mod's own HudDeclutter flags (declutter.set), a separate axis from the HUDOptions unit-icon
// controls. `key` is the group the command carries; the /hud-options `declutter` object reports
// each flag's HIDE state (true = hidden).
const DECLUTTER = [
  { key: 'weapon',  label: 'WEAPONS' },
  { key: 'minimap', label: 'MINIMAP' },
  { key: 'boxes',   label: 'FLIGHT' },
  { key: 'feed',    label: 'FEED' },   // native kill-feed ticker (docs/akf-page.md)
];

const contentEl    = document.getElementById('hud-content');
const dcEl         = document.getElementById('hud-declutter');
const modesEl      = document.getElementById('hud-modes');
const catsEl       = document.getElementById('hud-cats');
const emptyEl      = document.getElementById('hud-empty');
const vehiclesSec  = document.getElementById('hud-vehicles-sec');
const buildingsSec = document.getElementById('hud-buildings-sec');
const vehiclesEl   = document.getElementById('hud-vehicles');
const buildingsEl  = document.getElementById('hud-buildings');

let data = null;          // last /hud-options snapshot
let lastOptionsJson = '';
let commandResyncTimer = null;

// The game builds toggle names from the Encyclopedia with underscores (IR_SAM); the in-game screen
// shows them spaced.
function pretty(name) { return String(name).replace(/_/g, ' '); }

function load() {
  return fetch('/hud-options', { cache: 'no-store' })
    .then(function (r) { return r.ok ? r.text() : '{}'; })
    .then(function (text) {
      if (text !== lastOptionsJson) applyOptions(JSON.parse(text));
    })
    .catch(function () { applyOptions({}); });
}

function applyOptions(next) {
  const json = JSON.stringify(next || {});
  if (json === lastOptionsJson) return;
  lastOptionsJson = json;
  render(next || {});
}

function send(cmd, body) {
  // Optimistic edits deliberately invalidate the comparison key. A delayed one-shot read then
  // restores server truth even when a command is rejected and therefore produces no changed SSE
  // snapshot; rapid clicks collapse into one reconciliation request.
  lastOptionsJson = '';
  clearTimeout(commandResyncTimer);
  commandResyncTimer = setTimeout(load, 1200);
  return sendCommand(cmd, body).catch(function () {});
}

function render(d) {
  data = d;
  const has = d && Array.isArray(d.categories) && d.categories.length > 0;
  emptyEl.style.display   = has ? 'none' : '';
  contentEl.style.display = has ? '' : 'none';
  if (!has) return;
  // The declutter object rides the same payload but is its own axis; guard it separately so an older
  // plugin (or an empty payload) simply omits the strip rather than showing dead toggles.
  const hasDc = d.declutter && typeof d.declutter === 'object';
  dcEl.style.display = hasDc ? '' : 'none';
  if (hasDc) renderDeclutter();
  presetUi.sync(d.preset || { index: 1, name: '' });
  renderModes();
  renderCats();
  renderTypes('vehicle', vehiclesSec, vehiclesEl, d.vehicles);
  renderTypes('building', buildingsSec, buildingsEl, d.buildings);
}

// aria-pressed is both the assistive-tech state and what lit-panel.css lights (.lit[aria-pressed]).
function setPressed(btn, on) { btn.setAttribute('aria-pressed', on ? 'true' : 'false'); }
function isPressed(btn) { return btn.getAttribute('aria-pressed') === 'true'; }

function litButton(className, pressed) {
  const b = document.createElement('button');
  b.type = 'button';
  b.className = 'lit pad-hoverable ' + className;
  setPressed(b, pressed);
  return b;
}

function span(className, text) {
  const el = document.createElement('span');
  el.className = className;
  el.textContent = text;
  return el;
}

// The five preset cards and their SAVE PRESET dialog (services/preset-cards.js). The current
// slot's index/name rides the /hud-options snapshot (a `preset` field, TelemetryServer.
// RefreshHudOptions); the five names come from GET /hud-presets, refetched when that pair changes.
const presetUi = createPresetCards({
  cardsEl: document.getElementById('preset-cards'),
  endpoint: '/hud-presets',
  cmdPrefix: 'preset',
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

// The declutter strip: one card per native widget. Lit = the widget is SHOWN on the HUD (flag off);
// dark = hidden/decluttered. Inverts the reported HIDE flag so it reads like the rest of the page
// (lit green = visible on the HUD). The command's `on` is the HIDE state, so it's the inverse of lit.
function renderDeclutter() {
  dcEl.textContent = '';
  const dc = data.declutter;
  DECLUTTER.forEach(function (item) {
    const b = litButton('hud-dc', !dc[item.key]);   // flag true = hidden = not lit
    const status = span('lit-status', dc[item.key] ? 'HIDDEN' : 'SHOWN');
    b.appendChild(span('lit-title', item.label));
    b.appendChild(status);
    b.addEventListener('click', function () {
      const nextShown = !isPressed(b);
      setPressed(b, nextShown);                             // optimistic
      status.textContent = nextShown ? 'SHOWN' : 'HIDDEN';
      dc[item.key] = !nextShown;                            // store HIDE state back
      send('declutter.set', { group: item.key, on: !nextShown });
    });
    dcEl.appendChild(b);
  });
}

function renderModes() {
  modesEl.textContent = '';
  (data.modes || []).forEach(function (name, i) {
    const b = litButton('hud-mode', i === data.mode);
    b.appendChild(span('lit-title', name));
    b.addEventListener('click', function () {
      if (i === data.mode) return;
      data.mode = i;                        // optimistic; the resync brings the real preset back
      renderModes();
      send('hud.mode', { index: i });
    });
    modesEl.appendChild(b);
  });
}

function renderCats() {
  catsEl.textContent = '';
  // Label count vs. what the game sent — a mismatch means the category order drifted; show it.
  if (data.categories.length !== CATEGORY_LABELS.length) {
    console.warn('[hud] ' + data.categories.length + ' categories from the game, ' +
                 CATEGORY_LABELS.length + ' labels here — labels may be misaligned (docs/hud-page.md).');
  }
  data.categories.forEach(function (on, i) {
    catsEl.appendChild(catCard(CATEGORY_LABELS[i] || ('CAT ' + i), on, i));
  });
}

// One category card: lamp, glyph, name and a FULL (maximized) / MIN status. The two faction cards
// carry a colour class so they read in the game's cyan/red instead of the type green.
function catCard(label, on, index) {
  const btn = litButton('hud-cat' + (FACTION_CLASS[index] ? ' hud-cat-' + FACTION_CLASS[index] : ''), on);
  const state = span('hud-cat-state', on ? 'FULL' : 'MIN');
  const glyph = span('hud-cat-icon', '');
  glyph.appendChild(catIcon(index) || span('hud-cat-dot', ''));
  btn.appendChild(glyph);
  btn.appendChild(span('hud-cat-name', label));
  btn.appendChild(state);
  btn.addEventListener('click', function () {
    const next = !isPressed(btn);
    setPressed(btn, next);                  // optimistic
    state.textContent = next ? 'FULL' : 'MIN';
    data.categories[index] = next;
    send('hud.set', { group: 'category', index: index, on: next });
  });
  return btn;
}

// The grid under VEHICLE TYPES / BUILDING TYPES — one toggle per unit type, the real captured icon
// over its label (the same icon-over-label the TGT vehicle filter uses). The section is hidden when
// the game reports no types of that kind.
function renderTypes(group, sectionEl, gridEl, items) {
  gridEl.textContent = '';
  sectionEl.style.display = items && items.length ? '' : 'none';
  (items || []).forEach(function (it, i) {
    const chip = document.createElement('button');
    chip.type = 'button';
    chip.className = 'hud-type pad-hoverable';
    chip.setAttribute('aria-label', pretty(it.n));
    setPressed(chip, it.on);
    chip.appendChild(typeIcon(group, it.n));
    chip.appendChild(span('hud-type-label', pretty(it.n)));
    chip.addEventListener('click', function () {
      const next = !isPressed(chip);
      setPressed(chip, next);               // optimistic
      it.on = next;
      send('hud.set', { group: group, index: i, on: next });
    });
    gridEl.appendChild(chip);
  });
}

// The captured type sprite. The mod extracts these over the first few mission scans, so a request
// can 404 if the page is opened early — hide and retry a handful of times, the label carries it
// meanwhile. Exactly the TGT page's approach.
function typeIcon(group, name) {
  const img = document.createElement('img');
  img.className = 'hud-type-icon';
  img.alt = '';
  const url = ICON_BASE[group] + '?type=' + encodeURIComponent(name);
  let tries = 0;
  img.addEventListener('error', function () {
    img.style.visibility = 'hidden';
    if (++tries <= 6) setTimeout(function () { img.src = url + '&r=' + tries; }, 1200);
  });
  img.addEventListener('load', function () { img.style.visibility = ''; });
  img.src = url;
  return img;
}

// ── Preset tap / hold ─────────────────────────────────────────────────────────────────
// Tap a card = recall, hold = save (opens the dialog). A completed hold never also recalls; a press
// that is cancelled, leaves the card or loses focus is dropped. Enter / Space on a focused card
// presses it the same way, so the keyboard can save too.
const LONG_MS = 500;   // the same hold threshold as TGT's cells and the PAD cursor
let press = null;      // { slot, longFired, viaKey, timer }

function clearPress() { if (press) { clearTimeout(press.timer); press = null; } }

function startPress(card, viaKey) {
  clearPress();
  const slot = +card.dataset.slot;
  press = { slot: slot, longFired: false, viaKey: !!viaKey };
  press.timer = setTimeout(function () {
    if (!press) return;
    press.longFired = true;
    presetUi.save(slot);
  }, LONG_MS);
}

// `card` is the card the release landed on (null when it landed elsewhere).
function endPress(card) {
  if (!press) return;
  // A completed hold already opened the dialog and must not also recall. Pointer releases re-focus
  // the name entry: only a focus() inside a user gesture raises a touch keyboard.
  if (press.longFired) presetUi.focusEntry();
  else if (card && +card.dataset.slot === press.slot) presetUi.recall(press.slot);
  clearPress();
}

const hudPanel = document.getElementById('hud-panel');
const isPressKey = function (e) { return e.key === ' ' || e.key === 'Enter'; };

hudPanel.addEventListener('pointerdown', function (e) {
  const card = e.target.closest('.preset-card');
  if (card && e.button === 0) startPress(card);
});
// The release is heard on the window: a completed hold has put the dialog's scrim (outside the
// panel) under the pointer by then.
window.addEventListener('pointerup', function (e) { endPress(e.target.closest('.preset-card')); });
window.addEventListener('pointercancel', clearPress);
// Leaving the pressed card before the hold completes drops the press.
hudPanel.addEventListener('pointerout', function (e) {
  if (!press || press.longFired) return;
  const to = e.relatedTarget && e.relatedTarget.closest && e.relatedTarget.closest('.preset-card');
  if (!to || +to.dataset.slot !== press.slot) clearPress();
});
// Losing focus cancels a held key. A pointer press is exempt: the browser moves focus to the card
// right after pointerdown, and that focus change is the press itself, not a cancellation. Focus
// moving to the dialog's entry is the hold completing.
hudPanel.addEventListener('focusout', function () {
  if (press && press.viaKey && !press.longFired) clearPress();
});
hudPanel.addEventListener('keydown', function (e) {
  const card = e.target.closest('.preset-card');
  if (!card || !isPressKey(e)) return;
  e.preventDefault();   // the press is handled here, not by the synthesized click
  if (!e.repeat) startPress(card, true);
});
// keyup can land in the dialog's entry when the hold opened it, so this listens on the window.
window.addEventListener('keyup', function (e) {
  if (press && isPressKey(e)) endPress(e.target.closest('.preset-card'));
});
// A long touch press must not pop the browser's context menu over a card.
window.addEventListener('contextmenu', function (e) {
  if (e.target.closest('.preset-card')) e.preventDefault();
});

// ── PAD cursor (docs/page-cursor.md) ──────────────────────────────────────────────────
// Same crosshair/transport MAP uses (pad-cursor.js), driven here only while this HUD is the SOI's
// focused surface. .hud-panel scrolls its own content (overflow-y: auto — the page is often taller
// than the frame), so the cursor element lives OUTSIDE it (a sibling in hud.html) and is positioned
// in viewport coordinates instead of panel-local ones: a child positioned relative to a scrolling
// ancestor would drift with the content on every scroll, which a cursor overlay must never do
// (contrast TGT, whose .tgt-panel doesn't scroll, so panel-local coordinates work there).
const CURSORABLE = '.lit, .hud-type, .preset-kp-btn';
const padCursorEl = document.getElementById('pad-cursor');
const cursor = createPadCursor({
  el: padCursorEl,
  clampRect: () => {
    const r = hudPanel.getBoundingClientRect();
    return { dx: r.left, dy: r.top, dw: r.width, dh: r.height };
  },
  onSelect: padCursorSelectAt,
  onHold: padCursorHoldAt,
  onMove: padCursorMoveAt,
  holdMs: LONG_MS,
});

function cursorTarget(x, y) {
  const raw = document.elementFromPoint(x, y);
  return raw && raw.closest(CURSORABLE);
}

// Select's TAP outcome: whatever a click already does here, except a preset card, which recalls
// (a synthetic click on it would be the keyboard path, which this page routes to the press handlers).
function padCursorSelectAt(x, y) {
  const el = cursorTarget(x, y);
  if (!el) return;
  if (el.classList.contains('preset-card')) presetUi.recall(+el.dataset.slot);
  else el.click();
}

// Select's HOLD outcome: only a preset card has one (save); holding over anything else is a no-op,
// the same as holding the pointer down over a plain button.
function padCursorHoldAt(x, y) {
  const el = cursorTarget(x, y);
  if (el && el.classList.contains('preset-card')) presetUi.save(+el.dataset.slot);
}

// Hover feedback (docs/page-cursor.md #2): mark whatever's currently under the crosshair with the
// shared .pad-hover class (shared/theme.css), clearing it from whatever had it before.
let hoveredEl = null;
function padCursorMoveAt(x, y) {
  const el = x == null ? null : cursorTarget(x, y);
  if (el === hoveredEl) return;
  if (hoveredEl) hoveredEl.classList.remove('pad-hover');
  hoveredEl = el;
  if (hoveredEl) hoveredEl.classList.add('pad-hover');
}

// Zoom In/Out (map-act's zoom-in/zoom-out) are repurposed here to scroll the panel — nothing on
// this page to zoom, and the binds already exist end-to-end (docs/page-cursor.md).
const SCROLL_STEP = 60;   // flat constant tuned by feel, like pad-cursor.js's own SPEED

window.addEventListener('message', function (e) {
  const m = e.data;
  if (!m || m.mfd !== true) return;
  if (m.type === 'hud-options-push') {
    applyOptions(m.data || {});
  } else if (m.action === 'cursor-focus') {
    const r = hudPanel.getBoundingClientRect();
    cursor.setFocus(!!m.on, r.left + r.width / 2, r.top + r.height / 2);
  } else if (m.action === 'cursor') {
    cursor.setVector(m.x, m.y);
  } else if (m.action === 'cursor-held') {
    cursor.setSelectHeld(!!m.held);
  } else if (m.action === 'zoom-in') {
    hudPanel.scrollBy({ top: SCROLL_STEP });
  } else if (m.action === 'zoom-out') {
    hudPanel.scrollBy({ top: -SCROLL_STEP });
  }
});

// A one-shot GET makes the page useful standalone and fills the short gap before a shell has an SSE
// snapshot to replay. The push path carries subsequent game-side changes without periodic DOM work.
presetUi.render();
presetUi.refresh();
load();
