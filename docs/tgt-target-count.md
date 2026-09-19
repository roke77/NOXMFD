# TGT: selected-target count in the header

## Status

Built, `serve_web`-harness verified. Not yet in-game tested.

## Goal

Player request: show the total number of currently-selected targets on the TGT page, so a squadron
lead can match ordnance count to target count without leaving the map to check the cockpit MFD.

## Design decisions along the way

Two iterations, both from direct feedback on the first cut:

1. **A standalone line above the list, "N TARGETS SELECTED".** Rejected — sat above
   `.tgt-list-scroll` as its own row, which looked out of place and, worse, the full phrase wrapped
   to two lines in a narrow split pane or F-35 portal (the `.tgt-list-head` grid's NAME column is
   only `minmax(90px, 1fr)`), pushing the sticky header to two lines and breaking its alignment with
   the other column headers.
2. **Folded into the NAME column header instead.** The header cell's text is replaced outright —
   `TARGETS (N)` instead of `NAME` — with just `(N)`, no label word, so it can't wrap regardless of
   panel width. `(N)` is `--no-amber` (`tgt.css`'s `.tgt-count-n`), the same "value that matters at a
   glance" accent the TTI readout already uses; `TARGETS` itself inherits `.tgt-list-head`'s own dim
   color, unchanged.
3. **Hidden entirely at zero.** `(N)` renders empty (not `(0)`) when nothing is selected, so the
   header reads plain `TARGETS` rather than `TARGETS ()` (`tgt.js`'s `renderTargets`).

## What is built

| File | What |
|---|---|
| [`src/web/pages/tgt/tgt.js`](../src/web/pages/tgt/tgt.js) | `renderTargets()` sets `#tgt-count-n`'s text to `(N)` (empty string at `N === 0`) on every target-list refresh — no new state, just derived from `targets.length` each frame. |
| [`src/web/pages/tgt/tgt.html`](../src/web/pages/tgt/tgt.html) | The `tgt-list-head`'s first cell reads `TARGETS <span id="tgt-count-n">` instead of plain `NAME`. |
| [`src/web/pages/tgt/tgt.css`](../src/web/pages/tgt/tgt.css) | `.tgt-count-n { color: var(--no-amber); }`. |
| [`man/tgt.md`](../man/tgt.md) | Documents the header. |

## Verification performed

- `dotnet build -c Release` — 0 errors.
- Full `*.test.js` suite (no dedicated test file — the only new logic is a trivial ternary inline in
  a DOM-rendering function, same untested-one-liner shape as `tgt.js`'s existing `fmtHdg`).
- `serve_web` harness, live browser check: confirmed the header reads `TARGETS (12)` with `(12)` in
  `--no-amber` (`rgb(255, 170, 0)`) against the mock's 12-target list, on one line at a narrow (540px)
  panel width where the original standalone-line design wrapped.
- Not yet checked in-game.
