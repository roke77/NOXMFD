// F-35 layout — Stage 2 prototype (docs/layouts.md). Full view, one page, no navigation.
//
// Scope is deliberately tiny: this answers ONE question — how does a page look on a borderless
// panoramic display? It renders AVN and nothing else. No nav labels, no page switching, no
// splits. Label placement is the next decision; making it here would prejudge it.
//
// It consumes the shared layers Stage 1 established and adds no new contract:
//   • Data — the MAP iframe (#map-tap) owns the only EventSource('/stream') and posts derived
//     per-page slices up to its host shell. We forward the 'avn' slice into the page frame. Any
//     layout inherits this dependency, map or no map.
//   • Geometry — we never post 'avn-layout', so AVN stays in its `compact` profile (its default)
//     and needs no bezel key-band rects. That's the escape hatch docs/layouts.md identified, and
//     it means this shell owes pages no geometry contract at all.
//
// NAV (nav-model.js) is intentionally not loaded yet: this layout places no labels.
(function () {
  const mapTap    = document.getElementById('map-tap');
  const pageFrame = document.getElementById('page-frame');
  let lastAvn = null;

  function forwardAvn() {
    const w = pageFrame.contentWindow;
    if (w && lastAvn) w.postMessage(lastAvn, '*');
  }

  window.addEventListener('message', function (e) {
    const m = e.data;
    if (!m || m.mfd !== true) return;
    // Telemetry comes only from the tap. Same guard the bezel shell uses: a second map source
    // would drive the page from two out-of-phase feeds.
    if (e.source !== mapTap.contentWindow) return;
    if (m.type === 'avn') { lastAvn = m; forwardAvn(); }
  });

  // The first slice can land before the page frame finishes loading; replay the latest on load so
  // AVN never sits empty waiting for the next tick.
  pageFrame.addEventListener('load', forwardAvn);
})();
