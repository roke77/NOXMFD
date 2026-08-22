// Client for the SAVE/LOAD LAYOUT feature — GET /layout-options + POST /command layout.save.
// LayoutStore.cs (the plugin) is the single source of truth. Layouts are a small library a pilot
// names deliberately, not something that changes on its own, so this fetches fresh only when
// LOAD's picker opens rather than polling continuously in the background.
//
// Shared by both shells (mfd.js, f35.js) — a classic <script>, not a module, same as
// waypoints-store.js, so it works with no build step.
(function (root) {
  function list() {
    return fetch('/layout-options', { cache: 'no-store' })
      .then(function (r) { return r.json(); })
      .catch(function () { return { layouts: [] }; });
  }

  // dataObj is whatever shape the calling shell's own layout state serializes to (CLASSIC's
  // {splitMode,splitVariant,pages} or F-35's {cells,pages}) — this module doesn't need to know
  // which; it just carries it as an opaque JSON blob.
  function save(name, shell, dataObj) {
    return sendCommand('layout.save', { wname: name, group: shell, text: JSON.stringify(dataObj) });
  }

  // LOAD's picker manages the library — rename/remove act on an existing saved layout by id.
  function rename(id, name) {
    return sendCommand('layout.rename', { bind: id, wname: name });
  }
  function remove(id) {
    return sendCommand('layout.delete', { bind: id });
  }

  const api = { list: list, save: save, rename: rename, remove: remove };
  if (typeof module !== 'undefined' && module.exports) module.exports = api;
  else root.LayoutStore = api;
})(typeof self !== 'undefined' ? self : this);
