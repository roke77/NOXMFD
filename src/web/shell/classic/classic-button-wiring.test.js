// Self-check that every full-view frame-hosted page (layout-pages.js's CLASSIC_FULL) is actually
// wired into mfd.js's two HAND-WRITTEN per-page spots. Run: `node classic-button-wiring.test.js`.
//
// Adding a page to NAV/BEZEL_EXTRAS + layout-pages.js + split-slots.js (all covered by
// layout-coverage.test.js / server-route-coverage.test.js / split-slots.test.js) is NOT enough on
// its own: full view's actual click-to-render path is two separate switches in mfd.js that share
// no table with any of the above —
//   1. mfdButton()'s `case '<x>': ... showPage('<x>'); break;` — what a bezel key click DOES.
//   2. showPage()'s `showFramePage('<x>')` call — what actually loads the page into #page-frame.
// A page can pass every other coverage check and still be a dead button in full view because
// these two are hand-maintained with no shared source of truth. This is precisely the gap that let
// the SQD page ship wired everywhere else but silently do nothing when clicked (2026-08-20) — this
// test exists so the next new page fails loudly here instead of only in manual testing.
//
// Plain text scan, same spirit as server-route-coverage.test.js — no .js execution, just checking
// the literals are present.
const assert = require('assert');
const fs = require('fs');
const path = require('path');
const { CLASSIC_FULL } = require('../layout-pages.js');

const mfdPath = path.join(__dirname, 'mfd.js');
const mfdSrc = fs.readFileSync(mfdPath, 'utf8');

const pages = Object.keys(CLASSIC_FULL);
assert.ok(pages.length > 10, `found too few CLASSIC_FULL pages (${pages.length}) — layout-pages.js probably broke`);

for (const page of pages) {
  assert.ok(mfdSrc.includes(`showFramePage('${page}')`),
    `CLASSIC_FULL.${page} has no 'showFramePage(\'${page}\')' call anywhere in mfd.js's showPage() — ` +
    `#page-frame will never navigate to it, so the page stays blank even if its bezel key is wired`);
  assert.ok(mfdSrc.includes(`case '${page}':`),
    `CLASSIC_FULL.${page} has no 'case '${page}':' in mfd.js's mfdButton() full-view switch — ` +
    `clicking its bezel key (or MAIN's list entry) in full view silently does nothing`);
}

console.log(`classic-button-wiring.test.js: OK (${pages.length} full-view pages have both a mfdButton case and a showFramePage call)`);
