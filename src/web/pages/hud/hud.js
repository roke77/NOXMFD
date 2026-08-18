// HUD page — a clickable replica of the game's HUD OPTIONS screen (HUDOptions). Fetch-driven, not
// telemetry-pushed: it GETs the current state from /hud-options and POSTs hud.* commands back. See
// hud.html for the control contract and docs/hud-page.md for the model.
import { createPadCursor } from '/assets/services/pad-cursor.js';

// The seven categories in listCategories order. The game exposes no per-category display name, and
// this order is fixed in its inspector, so the labels live here (docs/hud-page.md). The count from
// /hud-options is checked against this so a game-side reorder surfaces rather than mislabels.
const CATEGORY_LABELS = ['FRIENDLY', 'ENEMY', 'AIRCRAFT', 'MISSILES', 'VEHICLES', 'BUILDINGS', 'SHIPS'];
// Which category rows own a sub-type chip grid, and the hud.set group that drives it.
const SUBTYPE_GROUP = { 4: 'vehicle', 5: 'building' };
// The mod's captured type sprites, by group — the real in-game icons. Vehicle names reuse the TGT
// page's capture; buildings have their own (a name like RDR is in both, so they can't share).
const ICON_BASE = { vehicle: '/tgt-icon', building: '/building-icon' };
// The two faction categories are coloured apart from the green type categories, as in game — see
// hud.css. Index into CATEGORY_LABELS.
const FACTION_CLASS = { 0: 'friendly', 1: 'enemy' };

// The in-game screen centres a real type glyph on AIRCRAFT/MISSILES/VEHICLES/BUILDINGS/SHIPS (not the
// two faction rows). HUDOptions exposes no per-category icon field for it, but it turned out to be a
// plain child Image ("TopContainer/Icon") on each row, found by a one-shot hierarchy dump and captured
// to /hud-cat-icon keyed by this label (docs/hud-page.md, AssetCapture.TryCaptureHudCategoryIcons).
// FRIENDLY/ENEMY (0, 1) have no entry — the game draws no glyph on those rows either.
const CAT_ICON_INDICES = new Set([2, 3, 4, 5, 6]);

// The category's captured glyph, or null for a row with none. Same retry-on-404 approach as subIcon
// below: the mod extracts these over the mission's first few scans, so an early request can 404.
function catIcon(index) {
  if (!CAT_ICON_INDICES.has(index)) return null;
  const img = document.createElement('img');
  img.className = 'hud-cat-icon';
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

// Native-HUD declutter toggles — the mod's own HudDeclutter flags (declutter.set), a separate axis
// from the HUDOptions unit-icon controls: they hide native game HUD widgets. `key` is the group the
// command carries; the /hud-options `declutter` object reports each flag's HIDE state (true = hidden).
const DECLUTTER = [
  { key: 'weapon',  label: 'WEAPONS' },
  { key: 'minimap', label: 'MINIMAP' },
  { key: 'boxes',   label: 'FLIGHT' },
  { key: 'feed',    label: 'FEED' },   // native kill-feed ticker (issue #34, docs/akf-page.md)
  // Not a native widget like the four above — this one hides the mod's OWN waypoint cue
  // (docs/hud-waypoint-indicator.md). It lives here because this strip is where HUD element
  // visibility is controlled, and a second control surface for one toggle isn't worth it.
  { key: 'wpt',     label: 'WPT' },
];

const dcEl    = document.getElementById('hud-declutter');
const modesEl = document.getElementById('hud-modes');
const catsEl  = document.getElementById('hud-cats');
const emptyEl = document.getElementById('hud-empty');

let data = null;          // last /hud-options snapshot

// The game builds the toggles from the Encyclopedia with underscored names (IR_SAM); the in-game
// screen shows them spaced. Match that.
function pretty(name) { return String(name).replace(/_/g, ' '); }

function load() {
  fetch('/hud-options', { cache: 'no-store' })
    .then(function (r) { return r.ok ? r.json() : {}; })
    .then(render)
    .catch(function () { render({}); });
}

function send(cmd, body) {
  sendCommand(cmd, body).catch(function () {});
}

function render(d) {
  data = d;
  const has = d && Array.isArray(d.categories) && d.categories.length > 0;
  // The declutter object rides the same payload but is its own axis; guard it separately so an older
  // plugin (or an empty payload) simply omits the strip rather than showing dead toggles.
  const hasDc = has && d.declutter && typeof d.declutter === 'object';
  emptyEl.style.display = has ? 'none' : '';
  dcEl.style.display    = hasDc ? '' : 'none';
  modesEl.style.display = has ? '' : 'none';
  catsEl.style.display  = has ? '' : 'none';
  if (!has) return;
  if (hasDc) renderDeclutter();
  renderModes();
  renderCats();
}

// The declutter strip: one toggle per native widget. Lit = the widget is SHOWN on the HUD (flag off);
// gray = hidden/decluttered. Inverts the reported HIDE flag so it reads like the rest of the page
// (lit green = visible on the HUD). The command's `on` is the HIDE state, so it's the inverse of lit.
function renderDeclutter() {
  dcEl.textContent = '';
  const dc = data.declutter;
  DECLUTTER.forEach(function (item) {
    const b = document.createElement('button');
    b.className = 'hud-dc pad-hoverable' + (dc[item.key] ? '' : ' on');   // flag true = hidden = not lit
    b.textContent = item.label;
    b.addEventListener('click', function () {
      const nextShown = !b.classList.contains('on');
      b.classList.toggle('on', nextShown);                  // optimistic
      dc[item.key] = !nextShown;                            // store HIDE state back
      send('declutter.set', { group: item.key, on: !nextShown });
    });
    dcEl.appendChild(b);
  });
}

function renderModes() {
  modesEl.textContent = '';
  (data.modes || []).forEach(function (name, i) {
    const b = document.createElement('button');
    b.className = 'hud-mode pad-hoverable' + (i === data.mode ? ' on' : '');
    b.textContent = name;
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
    catsEl.appendChild(catRow(CATEGORY_LABELS[i] || ('CAT ' + i), on, i));
    const group = SUBTYPE_GROUP[i];
    if (group) catsEl.appendChild(subGrid(group, data[group === 'vehicle' ? 'vehicles' : 'buildings'] || []));
  });
}

// One category row: name on the left, a MAXIMIZE toggle on the right. The two faction rows carry a
// colour class so the row and its toggle read in the game's cyan/red instead of the type green.
function catRow(label, on, index) {
  const row = document.createElement('div');
  row.className = 'hud-cat' + (FACTION_CLASS[index] ? ' faction-' + FACTION_CLASS[index] : '');
  const name = document.createElement('span');
  name.className = 'hud-cat-name';
  name.textContent = label;
  const btn = document.createElement('button');
  btn.className = 'hud-max pad-hoverable' + (on ? ' on' : '');
  btn.textContent = 'MAXIMIZE';
  btn.addEventListener('click', function () {
    const next = !btn.classList.contains('on');
    btn.classList.toggle('on', next);       // optimistic
    data.categories[index] = next;
    send('hud.set', { group: 'category', index: index, on: next });
  });
  row.appendChild(name);
  const icon = catIcon(index);
  if (icon) row.appendChild(icon);
  row.appendChild(btn);
  return row;
}

// The chip grid under VEHICLES / BUILDINGS — one toggle per unit type, the real captured icon over
// its label (the same icon-over-label the TGT vehicle filter uses).
function subGrid(group, items) {
  const grid = document.createElement('div');
  grid.className = 'hud-subs';
  items.forEach(function (it, i) {
    const chip = document.createElement('button');
    chip.className = 'hud-sub pad-hoverable' + (it.on ? ' on' : '');
    chip.appendChild(subIcon(group, it.n));
    const lbl = document.createElement('span');
    lbl.className = 'hud-sub-label';
    lbl.textContent = pretty(it.n);
    chip.appendChild(lbl);
    chip.addEventListener('click', function () {
      const next = !chip.classList.contains('on');
      chip.classList.toggle('on', next);    // optimistic
      it.on = next;
      send('hud.set', { group: group, index: i, on: next });
    });
    grid.appendChild(chip);
  });
  return grid;
}

// The captured type sprite. The mod extracts these over the first few mission scans, so a request
// can 404 if the page is opened early — hide and retry a handful of times, the label carries it
// meanwhile. Exactly the TGT page's approach.
function subIcon(group, name) {
  const img = document.createElement('img');
  img.className = 'hud-sub-icon';
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

// ── PAD cursor (docs/page-cursor.md) ──────────────────────────────────────────────────
// Same crosshair/transport MAP uses (pad-cursor.js), driven here only while this HUD is the SOI's
// focused surface. .hud-panel scrolls its own content (overflow-y: auto — the category list is
// often taller than the frame), so the cursor element lives OUTSIDE it (a sibling in hud.html) and
// is positioned in viewport coordinates instead of panel-local ones: a child positioned relative to
// a scrolling ancestor would drift with the content on every scroll, which a cursor overlay must
// never do (contrast TGT, whose .tgt-panel doesn't scroll, so panel-local coordinates work there).
const CURSORABLE = '.hud-dc, .hud-mode, .hud-max, .hud-sub';
const hudPanel   = document.getElementById('hud-panel');
const padCursorEl = document.getElementById('pad-cursor');
const cursor = createPadCursor({
  el: padCursorEl,
  clampRect: () => {
    const r = hudPanel.getBoundingClientRect();
    return { dx: r.left, dy: r.top, dw: r.width, dh: r.height };
  },
  onSelect: padCursorSelectAt,
  onMove: padCursorMoveAt,
});

// Select performs whatever a click already does here — every HUD control (declutter/mode/category/
// subtype) is wired to a plain 'click' listener, so this is a synthetic click at the crosshair's
// point, exactly what a mouse or touch tap already does. No hold behaviour on this page (unlike
// TGT's filter cells), so HUD keeps the plain edge-driven select (docs/page-cursor.md).
function padCursorSelectAt(x, y) {
  const raw = document.elementFromPoint(x, y);
  const el = raw && raw.closest(CURSORABLE);
  if (el) el.click();
}

// Hover feedback (docs/page-cursor.md #2): mark whatever's currently under the crosshair with the
// shared .pad-hover class (shared/theme.css), clearing it from whatever had it before.
let hoveredEl = null;
function padCursorMoveAt(x, y) {
  const raw = x == null ? null : document.elementFromPoint(x, y);
  const el = raw && raw.closest(CURSORABLE);
  if (el === hoveredEl) return;
  if (hoveredEl) hoveredEl.classList.remove('pad-hover');
  hoveredEl = el;
  if (hoveredEl) hoveredEl.classList.add('pad-hover');
}

// Zoom In/Out (map-act's zoom-in/zoom-out) are repurposed here to scroll the panel — nothing on
// this page to zoom, and the binds already exist end-to-end (docs/page-cursor.md).
const SCROLL_STEP = 60;   // ponytail: flat constant tuned by feel, like pad-cursor.js's own SPEED

window.addEventListener('message', function (e) {
  const m = e.data;
  if (!m || m.mfd !== true) return;
  if (m.action === 'cursor-focus') {
    const r = hudPanel.getBoundingClientRect();
    cursor.setFocus(!!m.on, r.left + r.width / 2, r.top + r.height / 2);
  } else if (m.action === 'cursor') {
    cursor.setVector(m.x, m.y);
  } else if (m.action === 'cursor-select') {
    cursor.select();
  } else if (m.action === 'zoom-in') {
    hudPanel.scrollBy({ top: SCROLL_STEP });
  } else if (m.action === 'zoom-out') {
    hudPanel.scrollBy({ top: -SCROLL_STEP });
  }
});

// Unlike the telemetry-pushed pages (which get a live stream and just react to it), HUD OPTIONS
// has no push channel — the plugin refreshes /hud-options on its own 1 Hz tick (TelemetryServer.
// RefreshHudOptions), so this page polls at the same cadence. One poll loop covers three cases at
// once: a toggle pressed in game rather than here, this page's own optimistic writes settling to
// the real state, and a mission starting/ending (render() already treats an empty payload as
// "unavailable", so the next poll after a new mission loads just repaints with the fresh state).
load();
setInterval(load, 1200);
