// Run: `node preset-cards.test.js`. Covers createPresetCards: the five cards' state, tap-to-recall,
// the SAVE PRESET dialog (SAVE / CLEAR / CANCEL / Escape / Enter, empty-name refusal, CLEAR disabled
// on an empty slot) and that the list is refetched only when the telemetry's current slot changes.
const assert = require('assert');
const { createPresetCards, PRESET_SLOTS } = require('./preset-cards.js');

function fakeEl() {
  const classes = new Set();
  const el = {
    dataset: {}, attrs: {}, children: [], listeners: {}, hidden: false, disabled: false,
    value: '', textContent: '', className: '',
    classList: {
      toggle: (c, on) => { if (on) classes.add(c); else classes.delete(c); },
      has: (c) => classes.has(c),
    },
    setAttribute(k, v) { this.attrs[k] = v; },
    appendChild(c) { this.children.push(c); },
    addEventListener(t, fn) { this.listeners[t] = fn; },
    focus() { this.focused = true; },
    select() { this.selected = true; },
  };
  return el;
}

function make(presetsResponse) {
  const sent = [];
  const fetched = [];
  const dialog = { scrim: fakeEl(), title: fakeEl(), input: fakeEl(), error: fakeEl(),
                   clear: fakeEl(), save: fakeEl(), cancel: fakeEl() };
  dialog.scrim.hidden = true;
  const doc = { createElement: fakeEl, addEventListener(t, fn) { this.keydown = fn; } };
  const cardsEl = fakeEl();
  const ui = createPresetCards({
    cardsEl, dialog, doc, endpoint: '/x-presets', cmdPrefix: 'x-preset',
    send: (cmd, args) => { sent.push({ cmd, args }); return Promise.resolve(); },
    fetchFn: (url) => {
      fetched.push(url);
      return Promise.resolve({ ok: true, json: () => Promise.resolve(presetsResponse) });
    },
  });
  return { ui, sent, fetched, dialog, doc, cards: cardsEl.children };
}

const RESPONSE = {
  current: 2,
  presets: [
    { index: 1, name: 'A2A CAP', hasData: true }, { index: 2, name: 'STRIKE', hasData: true },
    { index: 3, name: '', hasData: false }, { index: 4, name: '', hasData: false },
    { index: 5, name: '', hasData: false },
  ],
};
const tick = () => new Promise((r) => setTimeout(r, 0));

(async () => {
  // Five cards, position = slot; refresh mirrors names, empties and the current slot.
  {
    const { ui, cards } = make(RESPONSE);
    assert.strictEqual(cards.length, PRESET_SLOTS);
    assert.deepStrictEqual(cards.map((c) => c.dataset.slot), [1, 2, 3, 4, 5]);
    ui.render();
    assert.strictEqual(cards[0].children[0].textContent, 'EMPTY', 'unsaved slots read EMPTY');
    await ui.refresh();
    assert.deepStrictEqual(cards.map((c) => c.children[0].textContent), ['A2A CAP', 'STRIKE', 'EMPTY', 'EMPTY', 'EMPTY']);
    assert.deepStrictEqual(cards.map((c) => c.attrs['aria-pressed']), ['false', 'true', 'false', 'false', 'false']);
    assert.deepStrictEqual(cards.map((c) => c.classList.has('empty')), [false, false, true, true, true]);
    assert.strictEqual(cards[2].attrs['aria-label'], 'Preset 3: empty');
  }

  // Tap recalls: lights the card at once and sends x-preset.load with that slot.
  {
    const { ui, sent, cards } = make(RESPONSE);
    ui.recall(4);
    assert.deepStrictEqual(sent, [{ cmd: 'x-preset.load', args: { index: 4 } }]);
    assert.strictEqual(cards[3].attrs['aria-pressed'], 'true');
  }

  // Hold -> dialog: prefilled with the slot's name, CLEAR enabled only on a saved slot.
  {
    const { ui, dialog } = make(RESPONSE);
    await ui.refresh();
    ui.save(1);
    assert.strictEqual(dialog.scrim.hidden, false);
    assert.strictEqual(dialog.title.textContent, 'SAVE PRESET 1');
    assert.strictEqual(dialog.input.value, 'A2A CAP');
    assert.strictEqual(dialog.clear.disabled, false);
    assert.ok(dialog.input.focused && dialog.input.selected);
    ui.save(3);
    assert.strictEqual(dialog.input.value, '', 'an empty slot opens with no name');
    assert.strictEqual(dialog.clear.disabled, true, 'nothing to clear on an empty slot');
  }

  // SAVE: refuses an empty name, then sends save with the slot and makes it current.
  {
    const { ui, sent, dialog, cards } = make(RESPONSE);
    await ui.refresh();
    ui.save(3);
    dialog.input.value = '   ';
    dialog.save.listeners.click();
    assert.strictEqual(sent.length, 0);
    assert.strictEqual(dialog.error.textContent, 'Enter a name.');
    assert.strictEqual(dialog.scrim.hidden, false, 'stays open on an empty name');
    dialog.input.listeners.input();
    assert.strictEqual(dialog.error.textContent, '', 'typing clears the error');

    dialog.input.value = ' SEAD ';
    dialog.save.listeners.click();
    assert.deepStrictEqual(sent, [{ cmd: 'x-preset.save', args: { wname: 'SEAD', index: 3 } }]);
    assert.strictEqual(dialog.scrim.hidden, true);
    assert.strictEqual(cards[2].children[0].textContent, 'SEAD');
    assert.strictEqual(cards[2].attrs['aria-pressed'], 'true');
  }

  // CLEAR empties the slot (x-preset.delete) without touching the current slot.
  {
    const { ui, sent, dialog, cards } = make(RESPONSE);
    await ui.refresh();
    ui.save(1);
    dialog.clear.listeners.click();
    assert.deepStrictEqual(sent, [{ cmd: 'x-preset.delete', args: { index: 1 } }]);
    assert.strictEqual(cards[0].children[0].textContent, 'EMPTY');
    assert.strictEqual(cards[1].attrs['aria-pressed'], 'true', 'slot 2 stays current');
    assert.strictEqual(dialog.scrim.hidden, true);
  }

  // CANCEL, Escape and a scrim click close without sending; Enter only saves from the entry.
  {
    const { ui, sent, dialog, doc } = make(RESPONSE);
    await ui.refresh();
    ui.save(2); dialog.cancel.listeners.click();
    assert.strictEqual(dialog.scrim.hidden, true);
    ui.save(2); doc.keydown({ key: 'Escape', preventDefault() {} });
    assert.strictEqual(dialog.scrim.hidden, true);
    ui.save(2); dialog.scrim.listeners.pointerdown({ target: dialog.scrim });
    assert.strictEqual(dialog.scrim.hidden, true);
    ui.save(2); dialog.scrim.listeners.pointerdown({ target: dialog.input });
    assert.strictEqual(dialog.scrim.hidden, false, 'a press inside the panel keeps it open');
    doc.keydown({ key: 'Enter', target: dialog.cancel, preventDefault() {} });
    assert.strictEqual(sent.length, 0, 'Enter on a button is not a save');
    doc.keydown({ key: 'Enter', repeat: true, target: dialog.input, preventDefault() {} });
    assert.strictEqual(sent.length, 0, 'a repeat from the key that opened the dialog is not a save');
    doc.keydown({ key: 'Enter', target: dialog.input, preventDefault() {} });
    assert.deepStrictEqual(sent, [{ cmd: 'x-preset.save', args: { wname: 'STRIKE', index: 2 } }]);
  }

  // focusEntry re-focuses the entry only while the dialog is open.
  {
    const { ui, dialog } = make(RESPONSE);
    ui.focusEntry();
    assert.ok(!dialog.input.focused, 'nothing to focus while closed');
    ui.save(2);
    dialog.input.focused = false;
    ui.focusEntry();
    assert.strictEqual(dialog.input.focused, true);
  }

  // sync refetches only when the telemetry's current slot changes.
  {
    const { ui, fetched } = make(RESPONSE);
    ui.sync({ index: 1, name: '' });
    ui.sync({ index: 1, name: '' });
    assert.strictEqual(fetched.length, 1);
    assert.strictEqual(fetched[0], '/x-presets', 'refetches the injected endpoint');
    ui.sync({ index: 2, name: 'STRIKE' });
    assert.strictEqual(fetched.length, 2);
  }

  // A failed or malformed response leaves the cards as they were.
  {
    const bad = make(null);
    bad.ui.render();
    await bad.ui.refresh();
    assert.deepStrictEqual(bad.cards.map((c) => c.children[0].textContent), Array(PRESET_SLOTS).fill('EMPTY'));
    assert.strictEqual(bad.cards[0].attrs['aria-pressed'], 'true', 'the default current slot (1) is kept');
  }

  await tick();
  console.log('preset-cards.test.js: OK');
  process.exit(0);   // the post-command settle timers would otherwise keep node alive briefly
})().catch((e) => { console.error(e); process.exit(1); });
