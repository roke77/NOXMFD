// F-35 layout — Stage 2 (docs/layouts.md). A second layout renderer consuming the same NAV model
// the bezel shell does.
//
// What this layout owns (the doc's four: frame + label placement + split behaviour + page geometry):
//   • frame           — none. f35.html/css are borderless; the page IS the display.
//   • label placement — a grid drawn over the page, in one of two modes (NAV_LAYOUT below):
//                       'edge' hugs the left column like the bezel's key bank; 'center' puts the
//                       labels in the middle of the glass for MAIN, which has no page behind them.
//   • split behaviour — portals, paired. The glass is two pairs of two: four side-by-side MFDs,
//                       each with its own page, labels and state. The corner grips let a pair's
//                       members absorb each other, giving four portals, three or two — never one,
//                       because nothing reaches across the centre. Nothing of the bezel's split
//                       machinery (SplitKeymap, SPLIT_SLOTS) is reused: it resolves labels to
//                       physical keys, and there are none here.
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

  // A MAP portal mounts its own map. The tap is a data source only and is never shown — see
  // #map-tap in f35.css, and "the glass" below for why no portal can ever borrow it.
  const MAP_URL = '/map-view?bare';

  // Screens this layout can show, and the page each mounts. Every NAV action has an entry, so
  // nothing renders dimmed except this layout's own placeholders (MAIN_EXTRAS).
  //
  // MAIN maps to no page, and `null` is meaningful there — test membership with `in`, not
  // truthiness. Its whole content is its navigation, and this shell's grid already draws that, so
  // there is nothing left for a page to render. (The bezel needs MAIN twice: #info-box chrome in
  // full view, /main in a split pane. Here it needs it zero times; src/web/pages/main/ is
  // untouched and still serves the bezel.)
  const F35_PAGES = {
    main: null,
    map: MAP_URL,
    avn: '/avn',
    rwr: '/rwr',
    tgt: '/tgt',
    tgp: '/tgp',
    wpn: '/wpn',
  };

  // The telemetry each screen needs, by the tap's own type names. A page that just mounted has
  // missed whatever already arrived, and slices land while other screens are up — so the shell
  // caches every slice and each portal replays the relevant ones.
  //
  // TGT needs no command plumbing: it POSTs its own tgt.* via send-command.js.
  const PAGE_FEEDS = {
    avn: ['avn'],
    rwr: ['rwr', 'mw'],       // scope contacts + incoming-missile warnings
    tgt: ['tgt', 'targets'],
    tgp: ['tgp'],
    wpn: ['loadout', 'cm'],   // 'loadout' is derived, not forwarded as-is — see DERIVED
  };

  // The tap calls it 'targets'; TGT listens for 'tgt-targets'. The bezel renames it in exactly the
  // same place (mfd.js forwardTgtTargetsToFrame), so this mirrors the existing contract rather
  // than inventing one. Every other slice forwards under its own name.
  const FEED_AS = { targets: 'tgt-targets' };

  // Slices needing more than a rename — the portal derives these itself. WPN is the only one: the
  // page shows five rows, so the shell owns *which* five. Pagination is shell state, which is why
  // NAV.wpn is empty and why the bezel hand-rolls its WPN labels too.
  const DERIVED = { loadout: true };

  const WPN_MAX_DISPLAY = ROWS - 1;   // row 1 is the nav + CM band; rows 2..6 carry the weapons
  const WPN_ICON_INSET  = 20;         // keeps the image off its band edges, as the bezel does

  // Screens this layout puts on MAIN beyond NAV's. They are placeholders for pages that don't
  // exist yet, which is exactly why they can't go in NAV: NAV is the bezel's menu too, and it has
  // six physical keys for six items. Kept here, they stay F-35's business and the bezel is
  // unaffected. They have no F35_PAGES entry, so they render greyed and inert like any other
  // unimplemented action — no special case needed.
  const MAIN_EXTRAS = [
    { label: 'HUD', action: 'hud' },
    { label: 'LYT', action: 'lyt' },
    { label: 'PAL', action: 'pal' },
    { label: 'BDF', action: 'bdf' },
  ];

  // Paging actions, and the direction each moves. Not pages, so they dispatch separately.
  const PAGER = { 'wpn-prev': -1, 'wpn-next': 1 };

  // MAP's own actions → the message the map view listens for. Also not pages: they drive the map
  // in place rather than navigating. Same protocol the bezel uses (mfd.js mapSend), but routed to
  // the portal's OWN map — with several maps on the glass, "the map" is no longer unambiguous.
  const MAP_ACTIONS = { flw: 'toggle-follow', zin: 'zoom-in', zout: 'zoom-out' };

  // Where a screen's NAV items sit. Default 'edge' = the bezel's left key bank, minus the bezel.
  // MAIN is 'center': its labels ARE the screen, so they own the middle of the glass instead of
  // hugging an edge that frames nothing. Both modes consume NAV in order — only placement differs,
  // which is exactly the split the seam predicts.
  const NAV_LAYOUT = { main: 'center' };

  const slices  = Object.create(null);   // the tap's latest message, by type — shared by all portals
  // Not the source of orientation — a portal measures its own box for that (forwardOrientation).
  // This only says "the glass turned", which is one of the things that resizes a portal.
  const orientMq = window.matchMedia('(orientation: portrait)');
  let   pairs = [];   // the glass, left to right; livePortals() flattens them

  function has(page) { return Object.prototype.hasOwnProperty.call(F35_PAGES, page); }
  function feedsFor(page) { return PAGE_FEEDS[page] || []; }
  function canDo(action) { return has(action) || (action in PAGER) || (action in MAP_ACTIONS); }

  // 'edge' placement: an item's index → its cell. The left column, top-down, IS the bezel's left
  // key bank — the same derivation mfd.js fullViewSlot() uses, which is why NAV needs no placement
  // hints for full view. ('center' needs no function: items flow in NAV order and the grid's own
  // columns arrange them.)
  function cellOf(i) { return { row: i + 1, col: 1 }; }

  // ── Corner grips ─────────────────────────────────────────────────────────────────────
  // The F-35's expand/retract control: an outline triangle in a portal's bottom corner. Portal
  // chrome, not navigation — it resizes the glass rather than choosing a page, so it lives outside
  // the label grid (which renderNav rebuilds).
  //
  // A grip always sits in the corner facing the portal's pair partner, and only its DIRECTION
  // says what it does:
  //   * outward (away from the portal) — absorb the partner and take the whole pair.
  //   * inward  (back over the portal) — give the half back, splitting the pair again.
  // Slot 0 faces right, slot 1 faces left. No partner slot at all means no grip: nothing to
  // absorb, nothing to give back. Everything else follows from that — portals 1 and 4 have no
  // outward grip because only the screen edge lies that way, and 2 and 3 have none towards the
  // centre because that is the other pair's half.
  function gripCornerFor(slot)  { return slot === 0 ? 'right' : 'left'; }
  function gripPointsFor(slot, expanded) {
    const corner = gripCornerFor(slot);
    const inward = corner === 'right' ? 'left' : 'right';
    return expanded ? inward : corner;   // expanded → point back in, offering the half up
  }

  // Drawn as SVG because the reference's triangles are outlines, and the CSS border trick only
  // makes solid ones. The triangle fills its square button; non-scaling-stroke (in the CSS) keeps
  // the outline 2px however large that gets, and the 2-unit inset keeps the stroke inside the box.
  const GRIP_POINTS = { left: '2,50 98,2 98,98', right: '98,50 2,2 2,98' };
  function makeGrip(corner, onClick) {
    const b = document.createElement('button');
    b.type = 'button';
    b.className = 'portal-grip ' + corner;
    const svg = document.createElementNS('http://www.w3.org/2000/svg', 'svg');
    svg.setAttribute('viewBox', '0 0 100 100');
    svg.setAttribute('aria-hidden', 'true');
    const poly = document.createElementNS('http://www.w3.org/2000/svg', 'polygon');
    svg.appendChild(poly);
    b.appendChild(svg);
    b.addEventListener('click', onClick);
    // Re-aim rather than rebuild: the corner never moves, so only the points and the label change.
    b.aim = function (points, label) {
      poly.setAttribute('points', GRIP_POINTS[points]);
      b.setAttribute('aria-label', label);
      b.title = label;
    };
    return b;
  }

  // ── Portal ───────────────────────────────────────────────────────────────────────────
  // One independent MFD: a page iframe with a label grid over it, and the state that belongs to
  // *this* screen rather than the shell — which page is up, where its WPN list is paged to, and
  // whether its map is following. Everything a second portal must not share lives in here; only
  // the telemetry cache and the tap are shell-wide.
  function makePortal(pair, slot) {
    const el    = document.createElement('div');
    const frame = document.createElement('iframe');
    const grid  = document.createElement('div');
    el.className    = 'portal';
    frame.className = 'page-frame';
    frame.title     = 'page';
    grid.className  = 'nav-grid';
    el.appendChild(frame);
    el.appendChild(grid);

    // The grip exists only where a partner slot does. It never moves after that — expanding just
    // turns the triangle around (refreshGrip).
    const grip = pair.capacity > 1
      ? makeGrip(gripCornerFor(slot), function () { pair.toggle(slot); })
      : null;
    if (grip) el.appendChild(grip);

    let currentPage = null;
    let wpnPage     = 0;    // 0-indexed pagination state
    let wpnNavKey   = '';   // what this grid last drew; guards a per-tick rebuild
    let followOn    = false;

    function refreshGrip() {
      if (!grip) return;
      const expanded = pair.expandedBy() === slot;
      grip.aim(gripPointsFor(slot, expanded), expanded ? 'Split this half' : 'Expand over the next portal');
    }

    function frameWin() { return frame.contentWindow; }
    // A portal showing MAP mounts its own, so its map IS its page — the tap is never displayed.
    function isMapWin(w) { return currentPage === 'map' && w === frameWin(); }

    // ── Feeds ──────────────────────────────────────────────────────────────────────────
    function forwardSlice(type) {
      if (DERIVED[type]) return forwardWpn();
      const w = frameWin(), m = slices[type];
      if (!w || !m) return;
      w.postMessage(FEED_AS[type] ? Object.assign({}, m, { type: FEED_AS[type] }) : m, '*');
    }
    function onSlice(type) {
      if (feedsFor(currentPage).indexOf(type) !== -1) forwardSlice(type);
    }
    // Everything the current page needs — on its load, and whenever it changes.
    function forwardToPage() {
      feedsFor(currentPage).forEach(forwardSlice);
      if (currentPage === 'wpn') { forwardWpnLayout(); forwardOrientation(); }
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
      const st = wpnState();
      wpnPage = st.page;
      w.postMessage({ mfd: true, type: 'wpn', items: st.visible, selWeapon: lo.selWeapon,
                      page: st.maxPage > 0 ? st.page + 1 : 1, pages: st.maxPage + 1 }, '*');
      const key = st.page + '|' + st.maxPage + '|' + st.visible.map(function (it) { return it.n; }).join(',');
      if (currentPage === 'wpn' && key !== wpnNavKey) { wpnNavKey = key; renderNav(); }
    }

    // Row 1 is the CM band; rows 2..6 are the weapon slots; the image spans rows 2..6. The grid and
    // the frame are both inset:0 in the portal, so a row's offset is already the frame's own
    // coordinate space — no frameTop to subtract, unlike the bezel reading shell-side separators.
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
    //   main — NAV's items plus this layout's own, alphabetical. Sorting here (not in NAV) keeps
    //          ordering a rendering choice: the bezel keeps NAV's order, where TGT precedes TGP.
    //   wpn  — nothing from NAV (it's empty by design); its labels are pagination.
    function itemsFor(page) {
      if (page === 'wpn') return wpnState().nav;
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

    function dispatch(action) {
      if (action in PAGER)       { wpnPage = wpnState().page + PAGER[action]; forwardWpn(); return; }
      if (action in MAP_ACTIONS) { mapSend(MAP_ACTIONS[action]); return; }
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
        b.className   = 'nav-item' + (wired ? '' : ' pending') + (cell.col === 2 ? ' col-right' : '');
        b.textContent = item.label;
        b.dataset.action = item.action;   // markFollow finds FLW by this
        if (mode === 'edge') {
          b.style.gridRow    = String(cell.row);
          b.style.gridColumn = String(cell.col);
        }
        if (wired) b.addEventListener('click', function () { dispatch(item.action); });
        else       b.disabled = true;
        grid.appendChild(b);
      });
      if (currentPage === 'wpn') addWeaponHits();
      markFollow();   // the labels were just rebuilt; re-apply the state to the new FLW
    }

    function showPage(name) {
      if (!has(name)) return;
      currentPage = name;
      wpnNavKey = '';   // entering any page redraws the grid; don't let a stale key suppress it
      // A page with no content of its own (MAIN) blanks the frame rather than hiding it: the
      // iframe's background is the glass colour, so what shows through is the label grid on black.
      frame.src = F35_PAGES[name] || 'about:blank';
      renderNav();   // forwardToPage reruns on the frame's load
    }

    frame.addEventListener('load', forwardToPage);
    refreshGrip();

    return {
      el: el,
      showPage: showPage,
      onSlice: onSlice,
      isMapWin: isMapWin,
      setFollow: setFollow,
      refreshGrip: refreshGrip,
      // The portal's box just changed. WPN is the only page that cares, and it cares twice: its
      // rects come from the box, and its orientation IS the box's shape — a quarter is portrait, a
      // half may not be. Every other page reflows itself with CSS; the map notices via its own
      // resize handling, and must not be re-entered here — that would reload the iframe and throw
      // away the zoom and pan the pilot set.
      resized: function () {
        if (currentPage === 'wpn') { forwardOrientation(); forwardWpnLayout(); }
      },
      destroy: function () { el.remove(); },
    };
  }

  // ── Pair ─────────────────────────────────────────────────────────────────────────────
  // A pair owns an equal share of the glass and holds one or two portals. It owns the one piece of
  // state neither portal can: whether they are sharing the half, or one has taken all of it.
  //
  // The share is the pair's, not the portals' — which is why the pairs are real elements. Four
  // portals as flat siblings would give a survivor a THIRD of the glass when its partner went;
  // nested, it grows to exactly the half its pair already held, and the other pair never moves.
  function makePair(capacity) {
    const el = document.createElement('div');
    el.className = 'pair';
    const members = [];        // by slot; a hole where a portal has been absorbed
    let expandedBy = null;     // slot that took the whole pair, or null while sharing

    function add(slot) {
      const p = makePortal(api, slot);
      members[slot] = p;
      // Slot order is left-to-right, and a restored portal has to land back on its own side.
      if (slot === 0) el.insertBefore(p.el, el.firstChild);
      else            el.appendChild(p.el);
      p.showPage('main');
      return p;
    }

    // Absorb the partner, or give the half back. The survivor is left alone: it keeps its page and
    // everything on it, and simply gets wider. The absorbed portal is destroyed — with it goes its
    // iframe, and any map stream it was running — and comes back fresh on MAIN.
    function toggle(slot) {
      if (expandedBy === slot) {
        expandedBy = null;
        add(1 - slot);
      } else {
        const victim = members[1 - slot];
        if (!victim) return;
        victim.destroy();
        members[1 - slot] = null;
        expandedBy = slot;
      }
      // Both portals' boxes just changed, and so may the glass's only-portal count — which decides
      // who owns the tap. Tell everyone, not just this pair.
      livePortals().forEach(function (p) { p.refreshGrip(); p.resized(); });
    }

    const api = {
      el: el,
      capacity: capacity,
      expandedBy: function () { return expandedBy; },
      toggle: toggle,
      members: function () { return members.filter(Boolean); },
    };

    for (let s = 0; s < capacity; s++) add(s);
    return api;
  }

  // ── The glass ────────────────────────────────────────────────────────────────────────
  // Two pairs of two: the F-35's panoramic display is one wide sheet carrying four side-by-side
  // portals, each an independent MFD — not a 2x2 grid (issue #8's reference shots). This is the
  // whole layout, fixed, and the grips do the rest.
  //
  // The grips reach four portals, three, or two — never one. A pair's members can absorb each
  // other, but nothing reaches across the centre: that is the same rule that denies portals 2 and
  // 3 an inward grip, seen from the other end. So the centre divider is permanent and the glass is
  // never a single screen — which is what the real PCD does.
  //
  // The cost of never being solo: no portal can ever own the tap, so a MAP portal always mounts
  // its own map and runs a stream the tap is already running. The bezel pays exactly this in split
  // mode. Revealing the tap under a single portal is the only case that avoided it, and that case
  // no longer exists.
  const PAIRS = [2, 2];

  function livePortals() {
    return pairs.reduce(function (acc, pr) { return acc.concat(pr.members()); }, []);
  }

  function buildGlass() {
    portalsEl.textContent = '';
    pairs = PAIRS.map(makePair);
    pairs.forEach(function (pr) { portalsEl.appendChild(pr.el); });
    livePortals().forEach(function (p) { p.refreshGrip(); });
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

    // Telemetry comes only from the tap. A portal's own map streams too, and its duplicate posts
    // are ignored here — otherwise two out-of-phase feeds would drive the same page. This is the
    // bezel's canonical-source guard, for the same reason.
    if (e.source !== mapTap.contentWindow) return;
    slices[m.type] = m;   // cache every slice: the screen that wants it may not be up yet
    livePortals().forEach(function (p) { p.onSlice(m.type); });
  });

  // WPN's rects are derived from its portal's box, so they go stale when it changes. The bezel
  // recomputes from its separators for the same reason. Only WPN cares — every other page here
  // lays itself out with CSS.
  function relayoutAll() { livePortals().forEach(function (p) { p.resized(); }); }
  window.addEventListener('resize', relayoutAll);
  orientMq.addEventListener('change', relayoutAll);

  buildGlass();
})();
