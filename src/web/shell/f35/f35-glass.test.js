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
// One grip each, facing the centre: the left half reaches right, the right half reaches left.
// Both neighbours of a divider could offer the merge, but the offers differ only in which page
// survives — so one grip per divider costs a choice, not a layout.
let g = four();
assert.deepStrictEqual(actions(g, 0), ['merge-right'], 'portal 1 reaches inwards');
assert.deepStrictEqual(actions(g, 1), ['merge-right'], 'portal 2 is in the left half, so it too reaches right');
assert.deepStrictEqual(actions(g, 2), ['merge-left'],  'portal 3 is in the right half, so it reaches left');
assert.deepStrictEqual(actions(g, 3), ['merge-left'],  'portal 4 reaches inwards');
assert.strictEqual(g.reduce((n, _, i) => n + gripsFor(g, i).length, 0), 4);

// The centre divider is the one place two grips meet — the portals either side both face it.
// That is what makes (2 3) the only merge reachable from either direction.
assert.deepStrictEqual(corners(g, 1), ['right/right']);
assert.deepStrictEqual(corners(g, 2), ['left/left']);

// A grip sits in the corner facing what it acts on, and points that way to take it.
assert.deepStrictEqual(corners(g, 0), ['right/right']);

// ── Merging ─────────────────────────────────────────────────────────────────────────
// merge() itself takes any legal side — it is the mechanism, and gripsFor is the policy that
// decides which sides a pilot is offered. Same footprint, different survivor.
assert.strictEqual(shape(merge(four(), 0, 'right')), '2r,1,1');   // 1 takes 2
assert.strictEqual(shape(merge(four(), 1, 'left')),  '2l,1,1');   // 2 takes 1 — same slots, other page
assert.strictEqual(shape(merge(four(), 1, 'right')), '1,2r,1');   // the centre merge, from the left
assert.strictEqual(shape(merge(four(), 2, 'left')),  '1,2l,1');   // ...and from the right
assert.strictEqual(shape(merge(four(), 3, 'left')),  '1,1,2l');

// ── The five arrangements, and only those ───────────────────────────────────────────
// Walk only what the grips OFFER, from every state they lead to — this is what a pilot can
// actually reach by pressing things, which is the claim worth pinning. Anything outside the set
// means the rule leaks: an arrangement the design never sanctioned, or a portal past a pair.
const states = new Set(), layouts = new Set();
(function walk(cells) {
  const key = shape(cells);
  if (states.has(key)) return;
  states.add(key);
  layouts.add(layout(cells));
  assert.ok(valid(cells), 'reachable but not a valid glass: ' + key);
  cells.forEach((_, i) => {
    gripsFor(cells, i).forEach(grip => {
      if (grip.action === 'split') return;   // splits only walk back the way we came
      walk(merge(cells, i, grip.action === 'merge-left' ? 'left' : 'right'));
    });
  });
})(four());

// The design's list, exactly: 1 2 3 4 / (1 2) 3 4 / 1 (2 3) 4 / 1 2 (3 4) / (1 2) (3 4).
// All five survive one grip per divider — that choice costs a survivor, never a layout.
assert.deepStrictEqual([...layouts].sort(), ['1,1,1,1', '1,1,2', '1,2,1', '2,1,1', '2,2'].sort());

// Six states, not five: only the centre divider is reachable from both sides, so (2 3) alone has
// two survivors. The other merges have one grip each and so one survivor each.
assert.deepStrictEqual([...states].sort(),
  ['1,1,1,1', '1,1,2l', '1,2l,1', '1,2r,1', '2r,1,1', '2r,2l'].sort());

// No triples: a merged portal is never a merge candidate, and merge() refuses it outright even if
// a caller asks — gripsFor is policy, merge is the guard.
const merged = merge(four(), 0, 'right');            // (1 2) 3 4
assert.deepStrictEqual(actions(merged, 0), ['split'], 'a merged portal offers only its way back');
assert.deepStrictEqual(actions(merged, 1), [], 'portal 3 faces left, into the pair — so it has nothing');
assert.deepStrictEqual(actions(merged, 2), ['merge-left'], 'portal 4 can still take portal 3');
assert.strictEqual(merge(merged, 1, 'left'), null, 'merging into a merged neighbour is refused');

// The centre merge strands its neighbours: each faces a pair, so both wait.
const mid = merge(four(), 1, 'right');               // 1 (2 3) 4
assert.deepStrictEqual(actions(mid, 0), [], 'portal 1 faces right, into the pair');
assert.deepStrictEqual(actions(mid, 2), [], 'portal 4 faces left, into the pair');
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

// Both halves merged the way the grips actually get there — 1 takes 2 reaching right, 4 takes 3
// reaching left — then unpicked one at a time. The far pair must not move.
let both = merge(merge(four(), 0, 'right'), 2, 'left');
assert.strictEqual(shape(both), '2r,2l');
assert.strictEqual(shape(split(both, 0).cells), '1,1,2l', 'splitting the left pair leaves the right one alone');
assert.strictEqual(shape(split(both, 1).cells), '2r,1,1', 'and vice versa');

// SLOTS is the glass's width; every arrangement spends exactly that.
assert.strictEqual(SLOTS, 4);

console.log('f35-glass.test.js: OK');
