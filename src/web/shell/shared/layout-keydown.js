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
  // slotDialog (optional, CLASSIC): {slots} — SAVE opens the layout dialog and stores into the next
  // free slot, or, with all `slots` taken, asks which saved layout to replace. Without it SAVE is
  // the plain name prompt.
  function makeLayoutKeydownHandlers(shellName, captureLayoutState, applyLayoutState, getSoiSurfaces, slotDialog) {
    function shellLayouts() {
      return LayoutStore.list().then(function (data) {
        return (data.layouts || []).filter(function (l) { return l.shell === shellName; });
      });
    }

    // This display's current SOI-rotation membership, one flag per pane, for the save dialog's toggles.
    function soiFlags() {
      const s = getSoiSurfaces && getSoiSurfaces();
      const all = s ? s.names.map(function () { return true; }) : [];
      if (!s || !s.cid) return Promise.resolve(all);
      return fetch('/soi-excluded?cid=' + encodeURIComponent(s.cid), { cache: 'no-store' })
        .then(function (r) { return r.ok ? r.json() : { excluded: [] }; })
        .then(function (d) { return s.names.map(function (_, pane) { return (d.excluded || []).indexOf(pane) === -1; }); })
        .catch(function () { return all; });
    }

    // SAVE LAYOUT (CLASSIC): pick a slot for the current arrangement. A taken slot is REPLACEd (same
    // id and position, so it keeps its Layout N key); the first empty slot is a SAVE (the list
    // appends, so later empty slots can't be skipped to and are shown but not selectable). The key
    // recorded in the form goes to the chosen slot.
    function openSaveSlotDialog() {
      if (LayoutEditDialog.isOpen()) return;   // a held SAVE key repeating
      LayoutStore.list().then(function (data) {
        if (data.failed) {
          LayoutEditDialog.open({ title: 'SAVE LAYOUT', notice: "Can't reach the game, so saved layouts can't be read or written right now." });
          return;
        }
        const items = (data.layouts || []).filter(function (l) { return l.shell === shellName; });
        const arrangement = captureLayoutState();
        soiFlags().then(function (flags) {
          const rows = LytSlots.describeLayouts(items);
          const FREE = 'free';   // the choice id of the first empty slot
          const hasFree = items.length < slotDialog.slots;
          LayoutEditDialog.open({
            title: 'SAVE LAYOUT',
            panes: getSoiSurfaces().names.map(function (label, i) { return { label: label, on: flags[i] }; }),
            pendingKey: true,
            message: hasFree
              ? 'Select a slot for this layout. A taken slot is replaced.'
              : 'All ' + slotDialog.slots + ' layout slots are taken. Select the one to replace with this layout.',
            submitLabel: hasFree ? 'SAVE' : 'REPLACE',
            onSubmit: function (name, soi, id, key) {
              const data = Object.assign(arrangement, { soi: soi });
              // The chosen layout may have been deleted from another browser meanwhile: then the
              // update is a no-op on the plugin and there is no slot to bind.
              const target = rows.filter(function (r) { return r.id === id; })[0];
              const slot = id === FREE ? items.length + 1 : target && target.slot;
              if (id === FREE) LayoutStore.save(name, shellName, data).catch(LayoutStore.warn('save'));
              else LayoutStore.update(id, name, data).catch(LayoutStore.warn('replace'));
              if (key && slot) LayoutKeybinds.setSlotKey(slot, key);
            },
            buildList: function (choose) {
              const ol = document.createElement('ol');
              function addRow(li, radio, id, label) {
                radio.addEventListener('change', function () {
                  [].forEach.call(ol.children, function (c) { c.classList.toggle('picked', c === li); });
                  choose(id, label);
                });
                // The whole row is the target, not just the small radio.
                li.addEventListener('click', function (e) { if (e.target !== radio && !radio.disabled) radio.click(); });
                ol.appendChild(li);
              }
              function radioFor(text) {
                const radio = document.createElement('input');
                radio.type = 'radio'; radio.name = 'led-slot'; radio.className = 'led-pick';
                radio.setAttribute('aria-label', text);
                return radio;
              }
              rows.forEach(function (row) {
                const radio = radioFor('Replace ' + row.name);
                addRow(LytRow.build(row, [radio]), radio, row.id, 'REPLACE');
              });
              for (let n = items.length + 1; n <= slotDialog.slots; n++) {
                const first = n === items.length + 1;
                const radio = radioFor('Save in slot ' + n);
                radio.disabled = !first;
                const li = LytRow.buildEmpty(n, [radio]);
                li.classList.toggle('inert', !first);
                addRow(li, radio, FREE, 'SAVE');
                if (first) { radio.checked = true; li.classList.add('picked'); choose(FREE, 'SAVE'); }
              }
              return ol;
            },
          });
        });
      });
    }

    function openSaveLayoutModal() {
      if (slotDialog) { openSaveSlotDialog(); return; }
      LayoutModal.prompt('SAVE LAYOUT', function (name) {
        LayoutStore.save(name, shellName, captureLayoutState()).catch(LayoutStore.warn('save'));
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
      // CLASSIC: the LYT page's SAVED section as a popup.
      if (slotDialog) { if (!LayoutLoadDialog.isOpen()) LayoutLoadDialog.open(loadById); return; }
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

    // The LYT page's LOAD button: apply the saved layout with this id. A layout deleted in the
    // meantime is simply not found.
    function loadById(id) {
      shellLayouts().then(function (items) {
        const item = items.find(function (l) { return l.id === id; });
        if (item) applyItem(item);
      });
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
      loadById: loadById,
      handleLayoutKeydown: handleLayoutKeydown,
      wireLayoutKeydown: wireLayoutKeydown,
    };
  }

  const api = { makeLayoutKeydownHandlers: makeLayoutKeydownHandlers };
  if (typeof module !== 'undefined' && module.exports) module.exports = api;
  else root.LayoutKeydown = api;
})(typeof self !== 'undefined' ? self : this);
