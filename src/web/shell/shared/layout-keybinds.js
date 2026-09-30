// Configured keys for SAVE/LOAD LAYOUT and the five Layout Preset slots (issue #90). These are
// browser-side actions — the key each browser listens for is set once (on the /keybinds page, or a
// slot's box on the LYT page / in the LOAD list) and shared by every connected browser via the keybind
// configuration push, the same registry gameplay binds use (Keybinds.cs's "layout-save"/
// "layout-load" DefKeyOnly rows and "layout-preset-1..5" DefFree rows). This module tracks those
// current key names and matches a browser KeyboardEvent against them; each shell's own keydown
// listener (layout-keydown.js) calls it instead of hardcoding a key. A slot's joystick button is
// never matched here — the game reads that itself and relays it to the SOI browser as a map-act.
//
// Also builds the keybind boxes of the LYT page, the CLASSIC LOAD popup and the F-35 LOAD LAYOUT
// list (slotBox), and the record-only box of the save dialog (pendingKeyBox).
//
// Classic <script>, not a module, same as layout-store.js/layout-modal.js — a plain global, no
// build step. Depends on keybinds-keymap.js (KeybindsKeymap.codeToKey) being loaded first in a
// browser; requires it directly under Node so matchKey is unit-checkable (layout-keybinds.test.js).
(function (root) {
  const Keymap = (typeof module !== 'undefined' && module.exports)
    ? require('../../pages/keybinds/keybinds-keymap.js') : root.KeybindsKeymap;

  const SLOT_PREFIX = 'layout-preset-';
  const SLOT_COUNT = 5;   // KeybindConflict.LayoutSlotCount

  // Unity KeyCode names, or null = unbound. slots[i] is slot i+1's whole bind row
  // ({id, key, joyButton, joyNum}) so its box can show the joystick button too.
  let saveKey = null, loadKey = null;
  let slots = [];
  let rejectSeq = null;   // last seen /keybinds-config rejected.seq

  function applyConfig(data) {
    const next = [];
    (data.binds || []).forEach(function (b) {
      if (b.id === 'layout-save') saveKey = b.key || null;
      if (b.id === 'layout-load') loadKey = b.key || null;
      if (b.id.indexOf(SLOT_PREFIX) === 0) next[+b.id.slice(SLOT_PREFIX.length) - 1] = b;
    });
    slots = next;
    onConfig(data);
  }
  // remote-keybinds.js owns the shell's one bootstrap fetch; subsequent changes arrive over the
  // existing MAP SSE connection, so this consumer never downloads the full registry itself.
  if (typeof window !== 'undefined') window.addEventListener('message', function (e) {
    const m = e.data;
    if (m && m.mfd === true && m.type === 'keybinds-config-push') applyConfig(m.data || {});
  });

  // Pure: given the configured stored key names ({save, load, slots: [key|null, ...]}) and one
  // pressed key name (KeybindsKeymap.eventKeys form, chord or bare), decide which action (if any)
  // it triggers: 'save' | 'load' | 'slot-N' | null. Separated from match() so it's checkable
  // without a live KeyboardEvent (layout-keybinds.test.js).
  function matchKey(keys, key) {
    if (!key) return null;
    if (keys.save && key === keys.save) return 'save';
    if (keys.load && key === keys.load) return 'load';
    const slotKeys = keys.slots || [];
    for (let i = 0; i < slotKeys.length; i++) if (slotKeys[i] && key === slotKeys[i]) return 'slot-' + (i + 1);
    return null;
  }

  // e: a KeyboardEvent. Returns 'save' | 'load' | 'slot-N' | null. The chord is tried before the
  // bare key, so Alt+1 bound here wins over 1 bound here (KeybindsKeymap.eventKeys). With Ctrl or
  // Alt held only the exact chord counts: those are the browser's own shortcuts (Ctrl+S, Alt+D),
  // which must not also fire a bare S/D bound here.
  function match(e) {
    const keys = { save: saveKey, load: loadKey, slots: slots.map(function (b) { return b && b.key; }) };
    let names = Keymap.eventKeys(e);
    if (e.ctrlKey || e.altKey) names = names.slice(0, 1);
    for (let i = 0; i < names.length; i++) {
      const hit = matchKey(keys, names[i]);
      if (hit) return hit;
    }
    return null;
  }

  // ── Slot keybind box (LOAD LAYOUT's first five rows) ──────────────────────────────────────
  // Clicking listens for the next key (browser-side, same as the /keybinds page) AND arms the
  // plugin's joystick capture — whichever comes first wins. Esc cancels, Delete/Backspace clears.
  // The server refuses a key/button another bind already uses (KeybindConflict.cs) and names it in
  // the config push's `rejected`, which this shows in the box.
  let boxes = [];          // live {n, el}
  let listening = null;    // slot number being captured, or null
  let armSeen = false;     // the config has shown this slot's joystick capture armed
  let flash = null;        // {n, text} shown instead of the value until it times out
  let pending = null;      // modifiers held so far during capture ("ALT+…"), or null
  // A slot holds a keyboard key or a joystick button, never both. justSet is the half a capture
  // here just wrote ({n, kind: 'key'|'joy'}); the config push that shows both halves set is when
  // the other one is cleared — the write itself is only known to have landed once it shows up.
  let justSet = null;

  // A box is for slot 1..5 (key or joystick button) or for the key-only 'save' / 'load' binds.
  function isSlot(n) { return typeof n === 'number'; }
  function slotId(n) { return isSlot(n) ? SLOT_PREFIX + n : 'layout-' + n; }
  function bindRow(n) { return n === 'save' ? { key: saveKey } : n === 'load' ? { key: loadKey } : slots[n - 1]; }
  // The box a rejected bind's id belongs to: 1..5, 'save', 'load', or null.
  function boxOf(bindId) {
    if (bindId.indexOf(SLOT_PREFIX) === 0) return +bindId.slice(SLOT_PREFIX.length);
    return bindId === 'layout-save' ? 'save' : bindId === 'layout-load' ? 'load' : null;
  }

  function describe(b) {
    if (!b) return '—';
    const parts = [];
    if (b.key) parts.push(Keymap.displayName(b.key));
    if (b.joyButton >= 0) parts.push(b.joyNum > 0 ? 'J' + b.joyNum + ' B' + b.joyButton : 'JOY ' + b.joyButton);
    return parts.join(' · ') || '—';
  }

  function renderBox(x) {
    let text = describe(bindRow(x.n)), cls = '';
    if (flash && flash.n === x.n) { text = flash.text; cls = ' rejected'; }
    else if (listening === x.n) { text = pending || (isSlot(x.n) ? 'PRESS KEY/BUTTON' : 'PRESS KEY'); cls = ' capturing'; }
    else if (text === '—') { cls = ' unbound'; if (x.unboundText) text = x.unboundText; }
    x.el.textContent = text;
    x.el.className = 'layout-modal-kb' + cls;
  }
  function renderBoxes() {
    boxes = boxes.filter(function (x) { return x.el.isConnected; });   // drop closed/redrawn lists
    boxes.forEach(renderBox);
  }

  function showFlash(n, text) {
    flash = { n: n, text: text };
    renderBoxes();
    setTimeout(function () { if (flash && flash.n === n) { flash = null; renderBoxes(); } }, 1800);
  }

  function stopListening(cancelJoy) {
    if (listening === null) return;
    if (cancelJoy && isSlot(listening)) sendCommand('keybind.cancel-joy', {}).catch(function () {});
    listening = null;
    pending = null;
    window.removeEventListener('keydown', onCaptureKey, true);
    window.removeEventListener('keyup', onCaptureKey, true);
    renderBoxes();
  }

  // Window capture phase: runs before layout-modal.js's document-level Escape (which would close
  // the whole modal) and before the shell's own keydown matcher (which would fire the key's
  // current action) — the pressed key belongs to the capture, nothing else.
  function onCaptureKey(e) {
    // The modal closed mid-capture (Cancel, backdrop click): drop the capture, let the key through.
    if (!boxes.some(function (x) { return x.n === listening && x.el.isConnected; })) { stopListening(true); return; }
    e.preventDefault();
    e.stopImmediatePropagation();
    const n = listening;
    const down = e.type === 'keydown';
    if (down && e.code === 'Escape') { stopListening(true); return; }
    if (down && (e.code === 'Delete' || e.code === 'Backspace') && !e.ctrlKey && !e.altKey && !e.shiftKey) {
      sendCommand('keybind.set-key', { bind: slotId(n), key: '' }).catch(function () {});
      if (isSlot(n)) sendCommand('keybind.clear-joy', { bind: slotId(n) }).catch(function () {});
      stopListening(true);
      return;
    }
    // Modifiers alone keep listening; the next key finishes the chord (KeybindsKeymap.captureStep).
    const step = Keymap.captureStep(e);
    if (!step) return;
    if ('pending' in step) { pending = step.pending; renderBoxes(); return; }
    const key = step.key;
    stopListening(true);
    if (!key) { showFlash(n, 'UNSUPPORTED'); return; }
    sendCommand('keybind.set-key', { bind: slotId(n), key: key }).catch(function () {});
    if (isSlot(n)) setJustSet(n, 'key');
  }

  function setJustSet(n, kind) {
    const mine = justSet = { n: n, kind: kind };
    setTimeout(function () { if (justSet === mine) justSet = null; }, 3000);   // a refused write never shows up
  }

  function reconcile() {
    const b = justSet && slots[justSet.n - 1];
    if (!b || !b.key || !(b.joyButton >= 0)) return;
    if (justSet.kind === 'key') sendCommand('keybind.clear-joy', { bind: slotId(justSet.n) }).catch(function () {});
    else sendCommand('keybind.set-key', { bind: slotId(justSet.n), key: '' }).catch(function () {});
    justSet = null;
  }

  function onConfig(data) {
    const r = data.rejected;
    const fresh = r && rejectSeq !== null && r.seq !== rejectSeq;
    if (r) rejectSeq = r.seq;
    if (isSlot(listening)) {
      // Joystick capture ended: a button landed, was refused, or capture moved to another bind.
      const id = slotId(listening);
      if (data.capturing === id) armSeen = true;
      else if (armSeen || data.capturing || (fresh && r.bind === id)) {
        // A button landed unless the capture was refused or moved to another bind.
        if (armSeen && !data.capturing && !(fresh && r.bind === id)) setJustSet(listening, 'joy');
        stopListening(false);
      }
    }
    reconcile();
    if (fresh && boxOf(r.bind) !== null) showFlash(boxOf(r.bind), 'USED BY ' + r.by.toUpperCase());
    else renderBoxes();
  }

  // n: slot number 1..5, or 'save' / 'load' for those key-only binds. Returns the element to put in
  // the row. unboundText (optional) replaces the '—' shown while no key is set, for a host with
  // room for words.
  function slotBox(n, unboundText) {
    const el = document.createElement('button');
    el.type = 'button';
    el.title = (isSlot(n) ? 'Layout ' + n : n === 'save' ? 'Save layout' : 'Load layout') +
               ' keybind — click, then press a key (with Ctrl/Alt/Shift if you like)' +
               (isSlot(n) ? ' or a joystick button' : '') + '. Esc cancels, Delete clears.';
    el.addEventListener('click', function () {
      if (listening === n) { stopListening(true); return; }
      stopListening(listening !== null);
      listening = n;
      armSeen = false;
      flash = null;
      if (isSlot(n)) sendCommand('keybind.arm-joy', { bind: slotId(n) }).catch(function () {});
      window.addEventListener('keydown', onCaptureKey, true);
      window.addEventListener('keyup', onCaptureKey, true);
      renderBoxes();
    });
    const box = { n: n, el: el, unboundText: unboundText };
    boxes.push(box);
    renderBox(box);   // not in the document yet, so renderBoxes() would drop it
    return el;
  }

  // Bind slot n's keyboard key from code (the replace form applies the key it recorded once the
  // slot is known). Goes through the same one-or-the-other rule as a captured key: the slot's
  // joystick button is cleared when the new key is accepted.
  function setSlotKey(n, key) {
    sendCommand('keybind.set-key', { bind: slotId(n), key: key }).catch(function () {});
    setJustSet(n, 'key');
  }

  // A key box that only records a key (or chord) for the caller to apply later — it writes nothing
  // itself. Click, press a key; Esc cancels, Delete clears. value() is the stored key name or ''.
  function pendingKeyBox(unboundText) {
    const el = document.createElement('button');
    el.type = 'button';
    let key = '', capturing = false, held = null;
    function render() {
      el.textContent = capturing ? (held || 'PRESS KEY') : key ? Keymap.displayName(key) : unboundText;
      el.className = 'layout-modal-kb' + (capturing ? ' capturing' : key ? '' : ' unbound');
    }
    function stop() {
      capturing = false; held = null;
      window.removeEventListener('keydown', onKey, true);
      window.removeEventListener('keyup', onKey, true);
      render();
    }
    // Window capture phase, like the slot boxes: the pressed key belongs to the capture.
    function onKey(e) {
      if (!el.isConnected) { stop(); return; }
      e.preventDefault();
      e.stopImmediatePropagation();
      const down = e.type === 'keydown';
      if (down && e.code === 'Escape') { stop(); return; }
      if (down && (e.code === 'Delete' || e.code === 'Backspace') && !e.ctrlKey && !e.altKey && !e.shiftKey) { key = ''; stop(); return; }
      const step = Keymap.captureStep(e);
      if (!step) return;
      if ('pending' in step) { held = step.pending; render(); return; }
      if (step.key) key = step.key;
      stop();
    }
    el.addEventListener('click', function () {
      if (capturing) { stop(); return; }
      capturing = true;
      window.addEventListener('keydown', onKey, true);
      window.addEventListener('keyup', onKey, true);
      render();
    });
    render();
    return { el: el, value: function () { return key; } };
  }

  const api = { applyConfig: applyConfig, setSlotKey: setSlotKey, pendingKeyBox: pendingKeyBox, match: match, matchKey: matchKey, slotBox: slotBox, SLOT_COUNT: SLOT_COUNT };
  if (typeof module !== 'undefined' && module.exports) module.exports = api;
  else root.LayoutKeybinds = api;
})(typeof self !== 'undefined' ? self : this);
