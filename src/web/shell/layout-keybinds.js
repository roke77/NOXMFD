// Configured keys for SAVE/LOAD LAYOUT (issue #51 follow-up). These are browser-side actions —
// no joystick/HOTAS, no Unity/Rewired dispatch — but the KEY each browser listens for is set once
// on the /keybinds page and shared by every connected browser via /keybinds-config, the same
// registry gameplay binds use (Keybinds.cs's "layout-save"/"layout-load" rows, DefKeyOnly — no
// joystick entry, nothing in Poll() ever dispatches from them). This module just tracks those two
// current key names and matches a browser KeyboardEvent against them; each shell's own keydown
// listener (mfd.js, f35.js) calls it instead of hardcoding a key.
//
// Classic <script>, not a module, same as layout-store.js/layout-modal.js — a plain global, no
// build step. Depends on keybinds-keymap.js (KeybindsKeymap.codeToKey) being loaded first in a
// browser; requires it directly under Node so matchKey is unit-checkable (layout-keybinds.test.js),
// the same split keybinds-keymap.js itself already draws between pure logic and DOM/fetch glue.
(function (root) {
  const Keymap = (typeof module !== 'undefined' && module.exports)
    ? require('../pages/keybinds/keybinds-keymap.js') : root.KeybindsKeymap;

  let saveKey = null, loadKey = null;   // Unity KeyCode names (KeybindsKeymap's naming), or null = unbound

  function refresh() {
    if (typeof fetch !== 'function') return Promise.resolve();   // no fetch in this context (Node tests)
    return fetch('/keybinds-config', { cache: 'no-store' }).then(function (r) { return r.json(); })
      .then(function (data) {
        (data.binds || []).forEach(function (b) {
          if (b.id === 'layout-save') saveKey = b.key || null;
          if (b.id === 'layout-load') loadKey = b.key || null;
        });
      }).catch(function () { /* transient network error — next poll retries */ });
  }
  // A rebind is rare (set once on the KEY page, not a per-session thing), so a slow poll is enough
  // to keep every already-open browser in sync without adding a fast-cadence request just for this.
  // Only the top-level shell runs it — nothing under Node, mirroring waypoints-store.js's own guard.
  if (typeof window !== 'undefined') { refresh(); setInterval(refresh, 3000); }

  // Pure: given the two configured Unity KeyCode names and a raw KeyboardEvent.code, decide which
  // action (if any) it triggers. Split out from match() below so it's checkable without a live
  // KeyboardEvent or the module's own fetched state — see layout-keybinds.test.js.
  function matchKey(save, load, code) {
    const key = Keymap.codeToKey(code);
    if (!key) return null;
    if (save && key === save) return 'save';
    if (load && key === load) return 'load';
    return null;
  }

  // e: a KeyboardEvent. Returns 'save' | 'load' | null.
  function match(e) { return matchKey(saveKey, loadKey, e.code); }

  const api = { match: match, matchKey: matchKey };
  if (typeof module !== 'undefined' && module.exports) module.exports = api;
  else root.LayoutKeybinds = api;
})(typeof self !== 'undefined' ? self : this);
