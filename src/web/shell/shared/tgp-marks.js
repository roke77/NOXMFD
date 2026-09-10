// MAN/CLR/IR/WTV/STV NAV highlight state (docs/tgp-manual-control.md's NAV additions,
// docs/tgp-single-target-view.md's VIEW toggle), shared by both shells' bezel/glass renderers
// (mfd.js, f35.js) so the highlight rule lives in one place. Pure, DOM-free so it runs under node
// for the self-check (tgp-marks.test.js).
//
// MAN lights for the manual camera; CLR/IR mirror whichever feed the active camera (either one)
// is currently showing. `cnt` is 0 with no lock and no manual mode (TelemetryJson.cs's TgpBlock),
// so hasFeed — not just cnt > 0 — is what gates ir/clr meaningfully having a value at all. WTV/STV,
// like MAN, is a standing preference rather than something a feed gates — exactly one of the two
// is always lit, feed or no feed. (There used to be a `tgt` mark for the LCK button; LCK was
// removed in issue #81 and MAN became a blind toggle, so it's gone too.)
(function (root) {
  function tgpMarks(cnt, manual, ir, stv) {
    const hasFeed = cnt > 0 || manual;
    const irOn = hasFeed && !!ir;
    return { man: !!manual, clr: hasFeed && !irOn, ir: irOn, wtv: !stv, stv: !!stv };
  }

  const api = { tgpMarks };
  if (typeof module !== 'undefined' && module.exports) module.exports = api;
  else root.TgpMarks = api;
})(typeof self !== 'undefined' ? self : this);
