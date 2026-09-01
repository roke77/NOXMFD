// Run: `node td-nav.test.js`. Covers buildTgtNavPlan only — the pure core of td-nav.js. apply()'s
// fetch/message + mutation glue is deliberately thin and untested here, same convention
// ext-nav.test.js already uses for its own load()'s fetch glue.
const assert = require('assert');
const { buildTgtNavPlan } = require('./td-nav.js');

// No squad: NAV.tgt stays exactly its static baseline, no TD entry.
{
  const plan = buildTgtNavPlan([{ label: 'MAIN', action: 'main' }], false);
  assert.deepStrictEqual(plan, [{ label: 'MAIN', action: 'main' }]);
}

// In a squad (leader or member — poll() itself only checks role !== 'none'): TD appended after MAIN.
{
  const plan = buildTgtNavPlan([{ label: 'MAIN', action: 'main' }], true);
  assert.deepStrictEqual(plan, [
    { label: 'MAIN', action: 'main' },
    { label: 'TD', action: 'td' },
  ]);
}

// The input array is never mutated — buildTgtNavPlan must be safe to call repeatedly against
// NAV.tgt's live baseline without corrupting it (a future poll rebuilds from the same snapshot).
{
  const base = [{ label: 'MAIN', action: 'main' }];
  buildTgtNavPlan(base, true);
  assert.deepStrictEqual(base, [{ label: 'MAIN', action: 'main' }]);
}

console.log('td-nav.test.js: OK');
