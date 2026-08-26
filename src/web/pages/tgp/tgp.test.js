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
assert.ok(!elements['tgp-panel'].classList.contains('show-overlay'), 'no data payload should not show the overlay');

// Manual mode in NATIVE quality must NOT draw the client-side overlay: Native's captured video
// already bakes this in (TgpNativeOverlay populates the same TargetScreenUI fields the video
// capture reads, including its own crosshair) — drawing it again here double-shows everything.
const nativeManualData = {
  cnt: 0, manual: true, pointTrack: false, hasDetail: true,
  mag: 4.5, range: 2400, alt: 68, relAlt: -934, clo: '-267km/h', el: -8, brg: 135, grid: 'Kf53', ir: false,
};
listeners.message({ data: { mfd: true, type: 'tgp', active: true, quality: 'native', manual: true, data: nativeManualData } });
assert.ok(!elements['tgp-panel'].classList.contains('show-overlay'), 'manual data must NOT show the client overlay in native quality (already baked into the video)');
assert.ok(!elements['tgp-panel'].classList.contains('tgp-point-track'), 'no client overlay in native quality means no client Point Track box either');

// Manual-mode overlay data (docs/tgp-manual-control.md's "In-cockpit overlay" / web parity) draws
// in HQ quality, same corner-group elements as the locked-target case but a different field
// mapping (applyManualOverlay). Own-aircraft SPD is hidden, not dashed, matching the in-cockpit
// overlay's own SetActive(false).
listeners.message({ data: { mfd: true, type: 'tgp', active: true, quality: 'hq', manual: true, data: {
  cnt: 0, manual: true, pointTrack: false, hasDetail: true,
  mag: 4.5, range: 2400, alt: 68, relAlt: -934, clo: '-267km/h', el: -8, brg: 135, grid: 'Kf53', ir: false,
} } });
assert.ok(elements['tgp-panel'].classList.contains('show-overlay'), 'manual data should show the overlay in HQ quality');
assert.ok(!elements['tgp-panel'].classList.contains('tgp-point-track'), 'pointTrack:false should not set tgp-point-track');
assert.strictEqual(elements['tgp-ov-type'].textContent, 'MANUAL', 'manual type label');
assert.strictEqual(elements['tgp-ov-pilot'].textContent, '', 'manual mode has no pilot');
assert.ok(elements['tgp-ov-spd'].classList.contains('tgp-ov-hidden'), 'manual mode hides own-aircraft SPD');
assert.strictEqual(elements['tgp-ov-rng'].textContent, 'RNG 2.4km', 'manual range formatting');
assert.strictEqual(elements['tgp-ov-hdg'].textContent, 'EL -8°', 'manual mode shows elevation in the HDG slot');
// clo arrives pre-formatted (server-side UnitConverter.SpeedReading) — the client renders it
// verbatim rather than re-deriving units from a raw m/s number, so it can't drift from the
// in-cockpit overlay's own units (km/h or kt, whichever the player has set).
assert.strictEqual(elements['tgp-ov-relspd'].textContent, 'CLO -267km/h', 'manual mode shows closure rate, not target closing speed');
assert.strictEqual(elements['tgp-ov-grid'].textContent, 'GRID: Kf53', 'manual grid');
assert.strictEqual(elements['tgp-ov-mag'].textContent, 'Mag x4.5', 'manual magnification');

// Point Track locked — label and box both flip.
listeners.message({ data: { mfd: true, type: 'tgp', active: true, quality: 'hq', manual: true, data: {
  cnt: 0, manual: true, pointTrack: true, hasDetail: true,
  mag: 4.5, range: 2400, alt: 68, relAlt: -934, clo: '-267km/h', el: -8, brg: 135, grid: 'Kf53', ir: false,
} } });
assert.strictEqual(elements['tgp-ov-type'].textContent, 'POINT TRACK', 'point track type label');
assert.ok(elements['tgp-panel'].classList.contains('tgp-point-track'), 'pointTrack:true should set tgp-point-track');

// No raycast hit — dashes, not stale numbers from the previous update.
listeners.message({ data: { mfd: true, type: 'tgp', active: true, quality: 'hq', manual: true, data: {
  cnt: 0, manual: true, pointTrack: false, hasDetail: false,
  mag: 0.5, range: 0, alt: 0, relAlt: 0, clo: '-', el: 12, brg: -30, grid: '', ir: false,
} } });
assert.strictEqual(elements['tgp-ov-rng'].textContent, 'RNG -', 'no-hit range shows a dash');
assert.strictEqual(elements['tgp-ov-grid'].textContent, 'GRID: -', 'no-hit grid shows a dash');
assert.strictEqual(elements['tgp-ov-relspd'].textContent, 'CLO -', 'no-hit closure shows a dash');

listeners.message({ data: { mfd: true, type: 'tgp', active: false, manual: false, quality: 'hq', data: null } });
assert.ok(!elements['tgp-panel'].classList.contains('has-feed'), 'inactive feed should clear has-feed');
assert.ok(!elements['tgp-panel'].classList.contains('tgp-manual'), 'manual:false should clear tgp-manual');
assert.ok(!elements['tgp-panel'].classList.contains('tgp-point-track'), 'manual:false should clear tgp-point-track too');

listeners.message({ data: { mfd: true, type: 'orient', orientation: 'portrait' } });
assert.ok(document.body.classList.contains('portrait'), 'portrait orientation should be reflected on body');
assert.ok(!document.body.classList.contains('landscape'), 'portrait orientation should clear landscape');

console.log('tgp.test.js: OK');
