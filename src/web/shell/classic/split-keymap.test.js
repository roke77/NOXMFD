// Self-check for split-mode bezel key mapping. Run: `node split-keymap.test.js`.
const assert = require('assert');
const { paneKey } = require('./split-keymap.js');

// ── h (top/bottom): each pane keeps both columns; pane 1 offset by +3 ──────────
// Pane 0 (top): pane-local slot == physical index, same-side column.
assert.deepStrictEqual(paneKey('h', 0, 'left',  0), { bank: 'left',  index: 0 });
assert.deepStrictEqual(paneKey('h', 0, 'right', 2), { bank: 'right', index: 2 });
// Pane 1 (bottom): +3 on the same-side column.
assert.deepStrictEqual(paneKey('h', 1, 'left',  0), { bank: 'left',  index: 3 });
assert.deepStrictEqual(paneKey('h', 1, 'right', 2), { bank: 'right', index: 5 });

// ── v / vw (left/right): each pane owns its adjacent column ─────────────────────
// Pane 0 = LEFT pane → left column; page's left-side slots on 0..2, right-side on 3..5.
assert.deepStrictEqual(paneKey('v', 0, 'left',  0), { bank: 'left', index: 0 });
assert.deepStrictEqual(paneKey('v', 0, 'left',  2), { bank: 'left', index: 2 });
assert.deepStrictEqual(paneKey('v', 0, 'right', 0), { bank: 'left', index: 3 });
assert.deepStrictEqual(paneKey('v', 0, 'right', 2), { bank: 'left', index: 5 });
// Pane 1 = RIGHT pane → right column, same slot arithmetic.
assert.deepStrictEqual(paneKey('v', 1, 'left',  0), { bank: 'right', index: 0 });
assert.deepStrictEqual(paneKey('v', 1, 'right', 1), { bank: 'right', index: 4 });
// vw shares the v mapping (only the pane widths differ, not the keys).
assert.deepStrictEqual(paneKey('vw', 1, 'right', 2), { bank: 'right', index: 5 });
assert.deepStrictEqual(paneKey('vw', 0, 'left',  1), { bank: 'left',  index: 1 });

// The left/right panes must never share a physical key (each owns a distinct column).
for (const v of ['v', 'vw']) {
  for (const side of ['left', 'right']) {
    for (let slot = 0; slot <= 2; slot++) {
      assert.notStrictEqual(paneKey(v, 0, side, slot).bank, paneKey(v, 1, side, slot).bank);
    }
  }
}

console.log('split-keymap: all checks passed');
