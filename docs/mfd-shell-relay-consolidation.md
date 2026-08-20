# `mfd.js` relay consolidation — collapsing the `forward*ToPanes`/`forward*ToFrame` duplication

## Status

Planning — not started.

## Where this came from

An external code-review agent called `src/web/shell/classic/mfd.js` (2460 lines) "the main UI risk"
in general terms — a large orchestration file — and recommended continuing the existing
helper/test-extraction pattern (`NavModel.js`, `LayoutPages.js`, `SplitSlots.js`, `ClassicPaging.js`,
`SplitKeymap.js` are already extracted). That's true but too vague to act on. Reading the file found
the concrete thing worth naming.

## The actual finding

`mfd.js` defines **44 `forward*ToPanes`/`forward*ToFrame` functions** (grep count, 2026-08-20;
`grep -c '^function forward'`), one pair per data slice per page (AVN, AFM, TGP, EXT, RWR, RDR, MW,
TGT, BDF, PAL, MIS, OBJ, AKF, WPT, WPN, CM, wpt-routes...), spanning roughly lines 572-1065 plus a
later addition at 2426-2438. Each new MFD page has historically meant hand-writing one more pair.

Every pair follows the same shape (`forwardRwrToPanes`/`forwardRwrToFrame`,
`src/web/shell/classic/mfd.js:765`-`789`, is representative):

```js
function forwardRwrToPanes() {
  paneIframes.forEach(function(iframe, idx) {
    if (panePages[idx] !== 'rwr') return;
    if (!iframe.contentWindow) return;
    iframe.contentWindow.postMessage({ mfd: true, type: 'rwr', items: rwrData.items || [] }, '*');
  });
}
function forwardRwrToFrame() {
  const w = frameWin(); if (!w) return;
  w.postMessage({ mfd: true, type: 'rwr', items: rwrData.items || [] }, '*');
}
```

`ToPanes` always: iterate `paneIframes`, filter by `panePages[idx] === <page>`, guard
`contentWindow`, `postMessage` a payload. `ToFrame` always: get `frameWin()`, guard it, `postMessage`
the *same* payload. The only things that differ per pair are the page tag and the payload — which is
sometimes inline (`{ mfd: true, type: 'rwr', items: ... }`) and sometimes already its own small
builder function (`rdrMsg()`, `:790`, used by both `forwardRdrToPanes`/`forwardRdrToFrame`) — that
existing `rdrMsg()` pattern is effectively the shape this doc proposes generalizing.

## Why this is worth collapsing

- **Duplication risk, not line count, is the real cost.** A bug in the shared boilerplate (say, a
  missing `contentWindow` guard, or the pane-filter condition) has to be fixed identically in up to
  44 places; nothing enforces that the copies stay in sync as new pages get added.
- **Every new MFD page pays a fixed, avoidable tax.** Adding a page currently means writing a new
  `ToPanes`/`ToFrame` pair by hand, copy-pasting the surrounding pattern — exactly the kind of
  boilerplate this codebase's own conventions (CLAUDE.md: "deletion over addition") argue against.
- **It's mechanical, which makes it safe.** Unlike the split-layout math elsewhere in the same file
  (`renderSplitLabels`, `mainPaneSlice`, `avnPaneSlice`, etc. — genuinely bespoke per-page geometry,
  not a candidate for this treatment), the relay functions have zero page-specific logic beyond the
  tag and the payload shape.

## Proposed shape

Two small generic helpers replacing the 44 hand-written functions' boilerplate, keeping each page's
payload-building logic (the `rdrMsg()`-style functions) exactly as-is:

```js
function forwardToPanes(page, payload) {
  paneIframes.forEach(function(iframe, idx) {
    if (panePages[idx] !== page || !iframe.contentWindow) return;
    iframe.contentWindow.postMessage(payload, '*');
  });
}
function forwardToFrame(payload) {
  const w = frameWin(); if (!w) return;
  w.postMessage(payload, '*');
}
```

Call sites become one line each at whatever already triggers the forward today (e.g. on `/stream`
frame arrival): `forwardToPanes('rwr', rwrMsg()); forwardToFrame(rwrMsg());` — same two calls as
today, just without the hand-written wrapper around each. This is a mechanical, page-by-page
migration (44 call-site conversions), not a rewrite: convert one page, verify it in the
`tools/serve_web.py` harness + a live split-mode check, move to the next. No behavior change is
intended anywhere — this is pure de-duplication.

**Not proposed:** touching the split-layout geometry functions (`renderSplitLabels` and friends).
Those are real per-page logic, not boilerplate, and are already a reasonable size for what they do.
**Also not in scope:** `src/web/shell/f35/f35.js` (1130 lines) — checked, it has zero `forward*`
functions, so it doesn't share this duplication and needs no equivalent pass.

## Scope

- [ ] Add `forwardToPanes(page, payload)` / `forwardToFrame(payload)` helpers
- [ ] Migrate each of the 44 `forward*` functions to a call site, one page at a time, verifying in
      `tools/serve_web.py` (classic full view + split) after each
- [ ] Delete the now-unused hand-written wrappers as each page migrates (no dead code left behind)
- [ ] Confirm `layout-coverage.test.js` and `server-route-coverage.test.js` still pass after the full
      migration (no route/page coverage should change — this only touches how a payload gets to an
      already-reachable page)
