// Extended-keybinds page. Renders the plugin's bind registry (/keybinds-config) as a table and
// writes changes back over keybind.* commands. Keyboard capture happens here in the browser
// (KeyboardEvent.code → Unity KeyCode name); joystick capture is armed plugin-side and the result
// arrives via the poll. See keybinds.html header + docs/keybinds-page.md.

var rowsEl  = document.getElementById('kb-rows');
// Immersion options (docs/radar-master-arms.md) is a true second section, not a continuation of
// the table above — its 8 binds render into their own container, under their own header row.
var IMMERSION_SECTION = 'IMMERSION OPTIONS';
var immersionRowsEl = document.getElementById('kb-immersion-rows');
var panelEl = document.getElementById('kb-panel');

// Embedded in a shell (classic #page-frame or an F-35 portal) rather than opened standalone? Then
// the shell's own MAIN key is the way back, so drop the in-page back link — it's redundant, and it
// points at '/', which would reload the whole shell instead of just closing the page.
if (window.parent !== window) {
  var back = document.querySelector('.kb-back');
  if (back) back.remove();
}

var binds     = [];      // last /keybinds-config payload
var notes     = {};      // per-section shared-behaviour note, keyed by section title
var capturing = null;    // plugin-side joy/axis capture: bind id or null (server state, mirrored)
var capturingKind = null; // 'joy' | 'axis' | null — which capture `capturing` refers to
var kbCapture = null;    // browser-side keyboard capture: bind id or null (local state)
var bgInput   = false;   // InputWhenGameUnfocused — a plain setting, not a bind (server state)
// Immersion start-state settings (docs/radar-master-arms.md) — same shape as bgInput above: plain
// settings, not binds, default true (today's behaviour) until the first /keybinds-config poll.
var radarOnOnStart      = true;
var engineOnOnStart     = true;
var masterArmsOnOnStart = true;
var lastJson  = '';      // skip re-render when nothing changed

// ── Input-when-unfocused toggle ──────────────────────────────────────────────────────────────
var bgInputBtn = document.getElementById('kb-bg-input-btn');
function renderBgToggle() {
  bgInputBtn.textContent = bgInput ? 'ON' : 'OFF';
  bgInputBtn.classList.toggle('on', bgInput);
}
bgInputBtn.onclick = function () {
  var next = !bgInput;
  sendCommand('keybind.set-bg-input', { on: next }).catch(function () {});
  bgInput = next;   // optimistic: show it now, the poll confirms
  renderBgToggle();
};

// ── Immersion start-state toggles (docs/radar-master-arms.md) ───────────────────────────────
// Three settings, identical shape to the one above — a tiny factory instead of repeating it 3x.
function makeSettingToggle(btnId, cmd, get, set) {
  var btn = document.getElementById(btnId);
  function render() {
    btn.textContent = get() ? 'ON' : 'OFF';
    btn.classList.toggle('on', get());
  }
  btn.onclick = function () {
    var next = !get();
    sendCommand(cmd, { on: next }).catch(function () {});
    set(next);   // optimistic: show it now, the poll confirms
    render();
  };
  return render;
}
var renderRadarOnStart = makeSettingToggle('kb-radar-on-start-btn', 'keybind.set-radar-on-start',
  function () { return radarOnOnStart; }, function (v) { radarOnOnStart = v; });
var renderEngineOnStart = makeSettingToggle('kb-engine-on-start-btn', 'keybind.set-engine-on-start',
  function () { return engineOnOnStart; }, function (v) { engineOnOnStart = v; });
var renderMasterArmsOnStart = makeSettingToggle('kb-master-arms-on-start-btn', 'keybind.set-master-arms-on-start',
  function () { return masterArmsOnOnStart; }, function (v) { masterArmsOnOnStart = v; });

// Key naming (KeyboardEvent.code → Unity KeyCode name, and its compact display form) lives in
// keybinds-keymap.js, pure and unit-checked.
var codeToKey = KeybindsKeymap.codeToKey;
var displayKey = KeybindsKeymap.displayKey;

// ── Render ───────────────────────────────────────────────────────────────────────────────────
function cell(bind, kind) {
  var wrap = document.createElement('div');
  wrap.className = 'kb-cell';

  var val = document.createElement('button');
  val.className = 'kb-val';
  var bound = kind === 'key' ? !!bind.key : bind.joyButton >= 0;
  if (kind === 'key' && kbCapture === bind.id) { val.textContent = 'PRESS A KEY…';    val.className += ' capturing'; }
  else if (kind === 'joy' && capturing === bind.id && capturingKind === 'joy')
                                               { val.textContent = 'PRESS A BUTTON…'; val.className += ' capturing'; }
  else if (!bound)                             { val.textContent = '—';               val.className += ' unbound'; }
  // joystick display carries the device number when pinned ("J2 B55") — with a multi-stick
  // HOTAS the button index alone is ambiguous
  else val.textContent = kind === 'key' ? displayKey(bind.key)
    : (bind.joyNum > 0 ? 'J' + bind.joyNum + ' B' + bind.joyButton : 'JOY ' + bind.joyButton);
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
    sendCommand(cmd, args).then(refresh).catch(function () {});
    // optimistic: show the cleared state now, the poll confirms
    if (kind === 'key') bind.key = ''; else bind.joyButton = -1;
    render();
  };

  wrap.appendChild(val); wrap.appendChild(clear);
  return wrap;
}

// An axis-only row (docs/map-cursor.md — MAP Cursor Horizontal/Vertical): no key/joy cells make
// sense for a continuous value, so this renders one wide cell (spanning both value columns, see
// render()) with the axis value, an invert toggle, and clear.
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
  else val.textContent = bind.axisNum > 0 ? 'J' + bind.axisNum + ' A' + bind.axis : 'AXIS ' + bind.axis;
  val.onclick = function () { axisCellClick(bind.id); };

  var invert = document.createElement('button');
  invert.className = 'kb-invert' + (bind.axisInvert ? ' on' : '');
  invert.textContent = 'INVERT';
  invert.title = 'flip axis polarity';
  invert.onclick = function (e) {
    e.stopPropagation();
    var next = !bind.axisInvert;
    sendCommand('keybind.set-axis-invert', { bind: bind.id, on: next }).then(refresh).catch(function () {});
    bind.axisInvert = next;   // optimistic: show it now, the poll confirms
    render();
  };

  var clear = document.createElement('button');
  clear.className = 'kb-clear' + (bound ? ' bound' : '');
  clear.textContent = '×';
  clear.title = 'clear';
  clear.onclick = function (e) {
    e.stopPropagation();
    sendCommand('keybind.clear-axis', { bind: bind.id }).then(refresh).catch(function () {});
    bind.axis = -1; bind.axisNum = 0; bind.axisInvert = false;   // optimistic
    render();
  };

  wrap.appendChild(val); wrap.appendChild(invert); wrap.appendChild(clear);
  return wrap;
}

// One bind row — shared by the main table and the Immersion options table below it.
function buildRow(b) {
  var row = document.createElement('div');
  row.className = 'kb-row';
  var fn = document.createElement('div');
  var name = document.createElement('div');
  name.className = 'kb-name';
  name.textContent = b.label.toUpperCase();
  fn.appendChild(name);
  var desc = document.createElement('div');
  desc.className = 'kb-desc';
  desc.textContent = b.description || '';
  fn.appendChild(desc);
  row.appendChild(fn);
  if (b.axis !== undefined && b.key === undefined) {
    // Axis-only row: one wide cell spanning both value columns, rather than an always-empty
    // key cell next to an always-empty joy cell.
    var wide = axisCell(b);
    wide.style.gridColumn = '2 / span 2';
    row.appendChild(wide);
  } else if (b.key !== undefined && b.joyButton === undefined) {
    // Key-only row (issue #51 — SAVE/LOAD LAYOUT): browser-side only, deliberately no joystick/
    // HOTAS option, so there's no joyButton field to render a second cell for.
    var wideKey = cell(b, 'key');
    wideKey.style.gridColumn = '2 / span 2';
    row.appendChild(wideKey);
  } else {
    row.appendChild(cell(b, 'key'));
    row.appendChild(cell(b, 'joy'));
  }
  return row;
}

function render() {
  rowsEl.textContent = '';
  var section = null;
  binds.forEach(function (b) {
    if (b.section === IMMERSION_SECTION) return;   // its own table — see renderImmersionRows
    if (b.section !== section) {
      section = b.section;
      var h = document.createElement('div');
      h.className = 'kb-section';
      h.textContent = section;
      rowsEl.appendChild(h);
      if (notes[section]) {
        var note = document.createElement('div');
        note.className = 'kb-note';
        note.textContent = notes[section];
        rowsEl.appendChild(note);
      }
    }
    rowsEl.appendChild(buildRow(b));
  });
  renderImmersionRows();
}

// Immersion options (docs/radar-master-arms.md) — a true second section (its own title/description/
// settings in keybinds.html), so its binds get their own table here instead of a header inside the
// main one. No per-row section heading needed (there's only ever the one section); the server's
// shared-behaviour note still shows, just above this table's header instead of inside it.
function renderImmersionRows() {
  immersionRowsEl.textContent = '';
  document.getElementById('kb-immersion-note').textContent = notes[IMMERSION_SECTION] || '';
  binds.forEach(function (b) {
    if (b.section !== IMMERSION_SECTION) return;
    immersionRowsEl.appendChild(buildRow(b));
  });
}

// ── Keyboard capture (browser-side) ──────────────────────────────────────────────────────────
function keyCellClick(id) {
  if (capturing) sendCommand('keybind.cancel-joy', {}).catch(function () {});
  kbCapture = kbCapture === id ? null : id;
  render();
}

document.addEventListener('keydown', function (e) {
  if (!kbCapture) return;
  e.preventDefault();
  var id = kbCapture;
  kbCapture = null;
  if (e.code === 'Escape') { render(); return; }
  var key = codeToKey(e.code);
  if (!key) { flashRejected(id); return; }   // unmappable (media keys, ...)
  sendCommand('keybind.set-key', { bind: id, key: key }).then(refresh).catch(function () {});
  // optimistic: show it now, the poll confirms
  binds.forEach(function (b) { if (b.id === id) b.key = key; });
  render();
});

// brief red flash on the keyboard cell of a bind whose captured key can't be mapped
function flashRejected(id) {
  render();
  var rows = rowsEl.querySelectorAll('.kb-row');
  var i = -1;
  binds.forEach(function (b, n) { if (b.id === id) i = n; });
  if (i < 0 || !rows[i]) return;
  var val = rows[i].querySelectorAll('.kb-val')[0];
  val.classList.add('rejected');
  val.textContent = 'UNSUPPORTED';
  setTimeout(render, 900);
}

// ── Joystick capture (plugin-side) ───────────────────────────────────────────────────────────
function joyCellClick(id) {
  kbCapture = null;
  var already = capturing === id && capturingKind === 'joy';
  sendCommand(already ? 'keybind.cancel-joy' : 'keybind.arm-joy', { bind: id }).catch(function () {});
  capturing = already ? null : id;             // optimistic; the poll is the truth
  capturingKind = already ? null : 'joy';
  render();
}

// ── Axis capture (plugin-side, docs/map-cursor.md) ───────────────────────────────────────────
function axisCellClick(id) {
  kbCapture = null;
  var already = capturing === id && capturingKind === 'axis';
  sendCommand(already ? 'keybind.cancel-axis' : 'keybind.arm-axis', { bind: id }).catch(function () {});
  capturing = already ? null : id;
  capturingKind = already ? null : 'axis';
  render();
}

// ── Poll ─────────────────────────────────────────────────────────────────────────────────────
function refresh() {
  fetch('/keybinds-config').then(function (r) { return r.json(); }).then(function (cfg) {
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
    renderBgToggle();
    radarOnOnStart      = cfg.radarOnOnStart      !== false;
    engineOnOnStart     = cfg.engineOnOnStart     !== false;
    masterArmsOnOnStart = cfg.masterArmsOnOnStart !== false;
    renderRadarOnStart();
    renderEngineOnStart();
    renderMasterArmsOnStart();
    render();
  }).catch(function () { panelEl.classList.add('unavailable'); });
}

renderBgToggle();   // OFF until the first fetch resolves, rather than a blank button
renderRadarOnStart();          // ON until the first fetch resolves — true is the actual default
renderEngineOnStart();
renderMasterArmsOnStart();
refresh();
setInterval(refresh, 600);
