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
// Unlike full view, split placement is NOT derivable from the ordered list: MAP deliberately groups
// its zoom rocker (Z+ over Z-) on the RIGHT column instead of filling the left first, so each
// split-capable page declares its own. (layouts.md flags this as the open question — the answer is
// "a page can need a hint", and MAP is the page that needs one.)
//
// MAIN isn't here: its split placement is the MAIN_SPLIT_ITEMS pagination in renderSplitLabels, not
// a fixed slot table (there are eleven destinations and six keys). A page absent here entirely
// cannot be a split pane: LYT is a whole-document layout switch, not per-pane content, so picking it
// from a pane collapses the split instead (see mfdButton's pane branch).
(function (root) {
  const SPLIT_SLOTS = {
    // MAP pane is the bare map iframe (/map-view?bare) — it self-connects to the SSE stream, so the
    // shell forwards no data, only routes these controls to the pane's own map. Left column = nav
    // (MAIN back) + grid + follow; right column = the zoom rocker.
    map: [
      { side: 'left',  slot: 0 },   // MAIN — back to MAIN (this pane)
      { side: 'left',  slot: 1 },   // GRID — toggle the coordinate grid overlay (issue #41)
      { side: 'left',  slot: 2 },   // FLW  — toggle follow on this pane's map
      { side: 'right', slot: 0 },   // Z+
      { side: 'right', slot: 1 },   // Z-
    ],
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
    // WPN is a valid split page but places no NAV labels: its MAIN/PREV + NEXT depend on the pane's
    // pagination state, so renderSplitLabels' list branch owns them (NAV.wpn is empty to match).
    wpn: [],
  };

  const api = { SPLIT_SLOTS };
  if (typeof module !== 'undefined' && module.exports) module.exports = api;
  else root.SplitSlots = api;
})(typeof self !== 'undefined' ? self : this);
