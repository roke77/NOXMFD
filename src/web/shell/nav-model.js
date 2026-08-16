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
    // BDF, PAL, MIS and OBJ fold under one MAIN destination (MDT — BEZEL_EXTRAS.main, action still
    // 'bdf' so it lands on this same list) rather than four separate items: each carries the other
    // three as a direct switch, plus the way back, with `mark` on whichever one is current
    // (docs/mdt-pages.md). mfd.js's generic sweep (full view) and renderSplitLabels' static-nav
    // branch (split) both honor `mark`.
    bdf: [
      { label: 'MAIN', action: 'main' },
      { label: 'BDF',  action: 'bdf', mark: true },
      { label: 'PAL',  action: 'pal' },
      { label: 'MIS',  action: 'mis' },
      { label: 'OBJ',  action: 'obj' },
    ],
    pal: [
      { label: 'MAIN', action: 'main' },
      { label: 'BDF',  action: 'bdf' },
      { label: 'PAL',  action: 'pal', mark: true },
      { label: 'MIS',  action: 'mis' },
      { label: 'OBJ',  action: 'obj' },
    ],
    mis: [
      { label: 'MAIN', action: 'main' },
      { label: 'BDF',  action: 'bdf' },
      { label: 'PAL',  action: 'pal' },
      { label: 'MIS',  action: 'mis', mark: true },
      { label: 'OBJ',  action: 'obj' },
    ],
    obj: [
      { label: 'MAIN', action: 'main' },
      { label: 'BDF',  action: 'bdf' },
      { label: 'PAL',  action: 'pal' },
      { label: 'MIS',  action: 'mis' },
      { label: 'OBJ',  action: 'obj', mark: true },
    ],
    hud: [ { label: 'MAIN', action: 'main' } ],   // HUD OPTIONS page — reached via a layout extra, not MAIN
    // CFG (cfg-rates experiment, issue #39) folds KEY, LYT and RTS under one MAIN entry — same
    // pattern as BDF/PAL/MIS/OBJ above. Action 'keys' still lands on the keybinds page (CFG's own
    // MAIN-entry action, mirroring MDT staying 'bdf'); LYT's action is the pre-existing
    // CLASSIC/F-35 chooser (mfd.js BEZEL_EXTRAS.lyt / f35.js GLASS_ACTIONS.lyt) — only its entry
    // point moved here, its own rendering is untouched.
    keys: [
      { label: 'MAIN', action: 'main' },
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
      { label: 'KEY',  action: 'keys' },
      { label: 'LYT',  action: 'lyt'  },
      { label: 'RTS',  action: 'rates', mark: true },
    ],
    wpn: [],
    // WPT (issue #38) — reached from MAP's own nav row (above), so its way back is MAP, not MAIN,
    // same reasoning as tgp/avn/etc.'s single-entry back links.
    wpt: [ { label: 'MAP', action: 'map' } ],
  };

  const api = { NAV };
  if (typeof module !== 'undefined' && module.exports) module.exports = api;
  else root.NavModel = api;
})(typeof self !== 'undefined' ? self : this);
