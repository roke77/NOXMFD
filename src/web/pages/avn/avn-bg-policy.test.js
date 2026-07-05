// Self-check for the AVN silhouette request/retry policy. Run: `node avn-bg-policy.test.js`.
// Guards the respawn bug where the silhouette stuck on the previous aircraft.
const assert = require('assert');
const { shouldRequestBg, shouldRetryBg } = require('./avn-bg-policy.js');

// ── shouldRequestBg ──────────────────────────────────────────────────────────
// Fresh page (nothing shown yet) → request the new aircraft.
assert.strictEqual(shouldRequestBg(null, 'FS-12'), true);
// Same aircraft already shown → no redundant request (avoids restart storms at 10 Hz).
assert.strictEqual(shouldRequestBg('FS-12', 'FS-12'), false);
// THE BUG: switching to a DIFFERENT type (whose layout may already be cached) must re-request,
// not leave the silhouette on the old plane.
assert.strictEqual(shouldRequestBg('FS-12', 'Cricket'), true);
// No current aircraft (dead / no mission) → nothing to request.
assert.strictEqual(shouldRequestBg('FS-12', null), false);

// ── shouldRetryBg ────────────────────────────────────────────────────────────
// 404 while still the current plane and not yet loaded → keep retrying (async server capture).
assert.strictEqual(shouldRetryBg('Cricket', 'Cricket', false, 3, 120), true);
// Loaded → stop.
assert.strictEqual(shouldRetryBg('Cricket', 'Cricket', true, 3, 120), false);
// Aircraft changed out from under the retry → stop; don't chase a stale plane.
assert.strictEqual(shouldRetryBg('FS-12', 'Cricket', false, 3, 120), false);
// Nothing requested yet → nothing to retry.
assert.strictEqual(shouldRetryBg('Cricket', null, false, 3, 120), false);
// Safety cap reached → stop.
assert.strictEqual(shouldRetryBg('Cricket', 'Cricket', false, 120, 120), false);

console.log('avn-bg-policy: all checks passed');
