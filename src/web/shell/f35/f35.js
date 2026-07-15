// F-35 layout — Stage 2 (docs/layouts.md). A second layout renderer consuming the same NAV model
// the bezel shell does. Full view only; splits are not attempted yet.
//
// What this layout owns (the doc's four: frame + label placement + split behaviour + page geometry):
//   • frame           — none. f35.html/css are borderless; the page IS the display.
//   • label placement — a grid drawn over the page, in one of two modes (NAV_LAYOUT below):
//                       'edge' hugs the left column like the bezel's key bank; 'center' puts the
//                       labels in the middle of the glass for MAIN, which has no page behind them.
//   • split behaviour — not yet.
//   • page geometry   — none, deliberately. We never post '*-layout', so AVN stays in its
//                       `compact` profile (its default) and places itself with CSS. That's the
//                       escape hatch docs/layouts.md identified: a non-bezel layout owes pages no
//                       placement contract at all.
//
// Shared with the bezel and unchanged: NAV (nav-model.js) and the page iframes. NAV needed no
// edit to drive a structurally different shell — the point of the seam Stage 1 extracted.
//
// Data path: the MAP iframe (#map-tap) owns the only EventSource('/stream') and posts derived
// per-page slices up here; we cache the latest and forward the one the current page needs. Every
// layout inherits this dependency, map or no map.
(function () {
  const NAV       = NavModel.NAV;
  const mapTap    = document.getElementById('map-tap');
  const pageFrame = document.getElementById('page-frame');
  const navGrid   = document.getElementById('nav-grid');

  const ROWS = 6;   // 'edge' mode only — must match grid-template-rows in f35.css

  // Screens this layout can show, and the page each mounts. Anything else in NAV renders dimmed
  // and inert (.pending) — today that's MAP, which needs its own iframe and gestures.
  //
  // MAIN maps to no page on purpose. Its whole content is its navigation, and navigation is drawn
  // by this shell's grid — so there is nothing left for a page to render and the frame stays
  // blank. (The bezel needs MAIN twice — as #info-box chrome in full view and as /main in a split
  // pane; here it needs it zero times. src/web/pages/main/ is untouched and still serves the
  // bezel.) `null` is meaningful: use `in`, not truthiness, to test membership.
  const F35_PAGES = {
    main: null,
    avn: '/avn',
    rwr: '/rwr',
    tgt: '/tgt',
    tgp: '/tgp',
    wpn: '/wpn',
  };

  // The telemetry each screen needs, by the tap's own type names. A page that just mounted has
  // missed whatever already arrived, and slices land while other screens are up — so we cache
  // every slice and replay the relevant ones (forwardSlice).
  //
  // TGT needs no command plumbing: it POSTs its own tgt.* via send-command.js.
  const PAGE_FEEDS = {
    avn: ['avn'],
    rwr: ['rwr', 'mw'],       // scope contacts + incoming-missile warnings
    tgt: ['tgt', 'targets'],
    tgp: ['tgp'],
    wpn: ['loadout', 'cm'],   // 'loadout' is derived, not forwarded as-is — see FEED_DERIVE
  };

  // The tap calls it 'targets'; TGT listens for 'tgt-targets'. The bezel renames it in exactly the
  // same place (mfd.js forwardTgtTargetsToFrame), so this mirrors the existing contract rather
  // than inventing one. Every other slice forwards under its own name.
  const FEED_AS = { targets: 'tgt-targets' };

  // Slices needing more than a rename. WPN is the only one: the page shows five rows, so the shell
  // owns *which* five. Pagination is shell state — precisely why NAV.wpn is empty and why the bezel
  // hand-rolls its WPN labels too.
  const FEED_DERIVE = { loadout: forwardWpn };

  const WPN_MAX_DISPLAY = ROWS - 1;   // row 1 is the nav + CM band; rows 2..6 carry the weapons
  const WPN_ICON_INSET  = 20;         // keeps the image off its band edges, as the bezel does
  let   wpnPage = 0;                  // 0-indexed pagination state, owned by the shell
  let   wpnNavKey = '';               // what the WPN grid last drew; guards a per-tick rebuild

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

  // Paging actions, and the direction each moves. Not pages, so they dispatch separately (dispatch).
  const PAGER = { 'wpn-prev': -1, 'wpn-next': 1 };

  // What a screen puts on the grid.
  //   main — NAV's items plus this layout's own, alphabetical. Sorting here (not in NAV) keeps
  //          ordering a rendering choice: the bezel keeps NAV's order, where TGT precedes TGP.
  //   wpn  — nothing from NAV (it's empty by design); its labels are pagination, built below.
  function itemsFor(page) {
    if (page === 'wpn') return wpnState().nav;
    const items = (NAV[page] || []).slice();
    if (page !== 'main') return items;
    return items.concat(MAIN_EXTRAS).sort(function (a, b) { return a.label.localeCompare(b.label); });
  }

  // Where a screen's NAV items sit. Default 'edge' = the bezel's left key bank, minus the bezel.
  // MAIN is 'center': its labels ARE the screen, so they own the middle of the glass instead of
  // hugging an edge that frames nothing. Both modes consume NAV in order — only placement differs,
  // which is exactly the split the seam predicts.
  const NAV_LAYOUT = { main: 'center' };

  let currentPage = null;
  const slices = Object.create(null);   // the tap's latest message, by type

  function has(page) { return Object.prototype.hasOwnProperty.call(F35_PAGES, page); }
  function feedsFor(page) { return PAGE_FEEDS[page] || []; }

  // 'edge' placement: a NAV item's index → its cell. The left column, top-down, IS the bezel's
  // left key bank — the same derivation mfd.js fullViewSlot() uses, which is why NAV needs no
  // placement hints for full view. ('center' needs no function: the items flow in NAV order and
  // the grid's own columns arrange them.)
  function cellOf(i) { return { row: i + 1, col: 1 }; }

  // Push one cached slice into the page, under the name that page listens for.
  function forwardSlice(type) {
    if (FEED_DERIVE[type]) return FEED_DERIVE[type]();
    const w = pageFrame.contentWindow, m = slices[type];
    if (!w || !m) return;
    w.postMessage(FEED_AS[type] ? Object.assign({}, m, { type: FEED_AS[type] }) : m, '*');
  }

  // Everything the current page needs — on its load, and whenever it changes.
  function forwardToPage() {
    feedsFor(currentPage).forEach(forwardSlice);
    if (currentPage === 'wpn') { forwardWpnLayout(); forwardOrientation(); }
  }

  // ── WPN ──────────────────────────────────────────────────────────────────────────────
  // The one page that needs geometry from this layout. Everything else places itself: AVN/TGP/RWR
  // stay in their default profiles and TGT is fully clickable. WPN's `full` profile is the only
  // thing that renders a weapon image, and it only lays out against forwarded rects — so the
  // escape hatch docs/layouts.md banked on (drive the `compact` profile) can't serve this screen:
  // compact scatters weapons into four corners and draws no image at all.
  //
  // So this layout supplies its own rects, from its grid instead of the bezel's separators. The
  // page is untouched and doesn't know the difference; the row bands ARE the key bands.
  // The current page's slice + nav, from the loadout and the shell's page state. All the paging
  // math (clamp, slice boundaries, MAIN/PREV/NEXT labels) lives in the pure f35-wpn-paging module
  // so f35-wpn-paging.test.js can pin it; everything below just reads this.
  function wpnList()  { return (slices.loadout && slices.loadout.items) || []; }
  function wpnState() { return F35WpnPaging.wpnPaging(wpnList(), wpnPage, WPN_MAX_DISPLAY); }

  // Slice the loadout to this page and hand the page its five rows.
  //
  // The labels and hit targets depend on this slice, but a 'loadout' arrives on every tick and
  // most carry nothing but an ammo count. Re-rendering on each would destroy and rebuild the very
  // buttons under the pilot's cursor — the bezel never had to care, since its keys are static DOM
  // and only their labels get replaced. So rebuild only when something the grid shows actually
  // changes: the page, the page count, or the visible names. wpn.js keys its own row rebuild the
  // same way (layout + names).
  function forwardWpn() {
    const w = pageFrame.contentWindow, lo = slices.loadout;
    if (!w || !lo) return;
    const st = wpnState();
    wpnPage = st.page;
    w.postMessage({ mfd: true, type: 'wpn', items: st.visible, selWeapon: lo.selWeapon,
                    page: st.maxPage > 0 ? st.page + 1 : 1, pages: st.maxPage + 1 }, '*');
    const key = st.page + '|' + st.maxPage + '|' + st.visible.map(function (it) { return it.n; }).join(',');
    if (currentPage === 'wpn' && key !== wpnNavKey) { wpnNavKey = key; renderNav(); }
  }

  // Row 1 is the CM band; rows 2..6 are the weapon slots; the image spans rows 2..6. The grid and
  // #page-frame are both inset:0 in .portal, so a row's offset is already the frame's own
  // coordinate space — no frameTop to subtract, unlike the bezel reading shell-side separators.
  function forwardWpnLayout() {
    const w = pageFrame.contentWindow;
    if (!w) return;
    const rowH = pageFrame.getBoundingClientRect().height / ROWS;
    const slots = [];
    for (let k = 0; k < WPN_MAX_DISPLAY; k++) slots.push({ top: (k + 1) * rowH, height: rowH });
    w.postMessage({ mfd: true, type: 'wpn-layout', layout: 'full', slots: slots,
                    cmTop: 0, cmHeight: rowH,
                    iconTop: rowH + WPN_ICON_INSET,
                    iconHeight: (ROWS - 1) * rowH - 2 * WPN_ICON_INSET }, '*');
  }

  // WPN is the only page here that keys CSS off the orientation class: without body.landscape its
  // weapon image renders rotated 90° with swapped dimensions. A page can't read this from its own
  // box, so the shell is the source of truth — same reason the bezel forwards it.
  const orientMq = window.matchMedia('(orientation: portrait)');
  function forwardOrientation() {
    const w = pageFrame.contentWindow;
    if (w) w.postMessage({ mfd: true, type: 'orient',
                           orientation: orientMq.matches ? 'portrait' : 'landscape' }, '*');
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
      navGrid.appendChild(b);
    });
  }

  function dispatch(action) {
    if (action in PAGER) { wpnPage = wpnState().page + PAGER[action]; forwardWpn(); return; }
    if (has(action)) showPage(action);
  }
  function canDo(action) { return has(action) || (action in PAGER); }

  function renderNav() {
    const mode = NAV_LAYOUT[currentPage] || 'edge';
    navGrid.className = 'nav-grid ' + mode;
    navGrid.dataset.page = currentPage;   // lets f35.css special-case a page's labels (see TGT)
    navGrid.textContent = '';
    itemsFor(currentPage).forEach(function (item, i) {
      // An item may name its own cell (WPN's NEXT sits top-right); otherwise it takes the
      // index's. NAV items never carry placement — nav-model.test.js enforces that — so this only
      // ever fires for items this layout built itself.
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
      if (mode === 'edge') {
        b.style.gridRow    = String(cell.row);
        b.style.gridColumn = String(cell.col);
      }
      if (wired) b.addEventListener('click', function () { dispatch(item.action); });
      else       b.disabled = true;
      navGrid.appendChild(b);
    });
    if (currentPage === 'wpn') addWeaponHits();
  }

  function showPage(name) {
    if (!has(name)) return;
    currentPage = name;
    wpnNavKey = '';   // entering any page redraws the grid; don't let a stale key suppress it
    // A screen with no page (MAIN) blanks the frame rather than hiding it — the iframe's own
    // background is the glass colour, so what shows through is the grid on black.
    pageFrame.src = F35_PAGES[name] || 'about:blank';   // forwardToPage reruns on the frame's load
    renderNav();
  }

  window.addEventListener('message', function (e) {
    const m = e.data;
    if (!m || m.mfd !== true) return;
    // Telemetry comes only from the tap. Same guard the bezel shell uses: a second map source
    // would drive the page from two out-of-phase feeds.
    if (e.source !== mapTap.contentWindow) return;
    if (typeof m.type !== 'string') return;
    slices[m.type] = m;   // cache every slice: the screen that wants it may not be up yet
    if (feedsFor(currentPage).indexOf(m.type) !== -1) forwardSlice(m.type);
  });

  pageFrame.addEventListener('load', forwardToPage);

  // WPN's rects are derived from the viewport, so they go stale when it changes. The bezel
  // recomputes from its separators for the same reason. Only WPN cares — every other page here
  // lays itself out with CSS.
  window.addEventListener('resize', function () {
    if (currentPage === 'wpn') forwardWpnLayout();
  });
  orientMq.addEventListener('change', function () {
    if (currentPage === 'wpn') { forwardOrientation(); forwardWpnLayout(); }
  });

  // Land on MAIN — the menu, same as the bezel shell's landing page.
  showPage('main');
})();
