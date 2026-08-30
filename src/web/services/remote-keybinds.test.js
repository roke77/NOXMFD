// Self-check for remote-keybinds' pure bind-id mapping. Run: `node remote-keybinds.test.js`.
const assert = require('assert');
const {
  commandForBind,
  cursorRoleForBind,
  fireRoleForBind,
  buildKeyMap,
  buildCursorKeyMap,
  buildFireKeyMap,
  cursorStateFromActive,
  fireGroupsFromActive
} = require('./remote-keybinds.js');

assert.deepStrictEqual(commandForBind('cycle-guns'), {
  cmd: 'weapon.cycle',
  args: { group: 'guns' }
});
assert.deepStrictEqual(commandForBind('gear-down'), {
  cmd: 'gear.set',
  args: { group: 'down' }
});
assert.deepStrictEqual(commandForBind('radar-on'), {
  cmd: 'avn.set',
  args: { group: 'radar', on: true }
});
assert.deepStrictEqual(commandForBind('map-route-next'), {
  cmd: 'map.action',
  args: { wname: 'route-next' }
});
assert.deepStrictEqual(commandForBind('soi-select'), {
  cmd: 'soi.action',
  args: { wname: 'select' }
});
assert.deepStrictEqual(commandForBind('hud-preset-5'), {
  cmd: 'preset.load',
  args: { index: 5 }
});
assert.strictEqual(commandForBind('gun-trigger'), null, 'held fire state uses fire.set, not one-shot mapping');
assert.strictEqual(commandForBind('cursor-up'), null, 'held cursor state uses cursor.set, not one-shot mapping');
assert.strictEqual(commandForBind('layout-save'), null, 'layout modal shortcuts stay browser-local');
assert.strictEqual(commandForBind('unknown'), null);

const map = buildKeyMap([
  { id: 'cycle-guns', key: 'G' },
  { id: 'gun-trigger', key: 'Space' },
  { id: 'gear-up', key: '' },
  { id: 'flares', key: 'F' },
]);

assert.strictEqual(map.G.id, 'cycle-guns');
assert.strictEqual(map.F.id, 'flares');
assert.strictEqual(map.F.spec.repeat, true);
assert.strictEqual(map.Space, undefined);
assert.strictEqual(map.None, undefined);

assert.strictEqual(cursorRoleForBind('cursor-up'), 'up');
assert.strictEqual(cursorRoleForBind('cursor-select'), 'select');
assert.strictEqual(cursorRoleForBind('cycle-guns'), null);
assert.strictEqual(fireRoleForBind('gun-trigger'), 'gun');
assert.strictEqual(fireRoleForBind('weapon-release'), 'release');
assert.strictEqual(fireRoleForBind('jammer-pod'), 'jammer-pod');
assert.strictEqual(fireRoleForBind('flares'), null);

const cursorMap = buildCursorKeyMap([
  { id: 'cursor-up', key: 'ArrowUp' },
  { id: 'cursor-right', key: 'ArrowRight' },
  { id: 'cursor-select', key: 'Enter' },
  { id: 'cycle-guns', key: 'G' },
]);
assert.strictEqual(cursorMap.ArrowUp, 'up');
assert.strictEqual(cursorMap.ArrowRight, 'right');
assert.strictEqual(cursorMap.Enter, 'select');
assert.strictEqual(cursorMap.G, undefined);

assert.deepStrictEqual(cursorStateFromActive({ up: true, right: true }), { x: 1, y: -1, on: false });
assert.deepStrictEqual(cursorStateFromActive({ left: true, right: true, select: true }), { x: 0, y: 0, on: true });

const fireMap = buildFireKeyMap([
  { id: 'gun-trigger', key: 'Space' },
  { id: 'weapon-release', key: 'Enter' },
  { id: 'jammer-pod', key: 'J' },
  { id: 'jammer', key: 'K' },
]);
assert.strictEqual(fireMap.Space, 'gun');
assert.strictEqual(fireMap.Enter, 'release');
assert.strictEqual(fireMap.J, 'jammer-pod');
assert.strictEqual(fireMap.K, undefined);

assert.deepStrictEqual(fireGroupsFromActive({ gun: true, 'jammer-pod': true }), {
  gun: true,
  release: false,
  'jammer-pod': true
});

console.log('remote-keybinds.test.js: OK');
