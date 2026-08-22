// Self-check for the AFM silhouette request/retry policy. Run: `node afm-bg-policy.test.js`.
const assert = require('assert');
const { shouldRequestBg, shouldRetryBg } = require('./afm-bg-policy.js');

// ── shouldRequestBg ──────────────────────────────────────────────────────────
assert.strictEqual(shouldRequestBg(null, 'FS-12'), true);
assert.strictEqual(shouldRequestBg('FS-12', 'FS-12'), false);
// Different type, even if its layout is already cached, must re-request.
assert.strictEqual(shouldRequestBg('FS-12', 'Cricket'), true);
assert.strictEqual(shouldRequestBg('FS-12', null), false);

// ── shouldRetryBg ────────────────────────────────────────────────────────────
assert.strictEqual(shouldRetryBg('Cricket', 'Cricket', false, 3, 120), true);
assert.strictEqual(shouldRetryBg('Cricket', 'Cricket', true, 3, 120), false);
// Aircraft changed out from under the retry → stop.
assert.strictEqual(shouldRetryBg('FS-12', 'Cricket', false, 3, 120), false);
assert.strictEqual(shouldRetryBg('Cricket', null, false, 3, 120), false);
assert.strictEqual(shouldRetryBg('Cricket', 'Cricket', false, 120, 120), false);

console.log('afm-bg-policy: all checks passed');
