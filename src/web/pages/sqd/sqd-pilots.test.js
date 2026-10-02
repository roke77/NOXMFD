// Self-check for SQD's duplicate-callsign helpers. Run: `node sqd-pilots.test.js`.
const assert = require('assert');

(async () => {
  const { sharerIds, sharedWithText } = await import('./sqd-pilots.js');
  const pilots = {
    a: { d: 'COLT 1-1', dup: true },
    b: { d: 'colt 1-1', dup: true },
    c: { d: 'COLT 1-1', dup: true },
    d: { d: 'COLT 1-2', dup: false },
  };

  assert.deepStrictEqual(sharerIds(pilots, 'a'), ['b', 'c'], 'others with the same callsign, case-insensitive');
  assert.deepStrictEqual(sharerIds(pilots, 'd'), [], 'a unique callsign has no sharers');
  assert.deepStrictEqual(sharerIds(pilots, 'zz'), [], 'an unknown pilot has none');
  assert.deepStrictEqual(sharerIds(null, 'a'), [], 'no pilots map yet');

  assert.strictEqual(sharedWithText(['Widow']), 'WIDOW');
  assert.strictEqual(sharedWithText(['Widow', 'Reaper']), 'WIDOW, REAPER');
  assert.strictEqual(sharedWithText(['Widow', 'Reaper', 'Ghost', 'Havoc']), 'WIDOW, REAPER +2');
  assert.strictEqual(sharedWithText([]), '');

  console.log('sqd-pilots.test.js: OK');
})().catch(function (e) { console.error(e); process.exit(1); });
