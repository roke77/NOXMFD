// Self-check for the promise both layouts make. Run: `node layout-coverage.test.js`.
//
// NAV (nav-model.js) is layout-independent, which only pays off if every layout can actually render
// everything in it. F35_PAGES states this outright — "Every NAV action has an entry, so nothing
// renders dimmed" — and nothing enforced it until this file.
//
// The failure is silent and one-sided: add a page, wire it into one layout, and the button is dead
// in the other. Nobody notices until a pilot presses it there. The RDR and AFM entries carry
// "mirrors the bezel's FRAME_PAGES.rdr" comments precisely because that sync is done by hand.
//
// Scope: DESTINATIONS — the NAV actions that name a page. Behaviour actions (flw/grid/zin/zout/
// rng-in/rng-out) are deliberately out: the F-35 routes them through a MAP_ACTIONS table, but the
// bezel dispatches them from an if/else chain with no table to read, so any list of them here would
// be a hand-written copy that can drift from the code it claims to check. Destinations are the
// recurring risk anyway — every new page is one.
const assert = require('assert');
const { NAV } = require('./nav-model.js');
const { CLASSIC, F35 } = require('./layout-pages.js');

// Actions NAV can reach, and the pages they appear on (for a message that says where to look).
const origin = {};
for (const [page, items] of Object.entries(NAV))
  for (const it of items) (origin[it.action] = origin[it.action] || []).push(page);

// Behaviours, not destinations — see the scope note above. Listed explicitly so that a NEW action
// is treated as a destination by default: an unrecognised action fails this test until someone
// consciously classifies it, which is the safe direction to be wrong in.
const BEHAVIOURS = new Set(['flw', 'grid', 'zin', 'zout', 'rng-in', 'rng-out']);

const destinations = Object.keys(origin).filter(a => !BEHAVIOURS.has(a)).sort();
assert.ok(destinations.length > 0, 'no destinations found — NAV or this filter is wrong');

// `null` is a legitimate entry (F-35's MAIN mounts no page), so membership is `in`, not truthiness.
for (const action of destinations) {
  const where = origin[action].join(', ');
  assert.ok(action in CLASSIC,
    `NAV action '${action}' (on ${where}) has no classic entry — the bezel button would be dead`);
  assert.ok(action in F35,
    `NAV action '${action}' (on ${where}) has no F-35 entry — the portal button would be dead`);
}

// Both layouts must agree on the SET of destinations they can reach. A page present in one table
// and absent from the other is the drift this file exists to catch, in either direction.
const onlyClassic = Object.keys(CLASSIC).filter(k => !(k in F35)).sort();
const onlyF35     = Object.keys(F35).filter(k => !(k in CLASSIC)).sort();
assert.deepStrictEqual(onlyClassic, [], `pages the bezel can reach but the F-35 cannot: ${onlyClassic}`);
assert.deepStrictEqual(onlyF35, [], `pages the F-35 can reach but the bezel cannot: ${onlyF35}`);

// Every entry must be usable as a URL — a typo'd empty string mounts nothing and looks like a
// rendering bug rather than a routing one. F-35's MAIN is the one documented null.
for (const [layout, table] of [['classic', CLASSIC], ['f35', F35]]) {
  for (const [page, url] of Object.entries(table)) {
    if (url === null) {
      assert.ok(layout === 'f35' && page === 'main', `${layout}.${page} is null; only f35.main may be`);
      continue;
    }
    assert.ok(typeof url === 'string' && url.startsWith('/'),
      `${layout}.${page} should be a root-relative URL, got ${JSON.stringify(url)}`);
  }
}

console.log(`layout-coverage.test.js: OK (${destinations.length} destinations reachable in both layouts)`);
