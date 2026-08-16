// Self-check for split-mode slot coverage. Run: `node split-slots.test.js`.
//
// The invariant this guards: SPLIT_SLOTS[page] must be index-aligned with NAV[page] — entry i
// places NAV[page][i] (renderSplitLabels in mfd.js). A NAV list that grows without a matching
// SPLIT_SLOTS update silently drops items in a split pane; that's exactly the bug the CFG group
// (issue #39) shipped with — NAV.keys grew from 1 item to 4 but SPLIT_SLOTS.keys stayed at 1, so
// KEY/LYT/RTS were unreachable in a split pane until caught by hand in-game. This test exists so
// the next one is caught here instead.
const assert = require('assert');
const { NAV } = require('../nav-model.js');
const { SPLIT_SLOTS, MAP_SPLIT_ORDER, MAP_ROUTE_ACTIONS, mapSplitOrder,
        MAP_FULL_LEFT, MAP_FULL_RIGHT, mapFullRight } = require('./split-slots.js');
const { mainPageSizes, mainPaneSlice, listPaneLayout } = require('./classic-paging.js');

// 'main' and 'map' are documented exceptions (split-slots.js's header comment): both are paginated
// in mfd.js (MAIN_SPLIT_ITEMS / mapNavPaneSlice) rather than a fixed slot table, since MAIN has
// eleven destinations and MAP has ten (issue #38's R+/R- and W+/W-), both past six keys. 'lyt' isn't
// in NAV at all (nav-model.test.js asserts that), so it never reaches this loop — picking it from a
// pane collapses the split instead.
const NO_SPLIT_TABLE = new Set(['main', 'map']);

for (const [page, items] of Object.entries(NAV)) {
  if (NO_SPLIT_TABLE.has(page)) continue;
  const slots = SPLIT_SLOTS[page];
  assert.ok(slots, `NAV.${page} (${items.length} item(s)) has no SPLIT_SLOTS entry at all — it would be unreachable in a split pane`);
  assert.strictEqual(slots.length, items.length,
    `NAV.${page} has ${items.length} item(s) but SPLIT_SLOTS.${page} has ${slots.length} slot(s) — ` +
    `a split pane would silently drop ${items.length - slots.length > 0 ? 'the extra NAV item(s)' : 'nothing, but has unused slot(s)'}`);
}

// The reverse direction: no orphaned SPLIT_SLOTS entry for a page NAV doesn't know about.
for (const page of Object.keys(SPLIT_SLOTS)) {
  assert.ok(page in NAV, `SPLIT_SLOTS.${page} exists but NAV.${page} doesn't — dead entry?`);
}

// Every slot's own shape — same PLACEMENT_KEYS idea as nav-model.test.js, but for slots this IS
// the point: side must be 'left' or 'right', slot a small non-negative integer.
for (const [page, slots] of Object.entries(SPLIT_SLOTS)) {
  slots.forEach((s, i) => {
    const where = `SPLIT_SLOTS.${page}[${i}]`;
    assert.ok(s.side === 'left' || s.side === 'right', `${where}.side must be 'left' or 'right', got ${JSON.stringify(s.side)}`);
    assert.ok(Number.isInteger(s.slot) && s.slot >= 0, `${where}.slot must be a non-negative integer, got ${JSON.stringify(s.slot)}`);
  });
}

// MAP_SPLIT_ORDER's whole reason to exist (issue #38): mfd.js's mapSplitItems reorders NAV.map for
// split pagination so the ROUTE (R+/R-), WYPT (W+/W-) and ZOOM (Z+/Z-) decorator pairs each land on
// the SAME paginated page — placeMapPaneDecorator only draws a decorator when both of its keys are
// visible together. In NAV.map's own full-view order the fixed 5-then-5 page split falls INSIDE the
// R+/R- pair, so the ROUTE decorator could never render at all until this reordering fixed it. The
// checkOrder() helper below pins that down directly for both mapSplitOrder(hasRoute) cases, so a
// future NAV.map or split-slots.js edit that breaks either fails here instead of shipping a
// decorator (or a whole pane's worth of dead keys) nobody will ever see again.
{
  assert.deepStrictEqual(MAP_SPLIT_ORDER.slice().sort(), NAV.map.map(i => i.action).sort(),
    'MAP_SPLIT_ORDER must contain exactly NAV.map\'s actions — a NAV.map item added/removed here has no matching update');

  const byAction = {};
  NAV.map.forEach(item => { byAction[item.action] = item; });

  // Shared by both cases below: given a split-mode action ORDER, where a paginated action lands —
  // both which PAGE (pageOf) and, within that page, which physical { bank, index } key (bankIndexOf,
  // reproducing mfd.js's own positions/cells construction for page='map' in renderSplitLabels) —
  // for every orientation/pane combination, exactly the way a real split pane would place it.
  function checkOrder(order, label) {
    const items = order.map(a => byAction[a]);
    const pageOf = action => {
      for (let p = 0; p < mainPageSizes(items.length).length; p++) {
        if (mainPaneSlice(items, p).items.some(i => i.action === action)) return p;
      }
      throw new Error(`${label}: ${action} not found on any page`);
    };

    function assertSamePagePair(actionA, actionB, word) {
      assert.strictEqual(pageOf(actionA), pageOf(actionB),
        `${label}: ${actionA}/${actionB} must land on the same split-pagination page, or the ${word} decorator can never render`);
    }

    // Landing on the same PAGE isn't the whole story — placeMapPaneDecorator (mfd.js) also requires
    // both keys of a pair to land on the same BANK, adjacent to each other, exactly like
    // placeWpnPaneDecorator's own check. A pair can share a page yet still straddle the left/right
    // column boundary and silently get no decorator — this bit the W+/W- pair (issue #38 follow-up)
    // even though the same-page check alone was already green.
    function assertAdjacentPair(actionA, actionB, word, variant, paneIdx) {
      const L = listPaneLayout(variant, paneIdx, 'map');
      const positions = [L.main, L.items[0], L.items[1], L.items[2], L.items[3], L.next];
      const bankIndexOf = action => {
        const slice = mainPaneSlice(items, pageOf(action));
        const cells = new Array(positions.length).fill(null);
        if (slice.hasPrev) cells[0] = 'map-nav-prev';
        if (slice.hasNext) cells[cells.length - 1] = 'map-nav-next';
        let it = 0;
        for (let p = 0; p < cells.length; p++) {
          if (cells[p] === null && it < slice.items.length) { cells[p] = slice.items[it].action; it++; }
        }
        const i = cells.indexOf(action);
        return i < 0 ? null : positions[i];
      };
      const a = bankIndexOf(actionA), b = bankIndexOf(actionB);
      assert.ok(a && b, `${label}, ${word}: ${actionA}/${actionB} should both be placeable in a ${variant} pane ${paneIdx}`);
      assert.strictEqual(a.bank, b.bank,
        `${label}, ${word}: ${actionA} (${a.bank}${a.index}) and ${actionB} (${b.bank}${b.index}) land on different ` +
        `banks in a ${variant} pane ${paneIdx} — placeMapPaneDecorator would skip the ${word} decorator`);
      assert.strictEqual(Math.abs(a.index - b.index), 1,
        `${label}, ${word}: ${actionA}/${actionB} aren't adjacent (${a.bank}${a.index} vs ${a.bank}${b.index}) in a ` +
        `${variant} pane ${paneIdx} — placeMapPaneDecorator would skip the ${word} decorator`);
    }

    return { assertSamePagePair, assertAdjacentPair };
  }

  // With an active route: all 10 items, all three pairs must survive pagination.
  {
    const { assertSamePagePair, assertAdjacentPair } = checkOrder(mapSplitOrder(true), 'hasRoute');
    assertSamePagePair('rt-next', 'rt-prev', 'ROUTE');
    assertSamePagePair('wpt-next', 'wpt-prev', 'WYPT');
    assertSamePagePair('zin', 'zout', 'ZOOM');
    for (const variant of ['h', 'v', 'vw']) {
      for (const paneIdx of [0, 1]) {
        assertAdjacentPair('rt-next', 'rt-prev', 'ROUTE', variant, paneIdx);
        assertAdjacentPair('wpt-next', 'wpt-prev', 'WYPT', variant, paneIdx);
        assertAdjacentPair('zin', 'zout', 'ZOOM', variant, paneIdx);
      }
    }
  }

  // With no active route: R+/R-/W+/W- filter out entirely (mfd.js's showPage 'map' branch drops
  // them from full view for the same reason) — this pins the resulting 6-item list down exactly, so
  // it stays MAIN/GRID/FLW/Z+/Z- then WPT, the same grouping full view's MAP_FULL_LEFT/RIGHT use,
  // and confirms it fits a split pane's 6-key budget with no pagination at all.
  {
    const order = mapSplitOrder(false);
    assert.deepStrictEqual(order, ['main', 'grid', 'flw', 'zin', 'zout', 'wpt'],
      'mapSplitOrder(false) should be MAIN/GRID/FLW/Z+/Z- then WPT — the route-dependent actions filtered out entirely');
    for (const action of order)
      assert.ok(!MAP_ROUTE_ACTIONS.has(action), `mapSplitOrder(false) should never include route action '${action}'`);

    const { assertSamePagePair, assertAdjacentPair } = checkOrder(order, 'noRoute');
    assertSamePagePair('zin', 'zout', 'ZOOM');
    for (const variant of ['h', 'v', 'vw']) {
      for (const paneIdx of [0, 1]) {
        assertAdjacentPair('zin', 'zout', 'ZOOM', variant, paneIdx);
      }
    }
  }
}

// MAP_FULL_LEFT/RIGHT/mapFullRight's whole reason to live here rather than in mfd.js or f35.js
// (issue #38 follow-up): mfd.js's full view and f35.js's glass each build their own MAP placement
// from these same two lists — before this, they were two hand-written copies with nothing tying
// them together, so one could drift out of sync with the other (e.g. a relabel or reorder applied
// to only one) with no test catching it. Pinning the lists' content down here, plus mapFullRight's
// no-route collapse, is what makes that drift impossible without a test failure.
{
  assert.deepStrictEqual(MAP_FULL_LEFT, ['main', 'grid', 'flw', 'zin', 'zout'],
    'MAP_FULL_LEFT should be MAIN/GRID/FLW/Z+/Z- — mfd.js full view and f35.js both place this column verbatim');
  assert.deepStrictEqual(MAP_FULL_RIGHT, ['wpt', 'rt-next', 'rt-prev', 'wpt-next', 'wpt-prev'],
    'MAP_FULL_RIGHT should be WPT/R+/R-/W+/W- — mfd.js full view and f35.js both place this column (filtered by mapFullRight) verbatim');

  // Together they must name exactly NAV.map's actions — same completeness guarantee MAP_SPLIT_ORDER
  // gets above, so a NAV.map item added/removed here has no matching update either.
  assert.deepStrictEqual(MAP_FULL_LEFT.concat(MAP_FULL_RIGHT).sort(), NAV.map.map(i => i.action).sort(),
    'MAP_FULL_LEFT + MAP_FULL_RIGHT together must contain exactly NAV.map\'s actions');

  assert.deepStrictEqual(mapFullRight(true), MAP_FULL_RIGHT,
    'mapFullRight(true) should be the full WPT/R+/R-/W+/W- column — nothing filtered while a route is active');
  assert.deepStrictEqual(mapFullRight(false), ['wpt'],
    'mapFullRight(false) should collapse to WPT alone — R+/R-/W+/W- are dead keys with no active route');
  for (const action of mapFullRight(false))
    assert.ok(!MAP_ROUTE_ACTIONS.has(action), `mapFullRight(false) should never include route action '${action}'`);
}

console.log('split-slots.test.js: OK');
