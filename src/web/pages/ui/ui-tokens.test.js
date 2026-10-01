// Self-check that the UI page's token table and limits match what the plugin accepts (ThemeColors,
// ThemeStore) and that every token exists in colors.css. Run: `node ui-tokens.test.js`.
const assert = require('assert');
const fs = require('fs');
const path = require('path');
const { GROUPS } = require('./ui-tokens.js');

const root = path.join(__dirname, '..', '..', '..', '..');
const cs = fs.readFileSync(path.join(root, 'src', 'plugin', 'Stores', 'ThemeColors.cs'), 'utf8');
const block = /Tokens\s*=\s*\{([\s\S]*?)\};/.exec(cs);
assert.ok(block, 'ThemeColors.Tokens not found — the regex probably broke');
const pluginTokens = [...block[1].matchAll(/"(--no-[\w-]+)"/g)].map(m => m[1]);

const pageTokens = GROUPS.flatMap(g => g.tokens.map(t => t[0]));
assert.deepStrictEqual(pageTokens, pluginTokens, 'ui-tokens.js and ThemeColors.Tokens must list the same tokens in the same order');

// Theme files key each colour by its row label as a slug (ThemeColors.FileKeys), so a renamed label
// must rename its file key too.
const keysBlock = /FileKeys\s*=\s*\{([\s\S]*?)\};/.exec(cs);
assert.ok(keysBlock, 'ThemeColors.FileKeys not found — the regex probably broke');
const fileKeys = [...keysBlock[1].matchAll(/"([a-z0-9-]+)"/g)].map(m => m[1]);
const slugs = GROUPS.flatMap(g => g.tokens.map(t => t[1].toLowerCase().replace(/[^a-z0-9]+/g, '-').replace(/^-|-$/g, '')));
assert.deepStrictEqual(fileKeys, slugs, 'ThemeColors.FileKeys must be the row labels as slugs, in the same order');

const colors = fs.readFileSync(path.join(root, 'src', 'web', 'shared', 'colors.css'), 'utf8');
for (const t of pageTokens) assert.ok(new RegExp('^\\s*' + t + '\\s*:', 'm').test(colors), `${t} is not defined in colors.css`);

// ui.js mirrors three plugin constants for its own checks; they must agree with the C# ones.
const ui = fs.readFileSync(path.join(__dirname, 'ui.js'), 'utf8');
const store = fs.readFileSync(path.join(root, 'src', 'plugin', 'Stores', 'ThemeStore.cs'), 'utf8');
const pick = (src, re, what) => { const m = re.exec(src); assert.ok(m, `${what} not found`); return m[1]; };
assert.strictEqual(pick(ui, /var MAX_THEMES = (\d+);/, 'ui.js MAX_THEMES'), pick(store, /const int MaxThemes = (\d+);/, 'ThemeStore.MaxThemes'), 'MAX_THEMES must match ThemeStore.MaxThemes');
assert.strictEqual(pick(ui, /var MAX_NAME = (\d+);/, 'ui.js MAX_NAME'), pick(cs, /const int MaxNameLength = (\d+);/, 'ThemeColors.MaxNameLength'), 'MAX_NAME must match ThemeColors.MaxNameLength');
assert.strictEqual(pick(ui, /var CODE_PREFIX = '([^']+)';/, 'ui.js CODE_PREFIX'), pick(cs, /const string CodePrefix = "([^"]+)";/, 'ThemeColors.CodePrefix'), 'CODE_PREFIX must match ThemeColors.CodePrefix');

console.log(`ui-tokens.test.js: OK (${pageTokens.length} tokens match ThemeColors.Tokens)`);
