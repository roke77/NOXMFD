// Self-check for CLASSIC's layout pin. Run: `node layout-pin.test.js`.
const assert = require('assert');
const { sameArrangement, pinnable, pinFromState } = require('./layout-pin.js');

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

console.log('layout-pin.test.js OK');
