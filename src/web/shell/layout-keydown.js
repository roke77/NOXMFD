// SAVE/LOAD LAYOUT keyboard wiring, shared by both shells (docs/refactor-scan.md step 1) — mfd.js
// and f35.js each had a byte-identical copy of this, differing only in the shell name string and
// which capture/apply-state functions to call (those two genuinely differ per shell: classic's state
// is {splitMode, splitVariant, pages, pinnedPage}, f35's is {cells, pages} — real per-layout data,
// not accidental duplication, so they stay as constructor arguments rather than being folded in).
//
// Classic <script>, not a module, same as layout-store.js/layout-modal.js — a plain global, no
// build step.
(function (root) {
  // shellName: 'classic' | 'f35', used both as LayoutStore's shell tag and the list filter.
  // captureLayoutState()/applyLayoutState(state): the shell's own state get/set functions.
  function makeLayoutKeydownHandlers(shellName, captureLayoutState, applyLayoutState) {
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

    function openLoadLayoutModal() {
      LayoutModal.pickList('LOAD LAYOUT', shellLayouts, {
        onPick: function (item) {
          try { applyLayoutState(JSON.parse(item.data)); } catch (e) {}
        },
        onRename: function (item, name) { return LayoutStore.rename(item.id, name); },
        onDelete: function (item) { return LayoutStore.remove(item.id); },
      });
    }

    // A keydown only reaches window.addEventListener('keydown', ...) on the DOCUMENT it lands in —
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
