// Self-check for the shared TGT/TD range formatter. Run: `node range-format.test.js`.
const assert = require('assert');

(async () => {
  const { fmtRng } = await import('./range-format.js');

  assert.strictEqual(fmtRng(8.44, true), '8,4 km', 'metric uses a European decimal comma');
  assert.strictEqual(fmtRng(8.44, false), '4,6 nm', 'imperial converts km to nm (0.539957/km)');
  assert.strictEqual(fmtRng(null, true), '—', 'a non-number passes through as an em dash');
  assert.strictEqual(fmtRng(NaN, false), '—', 'NaN passes through as an em dash too');

  console.log('range-format.test.js: OK');
})();
