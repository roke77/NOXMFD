// Node self-check for AFM's paintAfmFailures() DOM-write guard (docs/web-efficiency-audit.md
// finding 05): the incremental update path used to tear down and rebuild the failure labels on
// every single 'afm' tick, even when the failure set hadn't changed. Run:
//   node src/web/pages/afm/afm.test.js
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
    this.style = {};
    this.dataset = {};
    this.children = [];
    this.parentNode = null;
    this.listeners = {};
    this.textContent = '';
  }
  addEventListener(type, cb) { this.listeners[type] = cb; }
  appendChild(child) { child.parentNode = this; this.children.push(child); return child; }
  remove() {
    if (!this.parentNode) return;
    const i = this.parentNode.children.indexOf(this);
    if (i >= 0) this.parentNode.children.splice(i, 1);
    this.parentNode = null;
  }
  getBoundingClientRect() { return { width: 300, height: 200, top: 0, left: 0, bottom: 200 }; }
}

const ids = [
  'afm-panel', 'afm-header', 'afm-name', 'afm-front-section', 'afm-front', 'afm-front-markers',
  'afm-frame', 'afm-bg', 'afm-parts', 'afm-empty',
];
const elements = Object.fromEntries(ids.map((id) => [id, new Element(id)]));
const listeners = {};

global.document = {
  getElementById(id) { return elements[id]; },
  createElement() { return new Element('created'); },
};
global.window = { addEventListener(type, cb) { listeners[type] = cb; } };
// Both layout fetches (own silhouette + front silhouette) resolve to a layoutDef with no `.parts`
// — buildAfmParts/buildAfmFrontMarkers then take their own early-return (see afm.js), which is all
// this guard needs: reaching the message handler's non-forced branch requires afmLayoutType to
// already equal the current aircraft type, and that's the only thing those two functions set here.
global.fetch = function () {
  return Promise.resolve({ ok: true, json: () => Promise.resolve({}) });
};
global.AfmBgPolicy = require('./afm-bg-policy.js');
global.AfmFailurePolicy = require('./afm-failure-policy.js');

require('./afm.js');

assert.ok(listeners.message, 'afm.js should register a message listener');

function send(data) { listeners.message({ data: Object.assign({ mfd: true, type: 'afm' }, data) }); }
const flush = () => new Promise((r) => setImmediate(r));

(async () => {
  const afmPartsEl = elements['afm-parts'];

  const failures1 = ['ENGINE FIRE L'];
  send({ name: 'F-16', failures: failures1 });
  // Let both layout fetches resolve (each a 2-link .then chain) so afmLayoutType catches up to the
  // current aircraft type and the message handler's non-forced branch becomes reachable.
  await flush(); await flush(); await flush(); await flush();
  assert.strictEqual(afmPartsEl.children.length, failures1.length, 'one label built for the one active failure');

  // Same aircraft, identical failure set repeated (a respawn-free tick) — the guard must skip the
  // teardown/rebuild entirely, not just happen to land on the same result. Prove it by counting
  // real DOM rebuilds rather than just re-checking the final label count.
  let rebuilds = 0;
  const realAppend = afmPartsEl.appendChild.bind(afmPartsEl);
  afmPartsEl.appendChild = function (c) { rebuilds++; return realAppend(c); };
  send({ name: 'F-16', failures: failures1.slice() });   // new array, same contents
  assert.strictEqual(rebuilds, 0, 'an unchanged failure set must not rebuild any label');

  // A real change (failure set grows) must still rebuild on the very next tick.
  send({ name: 'F-16', failures: ['ENGINE FIRE L', 'HYD LOW'] });
  assert.strictEqual(rebuilds, 2, 'a real failure-set change rebuilds all current labels');

  console.log('afm.test.js: OK');
})();
