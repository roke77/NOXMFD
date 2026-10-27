// Pure helpers behind CLASSIC's PIN/SWAP keys, which pin a whole arrangement
// ({splitMode, splitVariant, pages} — mfd.js's captureArrangement shape), not a single page.
// Carries no DOM refs so this can be unit-tested in Node (layout-pin.test.js).
(function (root) {
  // An unknown variant applies as 'h' (applyArrangement), so a pin normalises it the same way.
  const VARIANTS = ['h', 'v', 'vw', 'vwr'];

  // A full view carries no splitVariant, so two full views compare equal on page alone.
  function sameArrangement(a, b) {
    return a.splitMode === b.splitMode &&
      (!a.splitMode || a.splitVariant === b.splitVariant) &&
      a.pages.join('|') === b.pages.join('|');
  }

  // A full-view MENU is never pinnable: SWAP to it would only repeat the MAIN key.
  function pinnable(arr) {
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
    const arr = pin.splitMode
      ? { splitMode: true, splitVariant: VARIANTS.indexOf(pin.splitVariant) !== -1 ? pin.splitVariant : 'h',
          pages: pin.pages.slice(0, 2) }
      : { splitMode: false, pages: pin.pages.slice(0, 1) };
    return pinnable(arr) ? arr : null;
  }

  const api = { sameArrangement, pinnable, pinFromState };
  if (typeof module !== 'undefined' && module.exports) module.exports = api;
  else root.LayoutPin = api;
})(typeof self !== 'undefined' ? self : this);
