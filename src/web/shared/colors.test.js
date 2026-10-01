// Self-check that colors.css stays the only place a colour is defined. Run: `node colors.test.js`.
// Fails on a colour literal (hex, rgb/rgba, hsl/hsla) anywhere else under src/web, a --no-* colour
// token defined outside colors.css, or a --no-* token read that colors.css doesn't define.
const assert = require('assert');
const fs = require('fs');
const path = require('path');

const WEB = path.join(__dirname, '..');
const COLORS = path.join(__dirname, 'colors.css');
// Files allowed literals, with why.
const ALLOWED = {
  // Fills for a hidden 2x2 canvas that keeps a wake-lock video stream alive; never on screen.
  [path.join('shell', 'shared', 'wake-lock.js')]: true,
};
// --no-* custom properties that aren't colours.
const NOT_COLOURS = new Set(['--no-font']);

function walk(dir, out) {
  for (const e of fs.readdirSync(dir, { withFileTypes: true })) {
    const p = path.join(dir, e.name);
    if (e.isDirectory()) walk(p, out);
    else if (/\.(css|js|html)$/.test(e.name) && !e.name.endsWith('.test.js')) out.push(p);
  }
  return out;
}

// Comments, data: URIs (favicons, mask/icon SVGs, which can't read var()) and HTML entities can
// hold colour-looking text that renders nothing on its own.
function strip(src) {
  return src
    .replace(/\/\*[\s\S]*?\*\//g, '')
    .replace(/<!--[\s\S]*?-->/g, '')
    .replace(/(^|[^:'"\\])\/\/.*$/gm, '$1')
    .replace(/data:[^"')]*/g, '')
    .replace(/&#\d+;/g, '');
}

const LITERAL = /#(?:[0-9a-f]{8}|[0-9a-f]{6}|[0-9a-f]{3,4})(?![\w-])|\b(?:rgba?|hsla?)\(\s*\d/gi;
const defined = new Set([...fs.readFileSync(COLORS, 'utf8').matchAll(/(--no-[\w-]+)\s*:/g)].map(m => m[1]));
const problems = [];

for (const file of walk(WEB, [])) {
  const rel = path.relative(WEB, file);
  if (file === COLORS) continue;
  const src = fs.readFileSync(file, 'utf8');
  const code = strip(src);
  if (!ALLOWED[rel]) {
    for (const m of code.matchAll(LITERAL)) problems.push(rel + ': colour literal ' + m[0]);
  }
  for (const m of code.matchAll(/(--no-[\w-]+)\s*:/g)) {
    if (!NOT_COLOURS.has(m[1])) problems.push(rel + ': defines ' + m[1] + ' outside colors.css');
  }
  for (const m of src.matchAll(/(?:var\(|theme(?:Hex)?\(')(--no-[\w-]+)/g)) {
    if (!defined.has(m[1]) && !NOT_COLOURS.has(m[1])) problems.push(rel + ': reads undefined ' + m[1]);
  }
}

assert.deepStrictEqual(problems, [], 'colours outside shared/colors.css:\n  ' + problems.join('\n  '));
console.log('colors.test.js: OK');
