// Bezel layout renderer: split placement. Carries no DOM refs so it can be unit-tested in Node
// (split-slots.test.js), including a check that SPLIT_SLOTS stays index-aligned with NAV[page]
// the way nav-model.test.js checks NAV's own shape.
//
// Where each NAV item lands in a split pane, as pane-local { side, slot } — index-aligned with
// NAV[page], so entry i places NAV[page][i]. SplitKeymap.paneKey resolves the pane-local position
// to a physical bezel key per orientation (top/bottom vs left/right).
//
// Split placement is not derivable from the ordered list in general: some pages spill a sibling
// switch onto the other column or group a control on one side, so each static-nav split-capable
// page declares its own slot list here.
//
// MAIN and MAP aren't here: their split placement is pagination in renderSplitLabels
// (mainPaneSlice / mapNavPaneSlice), not a fixed slot table, since both have more destinations
// than fit in six keys. A page absent here entirely cannot be a split pane — picking it from a
// pane collapses the split instead (see mfdButton's pane branch).
//
// MAP_FULL_LEFT/RIGHT/mapFullRight (bottom of file) piggyback on this module despite naming for
// full view rather than split: this is the one place NAV.map's action list gets reordered/
// filtered for its various renderings, shared and tested so mfd.js's full view and f35.js's glass
// can't drift out of sync with each other.
(function (root) {
  const SPLIT_SLOTS = {
    // MAP has no entry here: NAV.map has more items than a split pane's 6-key budget, so it's
    // paginated instead (mfd.js's mapNavPaneSlice, same treatment as MAIN).
    //
    // AVN / AFM / TGP / RWR / TGT / HUD in a split pane each expose their single MAIN back-button
    // on the pane's top-left slot (L0 for top, physically L3 for bottom); it navigates only that
    // pane. TGT's filter toggles and HUD's toggles are clickable inside the pane iframe, so like
    // the others they need no key labels beyond MAIN.
    avn: [ { side: 'left', slot: 0 } ],
    afm: [ { side: 'left', slot: 0 } ],
    // TGP gets a second item now (CFG) — pinned to the bottom of the left column (slot 2, the
    // last slot before a page would spill onto the right bank), mirroring full view's own
    // bottom-of-column placement (mfd.js's dedicated 'tgp' branch) at this pane's smaller 3-slot
    // scale.
    tgp: [ { side: 'left', slot: 0 }, { side: 'left', slot: 2 } ],
    // EXT's static baseline is one item (MAIN), same shape as TGP/RWR — a runtime-added
    // extension's own NAV[<id>] is also always exactly one item (ext-nav.js), so mfd.js falls
    // back to this same slot for any of them rather than needing a per-extension entry here.
    ext: [ { side: 'left', slot: 0 } ],
    rwr: [ { side: 'left', slot: 0 } ],
    // RDR/HSD are one sibling group: MAIN, FCR, HSD, then the page-local range rocker. HSD gets a
    // 6th slot (right,0) for its own MODE toggle (CEN<->DEP, docs/rdr-fcr-hsd.md) — FCR has no
    // such control, so it stays at 5.
    rdr: [ { side: 'left', slot: 0 }, { side: 'left', slot: 1 }, { side: 'left', slot: 2 }, { side: 'right', slot: 1 }, { side: 'right', slot: 2 } ],
    hsd: [ { side: 'left', slot: 0 }, { side: 'left', slot: 1 }, { side: 'left', slot: 2 }, { side: 'right', slot: 0 }, { side: 'right', slot: 1 }, { side: 'right', slot: 2 } ],
    tgt: [ { side: 'left', slot: 0 } ],
    // AKF/BDF/PAL/MIS/OBJ get 6: MAIN, then the other four as a direct switch (NAV.akf/NAV.bdf/
    // NAV.pal/NAV.mis/NAV.obj), index-aligned with this list. Left holds MAIN+AKF+MIS; OBJ/BDF/PAL
    // spill onto the right column.
    akf: [ { side: 'left', slot: 0 }, { side: 'left', slot: 1 }, { side: 'left', slot: 2 }, { side: 'right', slot: 0 }, { side: 'right', slot: 1 }, { side: 'right', slot: 2 } ],
    bdf: [ { side: 'left', slot: 0 }, { side: 'left', slot: 1 }, { side: 'left', slot: 2 }, { side: 'right', slot: 0 }, { side: 'right', slot: 1 }, { side: 'right', slot: 2 } ],
    pal: [ { side: 'left', slot: 0 }, { side: 'left', slot: 1 }, { side: 'left', slot: 2 }, { side: 'right', slot: 0 }, { side: 'right', slot: 1 }, { side: 'right', slot: 2 } ],
    mis: [ { side: 'left', slot: 0 }, { side: 'left', slot: 1 }, { side: 'left', slot: 2 }, { side: 'right', slot: 0 }, { side: 'right', slot: 1 }, { side: 'right', slot: 2 } ],
    obj: [ { side: 'left', slot: 0 }, { side: 'left', slot: 1 }, { side: 'left', slot: 2 }, { side: 'right', slot: 0 }, { side: 'right', slot: 1 }, { side: 'right', slot: 2 } ],
    // CFG group: MAIN/HUD/KEY/LYT switch directly between each other, same shape as BDF/PAL/MIS/
    // OBJ above but only 4 items — all fit the left column. Index-aligned with NAV.hud/NAV.keys.
    hud:   [ { side: 'left', slot: 0 }, { side: 'left', slot: 1 }, { side: 'left', slot: 2 }, { side: 'right', slot: 0 } ],
    keys:  [ { side: 'left', slot: 0 }, { side: 'left', slot: 1 }, { side: 'left', slot: 2 }, { side: 'right', slot: 0 } ],
    // WPT gets a single MAIN-equivalent back-button, same shape as AVN/AFM/TGP/RWR/TGT — but back
    // to MAP, matching NAV.wpt (reached from MAP's own nav row, not MAIN).
    wpt: [ { side: 'left', slot: 0 } ],
    // MAP's and TGP's own CFG pages get the same single MAIN-equivalent back-button shape, back to
    // the page that reached them (NAV.mapcfg/NAV.tgpcfg).
    mapcfg: [ { side: 'left', slot: 0 } ],
    tgpcfg: [ { side: 'left', slot: 0 } ],
    // WPN is a valid split page but places no NAV labels: its MAIN/PREV + NEXT depend on the
    // pane's pagination state, so renderSplitLabels' list branch owns them (NAV.wpn is empty to
    // match).
    wpn: [],
  };

  // NAV.map's own item order for SPLIT pagination (mfd.js's mapSplitItems/mapNavPaneSlice) —
  // deliberately not NAV.map's own full-view order, chosen to mirror the bezel's own full-view
  // grouping: MAIN/GRID/FLW/Z+/Z- fill page 1, then CFG/R+/R-/WPT/W+/W- fill pages 2-3 —
  // mainPageSizes' generic even-fill of this 11-item list lands as 5/4/2, with mapcfg placed to
  // keep page 1 exactly the original 5 (see its own comment below for why). An action-name list,
  // not NAV items directly — this module holds no reference to NAV (nav-model.js), so mfd.js maps
  // these onto the real NAV.map items; split-slots.test.js checks the resulting pairing against
  // NAV.map directly, so an edit to NAV.map that breaks the pairing fails there instead of
  // shipping a decorator that can never render.
  //
  // Within page 2, R+/R- lead and W+/W- trail the bare 'wpt' entry rather than wpt/R+/R-/W+/W- in
  // literal reading order: in an 'h' (top/bottom) split, page 2's item slots fill
  // left-bank-then-right-bank (listPaneLayout's 'h' branch) — R+/R- on the left bank, then WPT
  // alone, then W+/W- adjacent on the right bank, so every pair lands on one bank rather than
  // straddling the left/right boundary (see split-slots.test.js's per-orientation adjacency
  // check).
  // mapcfg sits between the ROUTE pair and WPT, not right after FLW (its full-view position,
  // MAP_FULL_LEFT below) or right after the ZOOM pair — mainPageSizes' generic even-fill pages
  // this 11-item list as 5/4/2, and page 2 only has 4 physical slots (items0-3 of listPaneLayout's
  // 'h' branch: left+1/left+2/right+0/right+1) split 2-and-2 across the bank boundary. Leading
  // page 2 with R+/R- (as before) keeps them on items0-1 (both left) so they stay adjacent;
  // inserting mapcfg ahead of them instead would push R- onto items1 and R+ onto... no, would push
  // R+/R- onto items1/items2, straddling left/right and breaking the ROUTE decorator entirely
  // (split-slots.test.js's assertAdjacentPair catches exactly this). Page 1 (MAIN/GRID/FLW/Z+/Z-)
  // and page 3 (W+/W-) are untouched either way, since mapcfg lands inside page 2.
  const MAP_SPLIT_ORDER = ['main', 'grid', 'flw', 'zin', 'zout', 'rt-next', 'rt-prev', 'mapcfg', 'wpt', 'wpt-next', 'wpt-prev'];

  // A 'v'/'vw' split has no bank split — listPaneLayout's non-'h' branch keeps every item slot on
  // the same side, so no pair can straddle a boundary. WPT leads here, reading as "the page, then
  // its controls" instead of splitting the controls around it.
  // mapcfg trails WPT rather than leading page 2 — same 5/4/2 pagination as the 'h' order above,
  // just placed to keep "WPT leads page 2" intact (the whole reason this V order exists) instead
  // of mapcfg displacing it.
  const MAP_SPLIT_ORDER_V = ['main', 'grid', 'flw', 'zin', 'zout', 'wpt', 'mapcfg', 'rt-next', 'rt-prev', 'wpt-next', 'wpt-prev'];

  // R+/R- and W+/W- are dead keys under different conditions: R+/R- cycle the active route and
  // include a "none active" stop (wpt-route.js's cycleRoute), so they stay useful as long as any
  // route is saved, active or not — only an empty route list leaves them with nothing to cycle
  // to. W+/W- step the active route's next waypoint, so they need one actually active. Every MAP
  // rendering (full view, split, F-35) drops each pair independently rather than showing a dead
  // key; MAP_SPLIT_ORDER/mapSplitOrder and MAP_FULL_LEFT/RIGHT/mapFullRight below both filter
  // through this same pair of sets.
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

  // MAP's full-view left/right grouping — the single source both the classic bezel (mfd.js's
  // showPage 'map' branch) and the F-35 glass (f35.js's mapNavItems) build their own placement
  // from, so the two layouts can't silently drift out of sync on which actions land left vs.
  // right, or which filter drops which pair. MAIN/GRID/FLW/Z+/Z- always show; WPT/R+/R-/W+/W- via
  // mapFullRight(hasRoutes, hasActiveRoute), same independent R+/R- vs W+/W- filtering as above.
  const MAP_FULL_LEFT  = ['main', 'grid', 'flw', 'mapcfg', 'zin', 'zout'];
  const MAP_FULL_RIGHT = ['wpt', 'rt-next', 'rt-prev', 'wpt-next', 'wpt-prev'];
  function mapFullRight(hasRoutes, hasActiveRoute) {
    return filterMapRouteActions(MAP_FULL_RIGHT, hasRoutes, hasActiveRoute);
  }

  const api = { SPLIT_SLOTS, MAP_SPLIT_ORDER, MAP_SPLIT_ORDER_V, MAP_ROUTE_ACTIONS, MAP_WAYPOINT_ACTIONS, mapSplitOrder, MAP_FULL_LEFT, MAP_FULL_RIGHT, mapFullRight };
  if (typeof module !== 'undefined' && module.exports) module.exports = api;
  else root.SplitSlots = api;
})(typeof self !== 'undefined' ? self : this);
