// TGT/MAN/CLR/IR NAV highlight state (docs/tgp-manual-control.md's NAV additions), shared by both
// shells' bezel/glass renderers (mfd.js, f35.js) so the highlight rule lives in one place. Pure,
// DOM-free so it runs under node for the self-check (tgp-marks.test.js).
//
// TGT lights for a real (non-manual) unit lock, MAN for the manual camera; CLR/IR mirror whichever
// feed the active camera (either one) is currently showing. `cnt` is 0 with no lock and no manual
// mode (TelemetryJson.cs's TgpBlock), so hasFeed — not just cnt > 0 — is what gates ir/clr
// meaningfully having a value at all.
(function (root) {
  function tgpMarks(cnt, manual, ir) {
    const hasFeed = cnt > 0 || manual;
    const irOn = hasFeed && !!ir;
    return { tgt: hasFeed && !manual, man: !!manual, clr: hasFeed && !irOn, ir: irOn };
  }

  const api = { tgpMarks };
  if (typeof module !== 'undefined' && module.exports) module.exports = api;
  else root.TgpMarks = api;
})(typeof self !== 'undefined' ? self : this);
