// Shared PAD-cursor-anchored zoom (docs/page-cursor.md) — the same DCS-style "TDC depress"
// magnifier FCR and HSD both use: holding Cursor Select toggles a fixed-factor zoom centered on
// wherever the cursor sits, and each contact's own icon is counter-scaled so it doesn't visually
// balloon up by the same factor the zoom uses to pull overlapping icons apart.
//
// Deliberately DOM-adjacent but coordinate-system-agnostic: the caller owns viewBox geometry
// (its own viewport()/content-space conversion), and just hands this module plain numbers — it
// knows nothing about B-scope vs. plan-view projection, only which `<g>` ids to transform.
export function createCursorZoom({ groupIds, zoomScale = 3, iconShrink = 1 }) {
  let zoomed = false;
  let anchor = { x: 0, y: 0 };   // content-space viewBox units; meaningful only while zoomed
  // The caller's render() calls apply() every tick (~10 Hz) regardless of whether the zoom state
  // moved since the last one — the common case (docs/web-efficiency-audit.md finding 08). toggle()
  // below already changes `zoomed`/`anchor` before calling apply(), so the resulting `t` naturally
  // differs on a real toggle without this needing its own force path.
  let lastT = null;

  function apply() {
    const t = zoomed
      ? 'translate(' + anchor.x.toFixed(1) + ' ' + anchor.y.toFixed(1) + ') scale(' + zoomScale +
        ') translate(' + (-anchor.x).toFixed(1) + ' ' + (-anchor.y).toFixed(1) + ')'
      : '';
    if (t === lastT) return;
    lastT = t;
    groupIds.forEach(function (id) {
      const g = document.getElementById(id);
      if (g) g.setAttribute('transform', t);
    });
  }

  return {
    isZoomed: function () { return zoomed; },
    // Toggles zoom on (anchored at contentX/contentY, already converted by the caller) or off, and
    // repaints the transformed groups immediately.
    toggle: function (contentX, contentY) {
      if (zoomed) {
        zoomed = false;
      } else {
        anchor = { x: contentX, y: contentY };
        zoomed = true;
      }
      apply();
    },
    apply: apply,
    // The forward transform's exact inverse — maps a raw screen/viewBox point into CONTENT space,
    // so a caller's existing hit-test data (computed pre-zoom) keeps working unmodified regardless
    // of whether the picture is currently magnified.
    toContentSpace: function (vx, vy) {
      if (!zoomed) return { x: vx, y: vy };
      return { x: anchor.x + (vx - anchor.x) / zoomScale, y: anchor.y + (vy - anchor.y) / zoomScale };
    },
    // A per-icon counter-scale <g> transform attribute (empty string when not zoomed), centered on
    // the icon's own point so it shrinks/grows in place rather than drifting toward the zoom anchor.
    // Composed with the outer zoom transform, the net on-screen size while zoomed is exactly
    // iconShrink x normal, independent of zoomScale.
    iconTransform: function (px, py) {
      if (!zoomed) return '';
      const s = iconShrink / zoomScale;
      return ' transform="translate(' + px.toFixed(1) + ' ' + py.toFixed(1) + ') scale(' + s.toFixed(4) +
             ') translate(' + (-px).toFixed(1) + ' ' + (-py).toFixed(1) + ')"';
    },
  };
}
