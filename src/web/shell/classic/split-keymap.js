// Split-mode bezel key mapping, split out of mfd.js so it carries no DOM refs and can be
// unit-checked in Node (see split-keymap.test.js).
//
// In split mode the screen shows two panes. Each page emits pane-LOCAL label positions as
// { side: 'left'|'right', slot: 0..2 } (slot 0 = the nav row, 1..2 = content rows). This maps
// that pane-local position to the PHYSICAL bezel key { bank, index } for the current split
// orientation:
//   'h'  (top/bottom): each pane keeps BOTH columns; pane 0 = top → keys 0..2, pane 1 = bottom
//                      → keys 3..5 (a +3 offset on the same-side column).
//   'v' / 'vw' (left/right): each pane owns its ADJACENT column — pane 0 (left) → left column,
//                      pane 1 (right) → right column — with the page's left-side slots on that
//                      column's keys 0..2 and its right-side slots on keys 3..5.
(function (root) {
  function paneKey(variant, paneIdx, side, slot) {
    if (variant === 'h') {
      return { bank: side, index: slot + paneIdx * 3 };
    }
    // 'v' / 'vw' — left/right: the pane's own column carries both of the page's sides.
    return { bank: paneIdx === 0 ? 'left' : 'right', index: (side === 'left' ? slot : slot + 3) };
  }

  const api = { paneKey };
  if (typeof module !== 'undefined' && module.exports) module.exports = api;
  else root.SplitKeymap = api;
})(typeof self !== 'undefined' ? self : this);
