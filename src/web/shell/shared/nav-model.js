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
      { label: 'CFG',  action: 'mapcfg' },
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
    // TGP's own CFG (docs/tgp-high-quality-mode.md follow-up) — unlike every other single-MAIN
    // page below, its layout renderer pins CFG to the bottom of its column rather than wherever
    // the generic item-i-to-slot sweep would put it (mfd.js's own 'tgp' branch, f35.js needs no
    // equivalent since its glass has no fixed-slot column to pin against).
    tgp: [ { label: 'MAIN', action: 'main' }, { label: 'CFG', action: 'tgpcfg' } ],
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
      { label: 'FCR',  action: 'rdr', mark: true },
      { label: 'HSD',  action: 'hsd' },
      { label: 'R+',   action: 'rng-in' },  // steps the displayed range up; sends the same
                                             // 'zoom-in' message MAP's Zoom In sends
      { label: 'R-',   action: 'rng-out' },
    ],
    hsd: [
      { label: 'MAIN', action: 'main' },
      { label: 'FCR',  action: 'rdr' },
      { label: 'HSD',  action: 'hsd', mark: true },
      { label: 'R+',   action: 'rng-in' },
      { label: 'R-',   action: 'rng-out' },
      { label: 'MODE', action: 'hsd-mode' },   // toggles CEN<->DEP (docs/rdr-fcr-hsd.md); current
                                                // mode shows on HSD's own range readout, not here
    ],
    // TD (issue #47, docs/target-designator.md) is appended here at runtime by td-nav.js only
    // while in a squad, same "presence discovered at runtime" shape NAV.ext uses for extensions —
    // the static baseline below is just MAIN, same as every other single-MAIN page here.
    tgt: [ { label: 'MAIN', action: 'main' } ],
    // AKF, MIS, OBJ, BDF, PAL and DOC fold under one MAIN destination rather than six separate
    // items: each carries the other five as a direct switch, plus the way back, with `mark` on
    // whichever one is current (docs/md-pages.md, docs/doc-page.md). mfd.js's generic sweep (full
    // view) and f35.js's generic itemsFor() both honor `mark` and fit this fine (7 items, well
    // under either layout's 12-slot full-view/portal budget) — split panes are the one place a
    // 7-item list doesn't fit a bezel pane's 6-slot budget, so mfd.js paginates this group's split
    // rendering instead of declaring SPLIT_SLOTS for it (see that file's own comment). AKF leads
    // and is MD's default landing page; DOC trails as the newest member (issue #82).
    akf: [
      { label: 'MAIN', action: 'main' },
      { label: 'AKF',  action: 'akf', mark: true },
      { label: 'MIS',  action: 'mis' },
      { label: 'OBJ',  action: 'obj' },
      { label: 'BDF',  action: 'bdf' },
      { label: 'PAL',  action: 'pal' },
      { label: 'DOC',  action: 'doc' },
    ],
    mis: [
      { label: 'MAIN', action: 'main' },
      { label: 'AKF',  action: 'akf' },
      { label: 'MIS',  action: 'mis', mark: true },
      { label: 'OBJ',  action: 'obj' },
      { label: 'BDF',  action: 'bdf' },
      { label: 'PAL',  action: 'pal' },
      { label: 'DOC',  action: 'doc' },
    ],
    obj: [
      { label: 'MAIN', action: 'main' },
      { label: 'AKF',  action: 'akf' },
      { label: 'MIS',  action: 'mis' },
      { label: 'OBJ',  action: 'obj', mark: true },
      { label: 'BDF',  action: 'bdf' },
      { label: 'PAL',  action: 'pal' },
      { label: 'DOC',  action: 'doc' },
    ],
    bdf: [
      { label: 'MAIN', action: 'main' },
      { label: 'AKF',  action: 'akf' },
      { label: 'MIS',  action: 'mis' },
      { label: 'OBJ',  action: 'obj' },
      { label: 'BDF',  action: 'bdf', mark: true },
      { label: 'PAL',  action: 'pal' },
      { label: 'DOC',  action: 'doc' },
    ],
    pal: [
      { label: 'MAIN', action: 'main' },
      { label: 'AKF',  action: 'akf' },
      { label: 'MIS',  action: 'mis' },
      { label: 'OBJ',  action: 'obj' },
      { label: 'BDF',  action: 'bdf' },
      { label: 'PAL',  action: 'pal', mark: true },
      { label: 'DOC',  action: 'doc' },
    ],
    // CFG folds HUD, KEY and LYT under one MAIN entry, same pattern as BDF/PAL/MIS/OBJ above.
    // LYT's action is the CLASSIC/F-35 chooser (mfd.js BEZEL_EXTRAS.lyt / f35.js
    // GLASS_ACTIONS.lyt) — only its entry point lives here, its own rendering is untouched. The
    // TLM/TGP refresh-rate sliders live on MAP's and TGP's own CFG items instead (NAV.map/NAV.tgp
    // above, NAV.mapcfg/NAV.tgpcfg below), since each one only ever matters to a single page.
    hud: [
      { label: 'MAIN', action: 'main' },
      { label: 'HUD',  action: 'hud', mark: true },
      { label: 'KEY',  action: 'keys' },
      { label: 'LYT',  action: 'lyt'  },
    ],
    keys: [
      { label: 'MAIN', action: 'main' },
      { label: 'HUD',  action: 'hud' },
      { label: 'KEY',  action: 'keys', mark: true },
      { label: 'LYT',  action: 'lyt'  },
    ],
    // No NAV.lyt entry: BEZEL_EXTRAS.lyt places CLASSIC/F-35 at explicit left0/left1 after the
    // generic NAV[name] sweep (showPage), so a NAV.lyt list here would just get silently
    // overwritten at those two slots — no MAIN back-item is needed since CLASSIC/F-35 already
    // returns to MAIN.
    wpn: [],
    // WPT is reached from MAP's own nav row (above), so its way back is MAP, not MAIN — same
    // reasoning as tgp/avn/etc.'s single-entry back links.
    wpt: [ { label: 'MAP', action: 'map' } ],
    // SQD (docs/squadron-transport.md) — squad membership/invites, reached from MAIN like HUD/CFG/
    // MD/RDR/AFM (BEZEL_EXTRAS.main / f35.js's MAIN_EXTRAS), not from another page's own nav row.
    sqd: [ { label: 'MAIN', action: 'main' } ],
    // MAP's CFG (docs/tgp-high-quality-mode.md follow-up, NAV amendment) — just the MAP/telemetry
    // refresh-rate slider, formerly RTS's TLM setting. Reached from MAP's own nav row, so its way
    // back is MAP, same reasoning as WPT above.
    mapcfg: [ { label: 'MAP', action: 'map' } ],
    // TGP's CFG — the TGP camera feed rate + quality settings, formerly RTS's TGP settings. Its
    // way back is TGP, not MAIN, matching every other page reached from a single-purpose sibling
    // rather than MAIN itself.
    tgpcfg: [ { label: 'TGP', action: 'tgp' } ],
    // TD (issue #47, docs/target-designator.md) — reached from TGT's own nav row (once td-nav.js
    // has appended it there), so its way back is TGT, same reasoning as mapcfg/tgpcfg/wpt above.
    td: [ { label: 'TGT', action: 'tgt' } ],
    // DOC (kneeboard documents viewer, issue #82) — reached via the AKF/MIS/OBJ/BDF/PAL switch above
    // (its own DOC entry there), but once open shows its OWN small nav rather than mirroring that
    // whole switch back: MAIN and MD (MD = the same "back to the switch's landing page" action
    // BEZEL_EXTRAS.main's own MD button uses) are static, always present; INDX/NEXT/PREV are NOT
    // here — they only make sense once an image is open, so mfd.js hand-places them (like TGP's own
    // MAN/CLR/IR/etc.) gated on doc.js's live view state ('doc-view' messages — docs/doc-page.md),
    // rather than sitting here clickable-but-inert over the index. They act on the page in place
    // rather than naming a destination — same shape as RDR/HSD's own R+/R- above
    // (layout-coverage.test.js's BEHAVIOURS lists them, not CLASSIC_FULL/CLASSIC_SPLIT/F35).
    doc: [
      { label: 'MAIN', action: 'main' },
      { label: 'MD',   action: 'akf' },
    ],
  };

  const api = { NAV };
  if (typeof module !== 'undefined' && module.exports) module.exports = api;
  else root.NavModel = api;
})(typeof self !== 'undefined' ? self : this);
