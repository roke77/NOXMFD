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
const { NAV } = require('./shared/nav-model.js');
const { CLASSIC_FULL, CLASSIC_SPLIT, F35 } = require('./shared/layout-pages.js');

// The bezel routes full view and split panes from separate tables, so the same page can be present
// in one and missing from the other — it would work in a split pane and render blank in full view.
// MAIN and MAP are the two documented absences from the full-view table: MAIN is the shell's own
// info-box chrome there and MAP is the always-on base iframe, so neither is ever mounted in
// #page-frame.
const NOT_IN_FULL_VIEW = new Set(['main', 'map']);

// Actions NAV can reach, and the pages they appear on (for a message that says where to look).
const origin = {};
for (const [page, items] of Object.entries(NAV))
  for (const it of items) (origin[it.action] = origin[it.action] || []).push(page);

// Behaviours, not destinations — see the scope note above. Listed explicitly so that a NEW action
// is treated as a destination by default: an unrecognised action fails this test until someone
// consciously classifies it, which is the safe direction to be wrong in.
// 'lyt' (cfg-rates experiment, issue #39): NAV.keys/NAV.rates now offer it as a sibling switch
// alongside KEY/RTS, but it opens the CLASSIC/F-35 chooser overlay (BEZEL_EXTRAS.lyt /
// GLASS_ACTIONS.lyt) rather than naming a page, same as flw/grid/zin/zout below.
// 'rt-next'/'rt-prev' (issue #38): MAP's route switch — a MAP_ACTIONS/mapSend behaviour on the
// active waypoint route, same shape as flw/grid/zin/zout, not a destination.
// 'wpt-next'/'wpt-prev' (issue #38): MAP's manual waypoint step — same shape as rt-next/rt-prev,
// acting on the active route's progress instead of which route is active.
// 'hsd-mode' (docs/rdr-fcr-hsd.md): HSD's own CEN<->DEP toggle, same shape as rng-in/rng-out —
// acts on the page in place, doesn't navigate.
const BEHAVIOURS = new Set(['flw', 'grid', 'zin', 'zout', 'rng-in', 'rng-out', 'hsd-mode', 'lyt',
  'rt-next', 'rt-prev', 'wpt-next', 'wpt-prev']);

const destinations = Object.keys(origin).filter(a => !BEHAVIOURS.has(a)).sort();
assert.ok(destinations.length > 0, 'no destinations found — NAV or this filter is wrong');

// `null` is a legitimate entry (F-35's MAIN mounts no page), so membership is `in`, not truthiness.
for (const action of destinations) {
  const where = origin[action].join(', ');
  assert.ok(action in CLASSIC_SPLIT,
    `NAV action '${action}' (on ${where}) has no bezel split-pane entry — the pane would show about:blank`);
  assert.ok(action in F35,
    `NAV action '${action}' (on ${where}) has no F-35 entry — the portal button would be dead`);
  if (!NOT_IN_FULL_VIEW.has(action))
    assert.ok(action in CLASSIC_FULL,
      `NAV action '${action}' (on ${where}) has no bezel full-view entry — it would work in a split pane and render blank in full view`);
}

// The layouts must agree on the SET of destinations they can reach. A page present in one table and
// absent from another is the drift this file exists to catch, in every direction.
const onlyBezel = Object.keys(CLASSIC_SPLIT).filter(k => !(k in F35)).sort();
const onlyF35   = Object.keys(F35).filter(k => !(k in CLASSIC_SPLIT)).sort();
assert.deepStrictEqual(onlyBezel, [], `pages the bezel can reach but the F-35 cannot: ${onlyBezel}`);
assert.deepStrictEqual(onlyF35, [], `pages the F-35 can reach but the bezel cannot: ${onlyF35}`);

// The bezel's own two tables must agree with each other, modulo the documented full-view absences.
const splitOnly = Object.keys(CLASSIC_SPLIT).filter(k => !(k in CLASSIC_FULL) && !NOT_IN_FULL_VIEW.has(k)).sort();
const fullOnly  = Object.keys(CLASSIC_FULL).filter(k => !(k in CLASSIC_SPLIT)).sort();
assert.deepStrictEqual(splitOnly, [], `bezel pages that work split but render blank in full view: ${splitOnly}`);
assert.deepStrictEqual(fullOnly, [], `bezel pages that work in full view but not in a split pane: ${fullOnly}`);
// Guard the exception list itself: if MAIN/MAP ever do gain a full-view entry, this should be
// re-thought rather than silently tolerated.
for (const k of NOT_IN_FULL_VIEW)
  assert.ok(!(k in CLASSIC_FULL), `'${k}' now has a full-view entry — update NOT_IN_FULL_VIEW and its rationale`);

// Every entry must be usable as a URL — a typo'd empty string mounts nothing and looks like a
// rendering bug rather than a routing one. F-35's MAIN is the one documented null.
for (const [layout, table] of [['classic-full', CLASSIC_FULL], ['classic-split', CLASSIC_SPLIT], ['f35', F35]]) {
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
