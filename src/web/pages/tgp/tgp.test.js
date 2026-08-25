// Node self-check for the TGP page's postMessage contract. Run:
//   node src/web/pages/tgp/tgp.test.js

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
    this.children = [];
    this.textContent = '';
    this.className = '';
    this.clientWidth = 300;
    this.clientHeight = 200;
    this.naturalWidth = 300;
    this.naturalHeight = 200;
    this.listeners = {};
  }
  addEventListener(type, cb) { this.listeners[type] = cb; }
  appendChild(child) { this.children.push(child); }
  replaceChildren(...children) { this.children = children; }
}

const ids = [
  'tgp-panel', 'tgp-img', 'tgp-overlay', 'tgp-ov-type', 'tgp-ov-pilot', 'tgp-ov-rng',
  'tgp-ov-alt', 'tgp-ov-spd', 'tgp-ov-hdg', 'tgp-ov-relalt', 'tgp-ov-relspd',
  'tgp-ov-needle', 'tgp-ov-bearing', 'tgp-ov-grid', 'tgp-ov-mode', 'tgp-ov-mag',
  'tgp-ov-boxes',
];
const elements = Object.fromEntries(ids.map((id) => [id, new Element(id)]));
const listeners = {};

global.document = {
  body: new Element('body'),
  getElementById(id) { return elements[id]; },
  createElement() { return new Element('created'); },
};
global.window = {
  addEventListener(type, cb) { listeners[type] = cb; },
};
global.ResizeObserver = class {
  constructor(cb) { this.cb = cb; }
  observe() { this.cb(); }
};

require('./tgp.js');

assert.ok(listeners.message, 'tgp.js should register a message listener');

listeners.message({ data: { mfd: true, type: 'tgp', active: true, manual: true, quality: 'native' } });
assert.ok(elements['tgp-panel'].classList.contains('has-feed'), 'active feed should set has-feed');
assert.ok(elements['tgp-panel'].classList.contains('tgp-manual'), 'manual feed should set tgp-manual');
assert.ok(!elements['tgp-panel'].classList.contains('show-overlay'), 'native quality should not show HQ overlay');

listeners.message({ data: { mfd: true, type: 'tgp', active: false, manual: false, quality: 'hq', data: null } });
assert.ok(!elements['tgp-panel'].classList.contains('has-feed'), 'inactive feed should clear has-feed');
assert.ok(!elements['tgp-panel'].classList.contains('tgp-manual'), 'manual:false should clear tgp-manual');

listeners.message({ data: { mfd: true, type: 'orient', orientation: 'portrait' } });
assert.ok(document.body.classList.contains('portrait'), 'portrait orientation should be reflected on body');
assert.ok(!document.body.classList.contains('landscape'), 'portrait orientation should clear landscape');

console.log('tgp.test.js: OK');
