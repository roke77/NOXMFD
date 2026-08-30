// Run: `node ext-nav.test.js`. Covers buildExtNavPlan only — the pure core of ext-nav.js.
// load()'s fetch + mutation glue is deliberately thin and untested here, same convention the
// rest of this codebase uses for fetch/DOM glue (see nav-model.js's own test for the parallel
// "pure logic tested, wiring isn't" split).
const assert = require('assert');
const { buildExtNavPlan } = require('./ext-nav.js');

// No extensions: NAV.ext stays exactly its static baseline, no per-extension entries.
{
  const plan = buildExtNavPlan([{ label: 'MAIN', action: 'main' }], []);
  assert.deepStrictEqual(plan.ext, [{ label: 'MAIN', action: 'main' }]);
  assert.deepStrictEqual(plan.perExtension, {});
}

// One extension: appended after MAIN, plus its own MAIN-only back-link.
{
  const plan = buildExtNavPlan([{ label: 'MAIN', action: 'main' }],
    [{ id: 'example-camera', label: 'CAM' }]);
  assert.deepStrictEqual(plan.ext, [
    { label: 'MAIN', action: 'main' },
    { label: 'CAM', action: 'example-camera' },
  ]);
  assert.deepStrictEqual(plan.perExtension, {
    'example-camera': [{ label: 'MAIN', action: 'main' }],
  });
}

// Multiple extensions: each gets its own back-link entry; NAV.ext lists all of them in
// whatever order they're given (the server sorts by id — this function just preserves it).
{
  const plan = buildExtNavPlan([{ label: 'MAIN', action: 'main' }], [
    { id: 'aaa', label: 'AAA' },
    { id: 'bbb', label: 'BBB' },
  ]);
  assert.deepStrictEqual(plan.ext.map(function (i) { return i.action; }), ['main', 'aaa', 'bbb']);
  assert.ok(plan.perExtension.aaa && plan.perExtension.bbb);
}

// The input array is never mutated — buildExtNavPlan must be safe to call with NAV.ext's live
// baseline without corrupting it if called twice (e.g. a future re-fetch).
{
  const base = [{ label: 'MAIN', action: 'main' }];
  buildExtNavPlan(base, [{ id: 'x', label: 'X' }]);
  assert.deepStrictEqual(base, [{ label: 'MAIN', action: 'main' }]);
}

console.log('ext-nav.test.js: OK');
