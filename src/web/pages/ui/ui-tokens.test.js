// Self-check that the UI page's token table matches what the plugin accepts (ThemeColors.Tokens)
// and that every token exists in colors.css. Run: `node ui-tokens.test.js`.
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

const colors = fs.readFileSync(path.join(root, 'src', 'web', 'shared', 'colors.css'), 'utf8');
for (const t of pageTokens) assert.ok(new RegExp('^\\s*' + t + '\\s*:', 'm').test(colors), `${t} is not defined in colors.css`);

console.log(`ui-tokens.test.js: OK (${pageTokens.length} tokens match ThemeColors.Tokens)`);
