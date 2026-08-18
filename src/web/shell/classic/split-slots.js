// Bezel layout renderer: split placement. Split out of mfd.js so it carries no DOM refs and can be
// unit-checked in Node (split-slots.test.js) — specifically so NAV/SPLIT_SLOTS index-alignment can
// be asserted automatically, the way nav-model.test.js already checks NAV's own shape. This is the
// exact invariant a real bug hit (cfg-rates experiment, issue #39): NAV.keys grew from 1 item to 4
// (MAIN/KEY/LYT/RTS) but SPLIT_SLOTS.keys stayed at 1, so a split pane on the new CFG group silently
// dropped KEY/LYT/RTS — caught by hand in-game, not by any test, since this table lived inline in
// mfd.js (DOM-coupled, unrequireable) until now.
//
// Where each NAV item lands in a split pane, as pane-local { side, slot } — index-aligned with
// NAV[page], so entry i places NAV[page][i]. SplitKeymap.paneKey resolves the pane-local position
// to a physical bezel key per orientation (top/bottom vs left/right).
//
// Unlike full view, split placement is NOT derivable from the ordered list in general: BDF/PAL/MIS/
// OBJ spill their sibling switch onto the right column, RDR groups its range rocker on the left, so
// each static-nav split-capable page declares its own. (layouts.md flags this as the open question
// — the answer is "a page can need a hint".)
//
// MAIN and MAP aren't here: their split placement is pagination in renderSplitLabels (mainPaneSlice
// / mapNavPaneSlice), not a fixed slot table — MAIN has eleven destinations, MAP ten (issue #38's
// R+/R- and W+/W-), both past six keys. A page absent here entirely cannot be a split pane: LYT is a
// whole-document layout switch, not per-pane content, so picking it from a pane collapses the split
// instead (see mfdButton's pane branch).
//
// MAP_FULL_LEFT/RIGHT/mapFullRight (bottom of file, issue #38 follow-up) piggyback on this module
// for the same reason, despite naming for full view rather than split: this is already the one
// place NAV.map's action list gets reordered/filtered for its various renderings, shared and tested
// so mfd.js's full view and f35.js's glass can't drift out of sync with each other — a second module
// for one more list would just be a second place to keep them in sync by hand instead.
(function (root) {
  const SPLIT_SLOTS = {
    // MAP has no entry here (issue #38): NAV.map grew to 10 items once R+/R- and W+/W- were added,
    // past a split pane's 6-key budget, so it's paginated instead (mfd.js's mapNavPaneSlice, same
    // treatment as MAIN — see MAIN's own absence from this table for the same reason).
    //
    // AVN / AFM / TGP / RWR / TGT / HUD in a split pane each expose their single MAIN back-button on
    // the pane's top-left slot (L0 for top, physically L3 for bottom). It navigates ONLY that pane.
    // TGT's filter toggles and HUD's toggles are clickable inside the pane iframe, so like the others
    // they need no key labels beyond MAIN.
    avn: [ { side: 'left', slot: 0 } ],
    afm: [ { side: 'left', slot: 0 } ],
    tgp: [ { side: 'left', slot: 0 } ],
    rwr: [ { side: 'left', slot: 0 } ],
    // RDR gets MAIN plus the range rocker (R+/R-, issue #40 follow-up) all on the left column, right
    // after MAIN — unlike MAP's zoom rocker, which splits onto the right column (MAP has more items
    // filling the left column already; RDR doesn't).
    rdr: [ { side: 'left', slot: 0 }, { side: 'left', slot: 1 }, { side: 'left', slot: 2 } ],
    tgt: [ { side: 'left', slot: 0 } ],
    // AKF/BDF/PAL/MIS/OBJ instead get 6: MAIN, then the other four as a direct switch (NAV.akf/
    // NAV.bdf/NAV.pal/NAV.mis/NAV.obj), index-aligned with this list. Left holds MAIN+AKF+MIS (its
    // full 0..2 budget); OBJ/BDF/PAL spill onto the right column's own 0..2 budget — issue #34
    // fills the right column's last free slot (2), previously unused by this group.
    akf: [ { side: 'left', slot: 0 }, { side: 'left', slot: 1 }, { side: 'left', slot: 2 }, { side: 'right', slot: 0 }, { side: 'right', slot: 1 }, { side: 'right', slot: 2 } ],
    bdf: [ { side: 'left', slot: 0 }, { side: 'left', slot: 1 }, { side: 'left', slot: 2 }, { side: 'right', slot: 0 }, { side: 'right', slot: 1 }, { side: 'right', slot: 2 } ],
    pal: [ { side: 'left', slot: 0 }, { side: 'left', slot: 1 }, { side: 'left', slot: 2 }, { side: 'right', slot: 0 }, { side: 'right', slot: 1 }, { side: 'right', slot: 2 } ],
    mis: [ { side: 'left', slot: 0 }, { side: 'left', slot: 1 }, { side: 'left', slot: 2 }, { side: 'right', slot: 0 }, { side: 'right', slot: 1 }, { side: 'right', slot: 2 } ],
    obj: [ { side: 'left', slot: 0 }, { side: 'left', slot: 1 }, { side: 'left', slot: 2 }, { side: 'right', slot: 0 }, { side: 'right', slot: 1 }, { side: 'right', slot: 2 } ],
    // CFG group (issue #39, HUD joined 2026-08-20): MAIN/HUD/KEY/LYT switch directly between each
    // other, same shape as BDF/PAL/MIS/OBJ above but only 5 items — MAIN+HUD+KEY fill the left
    // column's 0..2 budget, LYT+RTS spill onto right slots 0..1. Index-aligned with
    // NAV.hud/NAV.keys/NAV.rates.
    hud:   [ { side: 'left', slot: 0 }, { side: 'left', slot: 1 }, { side: 'left', slot: 2 }, { side: 'right', slot: 0 }, { side: 'right', slot: 1 } ],
    keys:  [ { side: 'left', slot: 0 }, { side: 'left', slot: 1 }, { side: 'left', slot: 2 }, { side: 'right', slot: 0 }, { side: 'right', slot: 1 } ],
    rates: [ { side: 'left', slot: 0 }, { side: 'left', slot: 1 }, { side: 'left', slot: 2 }, { side: 'right', slot: 0 }, { side: 'right', slot: 1 } ],
    // WPT (issue #38) gets a single MAIN-equivalent back-button, same shape as AVN/AFM/TGP/RWR/TGT —
    // but back to MAP, matching NAV.wpt (reached from MAP's own nav row, not MAIN).
    wpt: [ { side: 'left', slot: 0 } ],
    // SQD (docs/squadron-transport.md), same single-back-button shape as WPT above.
    sqd: [ { side: 'left', slot: 0 } ],
    // WPN is a valid split page but places no NAV labels: its MAIN/PREV + NEXT depend on the pane's
    // pagination state, so renderSplitLabels' list branch owns them (NAV.wpn is empty to match).
    wpn: [],
  };

  // NAV.map's own item order for SPLIT pagination (mfd.js's mapSplitItems/mapNavPaneSlice) —
  // deliberately NOT NAV.map's own full-view order, and (issue #38 follow-up) chosen to mirror the
  // bezel's own full-view grouping: MAIN/GRID/FLW/Z+/Z- (mfd.js's MAP_FULL_LEFT) fill page 1, then
  // WPT/R+/R-/W+/W- (MAP_FULL_RIGHT) fill page 2 — mainPageSizes' fixed 5-then-5 split for 10 items
  // lands exactly on that boundary. An action-name list, not NAV items directly — this module holds
  // no reference to NAV (nav-model.js), so mfd.js maps these onto the real NAV.map items;
  // split-slots.test.js checks the resulting pairing against NAV.map directly, so an edit to NAV.map
  // that breaks the pairing (without updating this list to match) fails there instead of silently
  // shipping a decorator that can never render, the way the ROUTE one first did.
  //
  // Within page 2, R+/R- lead and W+/W- trail the bare 'wpt' entry (rather than wpt/R+/R-/W+/W- in
  // that literal reading order) because in an 'h' (top/bottom) split, page 2's item slots fill
  // left-bank-then-right-bank (2 slots then 3 — listPaneLayout's 'h' branch): R+/R- on the left bank
  // (its only two item slots), then WPT alone, then W+/W- adjacent on the right bank — every pair
  // lands on one bank, none straddles the left/right boundary the way naming order alone would put
  // wpt-next/wpt-prev (see split-slots.test.js's per-orientation adjacency check, which pins this
  // down directly — it caught exactly this class of bug once already).
  const MAP_SPLIT_ORDER = ['main', 'grid', 'flw', 'zin', 'zout', 'rt-next', 'rt-prev', 'wpt', 'wpt-next', 'wpt-prev'];

  // A 'v'/'vw' split has no such bank split — listPaneLayout's non-'h' branch keeps every item slot
  // on the SAME side (the pane's one adjacent column), so there's no boundary a pair could straddle.
  // WPT can lead there, reading naturally as "the page, then its controls" instead of splitting the
  // controls around it (issue #38 follow-up, WPT-leads-in-v-split follow-up).
  const MAP_SPLIT_ORDER_V = ['main', 'grid', 'flw', 'zin', 'zout', 'wpt', 'rt-next', 'rt-prev', 'wpt-next', 'wpt-prev'];

  // R+/R- and W+/W- are dead keys under different conditions (issue #38 follow-up, deactivate
  // follow-up): R+/R- cycle the active route and now include a "none active" stop (wpt-route.js's
  // cycleRoute), so they stay useful as long as ANY route is saved, active or not — only an empty
  // route list leaves them with nothing to cycle to. W+/W- step the ACTIVE route's next waypoint,
  // so they still need one actually active. Every MAP rendering (full view, split, F-35) drops each
  // pair independently rather than showing a dead key, and this is the one place that says so:
  // MAP_SPLIT_ORDER/mapSplitOrder use it for split-mode pagination (a pane's MAP list collapses from
  // 10 items down to as few as 6 — MAIN/GRID/FLW/Z+/Z-/WPT — rendering as a single unpaginated page
  // with no PREV/NEXT), and MAP_FULL_LEFT/RIGHT/mapFullRight below use it for full view and F-35's
  // own left/right grouping.
  const MAP_ROUTE_ACTIONS    = new Set(['rt-next', 'rt-prev']);
  const MAP_WAYPOINT_ACTIONS = new Set(['wpt-next', 'wpt-prev']);
  function filterMapRouteActions(list, hasRoutes, hasActiveRoute) {
    return list.filter(function (a) {
      if (MAP_ROUTE_ACTIONS.has(a)) return hasRoutes;
      if (MAP_WAYPOINT_ACTIONS.has(a)) return hasActiveRoute;
      return true;
    });
  }
  function mapSplitOrder(variant, hasRoutes, hasActiveRoute) {
    return filterMapRouteActions(variant === 'h' ? MAP_SPLIT_ORDER : MAP_SPLIT_ORDER_V, hasRoutes, hasActiveRoute);
  }

  // MAP's full-view left/right grouping (issue #38 follow-up) — the single source both the classic
  // bezel (mfd.js's showPage 'map' branch) and the F-35 glass (f35.js's mapNavItems) build their own
  // placement from, so the two layouts can't silently drift out of sync on which actions land left
  // vs. right, or which filter drops which pair — previously each hand-wrote its own copy of both
  // lists with nothing tying them together. MAIN/GRID/FLW/Z+/Z- always show; WPT/R+/R-/W+/W- via
  // mapFullRight(hasRoutes, hasActiveRoute), same independent R+/R- vs W+/W- filtering as above.
  const MAP_FULL_LEFT  = ['main', 'grid', 'flw', 'zin', 'zout'];
  const MAP_FULL_RIGHT = ['wpt', 'rt-next', 'rt-prev', 'wpt-next', 'wpt-prev'];
  function mapFullRight(hasRoutes, hasActiveRoute) {
    return filterMapRouteActions(MAP_FULL_RIGHT, hasRoutes, hasActiveRoute);
  }

  const api = { SPLIT_SLOTS, MAP_SPLIT_ORDER, MAP_SPLIT_ORDER_V, MAP_ROUTE_ACTIONS, MAP_WAYPOINT_ACTIONS, mapSplitOrder, MAP_FULL_LEFT, MAP_FULL_RIGHT, mapFullRight };
  if (typeof module !== 'undefined' && module.exports) module.exports = api;
  else root.SplitSlots = api;
})(typeof self !== 'undefined' ? self : this);
