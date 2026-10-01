// UI page (CFG > UI, issue #105). See ui.html's header for the command contract.

var DEFAULT_ID = 'default';
var MAX_THEMES = 20;          // ThemeStore.MaxThemes
var MAX_NAME = 32;            // ThemeColors.MaxNameLength
var CODE_PREFIX = 'NOXT1:';   // ThemeColors.CodePrefix

var state = { active: DEFAULT_ID, themes: [] };

var themesEl = document.getElementById('ui-themes');
var groupsEl = document.getElementById('ui-groups');
var hintEl = document.getElementById('ui-hint');
var btn = {
  create: document.getElementById('ui-new'),
  rename: document.getElementById('ui-rename'),
  remove: document.getElementById('ui-delete'),
  copy: document.getElementById('ui-copy'),
  paste: document.getElementById('ui-paste'),
  resetAll: document.getElementById('ui-reset-all'),
};

// DEFAULT and folder themes (state's file: true) can't be edited; a colour change saves a new theme.
function isReadOnly(t) { return !t || !!t.file; }

function activeTheme() {
  for (var i = 0; i < state.themes.length; i++) if (state.themes[i].id === state.active) return state.themes[i];
  return null;
}

// Sends a theme.* command, then re-reads /themes so a rejected command settles back to the
// plugin's state (the themes push brings the same state, and the shell's repaint, a moment later).
function send(cmd, body) {
  return sendCommand(cmd, body).catch(function () {}).then(function () {
    setTimeout(load, 300);
  });
}

function load() {
  return fetch('/themes', { cache: 'no-store' })
    .then(function (r) { return r.ok ? r.json() : null; })
    .then(function (d) { if (d) applyState(d); })
    .catch(function () {});
}

// ── effective colours ────────────────────────────────────────────────────────────────────────
// What a token currently resolves to in this document (colors.css default, or the active theme's
// override), as #rrggbb: a hidden probe resolves it, and a canvas fillStyle normalises the result.
var probe = document.createElement('span');
probe.hidden = true;
document.body.appendChild(probe);
var normaliser = document.createElement('canvas').getContext('2d');

function isTriple(token) { return /-rgb$/.test(token); }

function effectiveHex(token) {
  probe.style.color = isTriple(token) ? 'rgb(var(' + token + '))' : 'var(' + token + ')';
  normaliser.fillStyle = getComputedStyle(probe).color;
  return normaliser.fillStyle;
}

function parseHex(text) {
  var m = /^#?([0-9a-f]{6})$/i.exec((text || '').trim());
  return m ? '#' + m[1].toLowerCase() : null;
}

function cssValue(token, hex) {
  if (!isTriple(token)) return hex;
  return [1, 3, 5].map(function (i) { return parseInt(hex.slice(i, i + 2), 16); }).join(', ');
}

// ── colour rows ──────────────────────────────────────────────────────────────────────────────
var rows = {};   // token → { row, input, hex, reset }

UiTokens.GROUPS.forEach(function (group) {
  var heading = document.createElement('div');
  heading.className = 'ui-heading';
  heading.textContent = group.title;
  var list = document.createElement('div');
  list.className = 'ui-rows';
  group.tokens.forEach(function (pair) {
    var token = pair[0], label = pair[1];
    var row = document.createElement('div');
    row.className = 'ui-row';
    var input = document.createElement('input');
    input.type = 'color';
    input.className = 'ui-swatch pad-hoverable';
    input.setAttribute('aria-label', label);
    var name = document.createElement('span');
    name.className = 'ui-label';
    name.textContent = label;
    var hex = document.createElement('input');
    hex.type = 'text';
    hex.className = 'ui-hex';
    hex.maxLength = 7;
    hex.spellcheck = false;
    hex.autocomplete = 'off';
    hex.setAttribute('aria-label', label + ' hex value');
    var reset = document.createElement('button');
    reset.type = 'button';
    reset.className = 'ui-reset pad-hoverable';
    reset.textContent = '↺';
    reset.title = 'Reset to default';
    reset.setAttribute('aria-label', 'Reset ' + label + ' to default');
    row.append(input, name, hex, reset);
    list.appendChild(row);
    rows[token] = { row: row, input: input, hex: hex, reset: reset };

    // Dragging the picker previews on this page only; releasing it sends the change.
    input.addEventListener('input', function () {
      document.documentElement.style.setProperty(token, cssValue(token, input.value));
      hex.value = input.value.toUpperCase();
      hex.classList.remove('bad');
    });
    input.addEventListener('change', function () { pickColor(token, input.value); });

    // The hex field takes #rrggbb (the # optional) and applies it on Enter or when it loses focus;
    // anything else is flagged, then put back to the current colour on blur.
    hex.addEventListener('input', function () {
      var v = parseHex(hex.value);
      hex.classList.toggle('bad', !v);
      if (v) { input.value = v; document.documentElement.style.setProperty(token, cssValue(token, v)); }
    });
    hex.addEventListener('keydown', function (e) {
      if (e.key === 'Enter') { e.preventDefault(); hex.blur(); }
      else if (e.key === 'Escape') { e.preventDefault(); hex.value = rows[token].current.toUpperCase(); hex.blur(); render(); }
    });
    hex.addEventListener('change', function () {
      var v = parseHex(hex.value);
      if (v && v !== rows[token].current) { pickColor(token, v); return; }
      hex.value = rows[token].current.toUpperCase();
      render();
    });
    reset.addEventListener('click', function () { send('theme.reset-color', { group: token }); });
  });
  groupsEl.append(heading, list);
});

function pickColor(token, hex) {
  var active = activeTheme();
  if (!isReadOnly(active)) {
    send('theme.set-color', { group: token, text: hex });
    return;
  }
  // A read-only theme: the edit becomes the first change of a new theme copied from it.
  openDialog({
    title: (active ? active.name : 'DEFAULT') + ' IS READ-ONLY. NAME A NEW THEME',
    placeholder: 'THEME NAME',
    ok: 'SAVE',
    validate: validateNewName,
    onOk: function (name) {
      sendCommand('theme.create', { wname: name }).catch(function () {})
        .then(function () { send('theme.set-color', { group: token, text: hex }); });
    },
    onCancel: render,
  });
}

// ── render ───────────────────────────────────────────────────────────────────────────────────
function applyState(d) {
  state = { active: typeof d.active === 'string' ? d.active : DEFAULT_ID, themes: Array.isArray(d.themes) ? d.themes : [] };
  render();
}

function render() {
  for (var token in rows) document.documentElement.style.removeProperty(token);
  var active = activeTheme();
  var colors = (active && active.colors) || {};
  var readOnly = isReadOnly(active);

  themesEl.textContent = '';
  [{ id: DEFAULT_ID, name: 'DEFAULT' }].concat(state.themes).forEach(function (t) {
    var card = document.createElement('button');
    card.type = 'button';
    card.className = 'lit ui-theme pad-hoverable';
    card.setAttribute('aria-pressed', String(t.id === (active ? active.id : DEFAULT_ID)));
    var name = document.createElement('span');
    name.className = 'ui-theme-name';
    name.textContent = t.name;
    card.appendChild(name);
    if (t.file) {
      var tag = document.createElement('span');
      tag.className = 'ui-theme-tag';
      tag.textContent = 'FILE';
      card.appendChild(tag);
    }
    card.addEventListener('click', function () { if (t.id !== state.active) send('theme.select', { bind: t.id }); });
    themesEl.appendChild(card);
  });

  hintEl.textContent = !active ? '(default is read-only: changing a colour saves a new theme)'
    : readOnly ? '(from the themes folder, read-only: changing a colour saves a new theme)' : '';
  btn.rename.disabled = readOnly;
  btn.remove.disabled = readOnly;
  btn.copy.disabled = !active;
  btn.resetAll.disabled = readOnly || Object.keys(colors).length === 0;

  for (var t2 in rows) {
    var r = rows[t2];
    var overridden = Object.prototype.hasOwnProperty.call(colors, t2);
    var hex = overridden ? colors[t2] : effectiveHex(t2);
    r.current = hex;
    r.input.value = hex;
    if (document.activeElement !== r.hex) r.hex.value = hex.toUpperCase();
    r.hex.classList.remove('bad');
    r.row.classList.toggle('overridden', overridden);
    r.reset.hidden = !overridden || readOnly;
  }
}

// ── dialog ───────────────────────────────────────────────────────────────────────────────────
var dlg = {
  scrim: document.getElementById('ui-dialog'),
  title: document.getElementById('ui-dialog-title'),
  input: document.getElementById('ui-dialog-input'),
  error: document.getElementById('ui-dialog-error'),
  ok: document.getElementById('ui-dialog-ok'),
  cancel: document.getElementById('ui-dialog-cancel'),
};
var dialogOpts = null;

// opts: title, ok (button text), danger, value, placeholder, maxLength, noInput, readOnly,
// validate(text) → error text or '', onOk(text), onCancel().
function openDialog(opts) {
  dialogOpts = opts;
  dlg.title.textContent = opts.title;
  dlg.ok.textContent = opts.ok;
  dlg.ok.classList.toggle('danger', !!opts.danger);
  dlg.ok.classList.toggle('save', !opts.danger);
  dlg.input.hidden = !!opts.noInput;
  dlg.input.value = opts.value || '';
  dlg.input.placeholder = opts.placeholder || '';
  dlg.input.maxLength = opts.maxLength || MAX_NAME;
  dlg.input.readOnly = !!opts.readOnly;
  dlg.input.classList.remove('bad');
  dlg.error.textContent = '';
  dlg.scrim.hidden = false;
  if (opts.noInput) dlg.ok.focus();
  else { dlg.input.focus(); dlg.input.select(); }
}

function closeDialog() {
  dlg.scrim.hidden = true;
  dialogOpts = null;
}

function confirmDialog() {
  var opts = dialogOpts;
  if (!opts) return;
  var text = dlg.input.value.trim();
  var problem = opts.validate ? opts.validate(text) : '';
  if (problem) {
    dlg.error.textContent = problem;
    dlg.input.classList.add('bad');
    return;
  }
  closeDialog();
  if (opts.onOk) opts.onOk(text);
}

function cancelDialog() {
  var opts = dialogOpts;
  closeDialog();
  if (opts && opts.onCancel) opts.onCancel();
}

dlg.ok.addEventListener('click', confirmDialog);
dlg.cancel.addEventListener('click', cancelDialog);
dlg.input.addEventListener('input', function () { dlg.error.textContent = ''; dlg.input.classList.remove('bad'); });
dlg.scrim.addEventListener('keydown', function (e) {
  if (e.key === 'Enter') { e.preventDefault(); confirmDialog(); }
  else if (e.key === 'Escape') { e.preventDefault(); cancelDialog(); }
});

// The 20-theme cap (ThemeStore.MaxThemes) counts saved themes only, not folder ones.
function savedCount() { return state.themes.filter(function (t) { return !t.file; }).length; }
function validateName(text) { return text ? '' : 'ENTER A NAME'; }
function validateNewName(text) {
  if (savedCount() >= MAX_THEMES) return 'THEME LIMIT REACHED (' + MAX_THEMES + ')';
  return validateName(text);
}

// A share code decodes to {"n": name, "c": {...}}; the plugin re-validates every colour.
function validateCode(text) {
  if (savedCount() >= MAX_THEMES) return 'THEME LIMIT REACHED (' + MAX_THEMES + ')';
  if (text.indexOf(CODE_PREFIX) !== 0) return 'NOT A THEME CODE';
  try {
    var bytes = Uint8Array.from(atob(text.slice(CODE_PREFIX.length)), function (c) { return c.charCodeAt(0); });
    var json = JSON.parse(new TextDecoder().decode(bytes));
    if (!json || typeof json.n !== 'string' || !json.n.trim()) return 'NOT A THEME CODE';
  } catch (e) { return 'NOT A THEME CODE'; }
  return '';
}

// ── actions ──────────────────────────────────────────────────────────────────────────────────
btn.create.addEventListener('click', function () {
  openDialog({ title: 'NEW THEME FROM ' + (activeTheme() ? activeTheme().name : 'DEFAULT'), placeholder: 'THEME NAME', ok: 'SAVE',
               validate: validateNewName, onOk: function (name) { send('theme.create', { wname: name }); } });
});

btn.rename.addEventListener('click', function () {
  var t = activeTheme(); if (!t) return;
  openDialog({ title: 'RENAME THEME', value: t.name, ok: 'SAVE', validate: validateName,
               onOk: function (name) { send('theme.rename', { bind: t.id, wname: name }); } });
});

btn.remove.addEventListener('click', function () {
  var t = activeTheme(); if (!t) return;
  openDialog({ title: 'DELETE ' + t.name + '?', ok: 'DELETE', danger: true, noInput: true,
               onOk: function () { send('theme.delete', { bind: t.id }); } });
});

btn.resetAll.addEventListener('click', function () {
  var t = activeTheme(); if (!t) return;
  openDialog({ title: 'RESET EVERY COLOUR IN ' + t.name + '?', ok: 'RESET', danger: true, noInput: true,
               onOk: function () { send('theme.reset-color', { group: '' }); } });
});

// The async clipboard needs a secure context, which a tablet on the LAN (plain http) isn't; there
// the code is shown selected in the dialog, and execCommand('copy') is tried on it.
btn.copy.addEventListener('click', function () {
  var t = activeTheme(); if (!t || !t.code) return;
  var showCode = function () {
    openDialog({ title: 'THEME CODE FOR ' + t.name, value: t.code, readOnly: true, maxLength: 4096, ok: 'DONE' });
    try { document.execCommand('copy'); } catch (e) { /* the code stays selected to copy by hand */ }
  };
  if (navigator.clipboard && window.isSecureContext) {
    navigator.clipboard.writeText(t.code).then(function () { flashHint('code copied'); }, showCode);
  } else {
    showCode();
  }
});

btn.paste.addEventListener('click', function () {
  openDialog({ title: 'PASTE A THEME CODE', placeholder: CODE_PREFIX + '…', maxLength: 4096, ok: 'IMPORT',
               validate: validateCode, onOk: function (code) { send('theme.import', { text: code }); } });
});

var hintTimer = 0;
function flashHint(text) {
  hintEl.textContent = '(' + text + ')';
  clearTimeout(hintTimer);
  hintTimer = setTimeout(render, 1500);
}

window.addEventListener('message', function (e) {
  var m = e.data;
  if (!m || m.mfd !== true) return;
  if (m.type === 'themes-push') applyState(m.data || {});
});

render();
// Pick up theme files dropped in the folder since the last look (the push brings the result too).
sendCommand('theme.rescan', {}).catch(function () {}).then(load);
