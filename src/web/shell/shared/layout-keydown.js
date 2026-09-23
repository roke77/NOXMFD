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

    // A saved layout's data is an opaque JSON blob (LayoutStore never parses it), so a corrupted or
    // hand-edited one is skipped with a warning instead of throwing out of a click/keypress handler.
    function applyItem(item) {
      try { applyLayoutState(JSON.parse(item.data)); }
      catch (e) { console.warn('[layout] saved layout "' + item.name + '" could not be applied:', e); }
    }

    function openLoadLayoutModal() {
      soiCheckboxes().then(function (checkboxes) {
        LayoutModal.pickList('LOAD LAYOUT', shellLayouts, {
          checkboxes: checkboxes,
          onPick: applyItem,
          onRename: function (item, name) { return LayoutStore.rename(item.id, name); },
          onDelete: function (item) { return LayoutStore.remove(item.id); },
          // Layout Preset slots (issue #90) are positional: the first five rows carry slot 1-5's box.
          rowExtra: function (item, i) { return i < LayoutKeybinds.SLOT_COUNT ? LayoutKeybinds.slotBox(i + 1) : null; },
        });
      });
    }

    // Layout Preset slot n (issue #90): the nth layout in this shell's LOAD LAYOUT list, same apply
    // as picking it there. No layout at that position → nothing happens.
    function loadSlot(n) {
      shellLayouts().then(function (items) { if (items[n - 1]) applyItem(items[n - 1]); });
    }

    // The shells' map-act handler asks this first: a joystick/in-game press of Layout N arrives as
    // act 'layout-preset-N' at the SOI browser. Returns true when it was a slot (and loads it).
    function loadSlotAct(act) {
      const m = /^layout-preset-(\d)$/.exec(act || '');
      if (m) loadSlot(+m[1]);
      return !!m;
    }

    // A keydown only reaches window.addEventListener('keydown', ...) on the document it lands in —
    // it never bubbles across an iframe boundary to the parent. Almost everything a pilot clicks
    // (the map, a split pane/portal, any hosted page) is inside an iframe, so a listener on just the
    // shell's own top document misses most real presses. Same-origin, so attaching the identical
    // handler directly onto each iframe's contentWindow needs no postMessage relay — it just has to
    // be re-attached after every navigation, since reassigning src tears down that whole document
    // (and any listeners on it), same as a real page load.
    function handleLayoutKeydown(e) {
      if (e.metaKey) return;   // Ctrl/Alt chords are matched (LayoutKeybinds.match); Win/Cmd never
      const t = e.target;
      if (t && (t.tagName === 'INPUT' || t.tagName === 'TEXTAREA' || t.isContentEditable)) return;
      const action = LayoutKeybinds.match(e);
      if (action === 'save') openSaveLayoutModal();
      else if (action === 'load') openLoadLayoutModal();
      else if (action) loadSlot(+action.slice('slot-'.length));
    }
    function wireLayoutKeydown(iframe) {
      function attach() { try { iframe.contentWindow.addEventListener('keydown', handleLayoutKeydown); } catch (e) {} }
      iframe.addEventListener('load', attach);
      attach();   // in case it's already loaded
    }

    return {
      openSaveLayoutModal: openSaveLayoutModal,
      openLoadLayoutModal: openLoadLayoutModal,
      loadSlotAct: loadSlotAct,
      handleLayoutKeydown: handleLayoutKeydown,
      wireLayoutKeydown: wireLayoutKeydown,
    };
  }

  const api = { makeLayoutKeydownHandlers: makeLayoutKeydownHandlers };
  if (typeof module !== 'undefined' && module.exports) module.exports = api;
  else root.LayoutKeydown = api;
})(typeof self !== 'undefined' ? self : this);
