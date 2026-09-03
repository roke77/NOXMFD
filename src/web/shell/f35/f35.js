// F-35 layout — Stage 2 (docs/layouts.md). A second layout renderer consuming the same NAV model
// the bezel shell does.
//
// What this layout owns (the doc's four: frame + label placement + split behaviour + page geometry):
//   • frame           — none. f35.html/css are borderless; the page IS the display.
//   • label placement — a grid drawn over the page, in one of two modes (NAV_LAYOUT below):
//                       'edge' hugs the left column like the bezel's key bank; 'center' puts the
//                       labels in the middle of the glass for MAIN, which has no page behind them.
//   • split behaviour — portals. Four side-by-side MFDs, each with its own page, labels and
//                       state; the corner grips merge adjacent ones and split them back. The
//                       arrangement rule is f35-glass.js. Nothing of the bezel's split machinery
//                       (SplitKeymap, SPLIT_SLOTS) is reused: it resolves labels to physical keys,
//                       and there are none here.
//   • page geometry   — none, except WPN. See forwardWpnLayout.
//
// Shared with the bezel and unchanged: NAV (nav-model.js), the pages, and sendCommand.
//
// Data path: #map-tap owns the only EventSource('/stream') and posts the derived per-page slices
// up here; the shell caches them and each portal replays what its page needs. Every layout
// inherits this dependency, map or no map.
(function () {
  const NAV       = NavModel.NAV;
  const mapTap    = document.getElementById('map-tap');
  const portalsEl = document.getElementById('portals');

  const ROWS = 6;   // 'edge' mode only — must match grid-template-rows in f35.css

  // A MAP portal mounts its own map (its URL lives in layout-pages.js with the rest). The tap is a
  // data source only and is never shown — see #map-tap in f35.css, and "the glass" below for why no
  // portal can ever borrow it. (The tap keeps its own src in f35.html and needs no ?nochrome:
  // nothing about it is ever looked at.)

  // Screens this layout can show, and the page each mounts. Every NAV action has an entry, so
  // nothing renders dimmed except this layout's own placeholders (MAIN_EXTRAS).
  //
  // MAIN maps to no page, and `null` is meaningful there — test membership with `in`, not
  // truthiness. Its whole content is its navigation, and this shell's grid already draws that, so
  // there is nothing left for a page to render. (The bezel needs MAIN twice: #info-box chrome in
  // full view, /main in a split pane. Here it needs it zero times; src/web/pages/main/ is
  // untouched and still serves the bezel.)
  //
  // ?nochrome tells a page this shell already shows its own-ship readouts, so it should not draw
  // them twice. MAP does not get the flag: the strip carries no mission name/grid chip of its own
  // (that room goes to the gauges instead), so MAP is the one place left to see them, same as the
  // bezel. Each page owns the option and decides what it means; this layout only picks it. It is a
  // URL flag rather than a message because a page reads it before its first paint, and a message
  // would show the readouts and then take them away on every mount. The bezel passes it to nothing
  // and is unaffected.
  //
  // AVN does not get the flag either — its `?nochrome` handling is not present in avn.html/avn.css
  // at all. AVN's status tiles are bezel/portal-actuated toggles (directly clickable on the page
  // itself too), the strip's own copy stays read-only, and RPM/HEAT aren't on the strip at all, so
  // hiding AVN's content would leave this layout with no way to flip gear/radar/etc. Plain `/avn`
  // (like every other page here) accepts a little duplication of FUEL/THRL with the strip in
  // exchange for keeping AVN's own content — and its only toggle controls — intact.
  // This layout's half of layout-pages.js, which keeps it beside the bezel's table so the two can't
  // quietly diverge — every NAV destination needs an entry in both, or the button is dead here and
  // works there. MAIN maps to no page and `null` is meaningful, so membership is tested with `in`.
  const F35_PAGES = LayoutPages.F35;

  // The telemetry each screen needs, by the tap's own type names. A page that just mounted has
  // missed whatever already arrived, and slices land while other screens are up — so the shell
  // caches every slice and each portal replays the relevant ones.
  //
  // TGT needs no command plumbing: it POSTs its own tgt.* via send-command.js.
  const PAGE_FEEDS = {
    avn: ['avn'],
    afm: ['avn'],   // AFM shares AVN's snapshot (name/parts/failures/pylons) — see forwardSlice's afm case
    rwr: ['rwr', 'mw'],       // scope contacts + incoming-missile warnings
    tgt: ['tgt', 'targets', 'sqd-state', 'td-state-push'],   // sqd/td-state: leader-only TD column
    td: ['targets', 'sqd-state', 'td-state-push'],   // Target Designator (issue #47) — mirrors TGT's
                                                      // own live target list, plus its own squad/td tables
    tgp: ['tgp'],
    wpn: ['loadout', 'cm'],   // 'loadout' is derived, not forwarded as-is — see DERIVED
    bdf: ['bdf'],             // read-only faction-forces block (docs/bdf-page.md)
    pal: ['pal'],             // same, for PRIMEVA
    mis: ['mis'],             // mission-info block (docs/md-pages.md)
    obj: ['obj'],             // active-objectives list (docs/md-pages.md)
    akf: ['akf'],             // kill-feed/session-stats block (docs/akf-page.md)
    rdr: ['rdr'],             // radar contacts (docs/rdr-page.md)
    hsd: ['hsd', 'mapinfo', 'rdr', 'wpt-routes'],  // 360-degree datalink picture + FCR cone
                                                    // (docs/rdr-fcr-hsd.md) + active route overlay
    wpt: ['mapinfo', 'wpt-routes', 'sqd-state'],   // navigation readout + shared navigation library + share-button gate
    map: ['wpt-routes'],              // navigation library (docs/hud-waypoint-indicator.md perf fix) —
                                       // MAP mounts its own map.js/telemetry, so this is its only feed
    // SQD (docs/squadron-transport.md, docs/sse-push-refactor.md) — squad roster/role state now
    // rides this relayed push instead of its own /squad poll.
    sqd: ['sqd-state'],
  };

  // The tap calls it 'targets'; TGT listens for 'tgt-targets'. The bezel renames it in exactly the
  // same place (mfd.js forwardTgtTargetsToFrame), so this mirrors the existing contract rather
  // than inventing one. Every other slice forwards under its own name.
  const FEED_AS = { targets: 'tgt-targets' };

  // Slices needing more than a rename — the portal derives these itself. WPN is the only one: the
  // page shows five rows, so the shell owns *which* five. Pagination is shell state, which is why
  // NAV.wpn is empty and why the bezel hand-rolls its WPN labels too.
  const DERIVED = { loadout: true };

  // Pages carrying their own PAD cursor (pad-cursor.js) — docs/page-cursor.md, docs/tgt-keybind-nav.md.
  // Mirrors the bezel's own PAD_CURSOR_PAGES (mfd.js); kept as its own copy since this layout has
  // no shared module with the bezel to hang it on. AKF is included because its ALL/PLAYER resizer
  // is clickable and needs cursor support. TGP is the one exception to "draws a crosshair": it
  // doesn't use pad-cursor.js, but still wants the raw vector while focused — its on-screen
  // joystick uses it to detect physical PAD Cursor input and hide itself
  // (docs/tgp-manual-control.md's "On-screen joystick").
  const PAD_CURSOR_PAGES = { map: true, tgt: true, td: true, hud: true, rdr: true, wpt: true, sqd: true, akf: true, hsd: true, tgp: true };

  const WPN_MAX_DISPLAY = ROWS - 1;   // row 1 is the nav + CM band; rows 2..6 carry the weapons
  const WPN_ICON_INSET  = 20;         // keeps the image off its band edges, as the bezel does

  // Screens this layout puts on MAIN beyond NAV's — can't go in NAV since NAV is the bezel's menu
  // too, and it has six physical keys for six items. Kept here, they stay F-35's business and the
  // bezel is unaffected (there CFG, MD, RDR, AFM and SQD are their own BEZEL_EXTRAS keys). Each of
  // these has an F35_PAGES entry and renders as a real page (docs/bdf-page.md, src-architecture.md).
  // CFG, MD, RDR, AFM and SQD are frame pages with an F35_PAGES entry — CFG opens the CFG group
  // (HUD/KEY/LYT/RTS — cfg-rates experiment issue #39), landing on HUD by default (mirrors MD
  // staying 'akf' — the action names whichever sibling is the landing page; HUD joined this group
  // and no longer has a MAIN entry of its own, same as RTS). Selecting LYT from that group still
  // opens the layout chooser over the whole glass (GLASS_ACTIONS) exactly as before — only its
  // entry point moved from top-level MAIN into the CFG group. All match where the bezel keeps them
  // — MAIN — so a pilot finds the same names in the same place in either layout.
  //
  // MD is one combined entry covering PAL/BDF — mirrors the bezel's own SCR→MD rename and
  // BDF/PAL fold. Action is 'akf' (AKF is the group's default landing page); NAV.akf/NAV.mis/
  // NAV.obj/NAV.bdf/NAV.pal (shared, consumed generically below) carry the MAIN/AKF/MIS/OBJ/BDF/PAL
  // sub-nav once you're on any of them, with `mark` lighting whichever is current — this layout
  // already renders NAV[page] like any other.
  const MAIN_EXTRAS = [
    { label: 'CFG', action: 'hud' },   // CFG's own MAIN-entry action — lands on HUD now
    { label: 'MD', action: 'akf' },
    { label: 'RDR', action: 'rdr' },   // → RDR hub, landing on FCR at /rdr (docs/rdr-fcr-hsd.md)
    { label: 'AFM', action: 'afm' },   // → AFM airframe page — mirrors BEZEL_EXTRAS.main
    { label: 'SQD', action: 'sqd' },   // → SQD squad page (docs/squadron-transport.md) — mirrors BEZEL_EXTRAS.main
    // EXT is NOT here — it's a real, shared NAV.main entry (docs/extensions-api.md), not this
    // layout's own placeholder; a second entry here would render a duplicate "EXT" item.
  ];

  // Paging actions, and the direction each moves. Not pages, so they dispatch separately.
  const PAGER = { 'wpn-prev': -1, 'wpn-next': 1 };

  // Actions that act on the whole glass rather than the portal they were pressed from. LYT is the
  // only one: the chooser takes the portals' place entirely (showPicker), so which portal offered
  // it doesn't matter — the same reason the bezel's LYT collapses a split instead of filling a pane.
  // Declared here, run later: showPicker is a hoisted declaration further down the file.
  const GLASS_ACTIONS = { lyt: function () { showPicker(true); } };

  // MAP's own actions → the message the map view listens for. Also not pages: they drive the map
  // in place rather than navigating. Same protocol the bezel uses (mfd.js mapSend), but routed to
  // the portal's OWN map — with several maps on the glass, "the map" is no longer unambiguous.
  // rng-in/rng-out (RDR hub range rocker) reuse the same zoom-in/zoom-out action names MAP's
  // zin/zout send — mapSend() here already targets frameWin() generically (unlike the classic
  // shell's mapFrame-specific version), so FCR/HSD need nothing beyond this mapping.
  const MAP_ACTIONS = { flw: 'toggle-follow', zin: 'zoom-in', zout: 'zoom-out', grid: 'toggle-grid',
                         'rng-in': 'zoom-in', 'rng-out': 'zoom-out',
                         'rt-next': 'route-next', 'rt-prev': 'route-prev',
                         'wpt-next': 'waypoint-next', 'wpt-prev': 'waypoint-prev',
                         // HSD's CEN<->DEP toggle (docs/rdr-fcr-hsd.md) — self-mapped since it's
                         // not a MAP relay action, just reusing this generic "post straight to
                         // frameWin()" mechanism the same way rng-in/rng-out already do.
                         'hsd-mode': 'hsd-mode' };

  // ARM/SAFE (docs/radar-master-arms.md) — WPN's own unconditional controls, same shape as
  // MAP_ACTIONS: an action name maps to what it sends, dispatched by command rather than page nav.
  const MASTER_ARMS_ACTIONS = { 'master-arms-on': true, 'master-arms-off': false };
  // Right column, rows 2-3: col 1 rows 2..6 are the weapon-row hit targets (.wpn-hit, f35.css), so
  // col 2 below NEXT's row 1 is free. Appended in itemsFor() rather than living in f35-wpn-paging.js
  // — unconditional, unlike NEXT, so they don't belong in that pure/tested pagination module.
  const MASTER_ARMS_NAV = [
    { label: 'ARM',  action: 'master-arms-on',  cell: { row: 2, col: 2 } },
    { label: 'SAFE', action: 'master-arms-off', cell: { row: 3, col: 2 } },
  ];

  // Combat mode (docs/radar-master-arms.md) — same shape as ARM/SAFE, one row lower: rows 4-5 of
  // the right column are free too (only col 1 rows 2..6 are spoken for, by the weapon-row hits).
  // No ALL item — holding A/A or A/G already resets to ALL (PollTapHold, Keybinds.cs), so ALL just
  // reads as neither of these two lit, same as for the keybinds themselves. That hold behavior is
  // not wired up on these two on-screen buttons — see the pointerdown/pointerup pair on them in
  // renderNav() below.
  const COMBAT_MODE_ACTIONS = { 'combat-mode-aa': 'aa', 'combat-mode-ag': 'ag' };
  const COMBAT_MODE_HOLD_MS = 500;   // matches tgt.js's LONG_MS / pad-cursor.js's DEFAULT_HOLD_MS
  const COMBAT_MODE_NAV = [
    { label: 'A/A', action: 'combat-mode-aa', cell: { row: 4, col: 2 } },
    { label: 'A/G', action: 'combat-mode-ag', cell: { row: 5, col: 2 } },
  ];

  // LCK/MAN and CLR/IR (docs/tgp-manual-control.md's NAV additions) — the bezel's mfd.js twin
  // (placeTgpNavLabels/tgpMarks), same shape as MASTER_ARMS_ACTIONS/COMBAT_MODE_ACTIONS above:
  // an unconditional command pair, dispatched rather than paged to, with its own mark state
  // (markTgpMode/markTgpImg) since NAV.tgp carries no dynamic `mark`. Column 1 is the "go
  // somewhere else" column (MAIN/CFG plus the one-shot TRK/RST/STP below, tgpNavItems); column 2
  // is the "change how the feed looks" column (LCK/MAN, CLR/IR, Z+/Z-), matching mfd.js's own
  // placeTgpNavLabels left/right split.
  const TGP_MODE_ACTIONS = { 'tgp-manual-on': true, 'tgp-manual-off': false };
  const TGP_MODE_NAV = [
    { label: 'LCK', action: 'tgp-manual-off', cell: { row: 1, col: 2 } },
    { label: 'MAN', action: 'tgp-manual-on',  cell: { row: 2, col: 2 } },
  ];
  const TGP_IR_ACTIONS = { 'tgp-ir-on': true, 'tgp-ir-off': false };
  const TGP_IR_NAV = [
    { label: 'CLR', action: 'tgp-ir-off', cell: { row: 3, col: 2 } },
    { label: 'IR',  action: 'tgp-ir-on',  cell: { row: 4, col: 2 } },
  ];
  // Z+/Z- (manual camera zoom) — column 2's own rows 5-6, unlike COMBAT_MODE_ACTIONS/
  // TGP_MODE_ACTIONS/TGP_IR_ACTIONS this isn't an explicit-state "set" (no single boolean value to
  // react to) — it jumps between discrete magnification LEVELS (tgp.zoom.step,
  // TgpManualControl.StepZoom), wired with its own pointerdown/pointerup pair in renderNav() below
  // rather than through dispatch(); the value here is the dir tgp.zoom.step's `index` field
  // expects. Holding repeats the step at a fixed interval (typematic) rather than the physical
  // Cursor Zoom In/Out keybind's own continuous rate — see the wiring below for why
  // (docs/tgp-manual-control.md).
  const TGP_ZOOM_ACTIONS = { 'tgp-zoom-in': 1, 'tgp-zoom-out': -1 };
  const TGP_ZOOM_STEP_INITIAL_DELAY_MS = 350;
  const TGP_ZOOM_STEP_REPEAT_MS = 150;
  const TGP_ZOOM_NAV = [
    { label: 'Z+', action: 'tgp-zoom-in',  cell: { row: 5, col: 2 } },
    { label: 'Z-', action: 'tgp-zoom-out', cell: { row: 6, col: 2 } },
  ];
  // TRK/RST/STP — page-button twins of the Point Track / Manual Control Reset keybinds
  // (docs/tgp-manual-control.md) and the MARK STEER POINT command (docs/steer-points.md). One-shot
  // actions, not explicit-state pairs like the others above, so each is dispatched directly rather
  // than through an ACTIONS lookup. Column 1's rows 3-5 are free (MAIN/CFG only spoke for rows 1-2,
  // tgpNavItems); row 6 stays spare.
  const TGP_TRK_NAV = [
    { label: 'TRK', action: 'tgp-point-track', cell: { row: 3, col: 1 } },
  ];
  const TGP_RST_NAV = [
    { label: 'RST', action: 'tgp-manual-reset', cell: { row: 4, col: 1 } },
  ];
  const TGP_STP_NAV = [
    { label: 'STP', action: 'tgp-mark-steerpoint', cell: { row: 5, col: 1 } },
  ];

  // MAP's placement mirrors mfd.js's explicit control banks rather than using generic overflow. The action
  // lists (SplitSlots.MAP_FULL_LEFT/RIGHT/mapFullRight) are shared with the classic bezel — see
  // that module's own comment — so the two layouts can't drift out of sync. Route cycling and the
  // context-sensitive W+/W- or S+/S- pair are filtered independently; WPT remains reachable.
  function mapNavItems() {
    const byAction = {};
    (NAV.map || []).forEach(function (item) { byAction[item.action] = item; });
    const wptData = WaypointsStore.load();
    const hasRoutes = wptData.routes.length > 0;
    const hasActiveRoute = !!WaypointsStore.getActiveRoute();
    const hasSteerPoints = (wptData.steerPoints || []).length > 0;
    const left = SplitSlots.MAP_FULL_LEFT.map(function (a, i) {
      return Object.assign({}, byAction[a], { cell: { row: i + 1, col: 1 } });
    });
    return left.concat(SplitSlots.mapFullRight(hasRoutes, hasActiveRoute, hasSteerPoints).map(function (a, i) {
      const item = Object.assign({}, byAction[a]);
      item.label = SplitSlots.mapActionLabel(a, item.label, hasActiveRoute);
      item.cell = { row: i + 1, col: 2 };
      return item;
    }));
  }

  // TGP's own placement (mfd.js's own full-view twin, its dedicated 'tgp' branch): MAIN/CFG stack
  // at the top of column 1, with TRK/RST/STP (TGP_TRK_NAV/TGP_RST_NAV/TGP_STP_NAV below) filling
  // the rest of that column — matching the bezel's own left-bank order.
  function tgpNavItems() {
    return [
      Object.assign({}, NAV.tgp[0], { cell: { row: 1, col: 1 } }),
      Object.assign({}, NAV.tgp[1], { cell: { row: 2, col: 1 } }),
    ];
  }

  // Where a screen's NAV items sit. Default 'edge' = the bezel's left key bank, minus the bezel.
  // MAIN is 'center': its labels ARE the screen, so they own the middle of the glass instead of
  // hugging an edge that frames nothing. Both modes consume NAV in order — only placement differs,
  // which is exactly the split the seam predicts.
  const NAV_LAYOUT = { main: 'center' };

  const slices  = Object.create(null);   // the tap's latest message, by type — shared by all portals
  // Not the source of orientation — a portal measures its own box for that (forwardOrientation).
  // This only says "the glass turned", which is one of the things that resizes a portal.
  const orientMq = window.matchMedia('(orientation: portrait)');
  let   portals = [];   // the glass, left to right

  // Extension pages (docs/extensions-api.md) have no F35_PAGES entry — folding
  // ExtNav.isExtensionPage into `has` is what makes them a valid `showPage`/`dispatch`/`canDo`
  // target everywhere those three already gate on it, with no other change needed here.
  function has(page) { return Object.prototype.hasOwnProperty.call(F35_PAGES, page) || ExtNav.isExtensionPage(page); }
  function feedsFor(page) { return PAGE_FEEDS[page] || (ExtNav.isExtensionPage(page) ? ['ext_' + page] : []); }
  function canDo(action) {
    return has(action) || (action in PAGER) || (action in MAP_ACTIONS) || (action in GLASS_ACTIONS) ||
           (action in MASTER_ARMS_ACTIONS) || (action in COMBAT_MODE_ACTIONS) ||
           (action in TGP_MODE_ACTIONS) || (action in TGP_IR_ACTIONS) || (action in TGP_ZOOM_ACTIONS);
  }

  // 'edge' placement: an item's index → its cell. The left column, top-down, IS the bezel's left
  // key bank — the same derivation mfd.js fullViewSlot() uses, which is why NAV needs no placement
  // hints for full view — including the overflow into column 2 once 6 rows fill (MAP's R+/R- is
  // the first 'edge' page past that mark; WPN's NEXT already proved col 2 works via its own
  // item.cell). ('center' needs no function: items flow in NAV order and the grid's own columns
  // arrange them.)
  function cellOf(i) { return i < ROWS ? { row: i + 1, col: 1 } : { row: i - ROWS + 1, col: 2 }; }

  // ── Corner grips ─────────────────────────────────────────────────────────────────────
  // The F-35's expand/retract control: an outline triangle in a portal's bottom corner. Portal
  // chrome, not navigation — it resizes the glass rather than choosing a page, so it lives outside
  // the label grid (which renderNav rebuilds).
  //
  // A grip sits in the corner facing what it acts on, and only its DIRECTION says what that is:
  //   * outward — take the neighbour on that side, becoming twice as wide.
  //   * inward  — give back the slot it took, splitting in two again.
  // Which grips a portal has is f35-glass's rule, not this file's: it depends on how the whole
  // glass is currently divided, not on any one portal.
  const GRIP_POINTS = { left: '2,50 98,2 98,98', right: '98,50 2,2 2,98' };
  const GRIP_LABEL = {
    'merge-left':  'Expand over the portal to the left',
    'merge-right': 'Expand over the portal to the right',
    'split':       'Split this portal in two',
  };

  // Drawn as SVG because the reference's triangles are outlines, and the CSS border trick only
  // makes solid ones. The triangle fills its square button; non-scaling-stroke (in the CSS) keeps
  // the outline 2px however large that gets, and the 2-unit inset keeps the stroke inside the box.
  function makeGrip(spec, onClick) {
    const b = document.createElement('button');
    b.type = 'button';
    b.className = 'portal-grip ' + spec.corner;
    b.title = GRIP_LABEL[spec.action];
    b.setAttribute('aria-label', GRIP_LABEL[spec.action]);
    const svg = document.createElementNS('http://www.w3.org/2000/svg', 'svg');
    svg.setAttribute('viewBox', '0 0 100 100');
    svg.setAttribute('aria-hidden', 'true');
    const poly = document.createElementNS('http://www.w3.org/2000/svg', 'polygon');
    poly.setAttribute('points', GRIP_POINTS[spec.aim]);
    svg.appendChild(poly);
    b.appendChild(svg);
    b.addEventListener('click', function () { onClick(spec.action); });
    return b;
  }

  // ── Portal ───────────────────────────────────────────────────────────────────────────
  // One independent MFD: a page iframe with a label grid over it, and the state that belongs to
  // *this* screen rather than the shell — which page is up, where its WPN list is paged to, and
  // whether its map is following. Everything a second portal must not share lives in here; only
  // the telemetry cache and the tap are shell-wide.
  function makePortal(onGrip, onNavRendered) {
    const el    = document.createElement('div');
    const frame = document.createElement('iframe');
    const grid  = document.createElement('div');
    el.className    = 'portal';
    frame.className = 'page-frame';
    frame.title     = 'page';
    grid.className  = 'nav-grid';
    el.appendChild(frame);
    el.appendChild(grid);
    // SAVE/LOAD LAYOUT — see wireLayoutKeydown's own comment further down: every portal's content
    // is its own iframe, so it needs the keydown handler re-attached here too.
    wireLayoutKeydown(frame);

    let currentPage = null;
    let wpnPage     = 0;    // 0-indexed pagination state
    let wpnNavKey   = '';   // what this grid last drew; guards a per-tick rebuild
    let wpnSelSeen  = null; // last selWeapon this portal followed; guards the page jump below
    let followOn    = false;
    let gridOn      = false;   // corrected as soon as the map reports its real (persisted) state

    // This portal's footprint on the glass: one slot, or two with a memory of which side it ate.
    // f35-glass reads these to decide what the grips offer.
    const cell = { span: 1 };

    // Grips come and go with the arrangement — merging removes a neighbour and with it a divider —
    // so they are rebuilt rather than re-aimed. `api` is passed back so the shell can find this
    // portal's current index at click time, which merging shifts.
    function setGrips(specs) {
      el.querySelectorAll('.portal-grip').forEach(function (g) { g.remove(); });
      specs.forEach(function (spec) {
        el.appendChild(makeGrip(spec, function (action) { onGrip(api, action); }));
      });
    }

    function frameWin() { return frame.contentWindow; }
    // A portal showing MAP mounts its own, so its map IS its page — the tap is never displayed.
    function isMapWin(w) { return currentPage === 'map' && w === frameWin(); }

    // ── Feeds ──────────────────────────────────────────────────────────────────────────
    function forwardSlice(type) {
      if (DERIVED[type]) return forwardWpn();
      if (currentPage === 'hsd' && (type === 'hsd' || type === 'mapinfo' || type === 'rdr')) return forwardHsd();
      // AFM reuses the 'avn' feed but under its own message type (mirrors mfd.js
      // forwardAfmToFrame) — a per-page rename, unlike FEED_AS below which is global per type and
      // would also rename AVN's own 'avn' feed if used for this.
      if (type === 'avn' && currentPage === 'afm') return forwardAfm();
      const w = frameWin(), m = slices[type];
      if (!w || !m) return;
      // Extension slices (docs/extensions-api.md) always rename to the plain 'ext' type on the
      // wire — the same contract the classic bezel's forwardExtToPanes/Frame use — so an
      // extension's page.js is written once and renders identically under either layout. `id`
      // is dropped: the extension already knows who it is.
      if (type.indexOf('ext_') === 0) { w.postMessage({ mfd: true, type: 'ext', data: m.data }, '*'); return; }
      w.postMessage(FEED_AS[type] ? Object.assign({}, m, { type: FEED_AS[type] }) : m, '*');
    }
    function forwardAfm() {
      const w = frameWin(), m = slices.avn;
      if (!w || !m) return;
      w.postMessage({ mfd: true, type: 'afm', name: m.name, parts: m.parts, failures: m.failures, pylons: m.pylons }, '*');
    }
    function forwardHsd() {
      const w = frameWin(), hsd = slices.hsd || {}, mapinfo = slices.mapinfo || {}, rdr = slices.rdr || {};
      if (!w) return;
      w.postMessage({ mfd: true, type: 'hsd', metric: !!hsd.metric, hdg: mapinfo.hdg || 0,
                      ownX: mapinfo.x || 0, ownZ: mapinfo.z || 0,
                      radarPresent: !!rdr.present, radarRange: rdr.range || 0, radarCone: rdr.cone || 0,
                      items: Array.isArray(hsd.items) ? hsd.items : [],
                      focusedTargetId: hsd.focusedTargetId || 0 }, '*');
    }
    function onSlice(type) {
      if (feedsFor(currentPage).indexOf(type) !== -1) forwardSlice(type);
      // LCK/MAN/CLR/IR can change without the page changing (docs/tgp-manual-control.md's NAV
      // additions) — same "re-apply on every tick" need as markMasterArms/markCombatMode, which
      // get theirs via the 'loadout' feed's own forwardWpn() instead since WPN is a DERIVED feed.
      if (type === 'tgp' && currentPage === 'tgp') { markTgpMode(); markTgpImg(); }
    }
    // Everything the current page needs — on its load, and whenever it changes.
    function forwardToPage() {
      feedsFor(currentPage).forEach(forwardSlice);
      if (currentPage === 'wpn') { forwardWpnLayout(); forwardOrientation(); }
      // docs/map-cursor.md: a fresh document means a fresh message listener, so an earlier
      // cursor-focus post (sent the moment this portal's src changed, before its script had
      // attached one) was silently dropped — and a plain re-run of syncCursorFocus() wouldn't
      // resend it either, since frameWin()'s identity survives the reload. Resend directly,
      // bypassing that dedup, whenever this freshly-loaded portal is the one eligible. Any
      // PAD_CURSOR_PAGES page can land here, not just MAP — TGT/HUD/RDR reload their iframe on
      // every page switch exactly like MAP does.
      if (PAD_CURSOR_PAGES[currentPage] && focusedCursorWindow() === frameWin())
        frameWin().postMessage({ mfd: true, action: 'cursor-focus', on: true }, '*');
    }

    // ── WPN ────────────────────────────────────────────────────────────────────────────
    // The one page needing geometry from this layout. Everything else places itself: AVN/TGP/RWR
    // stay in their default profiles and TGT is fully clickable. WPN's `full` profile is the only
    // one that renders a weapon image, and it lays out solely against forwarded rects — so the
    // escape hatch docs/layouts.md banked on (drive the `compact` profile) can't serve a
    // full-screen WPN: compact scatters weapons into four corners and draws no image at all.
    //
    // So this layout supplies its own rects, derived from its grid instead of the bezel's
    // separators. The page is untouched and cannot tell the difference; the row bands ARE the key
    // bands.

    // This portal's slice + nav, from the loadout and its page state. All the paging math (clamp,
    // slice boundaries, MAIN/PREV/NEXT labels) lives in the pure f35-wpn-paging module so
    // f35-wpn-paging.test.js can pin it; everything below just reads this.
    function wpnList()  { return (slices.loadout && slices.loadout.items) || []; }
    function wpnState() { return F35WpnPaging.wpnPaging(wpnList(), wpnPage, WPN_MAX_DISPLAY); }

    // Slice the loadout to this portal's page and hand the page its five rows.
    //
    // The labels and hit targets depend on this slice, but a 'loadout' arrives on every tick and
    // most carry nothing but an ammo count. Re-rendering on each would destroy and rebuild the
    // very buttons under the pilot's cursor — the bezel never had to care, since its keys are
    // static DOM and only their labels get replaced. So rebuild only when something the grid shows
    // actually changes: the page, the page count, or the visible names. wpn.js keys its own row
    // rebuild the same way (layout + names).
    function forwardWpn() {
      const w = frameWin(), lo = slices.loadout;
      if (!w || !lo) return;
      // Follow the in-game selection to its page when it CHANGES (the weapon keybinds' cycle keys
      // select, possibly onto another page) — the bezel shell does the same. Change-gated so manual
      // paging survives the per-tick ammo loadouts.
      if (lo.selWeapon && lo.selWeapon !== wpnSelSeen) {
        wpnSelSeen = lo.selWeapon;
        const i = wpnList().findIndex(function (it) { return it.n === lo.selWeapon; });
        if (i >= 0) wpnPage = Math.floor(i / WPN_MAX_DISPLAY);
      }
      const st = wpnState();
      wpnPage = st.page;
      w.postMessage({ mfd: true, type: 'wpn', items: st.visible, selWeapon: lo.selWeapon,
                      softGun: lo.softGun || null, softRel: lo.softRel || null,
                      masterArmsOn: lo.masterArmsOn !== false,
                      page: st.maxPage > 0 ? st.page + 1 : 1, pages: st.maxPage + 1 }, '*');
      const key = st.page + '|' + st.maxPage + '|' + st.visible.map(function (it) { return it.n; }).join(',');
      if (currentPage === 'wpn' && key !== wpnNavKey) { wpnNavKey = key; renderNav(); }
      // masterArmsOn/combatMode can change without the page/weapon list changing, so re-apply them
      // every tick — renderNav() above is change-gated and would otherwise miss that case.
      if (currentPage === 'wpn') { markMasterArms(); markCombatMode(); }
    }

    // Row 1 is the CM band; rows 2..6 are the weapon slots; the image spans rows 2..6. The grid and
    // the frame fill the same box — the grid is inset:0 in the portal, the frame is 100%/100% of it
    // — so a row's offset is already the frame's own coordinate space, with no frameTop to subtract
    // unlike the bezel reading shell-side separators. (The portal's border shrinks both together.)
    function forwardWpnLayout() {
      const w = frameWin();
      if (!w) return;
      const rowH = frame.getBoundingClientRect().height / ROWS;
      const slots = [];
      for (let k = 0; k < WPN_MAX_DISPLAY; k++) slots.push({ top: (k + 1) * rowH, height: rowH });
      w.postMessage({ mfd: true, type: 'wpn-layout', layout: 'full', slots: slots,
                      cmTop: 0, cmHeight: rowH,
                      iconTop: rowH + WPN_ICON_INSET,
                      iconHeight: (ROWS - 1) * rowH - 2 * WPN_ICON_INSET }, '*');
    }

    // WPN is the only page here keying CSS off the orientation class: 'portrait' turns its weapon
    // image 90° and swaps its dimensions, to fill a box that is taller than it is wide. A page
    // can't read this from its own box — an iframe's media query sees only itself — so the host
    // has to tell it.
    //
    // This reports the PORTAL's shape, which is where the F-35 parts company with the bezel. The
    // bezel reports the window's on purpose: its panes are wide-and-short, so a pane measuring
    // itself would call a portrait device landscape, and the app's real orientation is the useful
    // answer. Portals are the opposite — a quarter of a panoramic glass is 320x720, genuinely
    // portrait on a landscape screen, and the page needs to know about the box it is actually in.
    // Reporting the window there leaves the weapon image an unrotated sliver in a tall column.
    // See docs/layouts.md, "Per-portal orientation".
    function forwardOrientation() {
      const w = frameWin();
      if (!w) return;
      const r = el.getBoundingClientRect();
      w.postMessage({ mfd: true, type: 'orient',
                      orientation: r.width < r.height ? 'portrait' : 'landscape' }, '*');
    }

    // Invisible click targets over the weapon bands. The page draws the rows; this is the F-35's
    // line-select key — the same weapon.select the bezel sends, with no physical key to press.
    function addWeaponHits() {
      wpnState().visible.forEach(function (it, k) {
        const b = document.createElement('button');
        b.className = 'wpn-hit';
        b.style.gridRow = String(k + 2);   // rows 2..6, aligned to the slots forwarded above
        b.title = it.n;
        b.setAttribute('aria-label', 'Select ' + it.n);
        b.addEventListener('click', function () {
          sendCommand('weapon.select', { wname: it.n }).catch(function () {});
        });
        grid.appendChild(b);
      });
    }

    // ── Nav ────────────────────────────────────────────────────────────────────────────
    // What this screen puts on the grid.
    //   main — NAV's items plus this layout's own (MAIN_EXTRAS), alphabetical. Sorting here rather
    //          than in NAV keeps ordering a rendering choice: it interleaves HUD/PAL/BDF among the
    //          NAV items, where the bezel just shows NAV's six in their given order.
    //   wpn  — nothing from NAV (it's empty by design); its labels are pagination.
    //   map  — map controls plus WPT/route/context-navigation actions via explicit cells
    //          (mapNavItems — see its own comment).
    //   tgp  — MAIN + CFG via explicit cells (tgpNavItems), CFG pinned to the bottom row rather
    //          than landing right under MAIN via the generic index-into-6-rows overflow.
    function itemsFor(page) {
      if (page === 'wpn') return wpnState().nav.concat(MASTER_ARMS_NAV, COMBAT_MODE_NAV);
      if (page === 'map') return mapNavItems();
      if (page === 'tgp') return tgpNavItems().concat(TGP_MODE_NAV, TGP_IR_NAV, TGP_ZOOM_NAV, TGP_STP_NAV, TGP_TRK_NAV, TGP_RST_NAV);
      const items = (NAV[page] || []).slice();
      if (page !== 'main') return items;
      return items.concat(MAIN_EXTRAS).sort(function (a, b) { return a.label.localeCompare(b.label); });
    }

    // Drive this portal's own map. Not the tap: several maps can be on the glass at once, so
    // "the map" only means something per portal.
    function mapSend(action) {
      const w = frameWin();
      if (w) w.postMessage({ mfd: true, action: action }, '*');
    }

    // FLW is a toggle, so it has to show its state. The bezel puts that in a separate FOLLOW chip
    // over the screen; with no chrome to hang one on, the label carries it — it's the control
    // itself. Per-portal, since each map follows independently. The map reports the state back (it
    // also follows on its own when the player moves), so this reflects the map rather than
    // assuming the click won.
    function setFollow(on) { followOn = on; markFollow(); }
    function markFollow() {
      const b = grid.querySelector('.nav-item[data-action="flw"]');
      if (b) b.classList.toggle('on', followOn);
    }
    // GRID's twin — same reasoning: the label is the control, so it carries the state, reflecting
    // the map's own report rather than assuming the click won.
    function setGrid(on) { gridOn = on; markGrid(); }
    function markGrid() {
      const b = grid.querySelector('.nav-item[data-action="grid"]');
      if (b) b.classList.toggle('on', gridOn);
    }

    // ARM/SAFE (docs/radar-master-arms.md) reflect masterArmsOn straight off the loadout slice —
    // no local state to track, unlike followOn (which the map reports back independently). Called
    // from forwardWpn() on every tick (not gated by the nav-rebuild key), since the amber state can
    // change without the weapon list/page changing.
    function markMasterArms() {
      const armed = !(slices.loadout && slices.loadout.masterArmsOn === false);
      const on  = grid.querySelector('.nav-item[data-action="master-arms-on"]');
      const off = grid.querySelector('.nav-item[data-action="master-arms-off"]');
      if (on)  on.classList.toggle('on', armed);
      if (off) off.classList.toggle('on', !armed);
    }

    // Combat mode (docs/radar-master-arms.md) — same shape as markMasterArms above.
    function markCombatMode() {
      const mode = (slices.loadout && slices.loadout.combatMode) || 'all';
      COMBAT_MODE_NAV.forEach(function (item) {
        const b = grid.querySelector('.nav-item[data-action="' + item.action + '"]');
        if (b) b.classList.toggle('on', COMBAT_MODE_ACTIONS[item.action] === mode);
      });
    }

    // LCK/MAN/CLR/IR (docs/tgp-manual-control.md's NAV additions) — read straight off the cached
    // tgp slice rather than tracked local state (no click here can change it on its own, unlike
    // followOn/gridOn). The actual rule lives in tgp-marks.js (shared with mfd.js's own equivalent,
    // so the two can't drift). Called on every 'tgp' slice tick (onSlice) as well as on nav rebuild.
    function tgpMarks() {
      const s = slices.tgp;
      const data = s && s.data;
      return TgpMarks.tgpMarks(data ? data.cnt : 0, s && s.manual, data && data.ir);
    }
    function markTgpMode() {
      const marks = tgpMarks();
      const tgt = grid.querySelector('.nav-item[data-action="tgp-manual-off"]');
      const man = grid.querySelector('.nav-item[data-action="tgp-manual-on"]');
      if (tgt) tgt.classList.toggle('on', marks.tgt);
      if (man) man.classList.toggle('on', marks.man);
    }
    function markTgpImg() {
      const marks = tgpMarks();
      const clr = grid.querySelector('.nav-item[data-action="tgp-ir-off"]');
      const ir  = grid.querySelector('.nav-item[data-action="tgp-ir-on"]');
      if (clr) clr.classList.toggle('on', marks.clr);
      if (ir)  ir.classList.toggle('on', marks.ir);
    }

    // Decorative MASTER/MODE labels (docs/radar-master-arms.md) — the bezel's mfd.js equivalent,
    // adapted to this layout: no separator element sits between ARM/SAFE (adjacent grid rows, not
    // bezel keys with a real gap), so the vertical centre is just the midpoint between the two
    // buttons' own rects; horizontal centre likewise averages their rects rather than sharing their
    // right-edge margin (which two different-width labels don't actually share, same reasoning as
    // the bezel version). Recomputed from scratch each call rather than diffed in place — cheap, and
    // simpler than tracking whether a resize invalidated the last position.
    function placeWpnDecorator(actionA, actionB, word) {
      const a = grid.querySelector('.nav-item[data-action="' + actionA + '"]');
      const b = grid.querySelector('.nav-item[data-action="' + actionB + '"]');
      if (!a || !b) return;
      const gRect = grid.getBoundingClientRect();
      const aRect = a.getBoundingClientRect();
      const bRect = b.getBoundingClientRect();
      const centerX = ((aRect.left + aRect.right) / 2 + (bRect.left + bRect.right) / 2) / 2;
      const centerY = (aRect.bottom + bRect.top) / 2;
      const el = document.createElement('div');
      el.className = 'wpn-decor';
      el.innerHTML =
        '<svg width="12" height="8" viewBox="0 0 12 8"><polygon points="6,0 12,8 0,8" fill="currentColor"/></svg>' +
        '<div class="wpn-decor-word">' + word + '</div>' +
        '<svg width="12" height="8" viewBox="0 0 12 8"><polygon points="0,0 12,0 6,8" fill="currentColor"/></svg>';
      grid.appendChild(el);
      el.style.left = (centerX - gRect.left - el.offsetWidth / 2) + 'px';
      el.style.top  = (centerY - gRect.top - el.offsetHeight / 2) + 'px';
    }
    function placeWpnDecorators() {
      grid.querySelectorAll('.wpn-decor').forEach(function (el) { el.remove(); });
      placeWpnDecorator('master-arms-on', 'master-arms-off', 'MASTER');
      placeWpnDecorator('combat-mode-aa', 'combat-mode-ag', 'MODE');
    }

    function dispatch(action) {
      // EXT (docs/extensions-api.md): F35_PAGES.ext already names the hub page, so `has('ext')` is
      // true and the generic showPage(action) tail below lands there like any other page. NAV.ext
      // (ext-nav.js) lists MAIN plus one entry per installed extension; picking one of those is
      // `has(action)` too, via ExtNav.isExtensionPage. The one thing EXT does need a special case
      // for: a rescan on every click, not just at boot — an extension whose own Awake() hadn't
      // registered it yet the first time this shell fetched now gets picked up without a full page
      // reload. renderNav() alone (not showPage again) once the rescan lands, so this only rebuilds
      // the nav grid, not the iframe's src — but only if EXT is still open when the fetch resolves.
      if (action === 'ext') {
        showPage('ext');
        ExtNav.load(NAV).then(function () { if (currentPage === 'ext') renderNav(); });
        return;
      }
      if (action in PAGER)       { wpnPage = wpnState().page + PAGER[action]; forwardWpn(); return; }
      if (action in MAP_ACTIONS) { mapSend(MAP_ACTIONS[action]); return; }
      if (action in GLASS_ACTIONS) { GLASS_ACTIONS[action](); return; }
      if (action in MASTER_ARMS_ACTIONS) {
        sendCommand('master-arms.set', { on: MASTER_ARMS_ACTIONS[action] }).catch(function () {});
        return;
      }
      if (action in COMBAT_MODE_ACTIONS) {
        sendCommand('combat-mode.set', { group: COMBAT_MODE_ACTIONS[action] }).catch(function () {});
        return;
      }
      if (action in TGP_MODE_ACTIONS) {
        sendCommand('tgp.manual.set', { on: TGP_MODE_ACTIONS[action] }).catch(function () {});
        return;
      }
      if (action in TGP_IR_ACTIONS) {
        sendCommand('tgp.ir.set', { on: TGP_IR_ACTIONS[action] }).catch(function () {});
        return;
      }
      if (action === 'tgp-mark-steerpoint') {
        sendCommand('tgp.mark-steerpoint').catch(function () {});
        return;
      }
      if (action === 'tgp-point-track') {
        sendCommand('tgp.point-track').catch(function () {});
        return;
      }
      if (action === 'tgp-manual-reset') {
        sendCommand('tgp.manual-reset').catch(function () {});
        return;
      }
      if (has(action)) showPage(action);
    }

    function renderNav() {
      const mode = NAV_LAYOUT[currentPage] || 'edge';
      grid.className = 'nav-grid ' + mode;
      grid.dataset.page = currentPage;   // lets f35.css special-case a page's labels (see TGT)
      grid.textContent = '';
      itemsFor(currentPage).forEach(function (item, i) {
        // An item may name its own cell (WPN's NEXT sits top-right); otherwise it takes the
        // index's. NAV items never carry placement — nav-model.test.js enforces that — so this
        // only ever fires for items this layout built itself.
        const cell = item.cell || cellOf(i);
        if (mode === 'edge' && cell.row > ROWS) {
          console.warn('[f35] ' + currentPage + '[' + i + '] "' + item.label +
                       '" falls outside the ' + ROWS + '-row grid — not placed');
          return;
        }
        const wired = canDo(item.action);
        const b = document.createElement('button');
        b.className   = 'nav-item' + (wired ? '' : ' pending') + (cell.col === 2 ? ' col-right' : '')
                       + (item.mark ? ' on' : '');   // e.g. NAV.bdf/NAV.pal lighting the live one
        b.textContent = item.label;
        b.dataset.action = item.action;   // markFollow finds FLW by this
        if (mode === 'edge') {
          b.style.gridRow    = String(cell.row);
          b.style.gridColumn = String(cell.col);
        }
        if (wired && item.action in COMBAT_MODE_ACTIONS) {
          // Press-and-HOLD resets combat mode to ALL (see the COMBAT_MODE_ACTIONS comment above) —
          // a real pointerdown/pointerup pair exists here (unlike soiAct-driven presses elsewhere),
          // so a client-only timer is enough; no server plumbing needed for this on-screen button.
          let holdTimer = null, holdFired = false;
          const clearHold = function () { if (holdTimer) { clearTimeout(holdTimer); holdTimer = null; } };
          b.addEventListener('pointerdown', function () {
            holdFired = false;
            holdTimer = setTimeout(function () {
              holdFired = true;
              sendCommand('combat-mode.set', { group: 'all' }).catch(function () {});
            }, COMBAT_MODE_HOLD_MS);
          });
          b.addEventListener('pointerup', clearHold);
          b.addEventListener('pointercancel', clearHold);
          b.addEventListener('pointerleave', clearHold);
          b.addEventListener('click', function () { if (!holdFired) dispatch(item.action); });
        } else if (wired && item.action === 'wpt-prev') {
          // Press-and-HOLD resets the active route to its first waypoint instead of stepping back
          // one — same shape as the combat-mode hold above, mirroring Keybinds.cs's PollTapHold
          // pair on the physical PC keybind (map-waypoint-prev). waypoint-reset (map.js) is a
          // route-only reset that no-ops with no active route.
          let holdTimer = null, holdFired = false;
          const clearHold = function () { if (holdTimer) { clearTimeout(holdTimer); holdTimer = null; } };
          b.addEventListener('pointerdown', function () {
            holdFired = false;
            holdTimer = setTimeout(function () {
              holdFired = true;
              mapSend('waypoint-reset');
            }, COMBAT_MODE_HOLD_MS);
          });
          b.addEventListener('pointerup', clearHold);
          b.addEventListener('pointercancel', clearHold);
          b.addEventListener('pointerleave', clearHold);
          b.addEventListener('click', function () { if (!holdFired) dispatch(item.action); });
        } else if (wired && item.action in TGP_ZOOM_ACTIONS) {
          // Discrete magnification LEVELS (tgp.zoom.step, TgpManualControl.StepZoom) — one jump
          // per press. Holding repeats the step at a fixed interval (typematic — once
          // immediately, then repeat after an initial delay) until released, so the plain
          // dispatch() click fallback below is skipped entirely — dispatch('tgp-zoom-in') would
          // be a harmless no-op anyway (not in any of its recognized dictionaries, and
          // has('tgp-zoom-in') is false), but there is no reason to fire it on top of the tap
          // step pointerdown already sends.
          const dir = TGP_ZOOM_ACTIONS[item.action];
          let repeatTimer = null;
          const stepZoom = function () { sendCommand('tgp.zoom.step', { index: dir }).catch(function () {}); };
          b.addEventListener('pointerdown', function () {
            stepZoom();
            repeatTimer = setTimeout(function repeat() {
              stepZoom();
              repeatTimer = setTimeout(repeat, TGP_ZOOM_STEP_REPEAT_MS);
            }, TGP_ZOOM_STEP_INITIAL_DELAY_MS);
          });
          const stop = function () { clearTimeout(repeatTimer); repeatTimer = null; };
          b.addEventListener('pointerup', stop);
          b.addEventListener('pointercancel', stop);
          b.addEventListener('pointerleave', stop);
        } else if (wired) {
          b.addEventListener('click', function () { dispatch(item.action); });
        } else {
          b.disabled = true;
        }
        grid.appendChild(b);
      });
      if (currentPage === 'wpn') { addWeaponHits(); markMasterArms(); markCombatMode(); placeWpnDecorators(); }
      if (currentPage === 'tgp') {
        markTgpMode(); markTgpImg();
        placeWpnDecorator('tgp-manual-off', 'tgp-manual-on', 'MODE');
        placeWpnDecorator('tgp-ir-off', 'tgp-ir-on', 'IMG');
        placeWpnDecorator('tgp-zoom-in', 'tgp-zoom-out', 'ZOOM');
      }
      // ZOOM between Z+/Z- and ROUTE between R+/R- — same decorator, MAP's twin of WPN's
      // MASTER/MODE. Found by data-action, so the 2-column overflow (cellOf) needs no
      // special-casing here — the decorator just measures wherever the two buttons actually landed.
      if (currentPage === 'map') { placeWpnDecorator('zin', 'zout', 'ZOOM'); placeWpnDecorator('rt-next', 'rt-prev', 'ROUTE'); placeWpnDecorator('wpt-next', 'wpt-prev', WaypointsStore.getActiveRoute() ? 'WYPT' : 'STRP'); }
      // RANGE between R+/R- — RDR/HSD's twin.
      if (currentPage === 'rdr' || currentPage === 'hsd') placeWpnDecorator('rng-out', 'rng-in', 'RANGE');
      markFollow();   // the labels were just rebuilt; re-apply the state to the new FLW
      markGrid();     // ...and the state to the new GRID
      // The grid was just rebuilt, so an SOI cursor mark on one of its items is gone — let the shell
      // re-apply it if this is the focused portal (the F-35 twin of mfd.js's post-rebuild renderSoiCursor).
      if (onNavRendered) onNavRendered(api);
    }

    function showPage(name) {
      if (!has(name)) return;
      currentPage = name;
      wpnNavKey = '';   // entering any page redraws the grid; don't let a stale key suppress it
      // A page with no content of its own (MAIN) blanks the frame rather than hiding it: the
      // iframe's background is the glass colour, so what shows through is the label grid on black.
      frame.src = F35_PAGES[name] || (ExtNav.isExtensionPage(name) ? '/ext/' + name : 'about:blank');
      renderNav();   // forwardToPage reruns on the frame's load
    }

    frame.addEventListener('load', forwardToPage);

    const api = {
      el: el,
      cell: cell,
      showPage: showPage,
      onSlice: onSlice,
      isMapWin: isMapWin,
      setFollow: setFollow,
      setGrid: setGrid,
      setGrips: setGrips,
      // For the SOI cursor: the page this portal shows (to tell a navigating SELECT from an
      // in-place one) and its enabled nav labels, in reading order, as the cursor's targets.
      page: function () { return currentPage; },
      // This portal's frame window regardless of what page it's showing — unlike cursorWin() below,
      // not gated to PAD_CURSOR_PAGES. Used to identify which portal a page-originated message
      // (e.source) came from (issue #47 follow-up: TD's DESIGNATE-returns-to-TGT redirect).
      frameWin: function () { return frameWin(); },
      navItems: function () { return [].slice.call(grid.querySelectorAll('.nav-item:not([disabled])')); },
      // docs/page-cursor.md — this portal's frame window, but only while it's showing a page with its
      // own PAD cursor (null otherwise), so the glass-level cursor forwarding can't target a page
      // with nothing listening for it.
      cursorWin: function () { return PAD_CURSOR_PAGES[currentPage] ? frameWin() : null; },
      // Rebuilds just the label grid — unlike showPage, doesn't touch frame.src, so it can't
      // reload (and so lose pan/zoom on) an already-showing MAP. The glass's
      // own 'storage' listener uses this to pick up an active route appearing/disappearing live.
      refreshNav: renderNav,
      // Flex-grow tracks the span, so a merged portal takes exactly the two slots it owns and its
      // neighbours keep theirs. All four slots are the same width, so the arithmetic is just the
      // span — no wrapper elements, no percentages.
      applySpan: function () { el.style.flexGrow = String(cell.span); },
      // The portal's box just changed. WPN is the only page that cares, and it cares twice: its
      // rects come from the box, and its orientation IS the box's shape — a quarter is portrait, a
      // half may not be. Every other page reflows itself with CSS; the map notices via its own
      // resize handling, and must not be re-entered here — that would reload the iframe and throw
      // away the zoom and pan the pilot set.
      resized: function () {
        if (currentPage === 'wpn') { forwardOrientation(); forwardWpnLayout(); placeWpnDecorators(); }
        if (currentPage === 'map') { placeWpnDecorator('zin', 'zout', 'ZOOM'); placeWpnDecorator('rt-next', 'rt-prev', 'ROUTE'); placeWpnDecorator('wpt-next', 'wpt-prev', WaypointsStore.getActiveRoute() ? 'WYPT' : 'STRP'); }
        if (currentPage === 'rdr' || currentPage === 'hsd') placeWpnDecorator('rng-out', 'rng-in', 'RANGE');
      },
      destroy: function () { el.remove(); },
    };
    return api;
  }

  // ── The glass ────────────────────────────────────────────────────────────────────────
  // The F-35's panoramic display is one wide sheet carrying four side-by-side portals, each an
  // independent MFD — not a 2x2 grid. Four slots, and a portal fills one or two of them, so any
  // two ADJACENT portals may merge. Which arrangements that allows, and
  // which grips each portal gets, is f35-glass's rule (and f35-glass.test.js pins it): five
  // layouts, no triples, and every merge reachable from either side.
  //
  // The glass is never one screen — a merge is a pair and nothing wider, so at least two portals
  // always remain. The real PCD isn't one screen either.
  //
  // What that costs: no portal ever covers the glass, so none can borrow the tap, and a MAP portal
  // always mounts its own map alongside the stream the tap is already running. The bezel pays the
  // same in split mode.
  function cells() { return portals.map(function (p) { return p.cell; }); }
  function livePortals() { return portals; }

  // Rebuild every portal's grips from the arrangement, and let each know its box moved. Called
  // after anything that changes the glass — which is only ever a merge or a split.
  function refreshGlass() {
    const cs = cells();
    portals.forEach(function (p, i) {
      p.applySpan();
      p.setGrips(F35Glass.gripsFor(cs, i));
      p.resized();
    });
    // The glass just changed shape — a portal was added or destroyed. Tell the server the new
    // surface count (SOI cycles portals), and re-apply the ring/cursor: a destroyed portal may have
    // held focus, and a merge shifts indices until the server's clamped target arrives.
    reportPanes();
    renderSoiRing();
    renderSoiCursor();
  }

  function addPortal(at) {
    const p = makePortal(onGrip, onNavRendered);
    portals.splice(at, 0, p);
    portalsEl.insertBefore(p.el, portalsEl.children[at] || null);
    p.applySpan();
    p.showPage('main');   // every portal opens on the menu, as the bezel's landing page does
    return p;
  }

  // A grip was pressed. The survivor is left alone — it keeps its page and everything on it, and
  // just changes width. An absorbed portal is destroyed, taking its iframe and any map stream with
  // it, and comes back fresh on MAIN.
  function onGrip(portal, action) {
    const i = portals.indexOf(portal);
    if (i < 0) return;

    if (action === 'split') {
      const next = F35Glass.split(cells(), i);
      if (!next) return;
      portal.cell.span = 1;
      delete portal.cell.ate;
      addPortal(next.survivor === i ? i + 1 : i);   // the newcomer takes the slot that was eaten
    } else {
      const side = action === 'merge-left' ? 'left' : 'right';
      if (!F35Glass.merge(cells(), i, side)) return;   // the rule refused; leave the glass alone
      const victim = portals[side === 'left' ? i - 1 : i + 1];
      victim.destroy();
      portals.splice(portals.indexOf(victim), 1);
      portal.cell.span = 2;
      portal.cell.ate  = side;
    }
    refreshGlass();
  }

  function buildGlass() {
    portalsEl.textContent = '';
    portals = [];
    for (let i = 0; i < F35Glass.SLOTS; i++) addPortal(i);
    refreshGlass();
  }

  // ── SOI (sensor of interest) ───────────────────────────────────────────────────────────
  // Focus is a SURFACE (docs/keybinds-page.md): on the F-35, a surface is one portal. The server
  // cycles the glass's live portals; this shell rings the focused one and walks a cursor over its
  // nav labels — the F-35 twin of the bezel's per-key cursor, over `.nav-item` divs since the glass
  // has no physical keys. soiPane is the focused portal's index (-1 = this glass isn't the SOI);
  // soiCursor indexes that portal's nav items.
  let soiPane = -1, soiCursor = -1, myCid = '';

  // Report the live surface count so the server cycles portals, not documents. Needs the cid, which
  // the tap supplies (soi-cid); until then this no-ops and the soi-cid handler re-invokes it.
  function reportPanes() {
    if (!myCid) return;
    sendCommand('soi.panes', { cid: myCid, n: portals.length }).catch(function () {});
    reportSoiPage();   // myCid may have just arrived after focus was already established (reconnect)
  }

  function focusedPortal() { return (soiPane >= 0 && soiPane < portals.length) ? portals[soiPane] : null; }

  // Ring the focused portal (a class on its box; f35.css draws it). Out of range — a merge just
  // removed it, before the server's clamped target lands — rings nothing, which is the safe default.
  //
  // The manual TGP camera (docs/tgp-manual-control.md's PAD Cursor consolidation plan) is a native
  // target, not a portal — soiPane never matches it, so focusedPortal() naturally returns null and
  // this rings nothing while the camera itself holds focus, with no special case needed. A real
  // TGP portal is its own separate, independently focusable ring member, so the ring must never
  // light up a TGP-showing portal just because the camera — a different ring member — holds focus.
  // The in-cockpit "SOI" tag (TgpNativeOverlay.SyncCrosshair) is the tell when the camera itself
  // holds focus.
  function renderSoiRing() {
    const fp = focusedPortal();
    portals.forEach(function (p) { p.el.classList.toggle('soi', p === fp); });
  }

  // Report which page the SOI-focused portal is showing (docs/tgp-manual-control.md's PAD Cursor
  // consolidation plan) — see mfd.js's twin for the full reasoning. Harmless no-op if this glass
  // isn't the SOI.
  function reportSoiPage() {
    const fp = focusedPortal();
    if (!fp || !myCid) return;
    sendCommand('soi.page', { cid: myCid, n: soiPane, wname: fp.page() || '' }).catch(function () {});
  }

  // Paint the cursor on the focused portal's cursored nav item, clearing any elsewhere. Clamped, so
  // a page change that shortened the list keeps it in range; re-run after any nav rebuild.
  function renderSoiCursor() {
    portals.forEach(function (p) {
      [].slice.call(p.el.querySelectorAll('.nav-item.cursor')).forEach(function (b) { b.classList.remove('cursor'); });
    });
    const fp = focusedPortal();
    if (!fp || soiCursor < 0) return;
    const items = fp.navItems();
    if (!items.length) { soiCursor = -1; return; }
    if (soiCursor >= items.length) soiCursor = items.length - 1;
    items[soiCursor].classList.add('cursor');
  }
  function setSoiCursor(i) { soiCursor = i; renderSoiCursor(); }

  // Focus moved (or cleared). A different portal drops the cursor — the destination reveals it fresh
  // on the next NAV, like a first focus.
  function onSoiFocus(m) {
    const prev = soiPane;
    soiPane = m.focused ? (typeof m.pane === 'number' ? m.pane : 0) : -1;
    if (soiPane !== prev) soiCursor = -1;
    renderSoiRing();
    renderSoiCursor();
    syncCursorFocus();
    reportSoiPage();   // newly (or still) focused — tell the server what page this portal shows
  }

  // A SOI key press, applied to the focused portal. SELECT clicks the cursored label through its own
  // wiring; a press that navigates the portal to a new page drops the cursor (as the bezel does),
  // one that stays put (paging, a map control) keeps it. NAV walks the portal's items, first press
  // revealing the cursor from the end it came from.
  function onSoiAct(act) {
    const fp = focusedPortal();
    if (!fp) return;
    const items = fp.navItems();
    if (!items.length) { setSoiCursor(-1); return; }

    if (act === 'select') {
      if (soiCursor >= 0 && soiCursor < items.length) {
        const before = fp.page();
        items[soiCursor].click();
        if (fp.page() !== before) setSoiCursor(-1); else renderSoiCursor();
      }
      return;
    }
    const dir = act === 'up' ? -1 : 1;
    if (soiCursor < 0) setSoiCursor(dir > 0 ? 0 : items.length - 1);
    else setSoiCursor(((soiCursor + dir) % items.length + items.length) % items.length);
  }

  // A focused portal just rebuilt its nav grid (page change, WPN paging) — re-apply the cursor mark,
  // and tell the server if this changed what page the SOI-focused portal shows (docs/tgp-manual-
  // control.md's PAD Cursor consolidation plan).
  function onNavRendered(p) {
    if (p !== focusedPortal()) return;
    renderSoiCursor();
    syncCursorFocus();   // the focused portal may have paged onto/off MAP under it
    reportSoiPage();
  }

  // ── PAD cursor forwarding (docs/page-cursor.md, docs/map-cursor.md) ───────────────────
  // Twin of the bezel's syncCursorFocus: tell the portal that just lost eligibility to drop its
  // cursor, and the one that just gained it (focused AND showing a PAD_CURSOR_PAGES page — MAP,
  // TGT, HUD, RDR) to show one. null-safe no-op when the answer didn't change.
  let cursorFocusTarget = null;
  function focusedCursorWindow() {
    const fp = focusedPortal();
    return fp ? fp.cursorWin() : null;
  }
  function syncCursorFocus() {
    const target = focusedCursorWindow();
    if (target === cursorFocusTarget) return;
    if (cursorFocusTarget) cursorFocusTarget.postMessage({ mfd: true, action: 'cursor-focus', on: false }, '*');
    if (target) target.postMessage({ mfd: true, action: 'cursor-focus', on: true }, '*');
    cursorFocusTarget = target;
  }

  window.addEventListener('message', function (e) {
    const m = e.data;
    if (!m || m.mfd !== true || typeof m.type !== 'string') return;

    // 'follow' belongs to whichever map sent it, so it routes by source rather than coming from
    // the canonical tap — with a map per portal, each follows independently. The bezel routes it
    // the same way and for the same reason.
    if (m.type === 'follow') {
      livePortals().forEach(function (p) { if (p.isMapWin(e.source)) p.setFollow(!!m.on); });
      return;
    }

    // 'grid' routes the same way, for the same reason.
    if (m.type === 'grid') {
      livePortals().forEach(function (p) { if (p.isMapWin(e.source)) p.setGrid(!!m.on); });
      return;
    }

    // 'wpt-routes-request' — a freshly-loaded portal catching up on the navigation library (docs/hud-
    // waypoint-indicator.md perf fix) — comes from the portal's own iframe, not the tap, same
    // reasoning as 'follow'/'grid' above.
    if (m.type === 'wpt-routes-request') {
      if (e.source) e.source.postMessage({ mfd: true, type: 'wpt-routes', data: WaypointsStore.load() }, '*');
      return;
    }

    // 'td-designated' (issue #47 follow-up) — TD's own DESIGNATE (leader) or AQUIRE (member) button
    // just fired; return that portal to TGT. Has to be the shell doing the navigating: TGT has no
    // telemetry connection of its own, so a bare iframe location change would strand it with no
    // data to render. Same routes-by-source reasoning as 'follow'/'grid' above.
    if (m.type === 'td-designated') {
      livePortals().forEach(function (p) { if (p.page() === 'td' && p.frameWin() === e.source) p.showPage('tgt'); });
      return;
    }

    // Telemetry comes only from the tap. A portal's own map streams too, and its duplicate posts
    // are ignored here — otherwise two out-of-phase feeds would drive the same page. This is the
    // bezel's canonical-source guard, for the same reason.
    if (e.source !== mapTap.contentWindow) return;

    // SOI control messages from the tap — not telemetry slices, so handle and return before caching.
    if (m.type === 'soi-cid') { myCid = m.cid || ''; reportPanes(); return; }
    if (m.type === 'soi')     { onSoiFocus(m); return; }
    if (m.type === 'soi-act') { onSoiAct(m.act); return; }
    if (m.type === 'cursor') {
      const w = focusedCursorWindow();
      if (w) w.postMessage({ mfd: true, action: 'cursor', x: m.x || 0, y: m.y || 0 }, '*');
      return;
    }
    if (m.type === 'cursor-select') {
      const w = focusedCursorWindow();
      if (w) w.postMessage({ mfd: true, action: 'cursor-select' }, '*');
      return;
    }
    if (m.type === 'cursor-held') {
      // docs/page-cursor.md — TGT/RDR's Select tap-vs-hold arbitration lives entirely in this
      // held state (pad-cursor.js's setSelectHeld), not the plain edge-driven cursor-select above;
      // MAP/HUD have no onHold registered and simply ignore it. Missing this meant those two pages'
      // Select never fired ANY outcome under this layout, not just the wrong one.
      const w = focusedCursorWindow();
      if (w) w.postMessage({ mfd: true, action: 'cursor-held', held: !!m.held }, '*');
      return;
    }
    if (m.type === 'map-act') {
      // Same wire action the bezel forwards (toggle-follow/zoom-in/zoom-out/tgt-next/tgt-prev/
      // tgt-datalink/tgt-stale, docs/tgt-keybind-nav.md) — no MAP_ACTIONS translation needed since
      // the server already sends those exact strings.
      const w = focusedCursorWindow();
      if (w) w.postMessage({ mfd: true, action: m.act }, '*');
      return;
    }

    slices[m.type] = m;   // cache every slice: the screen that wants it may not be up yet
    livePortals().forEach(function (p) { p.onSlice(m.type); });

    // The master strip isn't a portal, so it has no PAGE_FEEDS entry; the slices it shows are
    // handed to it straight from here. status → the connection line; avn → the flags and the
    // THRL/FUEL gauges.
    if (m.type === 'status') updateStripStatus(m);
    else if (m.type === 'avn') { updateStripFlags(m); updateStripGauges(m); }
  });

  // WPN's rects are derived from its portal's box, so they go stale when it changes. The bezel
  // recomputes from its separators for the same reason. Only WPN cares — every other page here
  // lays itself out with CSS.
  function relayoutAll() { livePortals().forEach(function (p) { p.resized(); }); }
  window.addEventListener('resize', relayoutAll);
  orientMq.addEventListener('change', relayoutAll);

  // MAP's route and context-navigation controls derive from the shared navigation library. The
  // plugin is the single source of truth (docs/steer-points.md) — this shell document loads its own
  // copy of waypoints-store.js (f35.html), which listens for the SSE-pushed 'wpt-options-push' and
  // fires this event on any change, from any page, any device (a squadmate's shared route or steer
  // point is applied directly plugin-side, Squad.HandleData, and shows up as a pendingShared entry
  // the same way — WPT's own ACCEPT/REJECT is what turns it into a real item). refreshNav
  // (not showPage) so it can't reload — and so can't lose the pan/zoom of — a MAP portal that's
  // already showing.
  //
  // Also pushes the route data into the same slices/onSlice relay every other feed uses — each
  // portal picks it up through the normal PAGE_FEEDS mechanism (map/wpt both list 'wpt-routes'
  // above), including automatic catch-up on a freshly loaded portal via forwardToPage(), the same
  // as every other slice.
  window.addEventListener('wptroutes:changed', function () {
    slices['wpt-routes'] = { mfd: true, type: 'wpt-routes', data: WaypointsStore.load() };
    livePortals().forEach(function (p) {
      p.onSlice('wpt-routes');
      if (p.page() === 'map') p.refreshNav();
    });
  });

  // ── Master strip ───────────────────────────────────────────────────────────────────────
  // Fixed chrome across the top (docs/layouts.md). It holds no page and no NAV, so it isn't a
  // portal — the status/avn/mapinfo slices reach it from the message pump above, and the URLs come
  // from the server once (the same /config the bezel's MAIN reads). The flags and the THRL gauge
  // reuse avn-status-policy and avn-throttle-policy, so the GEAR-down-is-red rule and the MIL/AB
  // split stay in one place each, shared with the AVN page.
  const stripEl      = document.querySelector('.master-strip');
  const stripFlags   = [].slice.call(document.querySelectorAll('.ms-flag'));
  const stripStatus  = document.getElementById('ms-status');
  const stripThr  = gauge('ms-thr', 'thr');
  const stripFuel = gauge('ms-fuel', 'fuel');

  // Click-to-toggle — data-kind already names the avn.toggle group 1:1 (gear, radar,
  // guns, eng, assist, nvg, lights, turret), so the click handler needs no mapping table. Fire-and-
  // forget: the next 'avn' telemetry frame repaints the tile via updateStripFlags below, same as
  // every other live-state indicator on this strip.
  stripFlags.forEach(function (el) {
    el.classList.add('pad-hoverable');
    el.addEventListener('click', function () {
      sendCommand('avn.toggle', { group: el.dataset.kind }).catch(function () {});
    });
  });

  function gauge(id, kind) {
    return { el: document.getElementById(id),
             fill: document.getElementById(id + '-fill'),
             num: document.getElementById(id + '-num'),
             kind: kind };
  }

  // FUEL's warning levels, as AVN calls them (avn.js paintAvnBars: cautionAt 0.25, criticalAt
  // 0.10). They live at that call site rather than in a policy module, so they are repeated here
  // rather than imported — the two must agree, or the strip and the page it summarises would
  // disagree about the same tank.
  const FUEL_CAUTION  = 0.25;
  const FUEL_CRITICAL = 0.10;
  // The URLs type in only once BOTH the loading bar has finished and /config has landed — whichever
  // is last. maybeRevealUrls checks both flags so the order of the two async events doesn't matter.
  let bootDone = false, urlsLoaded = false;

  function updateStripFlags(m) {
    stripFlags.forEach(function (el) {
      el.classList.remove('on', 'off', 'gear-down');
      el.classList.add(AvnStatusPolicy.tileClass(el.dataset.kind, !!m[el.dataset.field]));
    });
  }

  // THRL + FUEL, off the same 'avn' slice as the flags — fuel and throttle were already in it, so
  // the gauges cost no telemetry. Both values are 0..1, and < 0 means the airframe has no such
  // system (or there is no data yet).
  //
  // THRL's rule is AvnThrottlePolicy, shared with the AVN page: the MIL/AB split, the readout
  // string ('60%' / 'MIL' / the rescaled reheat %), and the zone all come from there, so the strip
  // can't drift from the gauge it is summarising.
  //
  // The fill's MIL/AB split is AVN's too, but it needs no measuring here: AVN sizes that gradient
  // in px because its boundary must stay pinned to a fraction of the tube while the fill grows past
  // it, and it remeasures every paint. The tube is a container query instead, so CSS resolves the
  // same fraction on its own — see .ms-gauge.ab-capable in f35.css.
  function updateStripGauges(m) {
    const t = AvnThrottlePolicy.throttleReadout(m.throttle, m.hasAb, m.abStart);
    // Where the fill turns from green to red. The CSS pins the boundary to a fraction of the tube
    // (see .ms-gauge.ab-capable), so it stays put while the fill grows past it — as on AVN.
    if (t.boundary !== null) stripThr.el.style.setProperty('--ab-start', t.boundary);
    setGauge(stripThr, t.na, t.fill, t.text,
             (t.boundary !== null ? ' ab-capable' : '') + (t.zone === 'ab' ? ' ab-active' : ''));

    const na = typeof m.fuel !== 'number' || m.fuel < 0;
    const v  = na ? 0 : Math.max(0, Math.min(1, m.fuel));
    setGauge(stripFuel, na, v, Math.round(v * 100) + '%',
             v <= FUEL_CRITICAL ? ' critical' : v <= FUEL_CAUTION ? ' caution' : '');
  }

  // `kind` is 'thr' or 'fuel' and is written back every time: the tube's styling keys off it (only
  // FUEL is segmented), and this assigns className wholesale rather than toggling each state off.
  function setGauge(g, na, fill, text, state) {
    g.el.className = 'ms-gauge ' + g.kind + (na ? ' na' : state);
    g.fill.style.width = (fill * 100).toFixed(1) + '%';
    g.num.textContent = na ? '--' : text;
  }
  function updateStripStatus(m) {
    stripStatus.className = 'ms-status ' + m.cls;
    stripStatus.textContent = m.text;
  }

  function setStripUrls(cfg) {
    if (cfg && cfg.localhost) document.getElementById('ms-local').textContent = cfg.localhost;
    document.getElementById('ms-lan').textContent = (cfg && cfg.lanUrl) || '';
    urlsLoaded = true;
    maybeRevealUrls();
  }
  function loadStripUrls() {
    fetch('/config', { cache: 'no-store' })
      .then(function (r) { if (!r.ok) throw new Error('config'); return r.json(); })
      .then(setStripUrls)
      // Fall back to the default localhost URL (as the bezel MAIN does) so the reveal still fires.
      .catch(function () { setStripUrls({ localhost: 'http://localhost:5005' }); });
  }

  // Boot loader: a LOADING… bar filling 0→100% over ~1s, then the URLs type in. The fill-bar and
  // typewriter mechanics are shared with mfd.js's runBootLoading/typewriterUrls via
  // src/web/shell/boot-reveal.js. The strip starts in .booting from the HTML so nothing flashes
  // before the bar; this only drives the fill and lifts .booting at 100%.
  function runStripBoot() {
    const fill = document.getElementById('ms-bar-fill');
    if (!stripEl || !fill) return;
    BootReveal.runBootFill(fill, function () {
      stripEl.classList.remove('booting');   // reveal the connection block
      bootDone = true;
      maybeRevealUrls();
    });
  }

  function maybeRevealUrls() {
    if (bootDone && urlsLoaded) typeStripUrls();
  }

  // Type the URL lines out. Runs once, when /config lands — BootReveal.typewriterReveal's
  // supersede-guard is a no-op here since this is only ever called once.
  function typeStripUrls() {
    const lines = [].slice.call(document.querySelectorAll('.ms-url'))
      .filter(function (el) { return el.textContent; });
    BootReveal.typewriterReveal(lines);
  }

  // SAVE/LOAD LAYOUT keyboard + modal wiring is shared with mfd.js via
  // src/web/shell/layout-keydown.js — only captureLayoutState/applyLayoutState (this shell's
  // own state shape, below) stay here. Declared here (ahead of its own captureLayoutState/
  // applyLayoutState definitions further down, which are hoisted function declarations so the
  // reference is fine) because the picker wiring just below needs openSaveLayoutModal/
  // openLoadLayoutModal as values, not just calls deferred to click time.
  const { openSaveLayoutModal, openLoadLayoutModal, handleLayoutKeydown, wireLayoutKeydown } =
    LayoutKeydown.makeLayoutKeydownHandlers('f35', captureLayoutState, applyLayoutState);

  // ── Layout picker ──────────────────────────────────────────────────────────────────────
  // LYT (a portal's MAIN, GLASS_ACTIONS) swaps the portals for a two-item chooser — the same place
  // the bezel keeps it, so the choice is named the same way in either layout. A layout is still the
  // whole glass's business, not the offering portal's: the chooser takes over the entire column.
  //
  // It replaces the portals CONTAINER, not their contents: hidden, the portals keep their pages,
  // their arrangement and their map streams, so coming back costs nothing and loses nothing.
  // #map-tap is absolute and outside the column, so telemetry keeps flowing the whole time.
  //
  // Which layout is current needs no state: this file IS the F-35 shell, so its item is marked in
  // the HTML and CLASSIC is simply somewhere else.
  const pickerEl = document.getElementById('layout-picker');

  function showPicker(on) {
    pickerEl.hidden  = !on;
    portalsEl.hidden = on;
    // Hidden, a portal's box is 0x0 — and the resize listener below still fires into it, handing
    // WPN a zero-height rect for every row. So rebuild on the way back: the glass may have changed
    // size while it was away, and whatever WPN is holding was measured against nothing. Only WPN
    // derives geometry from its box; every other page reflows itself with CSS.
    if (!on) relayoutAll();
  }

  // Remember the choice so a fresh load honors it (docs/layouts.md, Stage 3); the head guard in each
  // shell's HTML reads it and redirects before paint. Guarded — localStorage throws in some
  // private-mode browsers, and a failed write just means the choice isn't sticky.
  function setLayout(name) { try { localStorage.setItem('layout', name); } catch (e) {} }
  // F-35 is this document, so the way back is just showing the glass again — picking the layout you
  // are already on is how you leave the chooser, exactly as CLASSIC is on the bezel's LYT page.
  // CLASSIC is a different document: the bezel shell at /, which lands on its own MAIN.
  pickerEl.querySelector('[data-layout="f35"]').addEventListener('click', function () { setLayout('f35'); showPicker(false); });
  pickerEl.querySelector('[data-layout="classic"]').addEventListener('click', function () { setLayout('classic'); location.href = '/'; });

  // Touch-friendly path for SAVE/LOAD LAYOUT — same modals the keyboard shortcut opens, for a
  // tablet with no keyboard attached. Unlike CLASSIC's LYT, the picker never
  // touches portals/cells — they keep running underneath, hidden — so SAVE from here always
  // captures the real current arrangement, not a placeholder "picker" page.
  document.getElementById('layout-picker-save').addEventListener('click', openSaveLayoutModal);
  document.getElementById('layout-picker-load').addEventListener('click', openLoadLayoutModal);

  // ── Fullscreen ─────────────────────────────────────────────────────────────────────────
  // Same toggle as the bezel's top-key icon (mfd.js toggleFullscreen) — this shell has no bezel key
  // bank to carry it, so it gets its own button beside LAYOUT instead.
  document.getElementById('ms-fll').addEventListener('click', function () {
    const d = document, el = d.documentElement;
    if (!d.fullscreenElement && !d.webkitFullscreenElement) {
      (el.requestFullscreen || el.webkitRequestFullscreen || function () {}).call(el);
    } else {
      (d.exitFullscreen || d.webkitExitFullscreen || function () {}).call(d);
    }
  });

  // ── Screen wake-lock (docs/screen-wake-lock.md) ───────────────────────────────────────────
  // Same shared controller the bezel shell's WAKE key uses (shell/wake-lock.js) — this shell's
  // .nav-item.on already gives a master-strip button the amber engaged treatment, so no new CSS
  // state is needed here the way the bezel key needed one.
  (function () {
    const wakeButton = document.getElementById('ms-wake');
    const wakeError = document.getElementById('ms-wake-error');
    let wakeErrorTimer = null;
    const wakeController = WakeLock.createBrowserController({
      onState: function (state) {
        wakeButton.classList.toggle('on', state.enabled);
        wakeButton.setAttribute('aria-pressed', state.enabled ? 'true' : 'false');
        wakeButton.title = state.enabled ? 'Allow screen sleep' : 'Keep screen awake';
      },
      onError: function (error) {
        console.error('Wake lock failed:', error);
        wakeError.textContent = 'WAKE LOCK FAILED';
        wakeError.hidden = false;
        if (wakeErrorTimer !== null) clearTimeout(wakeErrorTimer);
        wakeErrorTimer = setTimeout(function () { wakeError.hidden = true; wakeErrorTimer = null; }, 5000);
      },
    });
    wakeButton.addEventListener('click', function () { wakeController.toggle(); });
    wakeController.start();
  })();

  // ── SAVE/LOAD LAYOUT — browser-side keyboard shortcuts only, no joystick/HOTAS. ──
  // S saves the glass's current arrangement (F35Glass cells + each portal's page) under a name;
  // L opens a picker of every saved F-35 layout and applies the one clicked. Storage is
  // server-side (LayoutStore.cs), so a layout saved here shows up on every other connected browser
  // (including the bezel shell, which keeps its own list — see mfd.js's twin of this block —
  // filtered by `shell` so a browser is never offered an arrangement it can't apply).
  function captureLayoutState() {
    return { cells: cells(), pages: portals.map(function (p) { return p.page(); }) };
  }

  // Rebuilds the glass directly from a saved arrangement, rather than replaying merge/split
  // actions to reach it — buildGlass()'s own addPortal always starts a portal at span 1, so this
  // is a small variant of it that seeds each portal's cell from the saved spec up front.
  function applyLayoutState(state) {
    const cs = state && state.cells;
    if (!cs || !F35Glass.valid(cs)) {
      buildGlass();
    } else {
      portalsEl.textContent = '';
      portals = cs.map(function (c) {
        const p = makePortal(onGrip, onNavRendered);
        p.cell.span = c.span;
        if (c.ate) p.cell.ate = c.ate; else delete p.cell.ate;
        portalsEl.appendChild(p.el);
        return p;
      });
      const pages = state.pages || [];
      portals.forEach(function (p, i) {
        p.applySpan();
        const pg = pages[i];
        p.showPage(pg && has(pg) ? pg : 'main');
      });
      refreshGlass();
    }
    // Reveal the result immediately even when LOAD was triggered from the picker — a no-op
    // (already showing the glass) when it wasn't open.
    showPicker(false);
  }

  window.addEventListener('keydown', handleLayoutKeydown);
  wireLayoutKeydown(mapTap);

  loadStripUrls();
  runStripBoot();
  // Extension nav discovery (docs/extensions-api.md) — fetches /ext-manifest and merges installed
  // extensions into NAV.ext / NAV[<id>]. Fire-and-forget at boot; also re-run every time EXT is
  // clicked (dispatch's 'ext' case) to pick up an extension that registered after this tab loaded.
  ExtNav.load(NAV);
  // TD nav discovery (issue #47, docs/target-designator.md) — rides the relayed 'sqd-state' push
  // and keeps NAV.tgt's TD entry in sync with live squad membership.
  TdNav.start(NAV);
  buildGlass();
})();
