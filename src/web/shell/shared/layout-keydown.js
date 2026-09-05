// SAVE/LOAD LAYOUT keyboard wiring, shared by both shells (mfd.js, f35.js). captureLayoutState/
// applyLayoutState are passed in per shell because the state shape genuinely differs: classic's is
// {splitMode, splitVariant, pages, pinnedPage}, f35's is {cells, pages}.
//
// Classic <script>, not a module, same as layout-store.js/layout-modal.js — a plain global, no
// build step.
(function (root) {
  // shellName: 'classic' | 'f35', used both as LayoutStore's shell tag and the list filter.
  // captureLayoutState()/applyLayoutState(state): the shell's own state get/set functions.
  // getSoiSurfaces() (optional, issue #58): returns { cid, labels } describing THIS browser's own
  // live surfaces right now — one label per pane/portal, in SOI's own pane-index order. The shell
  // is the only thing that knows whether it's a full view, an H/V split, or an F-35 portal count,
  // so it owns the exact wording ("Include TOP panel in SOI", "Include portal 2 in SOI", ...);
  // this module only knows how to fetch/set the server's included/excluded state generically.
  function makeLayoutKeydownHandlers(shellName, captureLayoutState, applyLayoutState, getSoiSurfaces) {
    function shellLayouts() {
      return LayoutStore.list().then(function (data) {
        return (data.layouts || []).filter(function (l) { return l.shell === shellName; });
      });
    }

    function openSaveLayoutModal() {
      LayoutModal.prompt('SAVE LAYOUT', function (name) {
        LayoutStore.save(name, shellName, captureLayoutState()).catch(function () {});
        LayoutModal.close();
      });
    }

    // Fetched fresh every time LOAD opens (server-side state, not part of a saved layout) so a
    // change made from another tab sharing the same cid is never shown stale.
    function soiCheckboxes() {
      const s = getSoiSurfaces && getSoiSurfaces();
      if (!s || !s.cid || !s.labels.length) return Promise.resolve([]);
      return fetch('/soi-excluded?cid=' + encodeURIComponent(s.cid), { cache: 'no-store' })
        .then(function (r) { return r.ok ? r.json() : { excluded: [] }; })
        .then(function (d) {
          const excluded = d.excluded || [];
          return s.labels.map(function (label, pane) {
            return {
              label: label,
              checked: excluded.indexOf(pane) === -1,
              onChange: function (checked) {
                sendCommand('soi.include', { cid: s.cid, n: pane, on: checked }).catch(function () {});
              },
            };
          });
        })
        .catch(function () { return []; });
    }

    function openLoadLayoutModal() {
      soiCheckboxes().then(function (checkboxes) {
        LayoutModal.pickList('LOAD LAYOUT', shellLayouts, {
          checkboxes: checkboxes,
          onPick: function (item) {
            try { applyLayoutState(JSON.parse(item.data)); } catch (e) {}
          },
          onRename: function (item, name) { return LayoutStore.rename(item.id, name); },
          onDelete: function (item) { return LayoutStore.remove(item.id); },
        });
      });
    }

    // A keydown only reaches window.addEventListener('keydown', ...) on the document it lands in —
    // it never bubbles across an iframe boundary to the parent. Almost everything a pilot clicks
    // (the map, a split pane/portal, any hosted page) is inside an iframe, so a listener on just the
    // shell's own top document misses most real presses. Same-origin, so attaching the identical
    // handler directly onto each iframe's contentWindow needs no postMessage relay — it just has to
    // be re-attached after every navigation, since reassigning src tears down that whole document
    // (and any listeners on it), same as a real page load.
    function handleLayoutKeydown(e) {
      if (e.ctrlKey || e.altKey || e.metaKey) return;
      const t = e.target;
      if (t && (t.tagName === 'INPUT' || t.tagName === 'TEXTAREA' || t.isContentEditable)) return;
      const action = LayoutKeybinds.match(e);
      if (action === 'save') openSaveLayoutModal();
      else if (action === 'load') openLoadLayoutModal();
    }
    function wireLayoutKeydown(iframe) {
      function attach() { try { iframe.contentWindow.addEventListener('keydown', handleLayoutKeydown); } catch (e) {} }
      iframe.addEventListener('load', attach);
      attach();   // in case it's already loaded
    }

    return {
      openSaveLayoutModal: openSaveLayoutModal,
      openLoadLayoutModal: openLoadLayoutModal,
      handleLayoutKeydown: handleLayoutKeydown,
      wireLayoutKeydown: wireLayoutKeydown,
    };
  }

  const api = { makeLayoutKeydownHandlers: makeLayoutKeydownHandlers };
  if (typeof module !== 'undefined' && module.exports) module.exports = api;
  else root.LayoutKeydown = api;
})(typeof self !== 'undefined' ? self : this);
