// Self-check for split-mode slot coverage. Run: `node split-slots.test.js`.
//
// The invariant this guards: SPLIT_SLOTS[page] must be index-aligned with NAV[page] — entry i
// places NAV[page][i] (renderSplitLabels in mfd.js). A NAV list that grows without a matching
// SPLIT_SLOTS update silently drops items in a split pane; that's exactly the bug the CFG group
// (issue #39) shipped with — NAV.keys grew from 1 item to 4 but SPLIT_SLOTS.keys stayed at 1, so
// KEY/LYT/RTS were unreachable in a split pane until caught by hand in-game. This test exists so
// the next one is caught here instead.
const assert = require('assert');
const { NAV } = require('../shared/nav-model.js');
const { SPLIT_SLOTS, MAP_SPLIT_ORDER, MAP_SPLIT_ORDER_V, MAP_ROUTE_ACTIONS, MAP_WAYPOINT_ACTIONS, mapSplitOrder,
        MAP_FULL_LEFT, MAP_FULL_RIGHT, mapFullRight } = require('./split-slots.js');
const { mainPageSizes, mainPaneSlice, listPaneLayout } = require('./classic-paging.js');

// 'main' and 'map' are documented exceptions (split-slots.js's header comment): both are paginated
// in mfd.js (MAIN_SPLIT_ITEMS / mapNavPaneSlice) rather than a fixed slot table, since MAIN has
// eleven destinations and MAP has ten (issue #38's R+/R- and W+/W-), both past six keys. 'lyt' isn't
// in NAV at all (nav-model.test.js asserts that), so it never reaches this loop — picking it from a
// pane collapses the split instead.
const NO_SPLIT_TABLE = new Set(['main', 'map']);

// 'tgt' is a documented exception too: td-nav.js appends a live TD entry to NAV.tgt at runtime
// once a squad exists, so the static NAV.tgt here (nav-model.js) undercounts its real length —
// SPLIT_SLOTS.tgt must declare capacity for the runtime-grown case, not the static baseline.
const DYNAMIC_GROWTH = { tgt: 2 };

for (const [page, items] of Object.entries(NAV)) {
  if (NO_SPLIT_TABLE.has(page)) continue;
  const slots = SPLIT_SLOTS[page];
  const expectedLen = DYNAMIC_GROWTH[page] || items.length;
  assert.ok(slots, `NAV.${page} (${items.length} item(s)) has no SPLIT_SLOTS entry at all — it would be unreachable in a split pane`);
  assert.strictEqual(slots.length, expectedLen,
    `NAV.${page} has ${items.length} item(s) (expected slot capacity ${expectedLen}) but SPLIT_SLOTS.${page} has ${slots.length} slot(s) — ` +
    `a split pane would silently drop ${expectedLen - slots.length > 0 ? 'the extra NAV item(s)' : 'nothing, but has unused slot(s)'}`);
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

// MAP_SPLIT_ORDER/MAP_SPLIT_ORDER_V's whole reason to exist (issue #38, WPT-leads-in-v-split
// follow-up): mfd.js's mapSplitItems reorders NAV.map for split pagination so the ROUTE (R+/R-),
// WYPT (W+/W-) and ZOOM (Z+/Z-) decorator pairs each land on the SAME paginated page —
// placeMapPaneDecorator only draws a decorator when both of its keys are visible together. In
// NAV.map's own full-view order the fixed 5-then-5 page split falls INSIDE the R+/R- pair, so the
// ROUTE decorator could never render at all until this reordering fixed it. An 'h' split additionally
// splits page 2's items across a left/right BANK boundary (2 slots then 3), which a pair must not
// straddle — 'v'/'vw' have no such boundary (one column), so WPT can lead there instead. The
// checkOrder() helper below pins both orders down directly for every mapSplitOrder(variant,
// hasRoutes, hasActiveRoute) case, so a future NAV.map or split-slots.js edit that breaks any of
// them fails here instead of shipping a decorator (or a whole pane's worth of dead keys) nobody
// will ever see again.
{
  assert.deepStrictEqual(MAP_SPLIT_ORDER.slice().sort(), NAV.map.map(i => i.action).sort(),
    'MAP_SPLIT_ORDER must contain exactly NAV.map\'s actions — a NAV.map item added/removed here has no matching update');
  assert.deepStrictEqual(MAP_SPLIT_ORDER_V.slice().sort(), NAV.map.map(i => i.action).sort(),
    'MAP_SPLIT_ORDER_V must contain exactly NAV.map\'s actions — a NAV.map item added/removed here has no matching update');
  assert.deepStrictEqual(MAP_SPLIT_ORDER_V, ['main', 'grid', 'flw', 'zin', 'zout', 'wpt', 'mapcfg', 'rt-next', 'rt-prev', 'wpt-next', 'wpt-prev'],
    'MAP_SPLIT_ORDER_V should lead page 2 with WPT ahead of CFG/R+/R-/W+/W- (v/vw split has no bank boundary to straddle)');

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

  // mapSplitOrder is variant-aware now (WPT-leads-in-v-split follow-up): 'h' gets MAP_SPLIT_ORDER
  // (bank-safe), 'v'/'vw'/'vwr' (V_SPLIT, V_WIDE_SPLIT_L, V_WIDE_SPLIT_R) all get
  // MAP_SPLIT_ORDER_V (WPT-first) — checked against their own real pane layouts only, since
  // MAP_SPLIT_ORDER_V would legitimately fail 'h''s bank-adjacency check.
  const VARIANT_GROUPS = [['h'], ['v', 'vw', 'vwr']];

  // With an active route (routes exist AND one is active): all 10 items, all three pairs must
  // survive pagination, in both variant groups.
  for (const variants of VARIANT_GROUPS) {
    const { assertSamePagePair, assertAdjacentPair } = checkOrder(mapSplitOrder(variants[0], true, true), `hasActiveRoute(${variants[0]})`);
    assertSamePagePair('rt-next', 'rt-prev', 'ROUTE');
    assertSamePagePair('wpt-next', 'wpt-prev', 'WYPT');
    assertSamePagePair('zin', 'zout', 'ZOOM');
    for (const variant of variants) {
      for (const paneIdx of [0, 1]) {
        assertAdjacentPair('rt-next', 'rt-prev', 'ROUTE', variant, paneIdx);
        assertAdjacentPair('wpt-next', 'wpt-prev', 'WYPT', variant, paneIdx);
        assertAdjacentPair('zin', 'zout', 'ZOOM', variant, paneIdx);
      }
    }
  }

  // Routes exist but none is active: R+/R- stay (there's something to cycle INTO), W+/W- filter out
  // (nothing active to step) — the deactivate-follow-up case, in both variant groups.
  {
    const orderH = mapSplitOrder('h', true, false);
    assert.deepStrictEqual(orderH, ['main', 'grid', 'flw', 'zin', 'zout', 'rt-next', 'rt-prev', 'mapcfg', 'wpt'],
      "mapSplitOrder('h', true, false) should keep R+/R- but drop W+/W- — a route is saved but none is active");
    const orderV = mapSplitOrder('v', true, false);
    assert.deepStrictEqual(orderV, ['main', 'grid', 'flw', 'zin', 'zout', 'wpt', 'mapcfg', 'rt-next', 'rt-prev'],
      "mapSplitOrder('v', true, false) should lead with WPT, keep R+/R-, drop W+/W-");
    for (const [order, variants] of [[orderH, ['h']], [orderV, ['v', 'vw', 'vwr']]]) {
      for (const action of order)
        assert.ok(!MAP_WAYPOINT_ACTIONS.has(action), `mapSplitOrder(..., true, false) should never include waypoint action '${action}'`);
      const { assertSamePagePair, assertAdjacentPair } = checkOrder(order, `hasRoutesNoneActive(${variants[0]})`);
      assertSamePagePair('rt-next', 'rt-prev', 'ROUTE');
      assertSamePagePair('zin', 'zout', 'ZOOM');
      for (const variant of variants) {
        for (const paneIdx of [0, 1]) {
          assertAdjacentPair('rt-next', 'rt-prev', 'ROUTE', variant, paneIdx);
          assertAdjacentPair('zin', 'zout', 'ZOOM', variant, paneIdx);
        }
      }
    }
  }

  // No routes saved at all: R+/R-/W+/W- both filter out entirely (mfd.js's showPage 'map' branch
  // drops them from full view for the same reason) — this pins the resulting 7-item list down
  // exactly: MAIN/GRID/FLW/Z+/Z- then CFG/WPT, in whichever order each variant leads with (H
  // trails CFG then WPT since CFG sat ahead of WPT in MAP_SPLIT_ORDER's page 2; V leads WPT then
  // CFG per MAP_SPLIT_ORDER_V's "WPT leads" design). Unlike before mapcfg existed, 7 items no
  // longer fits a split pane's 6-key budget in one page — this now spills a lone item onto a
  // second (PREV-only) page, same as any other list that crosses the boundary.
  for (const variants of VARIANT_GROUPS) {
    const order = mapSplitOrder(variants[0], false, false);
    const expected = variants[0] === 'h'
      ? ['main', 'grid', 'flw', 'zin', 'zout', 'mapcfg', 'wpt']
      : ['main', 'grid', 'flw', 'zin', 'zout', 'wpt', 'mapcfg'];
    assert.deepStrictEqual(order, expected,
      `mapSplitOrder('${variants[0]}', false, false) should be MAIN/GRID/FLW/Z+/Z- then CFG/WPT (order per variant) — every route-dependent action filtered out`);
    for (const action of order) {
      assert.ok(!MAP_ROUTE_ACTIONS.has(action), `mapSplitOrder(false, false) should never include route action '${action}'`);
      assert.ok(!MAP_WAYPOINT_ACTIONS.has(action), `mapSplitOrder(false, false) should never include waypoint action '${action}'`);
    }

    const { assertSamePagePair, assertAdjacentPair } = checkOrder(order, `noRoutes(${variants[0]})`);
    assertSamePagePair('zin', 'zout', 'ZOOM');
    for (const variant of variants) {
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
  assert.deepStrictEqual(MAP_FULL_LEFT, ['main', 'grid', 'flw', 'mapcfg', 'zin', 'zout'],
    'MAP_FULL_LEFT should be MAIN/GRID/FLW/CFG/Z+/Z- — mfd.js full view and f35.js both place this column verbatim');
  assert.deepStrictEqual(MAP_FULL_RIGHT, ['wpt', 'rt-next', 'rt-prev', 'wpt-next', 'wpt-prev'],
    'MAP_FULL_RIGHT should be WPT/R+/R-/W+/W- — mfd.js full view and f35.js both place this column (filtered by mapFullRight) verbatim');

  // Together they must name exactly NAV.map's actions — same completeness guarantee MAP_SPLIT_ORDER
  // gets above, so a NAV.map item added/removed here has no matching update either.
  assert.deepStrictEqual(MAP_FULL_LEFT.concat(MAP_FULL_RIGHT).sort(), NAV.map.map(i => i.action).sort(),
    'MAP_FULL_LEFT + MAP_FULL_RIGHT together must contain exactly NAV.map\'s actions');

  assert.deepStrictEqual(mapFullRight(true, true), MAP_FULL_RIGHT,
    'mapFullRight(true, true) should be the full WPT/R+/R-/W+/W- column — nothing filtered while a route is active');
  assert.deepStrictEqual(mapFullRight(true, false), ['wpt', 'rt-next', 'rt-prev'],
    'mapFullRight(true, false) should keep WPT/R+/R- but drop W+/W- — a route is saved but none is active');
  assert.deepStrictEqual(mapFullRight(false, false), ['wpt'],
    'mapFullRight(false, false) should collapse to WPT alone — no routes saved at all means R+/R-/W+/W- are all dead keys');
  for (const action of mapFullRight(false, false)) {
    assert.ok(!MAP_ROUTE_ACTIONS.has(action), `mapFullRight(false, false) should never include route action '${action}'`);
    assert.ok(!MAP_WAYPOINT_ACTIONS.has(action), `mapFullRight(false, false) should never include waypoint action '${action}'`);
  }
}

console.log('split-slots.test.js: OK');
