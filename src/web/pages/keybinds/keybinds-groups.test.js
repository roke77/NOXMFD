// Self-check for the KEY page's grouping. Run: `node keybinds-groups.test.js`.
const assert = require('assert');
const { GROUPS, build, isBound, joyText, axisText, searchText } = require('./keybinds-groups.js');

const bind = (id, section, extra) => Object.assign({ id, section, label: id, description: '', key: '', joyButton: -1, joyNum: 0 }, extra);

// One representative bind per registry section (Keybinds.SectionTitle's display titles) plus the
// id-claimed ones, and where each must land: [group id, sub-section title].
const EXPECTED = [
  ['power-on', 'IMMERSION OPTIONS', 'systems', 'POWER · ENGINE · RADAR'],
  ['engine-off', 'IMMERSION OPTIONS', 'systems', 'POWER · ENGINE · RADAR'],
  ['radar-on', 'IMMERSION OPTIONS', 'systems', 'POWER · ENGINE · RADAR'],
  ['master-arms-off', 'IMMERSION OPTIONS', 'systems', 'MASTER ARM'],
  ['gear-up', 'GEAR', 'systems', 'GEAR'],
  ['combat-mode-aa', 'IMMERSION OPTIONS', 'combat', 'COMBAT MODE'],
  ['gun-trigger', 'WEAPONS', 'combat', 'WEAPONS'],
  ['flares', 'COUNTERMEASURES', 'combat', 'COUNTERMEASURES'],
  ['tgt-next', 'TGT', 'sensors', 'TGT'],
  ['tgp-point-track', 'TGP', 'sensors', 'TGP'],
  ['soi-next', 'SOI', 'displays', 'SOI'],
  ['cursor-up', 'CURSOR', 'displays', 'CURSOR'],
  ['map-follow', 'MAP', 'nav', 'MAP'],
  ['layout-save', 'LAYOUT', 'setup', 'LAYOUT'],
  ['layout-preset-1', 'LAYOUT PRESETS', 'setup', 'LAYOUT PRESETS'],
  ['hud-preset-1', 'HUD PRESETS', 'setup', 'HUD PRESETS'],
  ['tgt-preset-1', 'TGT PRESETS', 'setup', 'TGT PRESETS'],
  ['units-toggle', 'Units Keybinds', 'setup', 'MISC'],
  ['internal-mfd-poc-toggle', 'Internal MFD Keybinds', 'setup', 'MISC'],
];

const binds = EXPECTED.map(([id, section]) => bind(id, section));
const groups = build(binds, {});
for (const [id, , gid, title] of EXPECTED) {
  const g = groups.find(x => x.id === gid);
  const s = g && g.secs.find(x => x.rows.some(r => r.id === id));
  assert.ok(s && s.t === title, `${id} should land in ${gid} / ${title}, got ${s ? s.t : 'nowhere'}`);
}
assert.ok(!groups.some(g => g.id === 'other'), 'every representative bind is claimed, so no OTHER group');
assert.deepStrictEqual(groups.map(g => g.n), ['01', '02', '03', '04', '05', '06'], 'groups keep their flight order');
assert.strictEqual(GROUPS.length, 6);

// A bind no sub-section claims still shows up, under its own section, in a trailing OTHER group.
const withNew = build(binds.concat(bind('new-thing', 'Brand New Keybinds')), {});
const other = withNew[withNew.length - 1];
assert.strictEqual(other.id, 'other');
assert.deepStrictEqual(other.secs.map(s => [s.t, s.rows.map(r => r.id)]), [['Brand New Keybinds', ['new-thing']]]);

// A bind is only ever listed once, and empty sections/groups are dropped.
assert.strictEqual(groups.reduce((n, g) => n + g.all, 0), binds.length);
assert.ok(build([bind('flares', 'COUNTERMEASURES')], {}).every(g => g.secs.length), 'no empty sections');

// Notes: a sub-section's own title by default; COMBAT MODE borrows the IMMERSION OPTIONS note.
const noted = build(binds, { 'IMMERSION OPTIONS': 'tap or hold', TGT: 'regardless of focus' });
const note = (gid, title) => noted.find(g => g.id === gid).secs.find(s => s.t === title).note;
assert.strictEqual(note('combat', 'COMBAT MODE'), 'tap or hold');
assert.strictEqual(note('sensors', 'TGT'), 'regardless of focus');
assert.strictEqual(note('sensors', 'TGP'), '');

// Bound counts: a key, a joystick button or an axis each count; unbound rows (joyButton -1) don't.
assert.ok(!isBound(bind('a', 'GEAR')));
assert.ok(isBound(bind('a', 'GEAR', { key: 'G' })));
assert.ok(isBound(bind('a', 'GEAR', { joyButton: 0 })), 'button 0 is a real button');
assert.ok(isBound({ id: 'ax', axis: 3 }) && !isBound({ id: 'ax', axis: -1 }));
assert.ok(!isBound({ id: 'k', key: '' }), 'a key-only row with no key is unbound');
const counted = build([bind('gear-up', 'GEAR', { key: 'G' }), bind('gear-down', 'GEAR')], {});
assert.deepStrictEqual([counted[0].bound, counted[0].all, counted[0].secs[0].bound], [1, 2, 1]);

// Display text and search.
assert.strictEqual(joyText({ joyNum: 2, joyButton: 11 }), 'J2 B11');
assert.strictEqual(joyText({ joyNum: 0, joyButton: 5 }), 'JOY 5');
assert.strictEqual(axisText({ axisNum: 2, axis: 3 }), 'J2 A3');
assert.strictEqual(axisText({ axisNum: 0, axis: 3 }), 'AXIS 3');
const shown = k => k.toUpperCase();
const g = bind('gear-up', 'GEAR', { label: 'Gear Up', description: 'Raise the landing gear.', key: 'g', joyButton: 11, joyNum: 2 });
for (const q of ['gear up', 'landing', 'j2 b11', 'g']) assert.ok(searchText(g, shown).includes(q), `search text should contain ${q}`);
assert.ok(!searchText(bind('x', 'GEAR', { label: 'X' }), shown).includes('j2'), 'an unbound row has no binding text');

console.log('keybinds-groups.test.js: OK');
