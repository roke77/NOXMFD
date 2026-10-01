// Extended-keybinds page. Renders the plugin's bind registry (/keybinds-config) as grouped tables
// beside a section rail, with the plain on/off settings as tiles above them, and writes changes
// back over keybind.* commands. Keyboard capture happens here in the browser (KeyboardEvent.code →
// Unity KeyCode name); joystick capture is armed plugin-side and the result arrives via the shell's
// relayed SSE snapshot. See keybinds.html header + docs/keybinds-page.md.

var rowsEl  = document.getElementById('kb-rows');
var panelEl = document.getElementById('kb-panel');
var mainEl  = document.getElementById('kb-main');
var findEl  = document.getElementById('kb-find');

// Embedded in a shell (classic #page-frame or an F-35 portal) rather than opened standalone? Then
// the shell's own MAIN key is the way back, so drop the in-page back link — it's redundant, and it
// points at '/', which would reload the whole shell instead of just closing the page.
if (window.parent !== window) {
  var back = document.querySelector('.kb-back');
  if (back) back.parentNode.remove();   // its slot too, so the title sits at the edge
}

var binds     = [];      // last /keybinds-config payload
var notes     = {};      // per-section shared-behaviour note, keyed by the server's section title
var capturing = null;    // plugin-side joy/axis capture: bind id or null (server state, mirrored)
var capturingKind = null; // 'joy' | 'axis' | null — which capture `capturing` refers to
var kbCapture = null;    // browser-side keyboard capture: bind id or null (local state)
var kbPending = null;    // modifiers held so far during that capture ("ALT+…"), or null
var bgInput   = false;   // InputWhenGameUnfocused — a plain setting, not a bind (server state)
var remoteKeybinds = false;  // per-browser remote-listening toggle (localStorage, not server state)
var remoteKeybindsSamePc = false;
// Start-state settings (docs/radar-master-arms.md, docs/power-toggle.md) — plain settings, not
// binds, default true (today's behaviour) until the first /keybinds-config bootstrap.
var radarOnOnStart      = true;
var engineOnOnStart     = true;
var masterArmsOnOnStart = true;
var powerOnOnStart      = true;
// HudCombatModeFilters' own on/off switch — default OFF, unlike the four above.
var hudFiltersOnCombatMode = false;
var query     = '';      // lower-cased search text; '' shows everything
var lastJson  = '';      // skip re-render when nothing changed
var rejectSeq = null;    // last seen cfg.rejected.seq (KeybindConflict.cs refusals)
var lastSetKind = {};    // bind id → 'key' | 'joy': which cell the last assignment came from
var capturePollTimer = null;
var commandResyncTimer = null;

// A push follows every accepted server mutation. The delayed one-shot GET also repairs optimistic
// UI if a command is rejected or this page is standalone; rapid edits share one timer.
function sendConfigCommand(cmd, args) {
  var request = sendCommand(cmd, args);
  clearTimeout(commandResyncTimer);
  commandResyncTimer = setTimeout(refresh, 1200);
  return request;
}

// ── Settings tiles ───────────────────────────────────────────────────────────────────────────
// Plain on/off settings, one tile each. `cmd` is the server command that persists it; the remote
// listener has none — it's per browser (localStorage, docs/remote-keybinds.md), so it still works
// while the panel is .unavailable.
var REMOTE_KEYBINDS_STORAGE = 'noxmfd.remoteKeybinds.enabled';

function readRemoteKeybinds() {
  try { return localStorage.getItem(REMOTE_KEYBINDS_STORAGE) === '1'; }
  catch (e) { return false; }
}

function writeRemoteKeybinds(on) {
  try { localStorage.setItem(REMOTE_KEYBINDS_STORAGE, on ? '1' : '0'); } catch (e) {}
  if (window.parent && window.parent !== window) {
    window.parent.postMessage({ type: 'remote-keybinds-enabled', enabled: !!on }, '*');
  }
}

var SETTING_GROUPS = [
  { t: 'INPUT & HUD', note: '' },
  { t: 'ON AT SPAWN', note: 'Which systems start on in a new aircraft. The ON/OFF binds are in 01 Systems.' }
];
var SETTINGS = [
  { grp: 1, label: 'RADAR', icon: 'radar', cmd: 'keybind.set-radar-on-start',
    desc: 'OFF: radar starts off, arm it yourself.',
    get: function () { return radarOnOnStart; }, set: function (v) { radarOnOnStart = v; } },
  { grp: 1, label: 'ENGINE', icon: 'engine', cmd: 'keybind.set-engine-on-start',
    desc: 'OFF: engine starts off, start it yourself.',
    get: function () { return engineOnOnStart; }, set: function (v) { engineOnOnStart = v; } },
  { grp: 1, label: 'MASTER ARM', icon: 'arm', cmd: 'keybind.set-master-arms-on-start',
    desc: 'OFF: guns, missiles and bombs blocked until you arm.',
    get: function () { return masterArmsOnOnStart; }, set: function (v) { masterArmsOnOnStart = v; } },
  { grp: 1, label: 'POWER', icon: 'power', cmd: 'keybind.set-power-on-start',
    desc: 'OFF: no in-cockpit HUD until you power up.',
    get: function () { return powerOnOnStart; }, set: function (v) { powerOnOnStart = v; } },
  { grp: 0, label: 'UNFOCUSED INPUT', icon: 'stick', cmd: 'keybind.set-bg-input', title: 'Input when game unfocused',
    desc: 'Keeps your HOTAS live while this browser has focus. ON on the game PC, OFF on a tablet or phone.',
    get: function () { return bgInput; }, set: function (v) { bgInput = v; } },
  { grp: 0, label: 'REMOTE KEYBINDS', icon: 'remote', title: 'Listen for keybinds (remote)',
    desc: 'This browser sends your keybinds to the game. For a second device; enable on one browser only.',
    warn: 'This browser appears to be on the game PC. Enabling can double-fire inputs.',
    get: function () { return remoteKeybinds; },
    set: function (v) { remoteKeybinds = v; writeRemoteKeybinds(v); } },
  { grp: 0, label: 'HUD BY MODE', icon: 'filter', cmd: 'keybind.set-hud-filters-on-combat-mode', title: 'Combat mode drives HUD filters',
    desc: 'A/A or A/G forces the HUD page preset; idle restores your own.',
    get: function () { return hudFiltersOnCombatMode; }, set: function (v) { hudFiltersOnCombatMode = v; } }
];

var SVG_NS = 'http://www.w3.org/2000/svg';
var ICON_PATHS = {
  radar:  ['M4 20a16 16 0 0 1 16-16', 'M8.5 20A11.5 11.5 0 0 1 20 8.5', 'M13 20a7 7 0 0 1 7-7'],
  engine: ['M12 3c1 3.5 5 5.5 5 10a5 5 0 0 1-10 0c0-2.6 1.4-4.2 2.6-5.4.4 1.9 1.4 2.9 2.4 3.4-.6-3 .1-5.6 0-8z'],
  arm:    ['M12 2v5M12 17v5M2 12h5M17 12h5'],
  power:  ['M12 3v8', 'M6.6 6.6a7.5 7.5 0 1 0 10.8 0'],
  stick:  ['M5 21h14', 'M8 21v-3h8v3', 'M12 18V8'],
  remote: ['M8.5 9.5a5 5 0 0 0 0 7M15.5 9.5a5 5 0 0 1 0 7M5.6 6.6a9 9 0 0 0 0 12.8M18.4 6.6a9 9 0 0 1 0 12.8'],
  filter: ['M4 7h10M18 7h2M4 17h4M12 17h8']
};
// Circles that finish an icon's path set: [cx, cy, r]
var ICON_CIRCLES = {
  radar: [[20, 20, 1.2]], arm: [[12, 12, 7]], stick: [[12, 6, 3]], remote: [[12, 13, 1.6]],
  filter: [[16, 7, 2], [10, 17, 2]]
};

function svgEl(tag, attrs) {
  var el = document.createElementNS(SVG_NS, tag);
  for (var k in attrs) el.setAttribute(k, attrs[k]);
  return el;
}

function iconEl(name) {
  var svg = svgEl('svg', { width: 20, height: 20, viewBox: '0 0 24 24', fill: 'none', stroke: 'currentColor',
    'stroke-width': 1.8, 'stroke-linecap': 'round', 'stroke-linejoin': 'round', 'aria-hidden': 'true' });
  (ICON_PATHS[name] || []).forEach(function (d) { svg.appendChild(svgEl('path', { d: d })); });
  (ICON_CIRCLES[name] || []).forEach(function (c) { svg.appendChild(svgEl('circle', { cx: c[0], cy: c[1], r: c[2] })); });
  return svg;
}

function textEl(tag, cls, text) {
  var el = document.createElement(tag);
  el.className = cls;
  el.textContent = text;
  return el;
}

function settingTile(st) {
  var on = st.get();
  var wrap = document.createElement('div');
  wrap.className = 'kb-set';

  var tile = document.createElement('button');
  tile.className = 'lit kb-tile' + (on ? ' on' : '');
  tile.title = st.title || st.label;
  tile.setAttribute('aria-pressed', on ? 'true' : 'false');
  var lbl = document.createElement('span');
  lbl.className = 'lbl';
  lbl.appendChild(iconEl(st.icon));
  lbl.appendChild(document.createTextNode(st.label));
  tile.appendChild(lbl);
  tile.onclick = function () {
    var next = !st.get();
    if (st.cmd) sendConfigCommand(st.cmd, { on: next }).catch(function () {});
    st.set(next);   // optimistic: the follow-up snapshot reconciles server truth
    renderSettings();
  };
  wrap.appendChild(tile);

  wrap.appendChild(textEl('div', 'kb-set-desc', st.desc));
  if (st.warn) {
    var warn = textEl('div', 'kb-warning' + (remoteKeybindsSamePc ? ' shown' : ''), st.warn);
    wrap.appendChild(warn);
  }
  return wrap;
}

function renderSettings() {
  var host = document.getElementById('kb-settings');
  host.textContent = '';
  if (query) return;   // searching is for binds; the tiles would only get in the way
  host.appendChild(groupHead('', '00', 'SETTINGS', SETTINGS.length + ''));
  SETTING_GROUPS.forEach(function (g, gi) {
    var box = document.createElement('div');
    box.className = 'kb-set-group';
    var head = document.createElement('div');
    head.className = 'kb-section-head';
    head.appendChild(textEl('h3', 'kb-section-t', g.t));
    if (g.note) head.appendChild(textEl('span', 'kb-note', g.note));
    box.appendChild(head);
    var tiles = document.createElement('div');
    tiles.className = 'kb-set-tiles';
    SETTINGS.forEach(function (st) { if (st.grp === gi) tiles.appendChild(settingTile(st)); });
    box.appendChild(tiles);
    host.appendChild(box);
  });
  fitTiles();
}

// Sizes every tile to the widest label plus the tile's own side padding (2 x 18px + 2px border).
// Measured rather than declared because the label width depends on the loaded font; the font-ready
// hook below re-fits once it arrives.
function fitTiles() {
  var widest = 0;
  document.querySelectorAll('#kb-settings .kb-tile .lbl').forEach(function (l) {
    widest = Math.max(widest, Math.ceil(l.getBoundingClientRect().width));
  });
  if (!widest) return;
  document.querySelectorAll('#kb-settings .kb-set-tiles').forEach(function (t) {
    t.style.setProperty('--kb-tile-w', (widest + 38) + 'px');
  });
}
if (document.fonts && document.fonts.ready) document.fonts.ready.then(fitTiles);

// Key naming (KeyboardEvent.code → Unity KeyCode name, and its compact display form) lives in
// keybinds-keymap.js, pure and unit-checked.
var displayName = KeybindsKeymap.displayName;
var isBound = KeybindsGroups.isBound, joyText = KeybindsGroups.joyText, axisText = KeybindsGroups.axisText;

// ── Render ───────────────────────────────────────────────────────────────────────────────────
function cell(bind, kind) {
  var wrap = document.createElement('div');
  wrap.className = 'kb-cell';

  var val = document.createElement('button');
  val.className = 'kb-val';
  var bound = kind === 'key' ? !!bind.key : bind.joyButton >= 0;
  if (kind === 'key' && kbCapture === bind.id) { val.textContent = kbPending || 'PRESS A KEY…'; val.className += ' capturing'; }
  else if (kind === 'joy' && capturing === bind.id && capturingKind === 'joy')
                                               { val.textContent = 'PRESS A BUTTON…'; val.className += ' capturing'; }
  else if (!bound)                             { val.textContent = '—';               val.className += ' unbound'; }
  // joystick display carries the device number when pinned ("J2 B55") — with a multi-stick
  // HOTAS the button index alone is ambiguous
  else val.textContent = kind === 'key' ? displayName(bind.key) : joyText(bind);
  val.onclick = function () { (kind === 'key' ? keyCellClick : joyCellClick)(bind.id); };

  var clear = document.createElement('button');
  clear.className = 'kb-clear' + (bound ? ' bound' : '');
  clear.textContent = '×';
  clear.title = 'clear';
  clear.onclick = function (e) {
    e.stopPropagation();
    if (kbCapture === bind.id) kbCapture = null;
    var cmd  = kind === 'key' ? 'keybind.set-key' : 'keybind.clear-joy';
    var args = kind === 'key' ? { bind: bind.id, key: '' } : { bind: bind.id };
    sendConfigCommand(cmd, args).catch(function () {});
    // Optimistic: the follow-up snapshot reconciles server truth.
    if (kind === 'key') bind.key = ''; else bind.joyButton = -1;
    render();
  };

  wrap.appendChild(val); wrap.appendChild(clear);
  return wrap;
}

// An axis-only row (docs/map-cursor.md — Cursor Horizontal/Vertical/Zoom): a continuous value has
// no keyboard side, so the row says so and this cell sits in the joystick column with the axis
// value, an invert toggle, and clear.
function axisCell(bind) {
  var wrap = document.createElement('div');
  wrap.className = 'kb-cell';

  var val = document.createElement('button');
  val.className = 'kb-val';
  var bound = bind.axis >= 0;
  if (capturing === bind.id && capturingKind === 'axis') { val.textContent = 'MOVE THE AXIS…'; val.className += ' capturing'; }
  else if (!bound)                                       { val.textContent = '—';              val.className += ' unbound'; }
  // carries the device number when pinned ("J2 A3") — with a multi-stick HOTAS the axis index
  // alone is ambiguous, same reasoning as the joystick button cell.
  else val.textContent = axisText(bind);
  val.onclick = function () { axisCellClick(bind.id); };

  var invert = document.createElement('button');
  invert.className = 'kb-invert' + (bind.axisInvert ? ' on' : '');
  invert.textContent = 'INV';
  invert.title = 'flip axis polarity';
  invert.onclick = function (e) {
    e.stopPropagation();
    var next = !bind.axisInvert;
    sendConfigCommand('keybind.set-axis-invert', { bind: bind.id, on: next }).catch(function () {});
    bind.axisInvert = next;   // optimistic: the follow-up snapshot reconciles server truth
    render();
  };

  var clear = document.createElement('button');
  clear.className = 'kb-clear' + (bound ? ' bound' : '');
  clear.textContent = '×';
  clear.title = 'clear';
  clear.onclick = function (e) {
    e.stopPropagation();
    sendConfigCommand('keybind.clear-axis', { bind: bind.id }).catch(function () {});
    bind.axis = -1; bind.axisNum = 0; bind.axisInvert = false;   // optimistic
    render();
  };

  wrap.appendChild(val); wrap.appendChild(invert); wrap.appendChild(clear);
  return wrap;
}

function buildRow(b) {
  var row = document.createElement('div');
  row.className = 'kb-row';
  row.dataset.bindId = b.id;   // lets flashRejected find this row
  var fn = document.createElement('div');
  fn.className = 'kb-fn';
  fn.appendChild(textEl('span', 'kb-name', b.label.toUpperCase()));
  var desc = textEl('span', 'kb-desc', b.description || '');
  desc.title = b.description || '';
  fn.appendChild(desc);
  row.appendChild(fn);
  if (b.axis !== undefined && b.key === undefined) {
    row.appendChild(textEl('span', 'kb-na', 'AXIS ONLY'));
    row.appendChild(axisCell(b));
  } else if (b.key !== undefined && b.joyButton === undefined) {
    // Key-only row (e.g. SAVE/LOAD LAYOUT): browser-side only, deliberately no joystick/HOTAS option.
    row.appendChild(cell(b, 'key'));
    row.appendChild(textEl('span', 'kb-na', 'KEYBOARD ONLY'));
  } else {
    row.appendChild(cell(b, 'key'));
    row.appendChild(cell(b, 'joy'));
  }
  return row;
}

// A numbered group header: the rail links to it by id.
function groupHead(id, n, t, count) {
  var head = document.createElement('div');
  head.className = 'kb-group-head';
  if (id) head.id = id;
  head.appendChild(textEl('span', 'kb-group-n', n));
  head.appendChild(textEl('h2', 'kb-group-t', t));
  head.appendChild(textEl('span', 'kb-group-c', count));
  return head;
}

function railLink(cls, targetId, parts) {
  var a = document.createElement('a');
  a.className = cls;
  if (cls === 'kb-rail-g') a.title = parts[1][1];   // the collapsed strip shows only the number
  a.href = '#' + targetId;
  parts.forEach(function (p) { a.appendChild(textEl('span', p[0], p[1])); });
  return a;
}

function renderRail(groups) {
  var rail = document.getElementById('kb-rail-links');
  rail.textContent = '';
  var set = document.createElement('div');
  set.className = 'kb-rail-group';
  set.appendChild(railLink('kb-rail-g', 'kb-settings', [['n', '00'], ['t', 'SETTINGS'], ['c', SETTINGS.length + '']]));
  SETTING_GROUPS.forEach(function (g, gi) {
    var n = SETTINGS.filter(function (s) { return s.grp === gi; }).length;
    set.appendChild(railLink('kb-rail-s', 'kb-settings', [['', g.t], ['', n + '']]));
  });
  rail.appendChild(set);
  groups.forEach(function (g) {
    var box = document.createElement('div');
    box.className = 'kb-rail-group';
    box.appendChild(railLink('kb-rail-g', 'kb-g-' + g.id, [['n', g.n], ['t', g.t], ['c', g.bound + '/' + g.all]]));
    var bar = document.createElement('div');
    bar.className = 'kb-rail-bar';
    var fill = document.createElement('div');
    fill.style.width = Math.round(100 * g.bound / g.all) + '%';
    bar.appendChild(fill);
    box.appendChild(bar);
    g.secs.forEach(function (s) {
      box.appendChild(railLink('kb-rail-s', 'kb-s-' + s.id, [['', s.t], ['', s.bound + '/' + s.rows.length]]));
    });
    rail.appendChild(box);
  });
}

function render() {
  var groups = KeybindsGroups.build(binds, notes);
  var all = 0, bound = 0;
  groups.forEach(function (g) { all += g.all; bound += g.bound; });
  document.getElementById('kb-count').textContent = bound + ' / ' + all + ' BOUND';
  renderRail(groups);
  renderSettings();

  rowsEl.textContent = '';
  var shown = 0;
  groups.forEach(function (g) {
    var secs = g.secs.map(function (s) {
      return { s: s, rows: query ? s.rows.filter(function (b) { return KeybindsGroups.searchText(b, displayName).indexOf(query) >= 0; }) : s.rows };
    }).filter(function (x) { return x.rows.length; });
    if (!secs.length) return;

    var box = document.createElement('section');
    box.className = 'kb-group';
    box.appendChild(groupHead('kb-g-' + g.id, g.n, g.t, g.bound + ' BOUND'));
    var cols = document.createElement('div');
    cols.className = 'kb-head';
    ['FUNCTION', 'KEYBOARD', 'JOYSTICK / HOTAS'].forEach(function (t) { cols.appendChild(textEl('span', '', t)); });
    box.appendChild(cols);
    secs.forEach(function (x) {
      var s = document.createElement('div');
      s.className = 'kb-section';
      s.id = 'kb-s-' + x.s.id;
      var head = document.createElement('div');
      head.className = 'kb-section-head';
      head.appendChild(textEl('h3', 'kb-section-t', x.s.t));
      if (x.s.note) head.appendChild(textEl('span', 'kb-note', x.s.note));
      s.appendChild(head);
      x.rows.forEach(function (b) { s.appendChild(buildRow(b)); shown++; });
      box.appendChild(s);
    });
    rowsEl.appendChild(box);
  });
  document.getElementById('kb-none').classList.toggle('shown', !!query && !shown);
}

// The rail's collapsed state is the pane's choice once made: the toggle saves it (per browser,
// localStorage) and a saved value wins over the width. With nothing saved, the rail starts
// collapsed on a pane 1152px wide or less and open otherwise, following the width as it changes.
var RAIL_STORAGE = 'noxmfd.keybinds.railCollapsed';
var railEl = document.getElementById('kb-rail');
var railToggle = document.getElementById('kb-rail-toggle');
var narrow = window.matchMedia('(max-width: 1152px)');

function readRailPref() {
  try {
    var v = localStorage.getItem(RAIL_STORAGE);
    return v === '1' ? true : v === '0' ? false : null;
  } catch (e) { return null; }
}

function setRailCollapsed(collapsed) {
  railEl.classList.toggle('collapsed', collapsed);
  railToggle.setAttribute('aria-expanded', collapsed ? 'false' : 'true');
}
function applyRailDefault() {
  var pref = readRailPref();
  setRailCollapsed(pref === null ? narrow.matches : pref);
}
railToggle.onclick = function () {
  var collapsed = !railEl.classList.contains('collapsed');
  try { localStorage.setItem(RAIL_STORAGE, collapsed ? '1' : '0'); } catch (e) {}
  setRailCollapsed(collapsed);
};
narrow.addEventListener('change', applyRailDefault);
applyRailDefault();

// The rail's links scroll the column rather than navigate: the page is usually inside a shell
// frame, where a hash change has nothing useful to do.
document.querySelector('.kb-rail').addEventListener('click', function (e) {
  var a = e.target.closest && e.target.closest('a');
  if (!a) return;
  e.preventDefault();
  var target = document.getElementById(a.getAttribute('href').slice(1));
  if (target) target.scrollIntoView({ block: 'start' });
});

findEl.addEventListener('input', function () {
  query = findEl.value.trim().toLowerCase();
  render();
});

// ── Keyboard capture (browser-side) ──────────────────────────────────────────────────────────
function keyCellClick(id) {
  if (capturing) sendConfigCommand('keybind.cancel-joy', {}).catch(function () {});
  kbCapture = kbCapture === id ? null : id;
  kbPending = null;
  render();
}

// Keyup too: holding only modifiers keeps listening (the cell shows "ALT+…"), and releasing a
// modifier before any other key binds that modifier on its own (KeybindsKeymap.captureStep).
function onCaptureKey(e) {
  if (!kbCapture) return;
  e.preventDefault();   // also keeps a lone Alt from focusing the browser's menu bar
  var cancel = e.type === 'keydown' && e.code === 'Escape';
  var step = cancel ? null : KeybindsKeymap.captureStep(e);
  if (!cancel && !step) return;
  if (step && 'pending' in step) { kbPending = step.pending; render(); return; }
  var id = kbCapture;
  kbCapture = null;
  kbPending = null;
  if (cancel) { render(); return; }
  var key = step.key;
  if (!key) { flashRejected(id); return; }   // unmappable (media keys, ...)
  sendConfigCommand('keybind.set-key', { bind: id, key: key }).catch(function () {});
  lastSetKind[id] = 'key';
  // Optimistic: the follow-up snapshot reconciles server truth.
  binds.forEach(function (b) { if (b.id === id) b.key = key; });
  render();
}
document.addEventListener('keydown', onCaptureKey);
document.addEventListener('keyup', onCaptureKey);

// brief red flash on the keyboard cell of a bind whose captured key can't be mapped. Looks the row
// up by id (row.dataset.bindId, set in buildRow) rather than a positional index. Also names the
// bind already using a refused key/button (text = 'USED BY …'); kind picks which of the row's two
// cells shows it.
function flashRejected(id, text, kind) {
  render();
  var row = document.querySelector('.kb-row[data-bind-id="' + id + '"]');
  if (!row) return;
  var vals = row.querySelectorAll('.kb-val');
  var val = vals[kind === 'joy' && vals.length > 1 ? 1 : 0];
  val.classList.add('rejected');
  val.textContent = text || 'UNSUPPORTED';
  setTimeout(render, text ? 1800 : 900);
}

// ── Joystick capture (plugin-side) ───────────────────────────────────────────────────────────
function joyCellClick(id) {
  kbCapture = null;
  var already = capturing === id && capturingKind === 'joy';
  sendConfigCommand(already ? 'keybind.cancel-joy' : 'keybind.arm-joy', { bind: id }).catch(function () {});
  lastSetKind[id] = 'joy';
  capturing = already ? null : id;             // optimistic; the poll is the truth
  capturingKind = already ? null : 'joy';
  render();
  updateCaptureFallback();
}

// ── Axis capture (plugin-side, docs/map-cursor.md) ───────────────────────────────────────────
function axisCellClick(id) {
  kbCapture = null;
  var already = capturing === id && capturingKind === 'axis';
  sendConfigCommand(already ? 'keybind.cancel-axis' : 'keybind.arm-axis', { bind: id }).catch(function () {});
  capturing = already ? null : id;
  capturingKind = already ? null : 'axis';
  render();
  updateCaptureFallback();
}

// ── Server state ─────────────────────────────────────────────────────────────────────────────
function applyConfig(cfg) {
    panelEl.classList.remove('unavailable');
    // never clobber an in-progress keyboard capture cell with a re-render; deliberately do NOT
    // record lastJson here, so the first poll after capture ends re-renders whatever changed
    if (kbCapture) return;
    var json = JSON.stringify(cfg);
    if (json === lastJson) return;
    lastJson  = json;
    binds     = cfg.binds || [];
    notes     = cfg.notes || {};
    capturing = cfg.capturing || null;
    capturingKind = cfg.capturingKind || null;
    bgInput = !!cfg.bgInput;
    remoteKeybinds = readRemoteKeybinds();
    remoteKeybindsSamePc = !!cfg.remoteKeybindsSamePc;
    radarOnOnStart      = cfg.radarOnOnStart      !== false;
    engineOnOnStart     = cfg.engineOnOnStart     !== false;
    masterArmsOnOnStart = cfg.masterArmsOnOnStart !== false;
    powerOnOnStart      = cfg.powerOnOnStart      !== false;
    hudFiltersOnCombatMode = !!cfg.hudFiltersOnCombatMode;   // defaults OFF, not ON like the four above
    render();
    updateCaptureFallback();
    var r = cfg.rejected;
    if (r && rejectSeq !== null && r.seq !== rejectSeq && lastSetKind[r.bind])
      flashRejected(r.bind, 'USED BY ' + r.by.toUpperCase(), lastSetKind[r.bind]);
    if (r) rejectSeq = r.seq;
}

function refresh() {
  return fetch('/keybinds-config', { cache: 'no-store' }).then(function (r) { return r.json(); })
    .then(applyConfig).catch(function () { panelEl.classList.add('unavailable'); });
}

// Embedded pages normally receive capture completion over SSE. A short-lived poll exists only
// while joystick/axis capture is armed so standalone KEY previews retain the same feedback path.
function updateCaptureFallback() {
  if (capturing && capturePollTimer == null) capturePollTimer = setInterval(refresh, 600);
  else if (!capturing && capturePollTimer != null) {
    clearInterval(capturePollTimer);
    capturePollTimer = null;
  }
}

window.addEventListener('message', function (e) {
  var m = e.data;
  if (m && m.mfd === true && m.type === 'keybinds-config-push') applyConfig(m.data || {});
});

remoteKeybinds = readRemoteKeybinds();
render();   // settings tiles at their defaults until the first fetch resolves
if (window.parent === window) refresh();
else window.parent.postMessage({ mfd: true, type: 'keybinds-config-request' }, '*');
