// Where each layout sends a NAV destination — the two routing tables, deliberately side by side.
//
// NAV (nav-model.js) says WHAT a pilot can reach; this says where each layout mounts it. They live
// together because they must agree: every destination in NAV needs an entry in EVERY table below,
// or that button is dead in one place and nobody notices until someone presses it there. Keeping
// them adjacent means adding a page puts the omission in front of you; layout-coverage.test.js is
// the backstop that fails if it still slips through.
//
// Three tables, not two — the bezel routes full view and split panes differently (?bare, and MAIN/
// MAP are chrome in full view but ordinary panes in a split), so it carries one of each.
//
// Layout-SPECIFIC data, unlike NAV — the URLs differ (the bezel serves panes ?bare, the F-35 mounts
// portals) and each layout may host a destination differently. That is the seam working as intended
// (docs/layouts.md), not a leak: NAV still carries no placement.
(function (root) {
  // Classic bezel, FULL view — pages that render in #page-frame; showPage switches the frame's src
  // as you move between them. MAIN and MAP are absent on purpose: MAIN's full view is the shell's
  // own info-box chrome and MAP is the always-on base iframe underneath, so neither is ever mounted
  // here. Every other destination must appear, or the page renders blank in full view while still
  // working in a split pane.
  const CLASSIC_FULL = {
    wpn:  '/wpn',
    tgp:  '/tgp',
    avn:  '/avn',
    afm:  '/afm',
    rwr:  '/rwr',
    rdr:  '/rdr',
    tgt:  '/tgt',
    hud:  '/hud',
    bdf:  '/bdf',
    pal:  '/bdf?pal',
    mis:  '/mis',
    obj:  '/obj',
    keys: '/keybinds',
    rates: '/rates',
    wpt: '/wpt',
  };

  // Classic bezel, SPLIT panes — the same destinations served ?bare, plus MAIN and MAP, which do
  // mount as ordinary pane iframes here. A page with no entry renders 'about:blank' on navigation
  // (paneUrl), a no-op signal rather than a crash.
  const CLASSIC_SPLIT = {
    main: '/main?bare',
    map:  '/map-view?bare',
    avn:  '/avn?bare',
    afm:  '/afm?bare',
    tgp:  '/tgp?bare',
    wpn:  '/wpn?bare',
    rwr:  '/rwr?bare',
    rdr:  '/rdr?bare',
    tgt:  '/tgt?bare',
    bdf:  '/bdf?bare',
    pal:  '/bdf?bare&pal',
    mis:  '/mis?bare',
    obj:  '/obj?bare',
    hud:  '/hud?bare',
    keys: '/keybinds?bare',
    rates: '/rates?bare',
    wpt: '/wpt?bare',
  };

  // F-35 glass — the page each portal mounts. MAIN maps to no page and `null` is meaningful there
  // (the portal shows this layout's own chooser), so membership is tested with `in`, not truthiness.
  const F35 = {
    main: null,
    // No ?nochrome: the master strip no longer carries the mission name/grid (removed to give the
    // THRL/FUEL gauges more room), so a MAP portal draws its own mission bar + GRID chip again,
    // same as the bezel's MAP.
    map:  '/map-view?bare',
    // ?f35: AVN reads this to hide its status-icon grid (avn.js) — the F-35 master strip already
    // shows those flags (issue #35), so the portal keeps just the gauges.
    avn:  '/avn?f35',
    afm:  '/afm',   // reuses the avn feed — see PAGE_FEEDS in f35.js
    rwr:  '/rwr',
    rdr:  '/rdr',
    tgt:  '/tgt',
    tgp:  '/tgp',
    wpn:  '/wpn',
    bdf:  '/bdf',
    pal:  '/bdf?pal',
    mis:  '/mis',
    obj:  '/obj',
    hud:  '/hud',
    keys: '/keybinds',
    rates: '/rates',
    wpt: '/wpt',
  };

  const api = { CLASSIC_FULL, CLASSIC_SPLIT, F35 };
  if (typeof module !== 'undefined' && module.exports) module.exports = api;
  else root.LayoutPages = api;
})(typeof self !== 'undefined' ? self : this);
