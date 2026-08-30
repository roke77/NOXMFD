// Node self-check for the TGP CFG resolution/quality controls.

const assert = require('assert');

class ClassList {
  constructor() { this.values = new Set(); }
  add(name) { this.values.add(name); }
  remove(name) { this.values.delete(name); }
  toggle(name, on) { on ? this.add(name) : this.remove(name); }
  contains(name) { return this.values.has(name); }
}

class Element {
  constructor(id) {
    this.id = id;
    this.classList = new ClassList();
    this.dataset = {};
    this.value = '';
    this.textContent = '';
    this.onclick = null;
    this.oninput = null;
    this.onchange = null;
  }
}

const ids = [
  'tcfg-panel', 'tcfg-tgp-slider', 'tcfg-tgp-val', 'tcfg-tgp-hz-warning',
  'tcfg-tgp-resolution-warning', 'tcfg-tgp-jpeg-quality-warning',
  'tcfg-tgp-combined-warning', 'tcfg-tgp-suppress-btn', 'tcfg-reset',
];
const elements = Object.fromEntries(ids.map((id) => [id, new Element(id)]));
const resolutionButtons = ['native', 'mid', 'high'].map((value) => {
  const button = new Element('resolution-' + value);
  button.dataset.resolution = value;
  return button;
});
const qualityButtons = ['low', 'mid', 'high'].map((value) => {
  const button = new Element('quality-' + value);
  button.dataset.jpegQuality = value;
  return button;
});

global.window = {};
window.parent = window;
global.document = {
  getElementById(id) { return elements[id]; },
  querySelector() { return null; },
  querySelectorAll(selector) {
    if (selector.indexOf('resolution-row') !== -1) return resolutionButtons;
    if (selector.indexOf('jpeg-quality-row') !== -1) return qualityButtons;
    return [];
  },
};

const commands = [];
global.sendCommand = (cmd, args) => {
  commands.push({ cmd, args });
  return Promise.resolve();
};
let fetchResponse = {
  tgpHz: 20,
  tgpResolution: 'high',
  tgpJpegQuality: 'high',
  tgpSuppressNative: true,
};
global.fetch = () => Promise.resolve({ json: () => Promise.resolve(fetchResponse) });

(async function run() {
  require('./tgpcfg.js');
  await new Promise((resolve) => setImmediate(resolve));

  assert.ok(resolutionButtons[2].classList.contains('active'), 'HIGH resolution should restore');
  assert.ok(qualityButtons[2].classList.contains('active'), 'HIGH JPEG quality should restore');
  assert.ok(elements['tcfg-tgp-combined-warning'].classList.contains('shown'), 'HIGH/HIGH warning should show');

  resolutionButtons[1].onclick();
  qualityButtons[0].onclick();
  assert.deepStrictEqual(commands.slice(-2), [
    { cmd: 'rates.set', args: { group: 'tgpResolution', wname: 'mid' } },
    { cmd: 'rates.set', args: { group: 'tgpJpegQuality', wname: 'low' } },
  ]);
  assert.ok(!elements['tcfg-tgp-combined-warning'].classList.contains('shown'), 'non-HIGH combination should hide warning');

  elements['tcfg-reset'].onclick();
  assert.ok(resolutionButtons[0].classList.contains('active'), 'reset should restore native resolution');
  assert.ok(qualityButtons[1].classList.contains('active'), 'reset should restore JPEG 50/MID');

  // A pre-extended-quality client only ever persisted tgpQuality: "hq" — no tgpResolution/
  // tgpJpegQuality keys at all. Re-run the module against that exact shape to prove the fallback
  // in tgpcfg.js's own fetch handler (not just telemetry-source.js's) resolves it to MID/MID
  // rather than leaving every resolution button unlit.
  for (const el of Object.values(elements)) el.classList = new ClassList();
  for (const btn of resolutionButtons.concat(qualityButtons)) btn.classList = new ClassList();
  commands.length = 0;
  fetchResponse = { tgpHz: 15, tgpQuality: 'hq', tgpSuppressNative: false };
  delete require.cache[require.resolve('./tgpcfg.js')];
  require('./tgpcfg.js');
  await new Promise((resolve) => setImmediate(resolve));

  assert.ok(resolutionButtons[1].classList.contains('active'), 'legacy tgpQuality:"hq" alone should restore MID resolution');
  assert.ok(qualityButtons[1].classList.contains('active'), 'a config with no tgpJpegQuality key should default JPEG quality to MID');

  console.log('tgpcfg.test.js: OK');
})().catch((error) => {
  console.error(error);
  process.exitCode = 1;
});
