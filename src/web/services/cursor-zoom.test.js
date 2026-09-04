// Self-check for the shared cursor-anchored zoom/icon-shrink module. Run: `node cursor-zoom.test.js`.
//
// Extracted from FCR's and HSD's identical zoom-transform/icon-shrink pair (docs/page-cursor.md) —
// DOM-free here since apply()/toggle() only touch document.getElementById when a matching element
// exists (there isn't one in this harness), so the pure math (toContentSpace/iconTransform) is
// exercised directly without needing a real DOM.
const assert = require('assert');

(async () => {
  const { createCursorZoom } = await import('./cursor-zoom.js');

  // ── toContentSpace: identity when not zoomed, exact inverse of the forward transform when zoomed ──
  {
    const z = createCursorZoom({ groupIds: [], zoomScale: 3 });
    assert.deepStrictEqual(z.toContentSpace(100, 200), { x: 100, y: 200 }, 'identity when not zoomed');

    z.toggle(300, 300);   // zoom in, anchored at (300,300)
    assert.strictEqual(z.isZoomed(), true);
    assert.deepStrictEqual(z.toContentSpace(300, 300), { x: 300, y: 300 }, 'the anchor itself maps to itself');
    const c = z.toContentSpace(300 + 3 * 30, 300);
    assert.ok(Math.abs(c.x - 330) < 1e-6, '30 content units right of anchor, scaled to screen, maps back to 30');

    z.toggle(999, 999);   // toggling again while zoomed turns it off, ignoring the new point
    assert.strictEqual(z.isZoomed(), false);
    assert.deepStrictEqual(z.toContentSpace(50, 60), { x: 50, y: 60 }, 'identity again once toggled off');
  }

  // ── iconTransform: empty when not zoomed; net scale is iconShrink/zoomScale, independent of iconShrink's own value ──
  {
    const z = createCursorZoom({ groupIds: [], zoomScale: 3, iconShrink: 0.5 });
    assert.strictEqual(z.iconTransform(10, 20), '', 'no icon transform when not zoomed');
    z.toggle(0, 0);
    const t = z.iconTransform(10, 20);
    assert.ok(t.indexOf('scale(' + (0.5 / 3).toFixed(4)) >= 0, 'icon transform scales by iconShrink/zoomScale');
    assert.ok(t.indexOf('translate(10.0 20.0)') >= 0, 'centered on the icon\'s own point, not the zoom anchor');
  }

  // ── Default iconShrink is 1 (icons keep their normal size, only spacing zooms) ──
  {
    const z = createCursorZoom({ groupIds: [], zoomScale: 4 });
    z.toggle(0, 0);
    const t = z.iconTransform(0, 0);
    assert.ok(t.indexOf('scale(' + (1 / 4).toFixed(4)) >= 0, 'default iconShrink is 1');
  }

  // ── apply(): DOM-write guard (docs/web-efficiency-audit.md finding 08) — the caller's render()
  // calls apply() every tick regardless of whether anything moved; an unchanged transform must
  // skip the setAttribute rewrite, and a real toggle must still write on the very next call. This
  // is the one path the tests above never touch, since they all pass an empty groupIds. ──
  {
    let calls = 0;
    const fakeGroup = { setAttribute() { calls++; } };
    global.document = { getElementById: (id) => (id === 'g1' ? fakeGroup : null) };

    const z = createCursorZoom({ groupIds: ['g1'], zoomScale: 3 });
    z.apply();
    assert.strictEqual(calls, 1, 'first apply() call always writes (module-load lastT is null)');

    z.apply();
    assert.strictEqual(calls, 1, 'repeating the same (unzoomed) transform does not rewrite the DOM');

    z.toggle(10, 20);   // zoom on — the transform actually changes
    assert.strictEqual(calls, 2, 'a real transform change still writes');

    z.toggle(10, 20);   // zoom off — changes back
    assert.strictEqual(calls, 3, 'toggling back off writes again');

    delete global.document;
  }

  console.log('cursor-zoom.test.js: OK');
})();
