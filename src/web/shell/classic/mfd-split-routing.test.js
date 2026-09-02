// mfd.js is a browser composition root with extensive DOM setup, so this follows
// server-route-coverage.test.js's source-scan approach. Every split-pane behavior must be handled
// before destination routing, and destination routing must preserve the pane on an unknown id.
const assert = require('assert');
const fs = require('fs');
const path = require('path');
const { NAV } = require('../shared/nav-model.js');
const { CLASSIC_SPLIT } = require('../shared/layout-pages.js');

const source = fs.readFileSync(path.join(__dirname, 'mfd.js'), 'utf8');
const splitStart = source.indexOf('if (splitMode && el.dataset.pane && el.dataset.action)');
const splitEnd = source.indexOf('\n  switch (el.dataset.action)', splitStart);

assert.ok(splitStart >= 0 && splitEnd > splitStart, 'could not isolate mfdButton split dispatcher');
const splitDispatcher = source.slice(splitStart, splitEnd);

const staticBehaviors = [...new Set(Object.values(NAV).flat()
  .map(item => item.action)
  .filter(action => !(action in CLASSIC_SPLIT)))];
const dynamicBehaviors = [
  'wpn-prev', 'wpn-next',
  'avn-prev', 'avn-next',
  'main-prev', 'main-next',
  'map-nav-prev', 'map-nav-next',
  'weapon.select',
  'master-arms-on', 'master-arms-off',
  'combat-mode-aa', 'combat-mode-ag',
  'avn.toggle',
  'tgp-manual-on', 'tgp-manual-off',
  'tgp-ir-on', 'tgp-ir-off',
  'tgp-nav-prev', 'tgp-nav-next',
  'tgp-zoom-in', 'tgp-zoom-out',
  'tgp-mark-steerpoint',
  'tgp-point-track', 'tgp-manual-reset',
];

for (const action of [...staticBehaviors, ...dynamicBehaviors]) {
  assert.ok(splitDispatcher.includes("act === '" + action + "'"),
    `${action} is not handled in the split dispatcher and would not perform its behavior`);
}
assert.ok(splitDispatcher.includes("sendCommand('tgp.manual.set', { on: act === 'tgp-manual-on' })"),
  'split TGT/MAN controls must dispatch tgp.manual.set');
assert.ok(splitDispatcher.includes("sendCommand('tgp.ir.set', { on: act === 'tgp-ir-on' })"),
  'split CLR/IR controls must dispatch tgp.ir.set');

const navigateStart = source.indexOf('function paneNavigate(paneIdx, page)');
const navigateEnd = source.indexOf('// Forwarding from shell', navigateStart);
assert.ok(navigateStart >= 0 && navigateEnd > navigateStart, 'could not isolate paneNavigate');
const paneNavigate = source.slice(navigateStart, navigateEnd);
const guardIndex = paneNavigate.indexOf("if (url === 'about:blank')");
const mutationIndex = paneNavigate.indexOf('panePages[paneIdx] = page');

assert.ok(paneNavigate.includes('const url = paneUrl(page)'),
  'paneNavigate must resolve the destination before changing pane state');
assert.ok(guardIndex >= 0 && guardIndex < mutationIndex,
  'paneNavigate must reject unknown pages before changing pane state');
assert.ok(paneNavigate.includes('paneIframes[paneIdx].src = url'),
  'paneNavigate must use the destination validated by its unknown-page guard');

console.log('mfd-split-routing.test.js: OK');
