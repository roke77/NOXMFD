// Self-check that every page document — every page under src/web/pages/ plus both shells — loads
// remote-keybinds.js. Run with the other *.test.js files.
//
// Keyboard events don't bubble across iframe boundaries: whichever document currently has focus is
// the only one that ever sees a keydown, so a page missing this script silently drops every remote
// keypress the instant that page (or its portal) has focus — the rest of the shell can work fine,
// making the gap easy to miss until someone reports "it works from MAIN, but not from <page>". That
// exact bug shipped for keybinds.html, sqd.html, and td.html; this test exists so a new page can't
// quietly reintroduce it. No .cs build/execution here — a plain text scan, same spirit as
// server-route-coverage.test.js.
const assert = require('assert');
const fs = require('fs');
const path = require('path');

const ROOT = path.join(__dirname, '..');   // src/web
const dirs = [path.join(ROOT, 'pages'), path.join(ROOT, 'shell')];

function findHtmlFiles(dir) {
  let out = [];
  for (const entry of fs.readdirSync(dir, { withFileTypes: true })) {
    const full = path.join(dir, entry.name);
    if (entry.isDirectory()) out = out.concat(findHtmlFiles(full));
    else if (entry.name.endsWith('.html')) out.push(full);
  }
  return out;
}

const htmlFiles = dirs.flatMap(findHtmlFiles);
assert.ok(htmlFiles.length > 15, `found too few page HTML files (${htmlFiles.length}) — search path probably broke`);

const missing = htmlFiles.filter(f => !fs.readFileSync(f, 'utf8').includes('/assets/services/remote-keybinds.js'));

assert.deepStrictEqual(missing.map(f => path.relative(ROOT, f)), [],
  'these pages never load remote-keybinds.js, so a remote keypress silently does nothing while ' +
  'one of them has focus');

console.log(`remote-keybinds-coverage.test.js: OK (${htmlFiles.length} page documents all load remote-keybinds.js)`);
