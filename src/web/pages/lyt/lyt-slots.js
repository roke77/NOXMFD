// What the LYT page's SAVED rows show for a saved CLASSIC layout. Pure: no DOM, no fetch.
//
// A saved layout's `data` is the shell's own serialized arrangement (mfd.js captureLayoutState):
// { splitMode, splitVariant: 'h'|'v'|'vw'|'vwr', pages: [...] } — one page in full view, one per
// pane in a split (pane 0 is the top or left one).
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
    // Same fallback applyLayoutState uses: an unknown variant is the top/bottom split.
    const split = !state.splitMode ? 'full' : (SPLITS[state.splitVariant] && state.splitVariant !== 'full' ? state.splitVariant : 'h');
    const { label, rects, sides } = SPLITS[split];
    const saved = Array.isArray(state.pages) ? state.pages : [];
    // A pane with no saved page lands on MAIN.
    const pages = rects.map((_, i) => String(saved[i] || 'main').toUpperCase());
    const soi = rects.map((_, i) => !(Array.isArray(state.soi) && state.soi[i] === false));
    return Object.assign(row, { label, rects, pages, state, soi, sides });
  }

  function describeLayouts(items) { return items.map(describeLayout); }

  const api = { SPLITS: SPLITS, SLOT_COUNT: SLOT_COUNT, describeLayout: describeLayout, describeLayouts: describeLayouts };
  if (typeof module !== 'undefined' && module.exports) module.exports = api;
  else root.LytSlots = api;
})(typeof self !== 'undefined' ? self : this);
