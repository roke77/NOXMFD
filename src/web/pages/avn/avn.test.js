// Node self-check for AVN's paintAvnStatus() DOM-write guard (docs/web-efficiency-audit.md finding
// 06): 8 status booleans get rewritten every 'avn' tick regardless, unless the guard skips an
// unchanged set. Run: node src/web/pages/avn/avn.test.js
const assert = require('assert');

class ClassList {
  constructor() { this.values = new Set(); }
  add(name) { this.values.add(name); }
  remove(name) { this.values.delete(name); }
  toggle(name, on) { on ? this.add(name) : this.remove(name); }
  contains(name) { return this.values.has(name); }
}

// Generic leaf node for querySelector results (gauge needle/fill/val/ab-path) — every caller here
// only ever touches .style, .textContent, .setAttribute, .getTotalLength.
function makeLeaf() {
  return { style: {}, textContent: '', setAttribute() {}, getTotalLength: () => 100 };
}

class Element {
  constructor(id) {
    this.id = id;
    this.classList = new ClassList();
    this.style = {};
    this.listeners = {};
    this._subs = Object.create(null);
  }
  addEventListener(type, cb) { this.listeners[type] = cb; }
  getBoundingClientRect() { return { width: 300, height: 200, top: 0, left: 0, bottom: 200 }; }
  querySelector(sel) { return this._subs[sel] || (this._subs[sel] = makeLeaf()); }
}

const ids = [
  'avn-panel', 'avn-empty', 'avn-content', 'avn-gauge-fuel', 'avn-gauge-rpm', 'avn-gauge-heat',
  'avn-gauge-thr', 'avn-tile-gear', 'avn-tile-radar', 'avn-tile-guns', 'avn-tile-eng',
  'avn-tile-assist', 'avn-tile-nvg', 'avn-tile-lights', 'avn-tile-turret',
];
const elements = Object.fromEntries(ids.map((id) => [id, new Element(id)]));
const bodyClassList = new ClassList();
const listeners = {};

global.document = {
  body: { classList: bodyClassList },
  getElementById(id) { return elements[id]; },
};
global.window = { addEventListener(type, cb) { listeners[type] = cb; } };
global.location = { search: '' };
global.sendCommand = function () { return { catch() {} }; };
// Real policy modules (already covered by their own *-policy.test.js) — pulled in as globals the
// same way avn.html loads them as classic scripts before avn.js.
global.AvnStatusPolicy = require('./avn-status-policy.js');
global.AvnThrottlePolicy = require('./avn-throttle-policy.js');

require('./avn.js');

assert.ok(listeners.message, 'avn.js should register a message listener');

function send(data) { listeners.message({ data: Object.assign({ mfd: true, type: 'avn' }, data) }); }

const gearTile = elements['avn-tile-gear'];
const radarTile = elements['avn-tile-radar'];

// First message for a given aircraft always goes through the full renderAvn() path (avnLastType
// starts null), which calls paintAvnStatus unconditionally — establishes the baseline.
send({ name: 'F-16', gearDown: true, radar: false });
assert.ok(gearTile.classList.contains('gear-down'), 'gear tile reflects gearDown=true');
assert.ok(radarTile.classList.contains('off'), 'radar tile reflects radar=false');

// Same aircraft, identical status booleans repeated (only a continuously-varying field like fuel
// would normally differ tick-to-tick) — the guard must skip the rewrite, not just leave the class
// unchanged by coincidence. Prove the skip by spying on the SAME tile instance avn.js already holds
// a reference to (reassigning the elements map wouldn't reach it — avn.js captured its DOM refs as
// module-load consts before this test ever runs).
let touched = false;
const realAdd = gearTile.classList.add.bind(gearTile.classList);
const realRemove = gearTile.classList.remove.bind(gearTile.classList);
gearTile.classList.add = function (n) { touched = true; return realAdd(n); };
gearTile.classList.remove = function (n) { touched = true; return realRemove(n); };
send({ name: 'F-16', fuel: 0.5, gearDown: true, radar: false });
assert.strictEqual(touched, false, 'an unchanged status set must not rewrite any tile class');
gearTile.classList.add = realAdd;
gearTile.classList.remove = realRemove;

// A real status change (radar comes on) must still render on the very next tick.
send({ name: 'F-16', gearDown: true, radar: true });
assert.ok(radarTile.classList.contains('on'), 'a real status change still repaints the affected tile');

console.log('avn.test.js: OK');
