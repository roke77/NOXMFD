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

  // Full view has more room than a pane: 5 line-select slots (keys 1..5).
  const WPN_MAX_DISPLAY = 5;

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

  // 0-indexed page holding the named selection, or -1 when nothing is selected or the selection
  // isn't in the list. Serves both layouts — the only difference between them is perPage
  // (WPN_SPLIT_MAX in a pane, WPN_MAX_DISPLAY in full view).
  //
  // A plain divide is correct even though buildWpnSplitPages inserts padding: the padding only ever
  // lands AFTER the weapon run (it exists to align the control pairs that follow), so weapons stay
  // contiguous from slot 0 and their index is their position.
  function pageOfSelection(list, sel, perPage) {
    if (!sel) return -1;
    const i = (list || []).findIndex(function (w) { return w.n === sel; });
    return i < 0 ? -1 : Math.floor(i / perPage);
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

  // Where a list page's nav and item rows land on the physical bezel keys, per split orientation.
  // The companion to split-keymap.js's paneKey: that maps a page's own pane-local slot, this one
  // hands a LIST page (which owns the whole pane) its fixed positions directly.
  //
  // 'h' (top/bottom): each pane keeps both columns, offset by 3 for the bottom pane. WPN and AVN
  // put NEXT top-right and take four item keys below the top band; every other list page has no
  // top-right control of its own, so NEXT sits at the column's end and the items start higher.
  // 'v'/'vw' (left/right): the pane owns one adjacent column — MAIN at its top key, NEXT at its
  // bottom, items filling the four between.
  function listPaneLayout(variant, paneIdx, page) {
    if (variant === 'h') {
      const off = paneIdx * 3;
      if (page === 'wpn' || page === 'avn') {
        return {
          main: { bank: 'left', index: off }, next: { bank: 'right', index: off },
          items: [{ bank: 'left', index: off + 1 }, { bank: 'left', index: off + 2 },
                  { bank: 'right', index: off + 1 }, { bank: 'right', index: off + 2 }],
          itemSides: ['left', 'left', 'right', 'right'],
        };
      }
      return {
        main: { bank: 'left', index: off }, next: { bank: 'right', index: off + 2 },
        items: [{ bank: 'left', index: off + 1 }, { bank: 'left', index: off + 2 },
                { bank: 'right', index: off }, { bank: 'right', index: off + 1 }],
        itemSides: ['left', 'left', 'right', 'right'],
      };
    }
    const side = paneIdx === 0 ? 'left' : 'right';   // left/right pane owns its adjacent column
    return {
      main: { bank: side, index: 0 }, next: { bank: side, index: 5 },
      items: [{ bank: side, index: 1 }, { bank: side, index: 2 },
              { bank: side, index: 3 }, { bank: side, index: 4 }],
      itemSides: [side, side, side, side],
    };
  }

  const api = { buildWpnSplitPages, wpnPaneSlice, avnPaneSlice, mainPageSizes, mainPaneSlice,
                listPaneLayout, pageOfSelection,
                WPN_SPLIT_MAX, WPN_MAX_DISPLAY, WPN_SPLIT_CONTROLS, AVN_PANE_PAGE_SIZE, MAIN_PANE_SLOTS };
  if (typeof module !== 'undefined' && module.exports) module.exports = api;
  else root.ClassicPaging = api;
})(typeof self !== 'undefined' ? self : this);
