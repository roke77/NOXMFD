// Self-check for the LYT page's saved-layout rows. Run: `node lyt-slots.test.js`.
const assert = require('assert');

{
  const { describeLayouts, SPLITS, SLOT_COUNT } = require('./lyt-slots.js');
  const item = (id, data) => ({ id, name: 'L' + id, data: typeof data === 'string' ? data : JSON.stringify(data) });

  const rows = describeLayouts([
    item(1, { splitMode: true, splitVariant: 'h', pages: ['map', 'tgt'] }),
    item(2, { splitMode: true, splitVariant: 'vwr', pages: ['hsd', 'wpn'] }),
    item(3, { splitMode: false, pages: ['rwr'] }),
    item(4, { splitMode: true, splitVariant: 'bogus', pages: ['avn'] }),
    item(5, 'not json'),
    item(6, { splitMode: false, pages: ['map'] }),
  ]);

  assert.deepStrictEqual([rows[0].label, rows[0].pages], ['H_SPLIT', ['MAP', 'TGT']]);
  assert.deepStrictEqual([rows[1].label, rows[1].pages], ['WIDE R', ['HSD', 'WPN']]);
  assert.deepStrictEqual([rows[2].label, rows[2].pages], ['F_VIEW', ['RWR']]);
  // unknown variant → top/bottom split, a missing second page → MAIN (what the shell itself does)
  // ...and the avn page shows under its visible name, ENG
  assert.deepStrictEqual([rows[3].label, rows[3].pages], ['H_SPLIT', ['ENG', 'MAIN']]);
  // unreadable data keeps the row, with nothing to draw
  assert.deepStrictEqual([rows[4].label, rows[4].rects.length, rows[4].pages.length], ['UNREADABLE', 0, 0]);

  // position is the keybind slot; only the first SLOT_COUNT have one
  assert.deepStrictEqual(rows.map(r => r.slot), [1, 2, 3, 4, 5, null]);
  assert.strictEqual(SLOT_COUNT, 5);

  // list position is the row's number; SOI membership defaults to every pane, a full view's one included
  assert.deepStrictEqual(rows.map(r => r.pos), [1, 2, 3, 4, 5, 6]);
  assert.deepStrictEqual([rows[2].soi, rows[2].sides], [[true], ['FULL VIEW']]);
  assert.strictEqual(rows[4].soi, null);   // unreadable: nothing to choose
  assert.deepStrictEqual(rows[0].soi, [true, true]);
  assert.deepStrictEqual(rows[0].sides, ['TOP PANE', 'BOTTOM PANE']);
  assert.deepStrictEqual(rows[1].sides, ['LEFT PANE', 'RIGHT PANE']);
  const left = describeLayouts([item(9, { splitMode: true, splitVariant: 'v', pages: ['map', 'tgt'], soi: [true, false] })])[0];
  assert.deepStrictEqual(left.soi, [true, false]);
  assert.strictEqual(left.state.soi[1], false);   // the parsed arrangement rides along for the edit dialog
  assert.strictEqual(rows[4].state, null);

  // every split's panes tile the screen exactly
  for (const [k, s] of Object.entries(SPLITS)) {
    const area = s.rects.reduce((a, r) => a + r[2] * r[3], 0);
    assert.ok(Math.abs(area - 1) < 1e-9, `${k} panes cover ${area}, not the whole screen`);
  }

  // F-35: one full-height column per portal, as wide as its span; bad cells → four single portals
  const [glass, bad] = describeLayouts([
    item(1, { cells: [{ span: 2, ate: 'right' }, { span: 1 }, { span: 1 }], pages: ['map', 'avn'], soi: [true, false, true] }),
    item(2, { cells: [{ span: 3 }, { span: 1 }], pages: ['tgp'] }),
  ]);
  assert.deepStrictEqual([glass.label, glass.pages, glass.sides[2]], ['3 PORTALS', ['MAP', 'ENG', 'MAIN'], 'PORTAL 3']);
  assert.deepStrictEqual(glass.rects, [[0, 0, 0.5, 1], [0.5, 0, 0.25, 1], [0.75, 0, 0.25, 1]]);
  assert.deepStrictEqual(glass.soi, [true, false, true]);
  assert.deepStrictEqual([bad.label, bad.rects.length, bad.pages], ['4 PORTALS', 4, ['TGP', 'MAIN', 'MAIN', 'MAIN']]);

  console.log('lyt-slots.test.js: OK');
}
