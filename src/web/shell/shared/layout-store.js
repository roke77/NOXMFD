// Client for the SAVE/LOAD LAYOUT feature — GET /layout-options + POST /command layout.save.
// LayoutStore.cs (the plugin) is the single source of truth. Layouts are a small library a pilot
// names deliberately, not something that changes on its own, so this fetches fresh only when
// LOAD's picker opens rather than polling continuously in the background.
//
// Shared by both shells (mfd.js, f35.js) — a classic <script>, not a module, same as
// waypoints-store.js, so it works with no build step.
(function (root) {
  // Resolves {layouts: [...]}; when the plugin can't be reached, {layouts: [], failed: true} — callers
  // that would otherwise show an empty library (or offer to save into it) check `failed` first.
  function list() {
    return fetch('/layout-options', { cache: 'no-store' })
      .then(function (r) { if (!r.ok) throw new Error('HTTP ' + r.status); return r.json(); })
      .catch(function () { return { layouts: [], failed: true }; });
  }

  // A rejection handler for a fire-and-forget write: a lost connection leaves a console line instead
  // of a save that silently did nothing.
  function warn(what) {
    return function (err) { if (typeof console !== 'undefined') console.warn('[layout] ' + what + ' failed:', err); };
  }

  // The library changed: tell the pages this document hosts (the LYT page lists it). The command is
  // queued for the plugin's main thread, so a page refetching on this waits a moment — see lyt.js.
  function changed(result) {
    if (typeof document !== 'undefined') {
      [].forEach.call(document.querySelectorAll('iframe'), function (f) {
        try { f.contentWindow.postMessage({ mfd: true, type: 'layouts-changed' }, '*'); } catch (e) {}
      });
    }
    return result;
  }

  // dataObj is whatever shape the calling shell's own layout state serializes to (CLASSIC's
  // {splitMode,splitVariant,pages} or F-35's {cells,pages}) — this module doesn't need to know
  // which; it just carries it as an opaque JSON blob.
  function save(name, shell, dataObj) {
    return sendCommand('layout.save', { wname: name, group: shell, text: JSON.stringify(dataObj) }).then(changed);
  }

  // LOAD's picker manages the library — rename/remove act on an existing saved layout by id.
  function rename(id, name) {
    return sendCommand('layout.rename', { bind: id, wname: name }).then(changed);
  }
  // The LYT page's edit dialog: new name and new arrangement together, same id and list position.
  function update(id, name, dataObj) {
    return sendCommand('layout.update', { bind: id, wname: name, text: JSON.stringify(dataObj) }).then(changed);
  }
  function remove(id) {
    return sendCommand('layout.delete', { bind: id }).then(changed);
  }

  const api = { list: list, warn: warn, save: save, rename: rename, update: update, remove: remove };
  if (typeof module !== 'undefined' && module.exports) module.exports = api;
  else root.LayoutStore = api;
})(typeof self !== 'undefined' ? self : this);
