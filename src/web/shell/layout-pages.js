// Where each layout sends a NAV destination — the two routing tables, deliberately side by side.
//
// NAV (nav-model.js) says WHAT a pilot can reach; this says where each layout mounts it. They live
// together because they must agree: every destination in NAV needs an entry in BOTH tables, or that
// button is dead in one layout and nobody notices until someone presses it there. Keeping them
// adjacent means adding a page puts the omission in front of you; layout-coverage.test.js is the
// backstop that fails if it still slips through.
//
// Layout-SPECIFIC data, unlike NAV — the URLs differ (the bezel serves panes ?bare, the F-35 mounts
// portals) and each layout may host a destination differently. That is the seam working as intended
// (docs/layouts.md), not a leak: NAV still carries no placement.
(function (root) {
  // Classic bezel — the iframe URL for each page. A page with no entry renders 'about:blank' on
  // navigation (paneUrl), a no-op signal rather than a crash.
  const CLASSIC = {
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
  };

  // F-35 glass — the page each portal mounts. MAIN maps to no page and `null` is meaningful there
  // (the portal shows this layout's own chooser), so membership is tested with `in`, not truthiness.
  const F35 = {
    main: null,
    // No ?nochrome: the master strip no longer carries the mission name/grid (removed to give the
    // THRL/FUEL gauges more room), so a MAP portal draws its own mission bar + GRID chip again,
    // same as the bezel's MAP.
    map:  '/map-view?bare',
    avn:  '/avn',
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
  };

  const api = { CLASSIC, F35 };
  if (typeof module !== 'undefined' && module.exports) module.exports = api;
  else root.LayoutPages = api;
})(typeof self !== 'undefined' ? self : this);
