// Self-check for doc-cycle. Run: node doc-cycle.test.js
const assert = require('assert');
const { step } = require('./doc-cycle.js');

// Empty folder → nothing to step to.
assert.strictEqual(step([], 'a.png', 1), null);
assert.strictEqual(step(null, 'a.png', 1), null);

// Wraps forward past the last file back to the first.
assert.strictEqual(step(['a.png', 'b.png', 'c.png'], 'c.png', 1), 'a.png');
// Wraps backward past the first file to the last.
assert.strictEqual(step(['a.png', 'b.png', 'c.png'], 'a.png', -1), 'c.png');

// Ordinary step, either direction.
assert.strictEqual(step(['a.png', 'b.png', 'c.png'], 'a.png', 1), 'b.png');
assert.strictEqual(step(['a.png', 'b.png', 'c.png'], 'c.png', -1), 'b.png');

// A single-file folder always steps back to itself.
assert.strictEqual(step(['only.png'], 'only.png', 1), 'only.png');
assert.strictEqual(step(['only.png'], 'only.png', -1), 'only.png');

// currentName no longer in the live listing (deleted mid-session) — lands on the first file.
assert.strictEqual(step(['a.png', 'b.png'], 'deleted.png', 1), 'a.png');
assert.strictEqual(step(['a.png', 'b.png'], 'deleted.png', -1), 'a.png');

console.log('doc-cycle.test.js: OK');
