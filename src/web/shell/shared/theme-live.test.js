// Self-check for theme-live.js: the imported override sheet is rewritten in place (also when the
// theme goes back to DEFAULT), nested iframes follow, and a document without the import gets a
// style element instead. Run: `node theme-live.test.js`.
const assert = require('assert');
const { apply } = require('./theme-live.js');

function sheet(rules) {
  return {
    cssRules: rules,
    deleteRule(i) { rules.splice(i, 1); },
    insertRule(css, i) { if (/\}\s*\S/.test(css)) throw new Error('one rule only'); rules.splice(i, 0, { cssText: css }); },
  };
}
function doc(sheets, frames) {
  const head = { children: [], appendChild(el) { this.children.push(el); } };
  return {
    styleSheets: sheets, head,
    getElementById(id) { return head.children.find(el => el.id === id) || null; },
    createElement() { return { id: '', textContent: '' }; },
    querySelectorAll() { return (frames || []).map(w => ({ contentWindow: w })); },
  };
}
const win = d => ({ document: d });

// theme.css → @import colors.css, @import /colors-override.css (holding a stale theme)
const override = sheet([{ cssText: ':root{--no-green-rgb:1, 2, 3;}' }]);
const themeCss = sheet([{ href: 'colors.css', styleSheet: sheet([]) }, { href: '/colors-override.css', styleSheet: override }]);
const childDoc = doc([sheet([{ href: '/assets/shared/theme.css', styleSheet: sheet([{ href: '/colors-override.css', styleSheet: sheet([]) }]) }])]);
const bareDoc = doc([]);   // an extension page without theme.css
const shell = win(doc([sheet([{ href: '/assets/shared/theme.css', styleSheet: themeCss }])], [win(childDoc), win(bareDoc)]));

apply(shell, ':root{--no-red-rgb:9, 9, 9;}');
assert.deepStrictEqual(override.cssRules.map(r => r.cssText), [':root{--no-red-rgb:9, 9, 9;}'], 'shell override rewritten in place');
const childOverride = childDoc.styleSheets[0].cssRules[0].styleSheet.cssRules[0].styleSheet;
assert.strictEqual(childOverride.cssRules.length, 1, 'nested iframe follows');
assert.strictEqual(bareDoc.getElementById('no-theme-live').textContent, ':root{--no-red-rgb:9, 9, 9;}', 'no import: style element');

apply(shell, '');
assert.strictEqual(override.cssRules.length, 0, 'DEFAULT clears the stale imported theme');
assert.strictEqual(bareDoc.getElementById('no-theme-live').textContent, '', 'DEFAULT clears the style element too');

const throwing = { get document() { throw new Error('cross-origin'); } };
apply(win(doc([], [throwing])), ':root{}');   // a cross-origin frame is skipped, not fatal

console.log('theme-live.test.js: OK');
