// Extended-keybinds page. Renders the plugin's bind registry (/keybinds-config) as a table and
// writes changes back over keybind.* commands. Keyboard capture happens here in the browser
// (KeyboardEvent.code → Unity KeyCode name); joystick capture is armed plugin-side and the result
// arrives via the poll. See keybinds.html header + docs/keybinds-page.md.

var rowsEl  = document.getElementById('kb-rows');
var panelEl = document.getElementById('kb-panel');

var binds     = [];      // last /keybinds-config payload
var notes     = {};      // per-section shared-behaviour note, keyed by section title
var capturing = null;    // plugin-side joy capture: bind id or null (server state, mirrored)
var kbCapture = null;    // browser-side keyboard capture: bind id or null (local state)
var lastJson  = '';      // skip re-render when nothing changed

// ── KeyboardEvent.code → Unity KeyCode name ──────────────────────────────────────────────────
// Letters/digits/F-keys/numpad are mechanical; the rest enumerated. Escape is reserved (cancels
// capture) and mouse buttons are not capturable — clicking is how this page is driven.
var CODE2KEY = {
  Space: 'Space', Tab: 'Tab', Enter: 'Return', Backspace: 'Backspace', Delete: 'Delete',
  Insert: 'Insert', Home: 'Home', End: 'End', PageUp: 'PageUp', PageDown: 'PageDown',
  ArrowUp: 'UpArrow', ArrowDown: 'DownArrow', ArrowLeft: 'LeftArrow', ArrowRight: 'RightArrow',
  ShiftLeft: 'LeftShift', ShiftRight: 'RightShift', ControlLeft: 'LeftControl',
  ControlRight: 'RightControl', AltLeft: 'LeftAlt', AltRight: 'RightAlt',
  CapsLock: 'CapsLock', ScrollLock: 'ScrollLock', Pause: 'Pause',
  Minus: 'Minus', Equal: 'Equals', BracketLeft: 'LeftBracket', BracketRight: 'RightBracket',
  Backslash: 'Backslash', Semicolon: 'Semicolon', Quote: 'Quote', Backquote: 'BackQuote',
  Comma: 'Comma', Period: 'Period', Slash: 'Slash',
  NumpadDivide: 'KeypadDivide', NumpadMultiply: 'KeypadMultiply', NumpadSubtract: 'KeypadMinus',
  NumpadAdd: 'KeypadPlus', NumpadDecimal: 'KeypadPeriod', NumpadEnter: 'KeypadEnter'
};
function codeToKey(code) {
  if (/^Key[A-Z]$/.test(code))    return code.slice(3);                  // KeyA → A
  if (/^Digit[0-9]$/.test(code))  return 'Alpha'  + code.slice(5);       // Digit1 → Alpha1
  if (/^Numpad[0-9]$/.test(code)) return 'Keypad' + code.slice(6);       // Numpad1 → Keypad1
  if (/^F([1-9]|1[0-5])$/.test(code)) return code;                       // F1..F15
  return CODE2KEY[code] || null;
}

// Compact display form of a Unity KeyCode name ("Alpha1" → "1", "LeftShift" → "L-SHIFT", ...).
function displayKey(k) {
  return k
    .replace(/^Alpha/, '')
    .replace(/^Keypad/, 'NUM ')
    .replace(/^Left(Shift|Control|Alt|Arrow|Bracket)$/, 'L-$1')
    .replace(/^Right(Shift|Control|Alt|Arrow|Bracket)$/, 'R-$1')
    .replace(/([a-z])([A-Z])/g, '$1 $2')
    .toUpperCase();
}

// ── Render ───────────────────────────────────────────────────────────────────────────────────
function cell(bind, kind) {
  var wrap = document.createElement('div');
  wrap.className = 'kb-cell';

  var val = document.createElement('button');
  val.className = 'kb-val';
  var bound = kind === 'key' ? !!bind.key : bind.joyButton >= 0;
  if (kind === 'key' && kbCapture === bind.id)      { val.textContent = 'PRESS A KEY…';    val.className += ' capturing'; }
  else if (kind === 'joy' && capturing === bind.id) { val.textContent = 'PRESS A BUTTON…'; val.className += ' capturing'; }
  else if (!bound)                                  { val.textContent = '—';               val.className += ' unbound'; }
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

function render() {
  rowsEl.textContent = '';
  var section = null;
  binds.forEach(function (b) {
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
    row.appendChild(cell(b, 'key'));
    row.appendChild(cell(b, 'joy'));
    rowsEl.appendChild(row);
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
  var cmd = capturing === id ? 'keybind.cancel-joy' : 'keybind.arm-joy';
  sendCommand(cmd, { bind: id }).catch(function () {});
  capturing = capturing === id ? null : id;   // optimistic; the poll is the truth
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
    render();
  }).catch(function () { panelEl.classList.add('unavailable'); });
}

refresh();
setInterval(refresh, 600);
