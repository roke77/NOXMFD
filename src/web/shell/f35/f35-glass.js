// Pure arrangement rules for the F-35 layout's glass. f35.js owns the portals and the DOM; this
// owns only the question "given how the glass is divided right now, what can each portal do?".
//
// The glass is four slots wide. A portal fills one slot or two — never three, so a merge joins a
// PAIR of neighbours and nothing larger. Every arrangement is therefore some set of adjacent
// merges that don't overlap, which is exactly five:
//
//     1 2 3 4        (1 2) 3 4        1 (2 3) 4        1 2 (3 4)        (1 2) (3 4)
//
// Nothing else is reachable: (1 2) and (2 3) would both need portal 2, and a triple would need a
// merged portal to merge again.
//
// Layout is not the whole story. (1 2) merged from the left and from the right occupy the same
// slots but keep different pages — the survivor is whoever pressed — so the same five layouts
// cover several states, and `ate` is what tells them apart. A split needs it to put the newcomer
// back on the side that was eaten.
//
// A `cell` is a portal's footprint: { span: 1 } unmerged, or { span: 2, ate: 'left'|'right' } for
// one that swallowed the neighbour on that side. `cells` is them in order, left to right, spans
// summing to SLOTS.
//
// Kept pure and importable so f35-glass.test.js can pin the rule; f35.js is the only caller.
(function (root) {
  const SLOTS = 4;

  // What this portal's grips offer. A grip sits in the corner facing what it acts on, and its aim
  // says which way the triangle points — outward to take, inward to give back.
  function gripsFor(cells, i) {
    const c = cells[i];
    if (!c) return [];

    // Merged: the only thing on offer is the way back. One grip, in the corner over the slot it
    // swallowed, pointing inward across it.
    if (c.span > 1) {
      return [{ corner: c.ate, aim: c.ate === 'right' ? 'left' : 'right', action: 'split' }];
    }

    // Unmerged: ONE grip, and it faces the centre of the glass. Both neighbours of a divider could
    // offer to merge across it, but the two offers differ only in which page survives — so giving
    // each divider a single grip costs a choice rather than a layout, and every arrangement stays
    // reachable. Facing the centre is what makes that choice symmetric: the outer portals reach
    // inwards, and the centre divider is the one place two grips still meet, because the portals
    // either side of it both point at it.
    const side = startOf(cells, i) < SLOTS / 2 ? 'right' : 'left';
    if (!mergeable(cells, side === 'left' ? i - 1 : i + 1)) return [];
    return [{ corner: side, aim: side, action: 'merge-' + side }];
  }

  // Which slot a portal starts on. Spans before it, added up — the arrangement is the only thing
  // that says where a portal sits.
  function startOf(cells, i) {
    let n = 0;
    for (let k = 0; k < i; k++) n += cells[k].span;
    return n;
  }

  function mergeable(cells, i) { return !!cells[i] && cells[i].span === 1; }

  // The arrangement after a merge or split. Pure: `cells` in, new `cells` out, so the caller can
  // ask what would happen before doing it to the DOM.
  //
  //   merge — `i` absorbs its neighbour; the survivor's cell takes both slots and remembers which
  //           side it ate, which is the only thing a later split needs to know.
  //   split — the merged cell becomes two, and the newcomer lands back on the side that was eaten.
  //           `survivor` is where the original portal ended up, since it keeps its page.
  function merge(cells, i, side) {
    const j = side === 'left' ? i - 1 : i + 1;
    if (!mergeable(cells, i) || !mergeable(cells, j)) return null;
    const out = cells.slice();
    out.splice(Math.min(i, j), 2, { span: 2, ate: side });
    return out;
  }

  function split(cells, i) {
    const c = cells[i];
    if (!c || c.span < 2) return null;
    const out = cells.slice();
    out.splice(i, 1, { span: 1 }, { span: 1 });
    // It ate to its right, so it sat on the left of the block and stays there; and vice versa.
    return { cells: out, survivor: c.ate === 'right' ? i : i + 1 };
  }

  // Every cells array this module produces should still describe the glass: spans summing to
  // SLOTS, none wider than a pair. Cheap enough to assert on every change.
  function valid(cells) {
    const total = cells.reduce(function (n, c) { return n + c.span; }, 0);
    return total === SLOTS && cells.every(function (c) {
      return c.span === 1 || (c.span === 2 && (c.ate === 'left' || c.ate === 'right'));
    });
  }

  const api = { SLOTS: SLOTS, gripsFor: gripsFor, merge: merge, split: split, valid: valid };
  if (typeof module !== 'undefined' && module.exports) module.exports = api;
  else root.F35Glass = api;
})(typeof self !== 'undefined' ? self : this);
