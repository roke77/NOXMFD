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

  // Screens this layout can show, and the page each mounts. Anything else in NAV renders dimmed
  // and inert (.pending): the remaining pages need their own telemetry forwarders (rwr/tgp/wpn/cm
  // slices), which is the next slice of work, not this one.
  //
  // MAIN maps to no page on purpose. Its whole content is its navigation, and navigation is drawn
  // by this shell's grid — so there is nothing left for a page to render and the frame stays
  // blank. (The bezel needs MAIN twice — as #info-box chrome in full view and as /main in a split
  // pane; here it needs it zero times. src/web/pages/main/ is untouched and still serves the
  // bezel.) `null` is meaningful: use `in`, not truthiness, to test membership.
  const F35_PAGES = { avn: '/avn', main: null };

  // Where a screen's NAV items sit. Default 'edge' = the bezel's left key bank, minus the bezel.
  // MAIN is 'center': its labels ARE the screen, so they own the middle of the glass instead of
  // hugging an edge that frames nothing. Both modes consume NAV in order — only placement differs,
  // which is exactly the split the seam predicts.
  const NAV_LAYOUT = { main: 'center' };

  const ROWS = 6;   // 'edge' mode only — must match grid-template-rows in f35.css

  let currentPage = null;
  let lastAvn     = null;

  function has(page) { return Object.prototype.hasOwnProperty.call(F35_PAGES, page); }

  // 'edge' placement: a NAV item's index → its cell. The left column, top-down, IS the bezel's
  // left key bank — the same derivation mfd.js fullViewSlot() uses, which is why NAV needs no
  // placement hints for full view. ('center' needs no function: the items flow in NAV order and
  // the grid's own columns arrange them.)
  function cellOf(i) { return { row: i + 1, col: 1 }; }

  // Push the slice the current page needs. Called on every fresh slice and on the frame's load,
  // since a page that just mounted has missed whatever already arrived.
  function forwardToPage() {
    const w = pageFrame.contentWindow;
    if (currentPage === 'avn' && lastAvn && w) w.postMessage(lastAvn, '*');
  }

  function renderNav() {
    const mode = NAV_LAYOUT[currentPage] || 'edge';
    navGrid.className = 'nav-grid ' + mode;
    navGrid.textContent = '';
    (NAV[currentPage] || []).forEach(function (item, i) {
      if (mode === 'edge' && i >= ROWS) {
        console.warn('[f35] NAV.' + currentPage + '[' + i + '] "' + item.label +
                     '" overflows the ' + ROWS + '-row grid — not placed');
        return;
      }
      const wired = has(item.action);
      const b = document.createElement('button');
      b.className   = 'nav-item' + (wired ? '' : ' pending');
      b.textContent = item.label;
      if (mode === 'edge') {
        const cell = cellOf(i);
        b.style.gridRow    = String(cell.row);
        b.style.gridColumn = String(cell.col);
      }
      if (wired) b.addEventListener('click', function () { showPage(item.action); });
      else       b.disabled = true;
      navGrid.appendChild(b);
    });
  }

  function showPage(name) {
    if (!has(name)) return;
    currentPage = name;
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
    if (m.type === 'avn') { lastAvn = m; if (currentPage === 'avn') forwardToPage(); }
  });

  pageFrame.addEventListener('load', forwardToPage);

  // Land on MAIN — the menu, same as the bezel shell's landing page.
  showPage('main');
})();
