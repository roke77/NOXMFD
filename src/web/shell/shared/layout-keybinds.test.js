// Self-check for SAVE/LOAD LAYOUT's and Layout 1-5's key matching. Run: `node layout-keybinds.test.js`.
//
// matchKey decides which configured key (or none) one pressed key name fires. The failure modes
// worth pinning: an unbound (null) key must never match an unmapped/empty press (that would fire
// an action nobody configured), save/load must stay mutually exclusive even if the same key were
// somehow assigned to both, and a chord bind must win over the same key bound bare.
const assert = require('assert');
const { applyConfig, match, matchKey } = require('./layout-keybinds.js');

applyConfig({ binds: [
  { id: 'layout-save', key: 'S' },
  { id: 'layout-load', key: 'L' },
] });
assert.strictEqual(match({ code: 'KeyS' }), 'save', 'pushed configuration should update save');
assert.strictEqual(match({ code: 'KeyL' }), 'load', 'pushed configuration should update load');

// ── basic matches ─────────────────────────────────────────────────────────────────────────
const SL = { save: 'S', load: 'L' };
assert.strictEqual(matchKey(SL, 'S'), 'save', 'configured save key should match');
assert.strictEqual(matchKey(SL, 'L'), 'load', 'configured load key should match');
assert.strictEqual(matchKey(SL, 'Q'), null, 'an unconfigured key should not match');

// ── unbound (null) keys never match, regardless of the code ─────────────────────────────────
assert.strictEqual(matchKey({ save: null, load: null }, 'S'), null, 'both unbound: nothing should match');
assert.strictEqual(matchKey({ save: null, load: 'L' }, 'S'), null, 'save unbound: KeyS should not fall through to load');
assert.strictEqual(matchKey({ save: 'S', load: null }, 'L'), null, 'load unbound: KeyL should not fall through to save');

// ── an unmappable/empty press never matches, even if a key happens to be configured ─────────
assert.strictEqual(matchKey(SL, ''), null, 'empty name should be refused');
assert.strictEqual(match({ code: 'Escape' }), null, 'unmappable code should be refused');

// ── save checked before load — if a bad config ever assigned the same key to both, save wins
// deterministically rather than the outcome depending on object key order ──────────────────
assert.strictEqual(matchKey({ save: 'S', load: 'S' }, 'S'), 'save', 'save is checked first on a collision');

// ── Layout Preset slots (issue #90) — slot N fires 'slot-N'; unbound slots never match ──────
assert.strictEqual(matchKey({ save: 'S', load: 'L', slots: [null, 'F2', null] }, 'F2'), 'slot-2', 'slot key should match its slot');
assert.strictEqual(matchKey({ save: 'S', load: 'L', slots: [null, 'F2'] }, 'F1'), null, 'unbound slot should not match');
applyConfig({ binds: [
  { id: 'layout-save', key: 'S' },
  { id: 'layout-load', key: 'L' },
  { id: 'layout-preset-3', key: 'F3', joyButton: -1, joyNum: 0 },
] });
assert.strictEqual(match({ code: 'F3' }), 'slot-3', 'pushed configuration should update slot keys');

// ── modifier chords: the chord is tried first, then the bare key ────────────────────────────
applyConfig({ binds: [
  { id: 'layout-save', key: 'S' },
  { id: 'layout-load', key: 'LeftAlt+S' },
] });
assert.strictEqual(match({ code: 'KeyS', altKey: true }), 'load', 'Alt+S bound: the chord wins over bare S');
assert.strictEqual(match({ code: 'KeyS' }), 'save', 'bare S still fires without Alt');
assert.strictEqual(match({ code: 'KeyS', shiftKey: true }), 'save', 'unclaimed Shift+S falls back to bare S');
assert.strictEqual(match({ code: 'KeyS', ctrlKey: true }), null, "unclaimed Ctrl+S is the browser's, not bare S");

console.log('layout-keybinds.test.js: OK');
