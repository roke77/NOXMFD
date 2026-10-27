// Self-check for the layout pin (both shells). Run: `node layout-pin.test.js`.
const assert = require('assert');
const { sameArrangement, pinnable, pinFromState, swapStep } = require('./layout-pin.js');

const full = (p) => ({ splitMode: false, pages: [p] });
const split = (v, a, b) => ({ splitMode: true, splitVariant: v, pages: [a, b] });

// Equality covers the split, its variant and every pane's page, in order.
assert.ok(sameArrangement(full('rwr'), full('rwr')));
assert.ok(!sameArrangement(full('rwr'), full('map')));
assert.ok(sameArrangement(split('v', 'map', 'rwr'), split('v', 'map', 'rwr')));
assert.ok(!sameArrangement(split('v', 'map', 'rwr'), split('vw', 'map', 'rwr')));
assert.ok(!sameArrangement(split('v', 'map', 'rwr'), split('v', 'rwr', 'map')));
assert.ok(!sameArrangement(full('map'), split('h', 'map', 'main')));
// A stray splitVariant left on a full view doesn't break equality.
assert.ok(sameArrangement(full('rwr'), Object.assign(full('rwr'), { splitVariant: 'h' })));

// Only a full-view MENU is unpinnable; MENU in a pane is a real arrangement.
assert.ok(!pinnable(full('main')));
assert.ok(pinnable(full('rwr')));
assert.ok(pinnable(split('h', 'main', 'main')));

// Saved-layout blobs: the current shape, the single-page shape, and malformed ones.
assert.deepStrictEqual(pinFromState({ pinnedLayout: split('vwr', 'map', 'tgp') }), split('vwr', 'map', 'tgp'));
assert.deepStrictEqual(pinFromState({ pinnedPage: 'rwr' }), full('rwr'));
assert.deepStrictEqual(pinFromState({ pinnedLayout: split('bogus', 'map', 'tgp') }), split('h', 'map', 'tgp'));
assert.strictEqual(pinFromState({ pinnedPage: 'main' }), null);
assert.strictEqual(pinFromState({ pinnedLayout: null, pinnedPage: null }), null);
assert.strictEqual(pinFromState({ pinnedLayout: { splitMode: true, pages: [] } }), null);
assert.strictEqual(pinFromState(null), null);

// F-35: the cells (span and eaten side) and every portal's page, in order.
const glass = (cells, pages) => ({ cells, pages });
const four = [{ span: 1 }, { span: 1 }, { span: 1 }, { span: 1 }];
const left = [{ span: 2, ate: 'right' }, { span: 1 }, { span: 1 }];
const right = [{ span: 2, ate: 'left' }, { span: 1 }, { span: 1 }];
assert.ok(sameArrangement(glass(left, ['map', 'rwr', 'tgp']), glass(left, ['map', 'rwr', 'tgp'])));
assert.ok(!sameArrangement(glass(left, ['map', 'rwr', 'tgp']), glass(right, ['map', 'rwr', 'tgp'])));
assert.ok(!sameArrangement(glass(left, ['map', 'rwr', 'tgp']), glass(left, ['map', 'tgp', 'rwr'])));
assert.ok(!sameArrangement(glass(four, ['rwr', 'main', 'main', 'main']), full('rwr')));
assert.ok(!pinnable(glass(four, ['main', 'main', 'main', 'main'])));
assert.ok(pinnable(glass(four, ['main', 'map', 'main', 'main'])));
assert.deepStrictEqual(pinFromState({ pinnedLayout: glass(left, ['map', 'rwr', 'tgp', 'extra']) }), glass(left, ['map', 'rwr', 'tgp']));
assert.strictEqual(pinFromState({ pinnedLayout: glass(four, ['main']) }), null);

// SWAP: to the pin and back, alternating; nothing to do without a pin or a way back.
const rwr = full('rwr'), map = full('map');
assert.strictEqual(swapStep(map, null, null), null);
assert.strictEqual(swapStep(rwr, rwr, null), null);
assert.deepStrictEqual(swapStep(map, rwr, null), { to: rwr, partner: map });
assert.deepStrictEqual(swapStep(rwr, rwr, map), { to: map, partner: map });
assert.deepStrictEqual(swapStep(glass(left, ['map', 'rwr', 'tgp']), glass(right, ['map', 'rwr', 'tgp']), null),
  { to: glass(right, ['map', 'rwr', 'tgp']), partner: glass(left, ['map', 'rwr', 'tgp']) });

console.log('layout-pin.test.js OK');
