// Self-check for f35-glass. Run: node f35-glass.test.js
//
// Guards the arrangement rule the F-35's corner grips rest on: which portals may merge, which way
// each grip points, and — the part worth pinning — that the reachable arrangements are exactly the
// five the design allows. A drift here either strands the pilot in a layout with no way out, or
// quietly grows a portal past a pair.
const assert = require('assert');
const { SLOTS, gripsFor, merge, split, valid } = require('./f35-glass.js');

const one  = () => ({ span: 1 });
const four = () => [one(), one(), one(), one()];
// Two views of the same glass, and the difference matters:
//   layout — which slots are merged. This is what the pilot sees, and the five configurations
//            the design allows are five LAYOUTS.
//   shape  — layout plus who ate whom. (1 2) reached from the left and from the right look
//            identical, but the survivor differs, so they are different states: it keeps its page
//            and its grip stays in its own corner.
const layout = cells => cells.map(c => String(c.span)).join(',');
const shape  = cells => cells.map(c => (c.span === 1 ? '1' : '2' + c.ate[0])).join(',');
const actions = (cells, i) => gripsFor(cells, i).map(g => g.action);
const corners = (cells, i) => gripsFor(cells, i).map(g => g.corner + '/' + g.aim);

// ── The grips on an untouched glass ─────────────────────────────────────────────────
// Every divider gets two grips facing it, one from each side, so a merge is reachable from
// either neighbour. Three dividers, six grips — the ends have only one neighbour each.
let g = four();
assert.deepStrictEqual(actions(g, 0), ['merge-right'], 'portal 1 has no left neighbour');
assert.deepStrictEqual(actions(g, 1), ['merge-left', 'merge-right']);
assert.deepStrictEqual(actions(g, 2), ['merge-left', 'merge-right']);
assert.deepStrictEqual(actions(g, 3), ['merge-left'], 'portal 4 has no right neighbour');
assert.strictEqual(g.reduce((n, _, i) => n + gripsFor(g, i).length, 0), 6);

// A grip sits in the corner facing what it acts on, and points that way to take it.
assert.deepStrictEqual(corners(g, 0), ['right/right']);
assert.deepStrictEqual(corners(g, 1), ['left/left', 'right/right']);

// ── Merging ─────────────────────────────────────────────────────────────────────────
// Either side may start it, and the survivor records which side it ate — the only thing a later
// split needs.
assert.strictEqual(shape(merge(four(), 0, 'right')), '2r,1,1');   // 1 takes 2
assert.strictEqual(shape(merge(four(), 1, 'left')),  '2l,1,1');   // 2 takes 1 — same footprint
assert.strictEqual(shape(merge(four(), 1, 'right')), '1,2r,1');   // the middle merge
assert.strictEqual(shape(merge(four(), 2, 'right')), '1,1,2r');

// ── The five arrangements, and only those ───────────────────────────────────────────
// Walk every merge from every reachable state. Anything outside this set means the rule leaks —
// an arrangement the design never sanctioned, or a portal grown past a pair.
const states = new Set(), layouts = new Set();
(function walk(cells) {
  const key = shape(cells);
  if (states.has(key)) return;
  states.add(key);
  layouts.add(layout(cells));
  assert.ok(valid(cells), 'reachable but not a valid glass: ' + key);
  cells.forEach((_, i) => {
    ['left', 'right'].forEach(side => {
      const next = merge(cells, i, side);
      if (next) walk(next);
    });
  });
})(four());

// The design's list, exactly: 1 2 3 4 / (1 2) 3 4 / 1 (2 3) 4 / 1 2 (3 4) / (1 2) (3 4).
assert.deepStrictEqual([...layouts].sort(), ['1,1,1,1', '1,1,2', '1,2,1', '2,1,1', '2,2'].sort());

// Each of the three merges can be reached from either side, so a layout with N merges has 2^N
// states: 1 + 2 + 2 + 2 + 4 = 11. Not a design decision — a consequence of the survivor keeping
// its page, and the reason `ate` is stored at all.
assert.strictEqual(states.size, 11);

// No triples: a merged portal is never a merge candidate, from either direction.
const merged = merge(four(), 0, 'right');            // (1 2) 3 4
assert.deepStrictEqual(actions(merged, 0), ['split'], 'a merged portal offers only its way back');
assert.deepStrictEqual(actions(merged, 1), ['merge-right'], 'portal 3 cannot merge into the pair on its left');
assert.strictEqual(merge(merged, 1, 'left'), null, 'merging into a merged neighbour is refused');

// The middle merge strands its neighbours: their only neighbour is a pair, so they wait.
const mid = merge(four(), 1, 'right');               // 1 (2 3) 4
assert.deepStrictEqual(actions(mid, 0), [], 'portal 1 has nothing to merge with');
assert.deepStrictEqual(actions(mid, 2), [], 'portal 4 has nothing to merge with');
assert.deepStrictEqual(actions(mid, 1), ['split']);

// ── Splitting ───────────────────────────────────────────────────────────────────────
// The grip stays in the corner it merged from and turns around — that is the whole visual rule.
assert.deepStrictEqual(gripsFor(merge(four(), 0, 'right'), 0), [{ corner: 'right', aim: 'left', action: 'split' }]);
assert.deepStrictEqual(gripsFor(merge(four(), 1, 'left'), 0),  [{ corner: 'left', aim: 'right', action: 'split' }]);

// Splitting undoes the merge, and the survivor lands back where it was: it kept its page, so the
// newcomer must take the side that was eaten.
let r = split(merge(four(), 0, 'right'), 0);
assert.strictEqual(shape(r.cells), '1,1,1,1');
assert.strictEqual(r.survivor, 0, 'ate right, so it sat on the left and stays there');
r = split(merge(four(), 1, 'left'), 0);
assert.strictEqual(shape(r.cells), '1,1,1,1');
assert.strictEqual(r.survivor, 1, 'ate left, so it sat on the right');

assert.strictEqual(split(four(), 0), null, 'an unmerged portal has nothing to split');

// Both halves merged, then unpicked one at a time — the far pair must not move.
let both = merge(merge(four(), 0, 'right'), 1, 'right');
assert.strictEqual(shape(both), '2r,2r');
assert.strictEqual(shape(split(both, 0).cells), '1,1,2r');

// SLOTS is the glass's width; every arrangement spends exactly that.
assert.strictEqual(SLOTS, 4);

console.log('f35-glass.test.js: OK');
