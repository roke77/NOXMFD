// Self-check for faction-funds formatting. Run: `node bdf-funds.test.js`.
//
// Worth pinning because the bands are easy to misread and impossible to eyeball: they compare
// SQUARES instead of using Math.abs, so `m*m < 100` means "|m| under ten million", and every band
// silently also covers the negative side. Get one threshold wrong and the MD shows "$0.5m" where
// the game shows "$500.0k" — plausible-looking, consistently wrong, and only visible if you happen
// to have the in-game panel open beside it.
//
// Input is in MILLIONS (docs/bdf-page.md).
const assert = require('assert');
const { fmtFunds } = require('./bdf-funds.js');

const eq = (m, want, why) => assert.strictEqual(fmtFunds(m), want, `${why} (input ${m})`);

// ── Band 1: under 10k raw, printed as plain rounded units ────────────────────────────────
eq(0, '$0', 'zero should be plain');
eq(0.000001, '$1', 'one dollar');
eq(0.0012345, '$1235', 'sub-10k rounds to whole units');
eq(0.009999, '$9999', 'just under the 10k boundary stays plain');

// ── Band 2: under 1m, printed in thousands ───────────────────────────────────────────────
eq(0.01, '$10.0k', 'exactly 10k crosses into the k band');
eq(0.5, '$500.0k', 'half a million reads as thousands');
eq(0.999, '$999.0k', 'just under a million stays in k');

// ── Band 3: under 10m, two decimals ──────────────────────────────────────────────────────
eq(1, '$1.00m', 'one million crosses into m with two decimals');
eq(9.99, '$9.99m', 'just under ten million keeps two decimals');

// ── Band 4: under 1000m, one decimal ─────────────────────────────────────────────────────
eq(10, '$10.0m', 'ten million drops to one decimal');
eq(999.9, '$999.9m', 'just under a billion stays in m');

// ── Band 5: billions ─────────────────────────────────────────────────────────────────────
eq(1000, '$1.00b', 'a thousand million reads as billions');
eq(999999, '$1000.00b', 'just under the trillion boundary is still b');

// ── Band 6: trillions ────────────────────────────────────────────────────────────────────
eq(1e6, '$1.000t', 'a million million reads as trillions');

// ── Negatives take the same band as their magnitude ──────────────────────────────────────
// This is the whole point of the squared comparisons; a faction can go into debt.
eq(-0.5, '$-500.0k', 'negative half-million uses the k band');
eq(-1, '$-1.00m', 'negative million uses the m band');
eq(-1000, '$-1.00b', 'negative billion uses the b band');
eq(-0.000001, '$-1', 'a negative dollar stays in the plain band');

// Sign aside, the band chosen must match the positive twin exactly — a mismatch means one of the
// squared comparisons was rewritten as a plain `<` and quietly lost the negative half.
for (const m of [0.005, 0.5, 5, 50, 5000, 5e6]) {
  const pos = fmtFunds(m).replace('$', ''), neg = fmtFunds(-m).replace('$-', '');
  assert.strictEqual(neg, pos, `negative ${m} should format like its positive twin`);
}

// ── Junk in, "$0" out — never NaN on the panel ───────────────────────────────────────────
for (const bad of [undefined, null, NaN, Infinity, -Infinity, '5', {}, []])
  assert.strictEqual(fmtFunds(bad), '$0', `non-finite input ${JSON.stringify(bad)} should read as $0`);

console.log('bdf-funds.test.js: OK');
