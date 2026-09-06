// Shared "PRESET N: name" bar + SAVE/LOAD wiring for a fixed 5-slot preset library shaped like
// HudPresetStore/TgtPresetStore: GET <endpoint> -> {current, presets:[{index,name,hasData}]},
// and <cmdPrefix>.save/.rename/.delete/.load commands (wname for a name, index for a slot).
// Extracted from hud.js once tgt.js (issue #78) became a second, near-identical copy of the same
// label/SAVE/LOAD/LayoutModal wiring — same reasoning as cursor-zoom.js/pending-selection.js.
export function createPresetBar({ endpoint, cmdPrefix, labelEl, saveBtn, loadBtn, send, getPreset, setPreset }) {
  function render() {
    const p = getPreset() || { index: 1, name: '' };
    labelEl.textContent = 'PRESET ' + p.index + ': ' + (p.name || '');
  }

  // LOAD's picker needs the full 5-slot list; the label alone doesn't, so this is fetched only
  // when the LOAD modal opens, not on the page's own regular poll/telemetry cadence.
  function fetchItems() {
    return fetch(endpoint, { cache: 'no-store' })
      .then(function (r) { return r.ok ? r.json() : { presets: [] }; })
      .then(function (d) {
        return (d.presets || []).map(function (p) {
          return {
            index: p.index,
            name: p.name || '',
            hasData: !!p.hasData,
            display: 'PRESET ' + p.index + (p.name ? ': ' + p.name : ''),
          };
        });
      })
      .catch(function () { return []; });
  }

  saveBtn.addEventListener('click', function () {
    LayoutModal.prompt('SAVE PRESET', function (name) {
      send(cmdPrefix + '.save', { wname: name });
      // Optimistic: we know exactly which slot (whatever's current) and name this just set.
      setPreset(Object.assign({}, getPreset(), { name: name }));
      render();
      LayoutModal.close();
    });
  });

  loadBtn.addEventListener('click', function () {
    LayoutModal.pickList('LOAD PRESET', fetchItems, {
      onPick: function (item) {
        send(cmdPrefix + '.load', { index: item.index });
        setPreset({ index: item.index, name: item.name });   // optimistic; the next snapshot settles it
        render();
      },
      onRename: function (item, name) { return send(cmdPrefix + '.rename', { index: item.index, wname: name }); },
      onDelete: function (item) { return send(cmdPrefix + '.delete', { index: item.index }); },
    });
  });

  return { render: render };
}
