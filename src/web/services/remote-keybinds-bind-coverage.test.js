// Self-check that every keybind registered in the plugin (Keybinds.cs) has a remote path in
// remote-keybinds.js — a case in commandForBind, cursorRoleForBind, or fireRoleForBind. This is
// the rule docs/remote-keybinds.md establishes: every existing and future KEY-page bind must work
// with Listen for Keybinds (REMOTE) on, with two documented exceptions that have no server-side
// action to relay in the first place (see NO_REMOTE_PATH below). Run with the other *.test.js files.
const assert = require('assert');
const fs = require('fs');
const path = require('path');

const keybindsSrc = fs.readFileSync(
  path.join(__dirname, '..', '..', 'plugin', 'Input', 'Keybinds.cs'), 'utf8');
const remoteSrc = fs.readFileSync(path.join(__dirname, 'remote-keybinds.js'), 'utf8');

// Every Def/DefFree/DefKeyOnly(config, "id", ...) literal. Deliberately excludes AddAxis rows
// (Cursor Horizontal/Vertical, Cursor Zoom Axis) — a different registration function entirely,
// for the axis-only binds NO_REMOTE_PATH's comment below covers.
const ids = new Set();
for (const m of keybindsSrc.matchAll(/\b(?:Def|DefFree|DefKeyOnly)\(\s*config,\s*"([a-z0-9-]+)"\s*,/g))
  ids.add(m[1]);
assert.ok(ids.size > 30, `found too few Keybinds.cs bind ids (${ids.size}) — regex probably broke`);

// td-assign-N and hud-preset-N are registered in a loop ("td-assign-" + slot), so the regex above
// can't see the individual ids the way it does every other bind — list them explicitly instead.
// Update these two loops if either range in Keybinds.cs ever changes: the squad-slot loop
// (`for (int slot = 1; slot <= 9; slot++)`) or HudPresetStore.SlotCount.
for (let s = 1; s <= 9; s++) ids.add('td-assign-' + s);
for (let p = 1; p <= 5; p++) ids.add('hud-preset-' + p);

// docs/remote-keybinds.md's "Binds that stay unrelayed, on purpose": SAVE/LOAD LAYOUT pop a
// client-side modal in whatever browser is looking at the KEY page — there's no server-side action
// to relay, and triggering a text-entry modal on a different browser than the one being typed into
// wouldn't make sense as "remote" in the first place. The axis-only binds need no entry here at
// all — AddAxis never produces a `key` field for a keydown to match, so they're already excluded
// by construction, not by this whitelist.
const NO_REMOTE_PATH = new Set(['layout-save', 'layout-load']);

const missing = [...ids].filter(id => !NO_REMOTE_PATH.has(id) && !remoteSrc.includes(`case '${id}':`));

assert.deepStrictEqual(missing.sort(), [],
  'these Keybinds.cs binds have no case in commandForBind/cursorRoleForBind/fireRoleForBind — a ' +
  'remote press of their configured key silently does nothing (docs/remote-keybinds.md)');

console.log(`remote-keybinds-bind-coverage.test.js: OK (${ids.size} binds all have a remote path, ` +
  `${NO_REMOTE_PATH.size} documented exceptions)`);
