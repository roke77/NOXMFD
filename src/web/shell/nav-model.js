// Navigation model — the layout-INDEPENDENT half of the shell (docs/layouts.md, "The seam").
// Split out of mfd.js so it carries no DOM refs and can be unit-checked in Node (nav-model.test.js),
// the same way split-keymap.js is.
//
// What a pilot can do from each page, as an ORDERED list of { label, action }. It deliberately says
// nothing about a bezel — no key, no side, no slot — which is what lets a structurally different
// shell (e.g. a borderless F-35 quadrant grid with edge labels) consume this table unchanged.
// WHERE a label lands is the layout renderer's job:
//   * bezel, full view → mfd.js `fullViewSlot()`  (item i → left-column key i)
//   * bezel, split     → mfd.js `SPLIT_SLOTS`     (per-page pane-local side+slot)
// `action` dispatch is shared by every layout (mfd.js `mfdButton`), so it isn't here either.
//
// Not in this table, on purpose:
//   * WPN's MAIN/PREV/NEXT — pagination is *shell* state, not page navigation, so the bezel
//     renderer owns those labels (placeWpnNavLabels / renderSplitLabels' list branch). NAV.wpn is
//     empty to say "this page contributes no navigation of its own".
//   * HIDE SHELL / FULL / PIN / SWAP / the split presets — layout-owned chrome (function controls),
//     wired once at startup on the top+bottom banks.
(function (root) {
  const NAV = {
    map: [
      { label: 'MAIN', action: 'main' },   // → MAIN page
      { label: 'FLW',  action: 'flw'  },   // → toggle map follow
      { label: 'Z+',   action: 'zin'  },   // → map zoom in
      { label: 'Z-',   action: 'zout' },   // → map zoom out
    ],
    main: [
      { label: 'AVN', action: 'avn' },     // → AVN page
      { label: 'MAP', action: 'map' },     // → MAP page
      { label: 'RWR', action: 'rwr' },     // → RWR page
      { label: 'TGP', action: 'tgp' },     // → TGP page
      { label: 'TGT', action: 'tgt' },     // → TGT page (target-selection filter)
      { label: 'WPN', action: 'wpn' },     // → WPN page
    ],
    tgp: [ { label: 'MAIN', action: 'main' } ],   // ← back to MAIN
    avn: [ { label: 'MAIN', action: 'main' } ],
    rwr: [ { label: 'MAIN', action: 'main' } ],
    tgt: [ { label: 'MAIN', action: 'main' } ],
    bdf: [ { label: 'MAIN', action: 'main' } ],   // ← back to MAIN (docs/bdf-page.md)
    wpn: [],
  };

  const api = { NAV };
  if (typeof module !== 'undefined' && module.exports) module.exports = api;
  else root.NavModel = api;
})(typeof self !== 'undefined' ? self : this);
