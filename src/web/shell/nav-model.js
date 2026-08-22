// Navigation model — the layout-independent half of the shell (docs/layouts.md, "the seam").
// Carries no DOM refs so it can be unit-tested in Node (nav-model.test.js).
//
// What a pilot can do from each page, as an ordered list of { label, action }. It deliberately says
// nothing about a bezel — no key, no side, no slot — which is what lets a structurally different
// shell (e.g. a borderless F-35 quadrant grid with edge labels) consume this table unchanged.
// WHERE a label lands is the layout renderer's job:
//   * bezel, full view -> mfd.js `fullViewSlot()`  (item i -> left-column key i)
//   * bezel, split     -> mfd.js `SPLIT_SLOTS`     (per-page pane-local side+slot)
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
      { label: 'MAIN', action: 'main' },
      { label: 'GRID', action: 'grid' },
      { label: 'FLW',  action: 'flw'  },
      { label: 'WPT',  action: 'wpt'  },
      { label: 'R+',   action: 'rt-next' },
      { label: 'R-',   action: 'rt-prev' },
      { label: 'W+',   action: 'wpt-next' },
      { label: 'W-',   action: 'wpt-prev' },
      { label: 'Z+',   action: 'zin'  },
      { label: 'Z-',   action: 'zout' },
    ],
    main: [
      { label: 'AVN', action: 'avn' },
      { label: 'MAP', action: 'map' },
      { label: 'RWR', action: 'rwr' },
      { label: 'TGP', action: 'tgp' },
      { label: 'TGT', action: 'tgt' },
      { label: 'WPN', action: 'wpn' },
      { label: 'EXT', action: 'ext' },
    ],
    tgp: [ { label: 'MAIN', action: 'main' } ],
    // EXT (docs/extensions-api.md) — unlike every other entry here, this one's contents are
    // discovered at runtime, not authored: ext-nav.js fetches /ext-manifest at boot and appends
    // one item per installed extension after this MAIN baseline. An extension's own page gets a
    // matching NAV[<its id>], set the same way. Kept here (not just in ext-nav.js) so a page
    // loaded before that fetch resolves still has a working MAIN back-link instead of an empty
    // nav.
    ext: [ { label: 'MAIN', action: 'main' } ],
    avn: [ { label: 'MAIN', action: 'main' } ],
    afm: [ { label: 'MAIN', action: 'main' } ],   // Airframe page — name + damage silhouette
    rwr: [ { label: 'MAIN', action: 'main' } ],
    rdr: [
      { label: 'MAIN', action: 'main' },
      { label: 'R+',   action: 'rng-in' },  // steps the displayed range up; sends the same
                                             // 'zoom-in' message MAP's Zoom In sends
      { label: 'R-',   action: 'rng-out' },
    ],
    tgt: [ { label: 'MAIN', action: 'main' } ],
    // AKF, BDF, PAL, MIS and OBJ fold under one MAIN destination rather than five separate items:
    // each carries the other four as a direct switch, plus the way back, with `mark` on whichever
    // one is current (docs/md-pages.md). mfd.js's generic sweep (full view) and
    // renderSplitLabels' static-nav branch (split) both honor `mark`. AKF leads and is MD's
    // default landing page.
    akf: [
      { label: 'MAIN', action: 'main' },
      { label: 'AKF',  action: 'akf', mark: true },
      { label: 'MIS',  action: 'mis' },
      { label: 'OBJ',  action: 'obj' },
      { label: 'BDF',  action: 'bdf' },
      { label: 'PAL',  action: 'pal' },
    ],
    mis: [
      { label: 'MAIN', action: 'main' },
      { label: 'AKF',  action: 'akf' },
      { label: 'MIS',  action: 'mis', mark: true },
      { label: 'OBJ',  action: 'obj' },
      { label: 'BDF',  action: 'bdf' },
      { label: 'PAL',  action: 'pal' },
    ],
    obj: [
      { label: 'MAIN', action: 'main' },
      { label: 'AKF',  action: 'akf' },
      { label: 'MIS',  action: 'mis' },
      { label: 'OBJ',  action: 'obj', mark: true },
      { label: 'BDF',  action: 'bdf' },
      { label: 'PAL',  action: 'pal' },
    ],
    bdf: [
      { label: 'MAIN', action: 'main' },
      { label: 'AKF',  action: 'akf' },
      { label: 'MIS',  action: 'mis' },
      { label: 'OBJ',  action: 'obj' },
      { label: 'BDF',  action: 'bdf', mark: true },
      { label: 'PAL',  action: 'pal' },
    ],
    pal: [
      { label: 'MAIN', action: 'main' },
      { label: 'AKF',  action: 'akf' },
      { label: 'MIS',  action: 'mis' },
      { label: 'OBJ',  action: 'obj' },
      { label: 'BDF',  action: 'bdf' },
      { label: 'PAL',  action: 'pal', mark: true },
    ],
    // CFG folds HUD, KEY, LYT and RTS under one MAIN entry, same pattern as BDF/PAL/MIS/OBJ
    // above. LYT's action is the CLASSIC/F-35 chooser (mfd.js BEZEL_EXTRAS.lyt / f35.js
    // GLASS_ACTIONS.lyt) — only its entry point lives here, its own rendering is untouched.
    hud: [
      { label: 'MAIN', action: 'main' },
      { label: 'HUD',  action: 'hud', mark: true },
      { label: 'KEY',  action: 'keys' },
      { label: 'LYT',  action: 'lyt'  },
      { label: 'RTS',  action: 'rates' },
    ],
    keys: [
      { label: 'MAIN', action: 'main' },
      { label: 'HUD',  action: 'hud' },
      { label: 'KEY',  action: 'keys', mark: true },
      { label: 'LYT',  action: 'lyt'  },
      { label: 'RTS',  action: 'rates' },
    ],
    // No NAV.lyt entry: BEZEL_EXTRAS.lyt places CLASSIC/F-35 at explicit left0/left1 after the
    // generic NAV[name] sweep (showPage), so a NAV.lyt list here would just get silently
    // overwritten at those two slots — no MAIN back-item is needed since CLASSIC/F-35 already
    // returns to MAIN.
    rates: [
      { label: 'MAIN', action: 'main' },
      { label: 'HUD',  action: 'hud' },
      { label: 'KEY',  action: 'keys' },
      { label: 'LYT',  action: 'lyt'  },
      { label: 'RTS',  action: 'rates', mark: true },
    ],
    wpn: [],
    // WPT is reached from MAP's own nav row (above), so its way back is MAP, not MAIN — same
    // reasoning as tgp/avn/etc.'s single-entry back links.
    wpt: [ { label: 'MAP', action: 'map' } ],
    // SQD (docs/squadron-transport.md) — squad membership/invites, reached from MAIN like HUD/CFG/
    // MDT/RDR/AFM (BEZEL_EXTRAS.main / f35.js's MAIN_EXTRAS), not from another page's own nav row.
    sqd: [ { label: 'MAIN', action: 'main' } ],
  };

  const api = { NAV };
  if (typeof module !== 'undefined' && module.exports) module.exports = api;
  else root.NavModel = api;
})(typeof self !== 'undefined' ? self : this);
