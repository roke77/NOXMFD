// Self-check for the navigation model. Run: `node nav-model.test.js`.
//
// The point of this file is to guard the ONE property that makes NAV worth having: it is
// layout-independent (docs/layouts.md, "The seam"). The bezel is the only layout today, so it
// would be easy — and invisible — to slide a bezel-ism like `key: 0` back into NAV and re-couple
// the model to this shell. That regression is what these asserts catch.
const assert = require('assert');
const { NAV } = require('./nav-model.js');

// ── The invariant: an item describes WHAT, never WHERE ──────────────────────────────
// No key/side/slot/bank/index — placement belongs to the layout renderer (mfd.js fullViewSlot /
// SPLIT_SLOTS), not here. If this fails, the seam has leaked and a second layout can't reuse NAV.
const PLACEMENT_KEYS = ['key', 'side', 'slot', 'bank', 'index', 'pane', 'paneOffset'];
for (const [page, items] of Object.entries(NAV)) {
  assert.ok(Array.isArray(items), `NAV.${page} must be an ordered array`);
  items.forEach((item, i) => {
    const where = `NAV.${page}[${i}]`;
    assert.deepStrictEqual(Object.keys(item).sort(), ['action', 'label'],
      `${where} must have exactly { label, action } — got ${JSON.stringify(Object.keys(item))}`);
    for (const k of PLACEMENT_KEYS) {
      assert.ok(!(k in item), `${where} carries layout placement "${k}" — NAV must stay layout-independent`);
    }
    assert.ok(typeof item.label === 'string' && item.label.length, `${where}.label must be a non-empty string`);
    assert.ok(typeof item.action === 'string' && item.action.length, `${where}.action must be a non-empty string`);
  });
}

// ── Ordering is the contract ────────────────────────────────────────────────────────
// A layout renderer places by INDEX (bezel full view: item i → left key i; bezel split:
// SPLIT_SLOTS[i] places NAV[i]). So order is meaningful and reordering is a behaviour change.
assert.deepStrictEqual(NAV.main.map(i => i.label), ['AVN', 'MAP', 'RWR', 'TGP', 'TGT', 'WPN']);
assert.deepStrictEqual(NAV.map.map(i => i.label), ['MAIN', 'FLW', 'Z+', 'Z-']);

// ── Every frame-hosted page can get back to MAIN ────────────────────────────────────
for (const page of ['avn', 'rwr', 'tgp', 'tgt']) {
  assert.deepStrictEqual(NAV[page], [{ label: 'MAIN', action: 'main' }], `${page} should be just a MAIN back-button`);
}

// WPN contributes no navigation of its own: its MAIN/PREV/NEXT are pagination, i.e. shell state
// owned by the bezel renderer. Empty (not absent) so the renderer can iterate it uniformly.
assert.deepStrictEqual(NAV.wpn, [], 'NAV.wpn must be empty — its labels are shell pagination state');

// No duplicate labels within a page (two keys with the same label would be unpressable-by-name).
for (const [page, items] of Object.entries(NAV)) {
  const labels = items.map(i => i.label);
  assert.strictEqual(new Set(labels).size, labels.length, `NAV.${page} has duplicate labels: ${labels}`);
}

console.log('nav-model.test.js: OK');
