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
const remote = require('./remote-keybinds.js');

remote.applyConfig({ remoteKeybindsSamePc: true, binds: [
  { id: 'cycle-guns', key: 'G' },
  { id: 'cursor-up', key: 'ArrowUp' },
] });
assert.strictEqual(remote.state().samePc, true, 'pushed config should update same-PC state');
assert.strictEqual(remote.state().remoteCapableCount, 2, 'pushed config should rebuild remote maps');

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
assert.deepStrictEqual(commandForBind('power-on'), { cmd: 'power.set', args: { on: true } });
assert.deepStrictEqual(commandForBind('power-off'), { cmd: 'power.set', args: { on: false } });
assert.deepStrictEqual(commandForBind('cursor-deselect'), {
  cmd: 'map.action',
  args: { wname: 'cursor-deselect' }
});
assert.deepStrictEqual(commandForBind('td-assign-7'), { cmd: 'td.assign', args: { index: 7, on: false } });
assert.strictEqual(commandForBind('cursor-zoom-in'), null, 'held zoom state uses fire.set, not one-shot mapping');
assert.deepStrictEqual(commandForBind('tgp-manual-toggle'), { cmd: 'tgp.manual-toggle' });
assert.deepStrictEqual(commandForBind('tgp-manual-reset'), { cmd: 'tgp.manual-reset' });
assert.deepStrictEqual(commandForBind('tgp-point-track'), { cmd: 'tgp.point-track' });
assert.deepStrictEqual(commandForBind('tgp-manual-snap-headtracker'), { cmd: 'tgp.snap-headtracker' });
assert.deepStrictEqual(commandForBind('tgp-manual-ir-toggle'), { cmd: 'tgp.ir-toggle' });
assert.deepStrictEqual(commandForBind('tgp-mark-steerpoint'), { cmd: 'tgp.mark-steerpoint' });
assert.deepStrictEqual(commandForBind('tgp-fullscreen-toggle'), { cmd: 'tgp.fullscreen-toggle' });
assert.deepStrictEqual(commandForBind('tgp-fullscreen-hud-toggle'), { cmd: 'tgp.fullscreen-hud-toggle' });
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
assert.strictEqual(fireRoleForBind('weapon-release-single'), 'release-single');
assert.strictEqual(fireRoleForBind('jammer-pod'), 'jammer-pod');
assert.strictEqual(fireRoleForBind('cursor-zoom-in'), 'zoom-in');
assert.strictEqual(fireRoleForBind('cursor-zoom-out'), 'zoom-out');
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
  { id: 'weapon-release-single', key: 'Shift+Enter' },
  { id: 'jammer-pod', key: 'J' },
  { id: 'jammer', key: 'K' },
]);
assert.strictEqual(fireMap.Space, 'gun');
assert.strictEqual(fireMap.Enter, 'release');
assert.strictEqual(fireMap['Shift+Enter'], 'release-single');
assert.strictEqual(fireMap.J, 'jammer-pod');
assert.strictEqual(fireMap.K, undefined);

assert.deepStrictEqual(fireGroupsFromActive({ gun: true, 'jammer-pod': true }), {
  gun: true,
  release: false,
  'release-single': false,
  'jammer-pod': true,
  'zoom-in': false,
  'zoom-out': false
});

console.log('remote-keybinds.test.js: OK');
