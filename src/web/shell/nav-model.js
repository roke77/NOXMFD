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
      { label: 'GRID', action: 'grid' },   // → toggle the coordinate grid overlay (issue #41)
      { label: 'FLW',  action: 'flw'  },   // → toggle map follow
      { label: 'WPT',  action: 'wpt'  },   // → WPT page: waypoints/route creator (issue #38)
      { label: 'R+',   action: 'rt-next' }, // → switch the active route to the NEXT one (issue #38)
      { label: 'R-',   action: 'rt-prev' }, // → switch the active route to the PREVIOUS one (issue #38)
      { label: 'W+',   action: 'wpt-next' }, // → manually advance to the NEXT waypoint (issue #38)
      { label: 'W-',   action: 'wpt-prev' }, // → manually rewind to the PREVIOUS waypoint (issue #38)
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
    afm: [ { label: 'MAIN', action: 'main' } ],   // Airframe page — name + damage silhouette
    rwr: [ { label: 'MAIN', action: 'main' } ],
    rdr: [
      { label: 'MAIN', action: 'main' },    // ← back to MAIN (docs/rdr-page.md)
      { label: 'R+',   action: 'rng-in' },  // → step the displayed range UP (bigger range number) —
                                             // sends the same 'zoom-in' message MAP's Zoom In sends
      { label: 'R-',   action: 'rng-out' }, // → step the displayed range DOWN (smaller range number)
    ],
    tgt: [ { label: 'MAIN', action: 'main' } ],
    // AKF, BDF, PAL, MIS and OBJ fold under one MAIN destination (MDT — BEZEL_EXTRAS.main, action
    // 'akf' so it lands on this list, on AKF specifically) rather than five separate items: each
    // carries the other four as a direct switch, plus the way back, with `mark` on whichever one is
    // current (docs/mdt-pages.md). mfd.js's generic sweep (full view) and renderSplitLabels'
    // static-nav branch (split) both honor `mark`. Order is AKF, MIS, OBJ, BDF, PAL (issue #34) —
    // AKF leads and is MDT's default landing page.
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
    // CFG (cfg-rates experiment, issue #39) folds KEY, LYT and RTS under one MAIN entry — same
    // pattern as BDF/PAL/MIS/OBJ above. LYT's action is the pre-existing CLASSIC/F-35 chooser
    // (mfd.js BEZEL_EXTRAS.lyt / f35.js GLASS_ACTIONS.lyt) — only its entry point moved here, its
    // own rendering is untouched. HUD joined the group later (2026-08-20): it no longer has a MAIN
    // entry of its own (BEZEL_EXTRAS.main), only this sub-nav row, same as RTS never having one —
    // and CFG's own MAIN-entry action was repointed from 'keys' to 'hud', so CFG now lands here.
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
    // No NAV.lyt entry: BEZEL_EXTRAS.lyt places CLASSIC/F-35 at explicit left0/left1 AFTER the
    // generic NAV[name] sweep (showPage), so a NAV.lyt list here would just get silently
    // overwritten at those two slots — the existing "no MAIN back-item, CLASSIC/F-35 already
    // returns to MAIN" design (see BEZEL_EXTRAS comment) stays exactly as it is.
    rates: [
      { label: 'MAIN', action: 'main' },
      { label: 'HUD',  action: 'hud' },
      { label: 'KEY',  action: 'keys' },
      { label: 'LYT',  action: 'lyt'  },
      { label: 'RTS',  action: 'rates', mark: true },
    ],
    wpn: [],
    // WPT (issue #38) — reached from MAP's own nav row (above), so its way back is MAP, not MAIN,
    // same reasoning as tgp/avn/etc.'s single-entry back links.
    wpt: [ { label: 'MAP', action: 'map' } ],
    // SQD (docs/squadron-transport.md) — squad membership/invites, reached from MAIN like HUD/CFG/
    // MDT/RDR/AFM (BEZEL_EXTRAS.main / f35.js's MAIN_EXTRAS), not from another page's own nav row.
    sqd: [ { label: 'MAIN', action: 'main' } ],
  };

  const api = { NAV };
  if (typeof module !== 'undefined' && module.exports) module.exports = api;
  else root.NavModel = api;
})(typeof self !== 'undefined' ? self : this);
