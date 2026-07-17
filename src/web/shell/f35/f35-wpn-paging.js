// Pure pagination for the F-35 layout's WPN screen. The page renders a fixed number of weapon
// rows, so the shell owns *which* rows show — this decides the clamped page, the visible slice,
// and the top-row nav labels: MAIN on page 0 / PREV after it (top-left), and NEXT (top-right)
// only while pages remain. Mirrors the bezel's placeWpnNavLabels without the bezel.
//
// Kept pure and importable so f35-wpn-paging.test.js can pin the slice boundaries and the
// MAIN/PREV/NEXT switch; f35.js is the only caller. NEXT names its own cell because it belongs
// top-right — legal here (this is the layout's own item, not a shared NAV item, which the
// nav-model invariant forbids from carrying placement).
//
// wpnPaging(items, page, maxDisplay) -> {
//   page:    the requested page clamped to [0, maxPage]
//   maxPage: highest 0-indexed page (0 when everything fits on one)
//   visible: the items on `page` (never more than maxDisplay)
//   nav:     [{label,action}, ...] — MAIN|PREV, then NEXT{cell} while pages remain
// }
(function (root) {
  function wpnPaging(items, page, maxDisplay) {
    const list = Array.isArray(items) ? items : [];
    const maxPage = Math.max(0, Math.ceil(list.length / maxDisplay) - 1);
    const cur = Math.min(Math.max(page | 0, 0), maxPage);
    const start = cur * maxDisplay;
    const visible = list.slice(start, start + maxDisplay);
    const nav = [cur > 0 ? { label: 'PREV', action: 'wpn-prev' }
                         : { label: 'MAIN', action: 'main' }];
    if (cur < maxPage) nav.push({ label: 'NEXT', action: 'wpn-next', cell: { row: 1, col: 2 } });
    return { page: cur, maxPage: maxPage, visible: visible, nav: nav };
  }

  const api = { wpnPaging: wpnPaging };
  if (typeof module !== 'undefined' && module.exports) module.exports = api;
  else root.F35WpnPaging = api;
})(typeof self !== 'undefined' ? self : this);
