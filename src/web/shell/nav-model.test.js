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
// `mark` is the one exception: it says "this is the current selection" (e.g. NAV.bdf/NAV.pal
// flagging whichever of the two is the live page) — a WHAT, like label/action, not a WHERE; every
// layout renderer honors it the same way BEZEL_EXTRAS.lyt's mark already does for CLASSIC/F-35.
const PLACEMENT_KEYS = ['key', 'side', 'slot', 'bank', 'index', 'pane', 'paneOffset'];
const ALLOWED_KEYS = new Set(['action', 'label', 'mark']);
for (const [page, items] of Object.entries(NAV)) {
  assert.ok(Array.isArray(items), `NAV.${page} must be an ordered array`);
  items.forEach((item, i) => {
    const where = `NAV.${page}[${i}]`;
    for (const k of Object.keys(item)) {
      assert.ok(ALLOWED_KEYS.has(k), `${where} has unexpected key "${k}" — got ${JSON.stringify(Object.keys(item))}`);
    }
    for (const k of PLACEMENT_KEYS) {
      assert.ok(!(k in item), `${where} carries layout placement "${k}" — NAV must stay layout-independent`);
    }
    assert.ok(typeof item.label === 'string' && item.label.length, `${where}.label must be a non-empty string`);
    assert.ok(typeof item.action === 'string' && item.action.length, `${where}.action must be a non-empty string`);
    if ('mark' in item) assert.strictEqual(typeof item.mark, 'boolean', `${where}.mark must be boolean when present`);
  });
}

// ── Ordering is the contract ────────────────────────────────────────────────────────
// A layout renderer places by INDEX (bezel full view: item i → left key i; bezel split:
// SPLIT_SLOTS[i] places NAV[i]). So order is meaningful and reordering is a behaviour change.
assert.deepStrictEqual(NAV.main.map(i => i.label), ['AVN', 'MAP', 'RWR', 'TGP', 'TGT', 'WPN']);
assert.deepStrictEqual(NAV.map.map(i => i.label), ['MAIN', 'GRID', 'FLW', 'WPT', 'R+', 'R-', 'Z+', 'Z-']);
assert.deepStrictEqual(NAV.rdr.map(i => i.label), ['MAIN', 'R+', 'R-']);

// ── Every frame-hosted page can get back to MAIN ────────────────────────────────────
for (const page of ['avn', 'afm', 'rwr', 'tgp', 'tgt', 'hud']) {
  assert.deepStrictEqual(NAV[page], [{ label: 'MAIN', action: 'main' }], `${page} should be just a MAIN back-button`);
}

// CFG group (cfg-rates experiment, issue #39): KEY/LYT/RTS folded together, same shape as
// BDF/PAL/MIS/OBJ below (reached from MAIN via CFG — mfd.js BEZEL_EXTRAS.main, action still
// 'keys'). LYT has no `mark` slot of its own — see nav-model.js's comment on why NAV.lyt doesn't
// exist (BEZEL_EXTRAS.lyt places CLASSIC/F-35 at fixed keys that would silently clobber it).
assert.deepStrictEqual(NAV.keys, [
  { label: 'MAIN', action: 'main' },
  { label: 'KEY',  action: 'keys', mark: true },
  { label: 'LYT',  action: 'lyt'  },
  { label: 'RTS',  action: 'rates' },
]);
assert.deepStrictEqual(NAV.rates, [
  { label: 'MAIN', action: 'main' },
  { label: 'KEY',  action: 'keys' },
  { label: 'LYT',  action: 'lyt'  },
  { label: 'RTS',  action: 'rates', mark: true },
]);
assert.ok(!('lyt' in NAV), 'NAV.lyt must not exist — BEZEL_EXTRAS.lyt owns that page\'s placement');

// WPT (waypoints/route creator, issue #38) is reached from MAP's own nav row, not MDT/CFG's
// sibling-group pattern — its way back is MAP, not MAIN.
assert.deepStrictEqual(NAV.wpt, [ { label: 'MAP', action: 'map' } ]);

// BDF/PAL/MIS/OBJ are folded together (reached from MAIN via MDT — mfd.js BEZEL_EXTRAS.main,
// action still 'bdf'): each gets MAIN plus a direct switch to the other three, with `mark` on
// whichever is live.
assert.deepStrictEqual(NAV.bdf, [
  { label: 'MAIN', action: 'main' },
  { label: 'BDF',  action: 'bdf', mark: true },
  { label: 'PAL',  action: 'pal' },
  { label: 'MIS',  action: 'mis' },
  { label: 'OBJ',  action: 'obj' },
]);
assert.deepStrictEqual(NAV.pal, [
  { label: 'MAIN', action: 'main' },
  { label: 'BDF',  action: 'bdf' },
  { label: 'PAL',  action: 'pal', mark: true },
  { label: 'MIS',  action: 'mis' },
  { label: 'OBJ',  action: 'obj' },
]);
assert.deepStrictEqual(NAV.mis, [
  { label: 'MAIN', action: 'main' },
  { label: 'BDF',  action: 'bdf' },
  { label: 'PAL',  action: 'pal' },
  { label: 'MIS',  action: 'mis', mark: true },
  { label: 'OBJ',  action: 'obj' },
]);
assert.deepStrictEqual(NAV.obj, [
  { label: 'MAIN', action: 'main' },
  { label: 'BDF',  action: 'bdf' },
  { label: 'PAL',  action: 'pal' },
  { label: 'MIS',  action: 'mis' },
  { label: 'OBJ',  action: 'obj', mark: true },
]);

// WPN contributes no navigation of its own: its MAIN/PREV/NEXT are pagination, i.e. shell state
// owned by the bezel renderer. Empty (not absent) so the renderer can iterate it uniformly.
assert.deepStrictEqual(NAV.wpn, [], 'NAV.wpn must be empty — its labels are shell pagination state');

// No duplicate labels within a page (two keys with the same label would be unpressable-by-name).
for (const [page, items] of Object.entries(NAV)) {
  const labels = items.map(i => i.label);
  assert.strictEqual(new Set(labels).size, labels.length, `NAV.${page} has duplicate labels: ${labels}`);
}

console.log('nav-model.test.js: OK');
