// Split-pane pagination for the classic bezel, split out of mfd.js so it carries no DOM refs and
// can be unit-checked in Node (see classic-paging.test.js) — the same treatment split-keymap.js
// and nav-model.js already get, and the bezel counterpart to f35/f35-wpn-paging.js.
//
// A split pane exposes a fixed number of physical keys, so the shell owns *which* rows show. Each
// slice function is pure: it takes the full list plus the CURRENT page index and returns the
// clamped page alongside the visible window. Clamping is returned rather than applied, so the
// caller writes `pageIndex` back into its own per-pane state (mfd.js does this in thin wrappers)
// and this module stays free of mutable state.
(function (root) {
  // WPN: a pane shows at most 4 weapons (slots L1, L2, R1, R2); the top band is reserved for
  // MAIN/PREV (L0) and NEXT (R0).
  const WPN_SPLIT_MAX = 4;

  // ARM/SAFE/A-A/A-G (docs/radar-master-arms.md) in a split pane: appended after the weapon list,
  // sharing the same 4-slot-per-page window weapons use (unlike full view, which has dedicated
  // right-column keys free for them). A pair is never split across a page boundary — since
  // (ARM,SAFE) and (A/A,A/G) are always two adjacent entries in the combined list, a pair only ever
  // splits when its first item would land on a page's LAST slot, so one empty slot inserted right
  // before the pair pushes the whole thing to the next page, leaving the leftover slot(s) blank.
  const WPN_SPLIT_CONTROLS = [
    { id: 'master-arms-on',  label: 'ARM'  },
    { id: 'master-arms-off', label: 'SAFE' },
    { id: 'combat-mode-aa',  label: 'A/A'  },
    { id: 'combat-mode-ag',  label: 'A/G'  },
  ];

  // AVN: a pane shows 4 of the 8 avn.toggle groups per page (item slots L1,L2,R1,R2 like WPN's).
  const AVN_PANE_PAGE_SIZE = 4;

  // MAIN: the physical keys a split pane exposes for the MAIN list.
  const MAIN_PANE_SLOTS = 6;

  function buildWpnSplitPages(weaponCount) {
    const slots = [];
    for (let i = 0; i < weaponCount; i++) slots.push({ type: 'weapon', index: i });
    function padIfWouldSplitNextPair() {
      if (slots.length % WPN_SPLIT_MAX === WPN_SPLIT_MAX - 1) slots.push({ type: 'empty' });
    }
    padIfWouldSplitNextPair();
    slots.push(Object.assign({ type: 'ctrl' }, WPN_SPLIT_CONTROLS[0]));
    slots.push(Object.assign({ type: 'ctrl' }, WPN_SPLIT_CONTROLS[1]));
    padIfWouldSplitNextPair();
    slots.push(Object.assign({ type: 'ctrl' }, WPN_SPLIT_CONTROLS[2]));
    slots.push(Object.assign({ type: 'ctrl' }, WPN_SPLIT_CONTROLS[3]));
    const pages = [];
    for (let i = 0; i < slots.length; i += WPN_SPLIT_MAX) pages.push(slots.slice(i, i + WPN_SPLIT_MAX));
    return pages;
  }

  function clamp(page, maxPage) {
    if (!(page >= 0)) return 0;              // also catches undefined/NaN
    return page > maxPage ? maxPage : page;
  }

  function wpnPaneSlice(weapons, page) {
    const list = weapons || [];
    const pages = buildWpnSplitPages(list.length);
    const maxPage = pages.length - 1;
    const p = clamp(page, maxPage);
    const slots = pages[p] || [];
    const items = slots.filter(function (s) { return s.type === 'weapon'; })
                       .map(function (s) { return list[s.index]; });
    return { items: items, slots: slots, hasPrev: p > 0, hasNext: p < maxPage,
             page: maxPage > 0 ? p + 1 : 1, pages: maxPage + 1, pageIndex: p };
  }

  function avnPaneSlice(groups, page) {
    const list = groups || [];
    const maxPage = Math.ceil(list.length / AVN_PANE_PAGE_SIZE) - 1;
    const p = clamp(page, maxPage);
    const start = p * AVN_PANE_PAGE_SIZE;
    return {
      items: list.slice(start, start + AVN_PANE_PAGE_SIZE),
      hasPrev: p > 0,
      hasNext: p < maxPage,
      // 1-indexed, mirrors wpnPaneSlice's page/pages — lets the page show a "PAGE x/y" indicator
      // (avn.js) so a pilot in a split pane knows 4 of the 8 groups are a NEXT press away.
      page: p + 1,
      pages: maxPage + 1,
      pageIndex: p,
    };
  }

  // How many MAIN items fit on each page: fill the pane's keys, minus PREV on every page but the
  // first and NEXT on every page but the last.
  function mainPageSizes(total) {
    const sizes = [];
    let placed = 0;
    while (placed < total) {
      const room = MAIN_PANE_SLOTS - (sizes.length === 0 ? 0 : 1);   // minus PREV on every page but the first
      if (total - placed <= room) { sizes.push(total - placed); break; }   // the rest fits, no NEXT needed
      sizes.push(room - 1);                                          // reserve the last key for NEXT
      placed += room - 1;
    }
    return sizes;
  }

  function mainPaneSlice(items, page) {
    const list = items || [];
    const sizes = mainPageSizes(list.length);
    const p = clamp(page, sizes.length - 1);
    let start = 0;
    for (let k = 0; k < p; k++) start += sizes[k];
    return { items: list.slice(start, start + sizes[p]),
             hasPrev: p > 0, hasNext: p < sizes.length - 1, pageIndex: p };
  }

  const api = { buildWpnSplitPages, wpnPaneSlice, avnPaneSlice, mainPageSizes, mainPaneSlice,
                WPN_SPLIT_MAX, WPN_SPLIT_CONTROLS, AVN_PANE_PAGE_SIZE, MAIN_PANE_SLOTS };
  if (typeof module !== 'undefined' && module.exports) module.exports = api;
  else root.ClassicPaging = api;
})(typeof self !== 'undefined' ? self : this);
