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
  // Fixed rect for the joystick tests below: center (140,140), radius 40.
  getBoundingClientRect() { return { left: 100, top: 100, width: 80, height: 80 }; }
  setPointerCapture() {}
}

const ids = [
  'tgp-panel', 'tgp-img', 'tgp-overlay', 'tgp-ov-type', 'tgp-ov-pilot', 'tgp-ov-rng',
  'tgp-ov-alt', 'tgp-ov-spd', 'tgp-ov-hdg', 'tgp-ov-relalt', 'tgp-ov-relspd',
  'tgp-ov-needle', 'tgp-ov-bearing', 'tgp-ov-grid', 'tgp-ov-mode', 'tgp-ov-mag',
  'tgp-ov-boxes', 'tgp-joystick', 'tgp-joystick-knob',
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

// Fakes for the on-screen joystick's outbound command + keepalive timer, so the drag math below
// runs without a real network call or a real timer left running past this script's exit.
const commandLog = [];
global.sendCommand = function (cmd, args) { commandLog.push({ cmd, args }); return { catch() {} }; };
let activeIntervalFn = null;
global.setInterval = function (fn) { activeIntervalFn = fn; return 1; };
global.clearInterval = function () { activeIntervalFn = null; };

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
// at MID or HIGH resolution, same corner-group elements as the locked-target case but a different
// field mapping (applyManualOverlay). Own-aircraft SPD is hidden, not dashed, matching the
// in-cockpit overlay's own SetActive(false).
listeners.message({ data: { mfd: true, type: 'tgp', active: true, resolution: 'high', quality: 'native', manual: true, data: {
  cnt: 0, manual: true, pointTrack: false, hasDetail: true,
  mag: 4.5, range: 2400, alt: 68, relAlt: -934, clo: '-267km/h', el: -8, brg: 135, grid: 'Kf53', ir: false,
} } });
assert.ok(elements['tgp-panel'].classList.contains('show-overlay'), 'manual data should show the overlay at HIGH resolution');
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

// MID resolution must show the overlay too, not just HIGH — a regression that narrowed the gate to
// resolution === 'high' would otherwise pass every other test in this file (the Point Track/dash
// tests below only ever check field formatting, not show-overlay itself).
listeners.message({ data: { mfd: true, type: 'tgp', active: true, resolution: 'mid', manual: true, data: {
  cnt: 0, manual: true, pointTrack: false, hasDetail: false,
  mag: 1.0, range: 0, alt: 0, relAlt: 0, clo: '-', el: 0, brg: 0, grid: '', ir: false,
} } });
assert.ok(elements['tgp-panel'].classList.contains('show-overlay'), 'manual data should show the overlay at MID resolution too');

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

// On-screen joystick (manual camera pan/tilt) — exercised directly through the registered pointer
// listeners, since this fake DOM has no real event dispatch. Element's fixed rect above puts the
// pad's center at (140,140) with radius 40.
const pad = elements['tgp-joystick'];

pad.listeners.pointerdown({ pointerId: 1, clientX: 140, clientY: 140 });
assert.strictEqual(commandLog.length, 1, 'pointerdown sends one cursor.set');
assert.deepStrictEqual(commandLog[0], { cmd: 'cursor.set', args: { x: 0, y: 0 } }, 'dead center is (0,0)');
assert.ok(activeIntervalFn, 'pointerdown starts the keepalive interval');

pad.listeners.pointermove({ pointerId: 1, clientX: 160, clientY: 140 });
assert.deepStrictEqual(commandLog[1], { cmd: 'cursor.set', args: { x: 0.5, y: 0 } },
  'half-radius right-only drag is (0.5, 0), unclamped — right is positive, matching Keybinds.cs\'s own screen-space convention');

// Diagonal overshoot clamps to the unit CIRCLE, not a unit square: dx=dy=1.5x the radius has
// magnitude 1.5*sqrt(2) before clamping, so both components land at 1/sqrt(2), not 1.
pad.listeners.pointermove({ pointerId: 1, clientX: 200, clientY: 200 });
const overshoot = commandLog[2].args;
assert.ok(Math.abs(overshoot.x - Math.SQRT1_2) < 1e-9 && Math.abs(overshoot.y - Math.SQRT1_2) < 1e-9,
  'diagonal overshoot clamps each axis to 1/sqrt(2), not 1');
assert.ok(Math.abs(Math.hypot(overshoot.x, overshoot.y) - 1) < 1e-9, 'clamped magnitude is exactly 1');

// A second pointer id must not hijack an in-progress drag (one at a time).
pad.listeners.pointerdown({ pointerId: 2, clientX: 100, clientY: 100 });
pad.listeners.pointermove({ pointerId: 2, clientX: 100, clientY: 100 });
assert.strictEqual(commandLog.length, 3, 'a second pointer id while dragging is ignored entirely');

pad.listeners.pointerup({ pointerId: 1, clientX: 200, clientY: 200 });
assert.deepStrictEqual(commandLog[3], { cmd: 'cursor.set', args: { x: 0, y: 0 } }, 'release resets to center');
assert.strictEqual(activeIntervalFn, null, 'release clears the keepalive interval');

// Auto-hide on physical PAD Cursor input — mfd.js/f35.js forward this as {action:'cursor', x, y}
// while TGP is the shell-focused page (docs/tgp-manual-control.md's "On-screen joystick").
listeners.message({ data: { mfd: true, action: 'cursor', x: 0.01, y: 0.01 } });
assert.ok(!elements['tgp-panel'].classList.contains('tgp-joystick-hidden'),
  'a magnitude under the deadzone does not hide the joystick');

listeners.message({ data: { mfd: true, action: 'cursor', x: 0.2, y: 0 } });
assert.ok(elements['tgp-panel'].classList.contains('tgp-joystick-hidden'),
  'nonzero physical cursor input past the deadzone hides the joystick');

listeners.message({ data: { mfd: true, action: 'cursor', x: 0, y: 0 } });
assert.ok(elements['tgp-panel'].classList.contains('tgp-joystick-hidden'),
  'hidden is sticky — it does not come back just because physical input stopped');

elements['tgp-img'].listeners.pointerdown({});
assert.ok(!elements['tgp-panel'].classList.contains('tgp-joystick-hidden'),
  'tapping the picture explicitly reveals it again');

// While the player is actively dragging the joystick itself, its own remote-merged vector rides
// back on the same 'cursor' broadcast — must never hide the control out from under them mid-drag.
pad.listeners.pointerdown({ pointerId: 3, clientX: 160, clientY: 140 });
listeners.message({ data: { mfd: true, action: 'cursor', x: 0.5, y: 0 } });
assert.ok(!elements['tgp-panel'].classList.contains('tgp-joystick-hidden'),
  'a cursor update while locally dragging must not hide the joystick mid-drag');
pad.listeners.pointerup({ pointerId: 3, clientX: 160, clientY: 140 });

listeners.message({ data: { mfd: true, action: 'cursor', x: 0.3, y: 0 } });
assert.ok(elements['tgp-panel'].classList.contains('tgp-joystick-hidden'),
  'physical input after releasing our own drag hides the joystick again');

console.log('tgp.test.js: OK');
