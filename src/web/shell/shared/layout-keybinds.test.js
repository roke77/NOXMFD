// Self-check for SAVE/LOAD LAYOUT's key matching. Run: `node layout-keybinds.test.js`.
//
// matchKey decides which of two configured keys (or neither) a keypress fires. The two failure
// modes worth pinning: an unbound (null) key must never match an unmapped/empty code (that would
// fire an action nobody configured), and save/load must stay mutually exclusive even if the same
// key were somehow assigned to both.
const assert = require('assert');
const { applyConfig, match, matchKey } = require('./layout-keybinds.js');

applyConfig({ binds: [
  { id: 'layout-save', key: 'S' },
  { id: 'layout-load', key: 'L' },
] });
assert.strictEqual(match({ code: 'KeyS' }), 'save', 'pushed configuration should update save');
assert.strictEqual(match({ code: 'KeyL' }), 'load', 'pushed configuration should update load');

// ── basic matches ─────────────────────────────────────────────────────────────────────────
assert.strictEqual(matchKey('S', 'L', 'KeyS'), 'save', 'configured save key should match');
assert.strictEqual(matchKey('S', 'L', 'KeyL'), 'load', 'configured load key should match');
assert.strictEqual(matchKey('S', 'L', 'KeyQ'), null, 'an unconfigured key should not match');

// ── unbound (null) keys never match, regardless of the code ─────────────────────────────────
assert.strictEqual(matchKey(null, null, 'KeyS'), null, 'both unbound: nothing should match');
assert.strictEqual(matchKey(null, 'L', 'KeyS'), null, 'save unbound: KeyS should not fall through to load');
assert.strictEqual(matchKey('S', null, 'KeyL'), null, 'load unbound: KeyL should not fall through to save');

// ── an unmappable/empty code never matches, even if a key happens to be configured ──────────
assert.strictEqual(matchKey('S', 'L', ''), null, 'empty code should be refused');
assert.strictEqual(matchKey('S', 'L', 'Escape'), null, 'unmappable code should be refused');

// ── save checked before load — if a bad config ever assigned the same key to both, save wins
// deterministically rather than the outcome depending on object key order ──────────────────
assert.strictEqual(matchKey('S', 'S', 'KeyS'), 'save', 'save is checked first on a collision');

console.log('layout-keybinds.test.js: OK');
