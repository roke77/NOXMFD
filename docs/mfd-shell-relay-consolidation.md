# `mfd.js` relay consolidation — collapsing the `forward*ToPanes`/`forward*ToFrame` duplication

## Status

Done. Also folded in as part of the same branch: removed two dead functions found by a separate
codebase-wide dead-code scan (`LayoutModal.isOpen`, `WaypointsStore.hasRoutes` — both exported but
never called anywhere; see git history for detail).

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

## Caveats — not every pair is as uniform as the RWR example

Checked every function, not just the representative one above. Three real deviations from the
"same shape every time" claim, all still compatible with the proposed consolidation but worth
knowing before migrating blind:

- **Not every function has a `Panes`/`Frame` sibling.** `forwardStatusToPanes` (`:572`) has no
  `forwardStatusToFrame` at all — it's pane-only. `forwardAfmLayoutToFrame` (`:688`) has no
  `forwardAfmLayoutToPanes` — frame-only, the opposite gap. And `forwardWptRoutesToPanes`/`ToFrame`
  (`:2426`-`:2430`) have a *third* sibling, `forwardWptRoutesToMap` (`:2438`), sending the same data
  to yet another destination (the always-on MAP iframe, not a pane or the page frame). The
  consolidation doesn't need every page to have both calls — it just means migrating a page means
  calling `forwardToPanes`/`forwardToFrame` (or a third helper, for the MAP case) only where a
  hand-written function already exists today, not forcing symmetry that isn't there now.
- **WPN's `ToPanes`/`ToFrame` pair genuinely diverge, not just superficially.** `forwardWpnToPanes`
  (`:927`) slices per-pane via `wpnPaneSlice(idx)`; `forwardWpnToFrame` (`:1030`) computes its own,
  separate pagination (`wpnPage`/`WPN_MAX_DISPLAY`/`maxPage`) **and** does extra work with no pane
  equivalent — wiring each visible weapon row to a physical bezel key
  (`keyBanks.left[k+1].dataset.action = 'weapon.select'`), explicitly commented "split-mode weapon
  rows aren't wired yet." This one isn't a `forwardToPanes(page, payload())` one-liner: the payload
  itself needs page-specific pagination logic kept intact, and the frame side keeps its extra
  bezel-wiring step outside the generic helper. Migrate WPN last, and expect it to stay a real
  function, not collapse to a single call like RWR/RDR do.
- **`f35.js` is not duplication-free by having zero `forward*` functions — it has several
  (`forwardSlice`, `forwardAfm`, `forwardWpnLayout`, `forwardOrientation`, `:297`-on), just not the
  duplicated pattern.** Correcting an earlier draft of this doc, which claimed zero. The real point
  stands: `f35.js` never grew 44 near-identical pairs, because `forwardSlice(type)` is already a
  generic dispatcher over a `slices` map, keyed by feed type, with per-type renames handled by a
  small `FEED_AS` table rather than a bespoke function per page. **This is a working precedent for
  exactly the consolidation this doc proposes for `mfd.js`** — not just supporting evidence that
  the duplication is avoidable, but a second implementation already proving the shape works in this
  codebase.

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

**Corrected during implementation:** call sites do NOT inline `forwardToPanes('rwr', rwrMsg())`
directly as first drafted above — each page's forwarders (~140 call sites total, across the
on-pane-load, on-frame-load, `showPage`, and periodic-tick dispatch tables) still call
`forwardAvnToPanes()`/`forwardAvnToFrame()` etc. by their original names. Only each function's
BODY shrank to a one-liner (`function forwardAvnToPanes() { forwardToPanes('avn', avnMsg()); }`),
and each page's previously-inlined-twice payload was factored into a small `xMsg()` builder
(`avnMsg()`, `tgtMsg()`, …) alongside the existing `rdrMsg()` precedent. This gets the identical
de-duplication result — a bug in the shared boilerplate now needs fixing in exactly one place, not
44 — without touching a single call site, which is a much smaller, lower-risk diff than rewriting
~140 call sites across four separate dispatch tables. No behavior change anywhere.

**Also discovered during implementation:** the geometry-computing forwarders aren't confined to
WPN. `forwardAvnLayoutToPanes`/`ToFrame`, `forwardAfmLayoutToFrame`, and `forwardWpnLayoutToPanes`/
`ToFrame` all compute their payload from live bezel-key `getBoundingClientRect()` reads — per-pane
for the `ToPanes` side (each pane has a different `paneTop`) — not a single shared value the two
generic helpers assume. These stay hand-written, same reasoning as `renderSplitLabels` below, not
just WPN's pagination/bezel-wiring divergence. Separately, `forwardWptRoutesToPanes`/`ToMap` have
no `panePages` filter at all (unconditional broadcast to every pane/the map iframe) — they don't
fit `forwardToPanes(page, payload)`'s shape either, so only `forwardWptRoutesToFrame` migrated;
all three now at least share one `wptRoutesMsg()` builder instead of the same payload written out
three times.

**Not proposed:** touching the split-layout geometry functions (`renderSplitLabels` and friends).
Those are real per-page logic, not boilerplate, and are already a reasonable size for what they do.
**Also not in scope:** `src/web/shell/f35/f35.js` (1130 lines) — see the caveats section above, it
already solved this differently (a generic `forwardSlice` dispatcher) and needs no equivalent pass.

## Scope

- [x] Add `forwardToPanes(page, payload)` / `forwardToFrame(payload)` helpers
- [x] Migrate the uniform pairs (RWR/RDR-shaped — most of the 44) — done for all of them: Status,
      Avn, Afm, Tgp, Ext, Rwr, Rdr, Mw, Tgt, TgtTargets, Bdf, Pal, Mis, Obj, Akf, Wpt, Cm
- [x] Migrate the pane-only/frame-only/third-destination cases (`forwardStatusToPanes`,
      `forwardWptRoutesToFrame`) — same helpers, just called on whichever sides actually exist
      today. `forwardAfmLayoutToFrame` turned out to be geometry (see above) — excluded, not
      migrated. `forwardWptRoutesToPanes`/`ToMap` have no page filter to key off — stayed
      hand-written, now sharing `wptRoutesMsg()` with the migrated `ToFrame` side.
- [x] Migrate WPN — reconsidered: its whole cluster (`ToPanes`/`ToFrame` AND
      `LayoutToPanes`/`LayoutToFrame`) stays hand-written, not just the pagination/bezel-wiring
      pair called out above. The Layout pair computes per-pane bezel geometry, the same reason
      `forwardAvnLayoutToPanes`/`ToFrame` also don't fit the generic shape (see above).
- [x] Delete the now-unused hand-written wrappers as each page migrates (no dead code left behind)
      — N/A as implemented: the wrapper functions themselves are what stayed (see the corrected
      call-site note above), so there was nothing separate left to delete.
- [x] Confirm `layout-coverage.test.js` and `server-route-coverage.test.js` still pass after the full
      migration (no route/page coverage should change — this only touches how a payload gets to an
      already-reachable page)
