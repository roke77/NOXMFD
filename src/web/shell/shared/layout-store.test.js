// Self-check for layout-store.js's list(): a healthy read passes through, an unreachable or failing
// plugin resolves {layouts: [], failed: true} so callers don't mistake it for an empty library.
// Run: `node layout-store.test.js`.
const assert = require('assert');

(async () => {
  const LayoutStore = require('./layout-store.js');
  const reply = (ok, body) => () => Promise.resolve({ ok: ok, status: ok ? 200 : 500, json: () => Promise.resolve(body) });

  global.fetch = reply(true, { layouts: [{ id: 'a' }] });
  assert.deepStrictEqual(await LayoutStore.list(), { layouts: [{ id: 'a' }] });

  global.fetch = () => Promise.reject(new Error('down'));
  assert.deepStrictEqual(await LayoutStore.list(), { layouts: [], failed: true });

  global.fetch = reply(false, { layouts: [{ id: 'stale' }] });   // an error status is not a library
  assert.deepStrictEqual(await LayoutStore.list(), { layouts: [], failed: true });

  global.fetch = () => Promise.resolve({ ok: true, json: () => Promise.reject(new SyntaxError('bad json')) });
  assert.strictEqual((await LayoutStore.list()).failed, true);

  // warn() logs and does not throw
  const seen = [];
  const log = console.warn; console.warn = (...a) => seen.push(a.join(' '));
  LayoutStore.warn('save')(new Error('x'));
  console.warn = log;
  assert.ok(/\[layout\] save failed/.test(seen[0]));

  console.log('layout-store.test.js: OK');
})();
