// Regression check for split-pane controls that change state in place. mfd.js is a browser
// composition root with extensive DOM setup, so this follows server-route-coverage.test.js's
// source-scan approach and verifies these actions stay inside the split dispatcher instead of
// falling through to paneNavigate(), which resolves an unknown action to about:blank.
const assert = require('assert');
const fs = require('fs');
const path = require('path');

const source = fs.readFileSync(path.join(__dirname, 'mfd.js'), 'utf8');
const splitStart = source.indexOf('if (splitMode && el.dataset.pane && el.dataset.action)');
const splitEnd = source.indexOf('\n  switch (el.dataset.action)', splitStart);

assert.ok(splitStart >= 0 && splitEnd > splitStart, 'could not isolate mfdButton split dispatcher');
const splitDispatcher = source.slice(splitStart, splitEnd);

for (const action of ['tgp-manual-on', 'tgp-manual-off', 'tgp-ir-on', 'tgp-ir-off']) {
  assert.ok(splitDispatcher.includes("act === '" + action + "'"),
    `${action} is not handled in the split dispatcher and would navigate the pane to about:blank`);
}
assert.ok(splitDispatcher.includes("sendCommand('tgp.manual.set', { on: act === 'tgp-manual-on' })"),
  'split TGT/MAN controls must dispatch tgp.manual.set');
assert.ok(splitDispatcher.includes("sendCommand('tgp.ir.set', { on: act === 'tgp-ir-on' })"),
  'split CLR/IR controls must dispatch tgp.ir.set');

console.log('mfd-split-routing.test.js: OK');
