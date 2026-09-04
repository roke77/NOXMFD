// Configured keys for SAVE/LOAD LAYOUT. These are browser-side actions — no joystick/HOTAS, no
// Unity/Rewired dispatch — but the key each browser listens for is set once on the /keybinds page
// and shared by every connected browser via the keybind configuration push, the same registry gameplay binds
// use (Keybinds.cs's "layout-save"/"layout-load" rows, DefKeyOnly — no joystick entry, nothing in
// Poll() ever dispatches from them). This module tracks those two current key names and matches a
// browser KeyboardEvent against them; each shell's own keydown listener (mfd.js, f35.js) calls it
// instead of hardcoding a key.
//
// Classic <script>, not a module, same as layout-store.js/layout-modal.js — a plain global, no
// build step. Depends on keybinds-keymap.js (KeybindsKeymap.codeToKey) being loaded first in a
// browser; requires it directly under Node so matchKey is unit-checkable (layout-keybinds.test.js).
(function (root) {
  const Keymap = (typeof module !== 'undefined' && module.exports)
    ? require('../../pages/keybinds/keybinds-keymap.js') : root.KeybindsKeymap;

  let saveKey = null, loadKey = null;   // Unity KeyCode names, or null = unbound

  function applyConfig(data) {
    (data.binds || []).forEach(function (b) {
      if (b.id === 'layout-save') saveKey = b.key || null;
      if (b.id === 'layout-load') loadKey = b.key || null;
    });
  }
  // remote-keybinds.js owns the shell's one bootstrap fetch; subsequent changes arrive over the
  // existing MAP SSE connection, so this two-value consumer never downloads the full registry.
  if (typeof window !== 'undefined') window.addEventListener('message', function (e) {
    const m = e.data;
    if (m && m.mfd === true && m.type === 'keybinds-config-push') applyConfig(m.data || {});
  });

  // Pure: given the two configured Unity KeyCode names and a raw KeyboardEvent.code, decide which
  // action (if any) it triggers. Separated from match() so it's checkable without a live
  // KeyboardEvent or the module's own fetched state (layout-keybinds.test.js).
  function matchKey(save, load, code) {
    const key = Keymap.codeToKey(code);
    if (!key) return null;
    if (save && key === save) return 'save';
    if (load && key === load) return 'load';
    return null;
  }

  // e: a KeyboardEvent. Returns 'save' | 'load' | null.
  function match(e) { return matchKey(saveKey, loadKey, e.code); }

  const api = { applyConfig: applyConfig, match: match, matchKey: matchKey };
  if (typeof module !== 'undefined' && module.exports) module.exports = api;
  else root.LayoutKeybinds = api;
})(typeof self !== 'undefined' ? self : this);
