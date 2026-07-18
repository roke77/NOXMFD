// Self-check for the sticky-layout redirect guards (docs/layouts.md, Stage 3).
// The guards live inline in each shell's HTML <head>; this models their decision so the
// termination property is locked: whatever the stored value, resolving lands on a stable
// document in one step and never ping-pongs. Run: node src/web/shell/layout-sticky.test.js
const assert = require('assert');

// Each doc's guard, mirroring the inline scripts:
//   classic (served at '/')     redirects to '/f35' iff stored === 'f35'
//   f35     (served at '/f35')  redirects to '/'    iff stored === 'classic'
function next(doc, stored) {
  if (doc === 'classic' && stored === 'f35') return 'f35';
  if (doc === 'f35' && stored === 'classic') return 'classic';
  return doc; // stable
}

// Resolve from an entry doc, capping steps so a loop would throw instead of hang.
function resolve(entry, stored) {
  let doc = entry;
  for (let i = 0; i < 5; i++) {
    const to = next(doc, stored);
    if (to === doc) return doc;
    doc = to;
  }
  throw new Error(`redirect loop: entry=${entry} stored=${stored}`);
}

for (const entry of ['classic', 'f35']) {
  // Unset / unknown → no redirect, stay where you landed (root = classic by default).
  assert.strictEqual(resolve(entry, null), entry);
  assert.strictEqual(resolve(entry, 'nonsense'), entry);
  // A stored choice wins regardless of entry doc, and settles there.
  assert.strictEqual(resolve(entry, 'f35'), 'f35');
  assert.strictEqual(resolve(entry, 'classic'), 'classic');
}

console.log('layout-sticky: ok');
