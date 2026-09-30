// Preset cards and their SAVE PRESET dialog, shared by the TGT and HUD pages (TGT: issue #78,
// docs/tgt-presets.md; HUD: docs/hud-presets.md).
//
// Five fixed slots as cards, mirroring GET <endpoint> -> {current, presets:[{index,name,hasData}]}.
// Only the current slot's index/name rides the page's own state (TGT: the 'tgt' telemetry block, HUD:
// the /hud-options snapshot), so a change to that pair — a recall or save from any browser, or one of
// the KEY-page recall binds — is the cue to refetch the whole list (sync); the page's own taps and
// holds update the cards immediately and refetch shortly after to settle a rejected command.
//   recall(slot) — tap: <cmdPrefix>.load; an empty slot just becomes current.
//   save(slot)   — hold: opens the dialog. SAVE stores the typed name and the live filters
//                  (<cmdPrefix>.save {wname,index}); CLEAR empties the slot (<cmdPrefix>.delete);
//                  CANCEL, Escape or a click on the scrim closes it. Enter in the entry saves.
// The DOM, fetch and send are injected so preset-cards.test.js can drive it without a browser.
export const PRESET_SLOTS = 5;
const SETTLE_MS = 300;   // how long after a command the list is refetched

export function createPresetCards({ cardsEl, dialog, send, endpoint, cmdPrefix, doc = document, fetchFn = fetch }) {
  let presets = Array.from({ length: PRESET_SLOTS }, function (_, i) { return { index: i + 1, name: '', hasData: false }; });
  let current = 1;
  let telemetryKey = '';
  let dialogSlot = 0;   // 0 = closed

  const cards = [];
  for (let i = 1; i <= PRESET_SLOTS; i++) {
    const card = doc.createElement('button');
    card.type = 'button'; card.className = 'lit preset-card pad-hoverable'; card.dataset.slot = i;
    const name = doc.createElement('span'); name.className = 'preset-name';
    card.appendChild(name);
    cardsEl.appendChild(card);
    cards.push({ card: card, name: name });
  }

  function render() {
    cards.forEach(function (c, i) {
      const p = presets[i];
      c.card.classList.toggle('empty', !p.hasData);
      c.card.setAttribute('aria-pressed', current === i + 1 ? 'true' : 'false');
      c.card.setAttribute('aria-label', 'Preset ' + (i + 1) + ': ' + (p.hasData ? p.name : 'empty'));
      c.name.textContent = p.hasData ? p.name : 'EMPTY';
    });
  }

  function refresh() {
    return fetchFn(endpoint, { cache: 'no-store' })
      .then(function (r) { return r.ok ? r.json() : null; })
      .then(function (d) {
        if (!d || !Array.isArray(d.presets)) return;
        d.presets.forEach(function (p) {
          if (p.index >= 1 && p.index <= PRESET_SLOTS) presets[p.index - 1] = { index: p.index, name: p.name || '', hasData: !!p.hasData };
        });
        if (d.current >= 1 && d.current <= PRESET_SLOTS) current = d.current;
        render();
      })
      .catch(function () {});
  }

  function sendPreset(cmd, args) {
    send(cmd, args).then(function () { setTimeout(refresh, SETTLE_MS); });
  }

  function recall(slot) {
    current = slot;
    render();
    sendPreset(cmdPrefix + '.load', { index: slot });
  }

  function setError(message) {
    dialog.input.classList.toggle('bad', !!message);
    dialog.error.textContent = message;
  }

  function closeDialog() { dialogSlot = 0; dialog.scrim.hidden = true; }

  function save(slot) {
    dialogSlot = slot;
    dialog.title.textContent = 'SAVE PRESET ' + slot;
    dialog.input.value = presets[slot - 1].name;
    setError('');
    dialog.clear.disabled = !presets[slot - 1].hasData;
    dialog.scrim.hidden = false;
    dialog.input.focus();
    dialog.input.select();
  }

  function submit() {
    const name = dialog.input.value.trim();
    if (!name) { setError('Enter a name.'); return; }
    presets[dialogSlot - 1] = { index: dialogSlot, name: name, hasData: true };
    current = dialogSlot;
    render();
    sendPreset(cmdPrefix + '.save', { wname: name, index: dialogSlot });
    closeDialog();
  }

  function clearSlot() {
    presets[dialogSlot - 1] = { index: dialogSlot, name: '', hasData: false };
    render();
    sendPreset(cmdPrefix + '.delete', { index: dialogSlot });
    closeDialog();
  }

  dialog.save.addEventListener('click', submit);
  dialog.cancel.addEventListener('click', closeDialog);
  dialog.clear.addEventListener('click', clearSlot);
  dialog.input.addEventListener('input', function () { setError(''); });
  dialog.scrim.addEventListener('pointerdown', function (e) { if (e.target === dialog.scrim) closeDialog(); });
  doc.addEventListener('keydown', function (e) {
    if (!dialogSlot) return;
    // A key still held from the press that opened the dialog repeats into the entry; it must not save.
    if (e.key === 'Enter' && e.target === dialog.input) { e.preventDefault(); if (!e.repeat) submit(); }
    else if (e.key === 'Escape') { e.preventDefault(); closeDialog(); }
  });

  // Re-focus the entry while the dialog is open, from the release of the opening hold: only a
  // focus() inside a user gesture raises a touch keyboard.
  function focusEntry() { if (dialogSlot) dialog.input.focus(); }

  // The 'tgt' frame's current slot; refetch the list only when that pair changes.
  function sync(preset) {
    const key = preset.index + '|' + preset.name;
    if (key === telemetryKey) return;
    telemetryKey = key;
    refresh();
  }

  return { render: render, refresh: refresh, recall: recall, save: save, focusEntry: focusEntry, sync: sync };
}
