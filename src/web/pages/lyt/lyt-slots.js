// What the LYT page's SAVED rows show for a saved layout. Pure: no DOM, no fetch.
//
// A saved layout's `data` is the shell's own serialized arrangement:
// - CLASSIC (mfd.js captureLayoutState): { splitMode, splitVariant: 'h'|'v'|'vw'|'vwr', pages: [...] }
//   — one page in full view, one per pane in a split (pane 0 is the top or left one).
// - F-35 (f35.js captureLayoutState): { cells: [{span, ate?}], pages: [...] } — one cell per portal,
//   left to right, spans summing to the glass's four slots (f35-glass.js).
//
// Classic <script> (global LytSlots), not a module: the LYT page and the CLASSIC shell's save dialog
// both use it, and the shell has no module graph.
(function (root) {
  const THIRD = 1 / 3;

  // Pane rectangles as fractions [x, y, w, h] of the screen, keyed by the shell's splitVariant
  // ('full' is the unsplit view). Labels are the names the bezel's own split keys use; sides name
  // each pane the way the shell's SOI checkboxes do (mfd.js soiSurfaces).
  const SPLITS = {
    full: { label: 'F_VIEW', rects: [[0, 0, 1, 1]], sides: ['FULL VIEW'] },
    h:    { label: 'H_SPLIT', rects: [[0, 0, 1, 0.5], [0, 0.5, 1, 0.5]], sides: ['TOP PANE', 'BOTTOM PANE'] },
    v:    { label: 'V_SPLIT', rects: [[0, 0, 0.5, 1], [0.5, 0, 0.5, 1]], sides: ['LEFT PANE', 'RIGHT PANE'] },
    vw:   { label: 'WIDE L', rects: [[0, 0, 2 * THIRD, 1], [2 * THIRD, 0, THIRD, 1]], sides: ['LEFT PANE', 'RIGHT PANE'] },
    vwr:  { label: 'WIDE R', rects: [[0, 0, THIRD, 1], [THIRD, 0, 2 * THIRD, 1]], sides: ['LEFT PANE', 'RIGHT PANE'] },
  };

  // The F-35 glass's slot count (F35Glass.SLOTS); repeated so the LYT page needn't load f35-glass.js.
  const F35_SLOTS = 4;

  // Keybinds exist for the first five layouts only (KeybindConflict.LayoutSlotCount).
  const SLOT_COUNT = 5;

  // One saved layout ({id, name, data}) → a row. `pos` is its place in the list, `slot` its Layout N
  // keybind (first five only). `state` is the parsed arrangement (null when unreadable) — the edit
  // dialog writes it back with its changes. `soi` is each pane's SOI-rotation membership (all in
  // unless the arrangement says `soi: [false, …]`), or null when the data is unreadable.
  // Data that can't be read still gives a row (with no panes), so the layout can be found and deleted.
  function describeLayout(item, index) {
    let state;
    try { state = JSON.parse(item.data); } catch (e) { state = null; }
    const row = { id: item.id, name: String(item.name || ''), pos: index + 1, slot: index < SLOT_COUNT ? index + 1 : null, state: null, soi: null, sides: [] };
    if (!state || typeof state !== 'object') return Object.assign(row, { label: 'UNREADABLE', rects: [], pages: [] });
    const { label, rects, sides } = Array.isArray(state.cells) ? f35Split(state.cells) : classicSplit(state);
    const saved = Array.isArray(state.pages) ? state.pages : [];
    // A pane with no saved page lands on MAIN. Pages labelled apart from their ids show under the
    // label: `avn` as ENG, `afm` as SYS (nav-model.js's Avionics hub).
    const LABEL = { avn: 'ENG', afm: 'SYS' };
    const pages = rects.map((_, i) => { const p = String(saved[i] || 'main'); return LABEL[p] || p.toUpperCase(); });
    const soi = rects.map((_, i) => !(Array.isArray(state.soi) && state.soi[i] === false));
    return Object.assign(row, { label, rects, pages, state, soi, sides });
  }

  function classicSplit(state) {
    // Same fallback applyLayoutState uses: an unknown variant is the top/bottom split.
    const split = !state.splitMode ? 'full' : (SPLITS[state.splitVariant] && state.splitVariant !== 'full' ? state.splitVariant : 'h');
    return SPLITS[split];
  }

  // Each portal a full-height column, as wide as its span. Cells the glass can't take fall back to
  // four single portals, as f35.js applyLayoutState does.
  function f35Split(cells) {
    const ok = cells.every((c) => c && (c.span === 1 || c.span === 2)) &&
               cells.reduce((n, c) => n + c.span, 0) === F35_SLOTS;
    const spans = ok ? cells.map((c) => c.span) : [1, 1, 1, 1];
    let x = 0;
    const rects = spans.map((n) => { const r = [x / F35_SLOTS, 0, n / F35_SLOTS, 1]; x += n; return r; });
    return { label: spans.length + ' PORTALS', rects, sides: spans.map((_, i) => 'PORTAL ' + (i + 1)) };
  }

  function describeLayouts(items) { return items.map(describeLayout); }

  const api = { SPLITS: SPLITS, SLOT_COUNT: SLOT_COUNT, describeLayout: describeLayout, describeLayouts: describeLayouts };
  if (typeof module !== 'undefined' && module.exports) module.exports = api;
  else root.LytSlots = api;
})(typeof self !== 'undefined' ? self : this);
