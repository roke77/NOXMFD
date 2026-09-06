// Run: `node preset-bar.test.js`. Covers createPresetBar's SAVE/LOAD/rename/delete wiring — in
// particular that all four operations go through the INJECTED `send` callback, not the global
// `sendCommand` (a prior version called `sendCommand` directly for rename/delete, which skipped
// whatever page-specific bookkeeping a page's own `send` does around a command — e.g. hud.js's
// `send` also resets its resync timer so a stale label gets corrected if the command is rejected).
const assert = require('assert');
const { createPresetBar } = require('./preset-bar.js');

function fakeButton() {
  return { handler: null, addEventListener: function (evt, fn) { this.handler = fn; } };
}

// A minimal LayoutModal stub: prompt() invokes its callback with a canned name immediately;
// pickList() just records its options so a test can drive onPick/onRename/onDelete directly,
// the way a real click on a rendered row would.
function installFakeLayoutModal(promptName) {
  const pickListCalls = [];
  global.LayoutModal = {
    prompt: function (title, onSubmit) { onSubmit(promptName); },
    pickList: function (title, fetchItems, opts) { pickListCalls.push(opts); },
    close: function () {},
  };
  return pickListCalls;
}

function makeBar(overrides) {
  const sent = [];
  const send = function (cmd, args) { sent.push({ cmd: cmd, args: args }); return Promise.resolve('ok'); };
  const saveBtn = fakeButton();
  const loadBtn = fakeButton();
  let preset = { index: 1, name: '' };
  const bar = createPresetBar(Object.assign({
    endpoint: '/x-presets',
    cmdPrefix: 'x-preset',
    labelEl: { textContent: '' },
    saveBtn: saveBtn,
    loadBtn: loadBtn,
    send: send,
    getPreset: function () { return preset; },
    setPreset: function (p) { preset = p; },
  }, overrides));
  return { bar: bar, sent: sent, saveBtn: saveBtn, loadBtn: loadBtn, getPreset: function () { return preset; } };
}

// SAVE: already used the injected `send` before this fix — confirm it still does, with the
// {wname} shape the plugin's tgt-preset.save/preset.save commands expect.
{
  const pickListCalls = installFakeLayoutModal('BVR');
  const { bar, sent, saveBtn, getPreset } = makeBar();
  saveBtn.handler();   // simulates a click
  assert.strictEqual(sent.length, 1);
  assert.deepStrictEqual(sent[0], { cmd: 'x-preset.save', args: { wname: 'BVR' } });
  assert.strictEqual(getPreset().name, 'BVR', 'save optimistically updates the current preset\'s name');
  assert.strictEqual(pickListCalls.length, 0);
}

// Drive LOAD's onPick/onRename/onDelete directly — this is the part the review flagged: before
// the fix, onRename/onDelete called the global `sendCommand`, bypassing `send` entirely.
{
  const pickListCalls = installFakeLayoutModal('unused');
  const { bar, sent, loadBtn, getPreset } = makeBar();
  loadBtn.handler();
  assert.strictEqual(pickListCalls.length, 1, 'LOAD click opens exactly one picker');
  const opts = pickListCalls[0];

  opts.onPick({ index: 3, name: 'CAS' });
  assert.deepStrictEqual(sent[sent.length - 1], { cmd: 'x-preset.load', args: { index: 3 } });
  assert.deepStrictEqual(getPreset(), { index: 3, name: 'CAS' }, 'onPick optimistically sets the current preset');

  const renameResult = opts.onRename({ index: 2, name: 'old' }, 'new');
  assert.deepStrictEqual(sent[sent.length - 1], { cmd: 'x-preset.rename', args: { index: 2, wname: 'new' } });
  assert.ok(renameResult && typeof renameResult.then === 'function',
    'onRename must return the injected send()\'s promise, not a bare sendCommand() call, so ' +
    'LayoutModal\'s .then(refresh) has something to chain onto');

  const deleteResult = opts.onDelete({ index: 4, name: 'old' });
  assert.deepStrictEqual(sent[sent.length - 1], { cmd: 'x-preset.delete', args: { index: 4 } });
  assert.ok(deleteResult && typeof deleteResult.then === 'function', 'onDelete must also return send()\'s promise');
}

// fetchItems() shapes a raw /x-presets response into LayoutModal's {display,...} item shape.
{
  installFakeLayoutModal('unused');
  const { bar, loadBtn } = makeBar();
  global.fetch = function (url) {
    assert.strictEqual(url, '/x-presets');
    return Promise.resolve({ ok: true, json: function () {
      return Promise.resolve({ presets: [{ index: 1, name: 'BVR', hasData: true }, { index: 2, name: '', hasData: false }] });
    } });
  };
  const pickListCalls = [];
  global.LayoutModal.pickList = function (title, fetchItems, opts) { pickListCalls.push(fetchItems); };
  loadBtn.handler();
  pickListCalls[0]().then(function (items) {
    assert.deepStrictEqual(items[0], { index: 1, name: 'BVR', hasData: true, display: 'PRESET 1: BVR' });
    assert.deepStrictEqual(items[1], { index: 2, name: '', hasData: false, display: 'PRESET 2' });
    console.log('preset-bar.test.js: OK');
  });
}
