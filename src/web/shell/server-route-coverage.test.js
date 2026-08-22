// Self-check that every page URL the shell can navigate to (layout-pages.js) has a matching route
// in the C# HTTP router (src/plugin/Http/TelemetryHttpRouter.cs). Run: `node server-route-coverage.test.js`.
//
// layout-coverage.test.js catches a page missing from one of the JS routing tables. It can't catch
// this: a page present in every JS table but never wired into the server's own path switch. The
// request then falls through TelemetryServer's `else Redirect(ctx, "/")` — which reloads the whole
// shell into whatever iframe asked for it, not the page you meant (issue #38's WPT route, missed
// four times over). No .cs build/execution here — this is a plain text scan of the routing method's
// `path == "/..."` literals, no different in spirit from grepping it by hand before shipping, just
// automated so it can't be skipped.
const assert = require('assert');
const fs = require('fs');
const path = require('path');
const { CLASSIC_FULL, CLASSIC_SPLIT, F35 } = require('./layout-pages.js');

const serverPath = path.join(__dirname, '..', '..', 'plugin', 'Http', 'TelemetryHttpRouter.cs');
const serverSrc = fs.readFileSync(serverPath, 'utf8');

// Every `path == "/xyz"` literal in the routing chain (else-if or plain if), wherever it appears —
// deliberately not anchored to one method, so this survives the chain being reshuffled or renamed.
const routed = new Set();
for (const m of serverSrc.matchAll(/path == "(\/[^"]*)"/g)) routed.add(m[1]);
assert.ok(routed.size > 10, `found too few routes (${routed.size}) in TelemetryHttpRouter.cs — regex probably broke`);

for (const [layout, table] of [['classic-full', CLASSIC_FULL], ['classic-split', CLASSIC_SPLIT], ['f35', F35]]) {
  for (const [page, url] of Object.entries(table)) {
    if (url === null) continue;   // f35.main — no page to route
    const base = url.split('?')[0];   // '/wpt?bare' → '/wpt': the server routes on path, not query
    assert.ok(routed.has(base),
      `${layout}.${page} points at '${url}', but TelemetryHttpRouter.cs has no 'path == "${base}"' route — ` +
      `it would fall through to the catch-all and reload the whole shell instead of this page`);
  }
}

console.log(`server-route-coverage.test.js: OK (${routed.size} server routes cover every layout-pages.js URL)`);
