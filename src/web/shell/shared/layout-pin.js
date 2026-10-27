// Pure helpers behind PIN/SWAP, which pin a whole arrangement, not a single page — CLASSIC's
// {splitMode, splitVariant, pages} (mfd.js captureArrangement) or the F-35's {cells, pages}
// (f35.js captureArrangement, cells as in f35-glass.js). Carries no DOM refs so this can be
// unit-tested in Node (layout-pin.test.js).
(function (root) {
  // An unknown variant applies as 'h' (applyArrangement), so a pin normalises it the same way.
  const VARIANTS = ['h', 'v', 'vw', 'vwr'];

  // The arrangement's shape without its pages. A full view carries no splitVariant, so two full
  // views compare equal on page alone; F-35 cells compare by span and the side each merge ate.
  function shape(a) {
    if (Array.isArray(a.cells)) return a.cells.map(function (c) { return c.span + (c.ate || ''); }).join(',');
    return a.splitMode ? a.splitVariant : 'full';
  }

  function sameArrangement(a, b) {
    return shape(a) === shape(b) && a.pages.join('|') === b.pages.join('|');
  }

  // Nothing but MAIN is never pinnable: SWAP to it would only repeat the MAIN key. On CLASSIC a split
  // of MAINs still counts, as a real arrangement of panes.
  function pinnable(arr) {
    if (Array.isArray(arr.cells)) return arr.pages.some(function (p) { return p !== 'main'; });
    return arr.splitMode || arr.pages[0] !== 'main';
  }

  // The pin a saved layout blob carries, or null. Blobs saved while PIN held a single page carry
  // `pinnedPage` instead; that reads as a full view of that page. Anything malformed degrades to
  // no pin rather than an arrangement SWAP can't apply.
  function pinFromState(state) {
    if (!state) return null;
    const pin = state.pinnedLayout ||
      (typeof state.pinnedPage === 'string' ? { splitMode: false, pages: [state.pinnedPage] } : null);
    if (!pin || !Array.isArray(pin.pages) || !pin.pages.length) return null;
    // F-35 cells the glass can't take rebuild the default glass on apply (f35.js applyArrangement).
    const arr = Array.isArray(pin.cells)
      ? { cells: pin.cells.map(function (c) { return c.ate ? { span: c.span, ate: c.ate } : { span: c.span }; }),
          pages: pin.pages.slice(0, pin.cells.length) }
      : pin.splitMode
      ? { splitMode: true, splitVariant: VARIANTS.indexOf(pin.splitVariant) !== -1 ? pin.splitVariant : 'h',
          pages: pin.pages.slice(0, 2) }
      : { splitMode: false, pages: pin.pages.slice(0, 1) };
    return pinnable(arr) ? arr : null;
  }

  const api = { sameArrangement, pinnable, pinFromState };
  if (typeof module !== 'undefined' && module.exports) module.exports = api;
  else root.LayoutPin = api;
})(typeof self !== 'undefined' ? self : this);
