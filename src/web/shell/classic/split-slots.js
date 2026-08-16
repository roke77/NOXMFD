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
// / mapNavPaneSlice), not a fixed slot table — MAIN has eleven destinations, MAP eight (issue #38's
// R+/R-), both past six keys. A page absent here entirely cannot be a split pane: LYT is a
// whole-document layout switch, not per-pane content, so picking it from a pane collapses the split
// instead (see mfdButton's pane branch).
(function (root) {
  const SPLIT_SLOTS = {
    // MAP has no entry here (issue #38): NAV.map grew to 8 items once R+/R- were added, past a
    // split pane's 6-key budget, so it's paginated instead (mfd.js's mapNavPaneSlice, same
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
    // BDF/PAL/MIS/OBJ instead get 5: MAIN, then the other three as a direct switch (NAV.bdf/NAV.pal/
    // NAV.mis/NAV.obj), index-aligned with this list. Left holds MAIN+BDF+PAL (its full 0..2 budget);
    // MIS/OBJ spill onto the right column's own 0..2 budget, nothing else uses it here.
    bdf: [ { side: 'left', slot: 0 }, { side: 'left', slot: 1 }, { side: 'left', slot: 2 }, { side: 'right', slot: 0 }, { side: 'right', slot: 1 } ],
    pal: [ { side: 'left', slot: 0 }, { side: 'left', slot: 1 }, { side: 'left', slot: 2 }, { side: 'right', slot: 0 }, { side: 'right', slot: 1 } ],
    mis: [ { side: 'left', slot: 0 }, { side: 'left', slot: 1 }, { side: 'left', slot: 2 }, { side: 'right', slot: 0 }, { side: 'right', slot: 1 } ],
    obj: [ { side: 'left', slot: 0 }, { side: 'left', slot: 1 }, { side: 'left', slot: 2 }, { side: 'right', slot: 0 }, { side: 'right', slot: 1 } ],
    hud: [ { side: 'left', slot: 0 } ],
    // CFG group (issue #39): KEY/LYT/RTS switch directly between each other, same shape as
    // BDF/PAL/MIS/OBJ above but only 4 items — MAIN+KEY+LYT fill the left column's 0..2 budget, RTS
    // spills onto right slot 0. Index-aligned with NAV.keys/NAV.rates.
    keys:  [ { side: 'left', slot: 0 }, { side: 'left', slot: 1 }, { side: 'left', slot: 2 }, { side: 'right', slot: 0 } ],
    rates: [ { side: 'left', slot: 0 }, { side: 'left', slot: 1 }, { side: 'left', slot: 2 }, { side: 'right', slot: 0 } ],
    // WPT (issue #38) gets a single MAIN-equivalent back-button, same shape as AVN/AFM/TGP/RWR/TGT —
    // but back to MAP, matching NAV.wpt (reached from MAP's own nav row, not MAIN).
    wpt: [ { side: 'left', slot: 0 } ],
    // WPN is a valid split page but places no NAV labels: its MAIN/PREV + NEXT depend on the pane's
    // pagination state, so renderSplitLabels' list branch owns them (NAV.wpn is empty to match).
    wpn: [],
  };

  // NAV.map's own item order for SPLIT pagination (mfd.js's MAP_SPLIT_ITEMS/mapNavPaneSlice) —
  // deliberately NOT NAV.map's own full-view order. mainPageSizes' fixed 5-then-3 split for 8 items
  // always lands the boundary between NAV.map's 5th and 6th full-view-order entries; in full-view
  // order (MAIN,GRID,FLW,WPT,R+,R-,Z+,Z-) that boundary falls INSIDE the R+/R- pair, so their ROUTE
  // decorator (mfd.js's placeMapPaneDecorator) could never find both keys on the same page — only
  // ZOOM (Z+/Z-, both on page 2) ever would. This order keeps each pair whole within a page instead:
  // MAIN/GRID/FLW/R+/R- fill page 1, WPT/Z+/Z- fill page 2. An action-name list, not NAV items
  // directly — this module holds no reference to NAV (nav-model.js), so mfd.js maps these onto the
  // real NAV.map items; split-slots.test.js checks the resulting pairing against NAV.map directly,
  // so an edit to NAV.map that breaks the pairing (without updating this list to match) fails there
  // instead of silently shipping a decorator that can never render, the way the ROUTE one first did.
  const MAP_SPLIT_ORDER = ['main', 'grid', 'flw', 'rt-next', 'rt-prev', 'wpt', 'zin', 'zout'];

  const api = { SPLIT_SLOTS, MAP_SPLIT_ORDER };
  if (typeof module !== 'undefined' && module.exports) module.exports = api;
  else root.SplitSlots = api;
})(typeof self !== 'undefined' ? self : this);
