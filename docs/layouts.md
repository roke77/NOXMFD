# Layouts — swappable shell/navigation designs

## Status

**In progress.** Stage 1 (the seam) and most of Stage 2 (a second layout)
are built and on `feat/layouts`. The bezel remains the default and is
unchanged.

- The **bezel** layout ships: a metallic 4/6/4/6 button frame, served at `/`.
- The **F-35** layout is a working prototype, served at `/f35`: borderless,
  no keys, labels drawn on the glass. Every page renders on it (MAIN, MAP,
  AVN, RWR, TGT, TGP, WPN) in full view. **Splits are not built yet** — that
  is the remaining Stage-2 work, and the hard part.

Both consume one shared navigation model. `NAV` has never been edited to
serve the second layout, and no page has changed — which was this plan's
central claim.

See [issue #8](https://github.com/roke77/NOXMFD/issues/8) for the F-35
reference screenshots that motivated this.

## Terminology

We call this **layout**, not "theme", on purpose. A theme implies a skin
(colors, textures) over the same structure. What changes here is
structural: the shell frame, where navigation labels live, and how
splits work. Colors are the smallest part of it.

## Goal

Let a user pick a different shell/navigation design while every MFD page
renders the exact same content. The page *content* is fixed; the
*surrounding shell*, the navigation-label placement, and the split
behavior are what a layout owns.

| Layout            | Frame            | Nav labels                          | Split model              |
|-------------------|------------------|-------------------------------------|--------------------------|
| **Bezel** (today) | metallic bezel   | 4/6/4/6 physical keys around screen | H/V bezel-key splits     |
| **F-35**          | borderless       | clickable labels drawn on the page  | 4 vertical portals (TBD) |

The F-35's split model is **four side-by-side vertical portals**, not a 2×2
quadrant grid — see the reference screenshots on issue #8. Each portal is
effectively an independent MFD with its own page and its own edge labels.

## What's already decoupled

Page *content* is independent of the shell. Every page — AVN / MAP / RWR /
TGT / TGP / WPN — is its own iframe under `src/web/pages/*`, served "bare"
and mounted into a host frame. The shell hosts the frame and feeds it data;
it does not know what's inside a page. The F-35 layout renders all of them
without a single page edit.

Two qualifications, both learned by building the second layout:

- It covers *what* a page renders, not always *where*. Some pages are handed
  geometry — see the next section.
- **The telemetry tap lives inside a page.** `TelemetrySource` owns the only
  `EventSource('/stream')` and it lives in the MAP iframe, which parses each
  frame and posts the derived per-page slices *up* to whatever shell hosts
  it. So every layout must embed a map iframe as a data tap **even if it
  never shows a map**. The F-35 does exactly that (`#map-tap`, `opacity: 0`),
  and showing MAP just reveals the iframe already running. A second map
  iframe would open a second stream and drive every page from two
  out-of-phase feeds; the bezel guards against that explicitly by ignoring
  posts from any window but its canonical map.

## What's coupled to a layout

- **Frame markup + CSS.** `mfd.html` hardcodes the bezel's four key banks
  (`#keys-top` 4, `#keys-left` 6, `#keys-bottom` 4, `#keys-right` 6) around
  `#screen`; `mfd.css` (~56KB) is its metallic look and geometry. The F-35's
  equivalents are `f35.html` / `f35.css`, which share nothing with them
  structurally.
- **Split behavior.** `SplitKeymap` (`split-keymap.js`) and `SPLIT_SLOTS`
  (`mfd.js`) resolve labels to physical bezel keys per orientation. Written
  around bezel-key geometry; **will not carry over** to a portal model.
- **Page placement geometry (shell → page).** The exception to "pages are
  decoupled": some pages are handed *layout geometry*, not just data.
  `forwardAvnLayoutToFrame` and `forwardWpnLayoutToFrame` (`mfd.js`) read the
  bezel key-separator rects (`sepEls`) and post `{avn,wpn}-layout` messages so
  each page's rows align to the physical key bands:

  ```js
  // mfd.js — forwardWpnLayoutToFrame()
  function bot(i) { return sepEls[i].getBoundingClientRect().bottom - frameTop; }
  w.postMessage({ mfd: true, type: 'wpn-layout', layout: 'full', slots: slots }, '*');
  ```

  This is not theoretical. HIDE SHELL has to keep the `.keys.v` columns in the
  layout — zero-width and invisible — *purely* so those separator rects stay
  valid; drop them and the AVN/WPN full-view geometry collapses.

  **The `compact` escape hatch, and its limit.** AVN and WPN both have a
  `compact` profile that needs no geometry at all (written for split panes;
  it is also the default, so a layout that forwards nothing gets it for
  free). AVN, TGT, RWR and TGP need nothing more than that: the F-35 sends
  them no geometry and they place themselves.

  **WPN is the exception.** Its `compact` profile scatters weapons into four
  corners and draws *no weapon image*, so a full-screen WPN can't use it —
  only the `full` profile renders the image, and `full` lays out solely
  against forwarded rects. So the F-35 does owe WPN geometry, and supplies
  its own: `forwardWpnLayout` in `f35.js` derives the row bands from its
  own 6-row grid instead of bezel separators. The page is untouched and
  cannot tell the difference. **A layout that hosts WPN full-screen must
  supply rects.** WPN also keys CSS off `body.landscape`, so its host must
  forward `orient` too.

## The seam

`PAGES` was split into two layers. Both are now real files:

1. **Navigation model** — `src/web/shell/nav-model.js`. Layout-independent
   data: per page, an ordered list of `{ label, action }`. No `key`, no
   `side`, no `slot`. This is "what a pilot can do from this page," and it is
   identical on an F-35. `nav-model.test.js` enforces the invariant — an item
   carrying placement fails the check.
2. **Layout renderer** — swappable. Consumes the navigation model plus the
   active page and split state, and decides *where and how* labels render and
   how the frame looks. The bezel maps items to 4/6/4/6 physical slots
   (`fullViewSlot`, `SPLIT_SLOTS`); the F-35 places them on a grid over the
   page (`cellOf`, `NAV_LAYOUT`).

`action` dispatch stays shared: both layouts call the same `send-command`
handlers.

```
            ┌─────────────────────────┐
            │   Navigation model       │  { page: [{label, action}, …] }
            │   (layout-independent)   │
            └───────────┬─────────────┘
                        │
          ┌─────────────┴─────────────┐
          ▼                           ▼
   ┌──────────────┐            ┌──────────────┐   (each owns frame +
   │ Bezel layout │            │ F-35 layout  │    label placement +
   │  renderer    │            │  renderer    │    split behavior +
   └──────┬───────┘            └──────┬───────┘    page geometry)
          └─────────────┬────────────┘
                        ▼
             shared action dispatch  →  page iframes (unchanged)
```

A layout also owns the **page placement geometry** it feeds pages (the
`*-layout` messages) — the fourth item above.

### What a layout may add on its own

`NAV` is shared, so a layout cannot grow it: the bezel has six physical keys
for MAIN's six items, and `nav-model.test.js` pins that list. A layout that
wants more puts them in its own table — the F-35 keeps `MAIN_EXTRAS`
(HUD/LYT/PAL/BDF placeholders) beside `NAV` and merges the two when
rendering. Consequently ordering is a *rendering* choice: the F-35 sorts its
MAIN menu alphabetically (so TGP precedes TGT) while the bezel keeps `NAV`'s
order (TGT, TGP).

The same asymmetry applies to placement. `NAV` items may never carry a cell,
but a layout's own items may — WPN's `NEXT` names its top-right cell,
because the shell built it.

## The honest caveat: a layout is not a stylesheet

The F-35's portal screen is a *different layout engine* than the bezel's H/V
splits. The current split logic (`SplitKeymap`, top/bottom vs left/right
resolution) is written around bezel-key geometry and **will not carry over** —
a portal layout needs its own split/placement behavior.

So a layout owns: **frame + label placement + split behavior + page
placement geometry**, sharing only (a) page content and (b) action dispatch.
The abstraction is "a layout owns the shell and navigation rendering," *not*
"a layout is a skin." Trying to make one parametric shell serve both a
physical bezel and a borderless portal grid would be worse than two focused
shell implementations sharing the content and action layers.

Building the second layout supports this. `f35.js` reimplements label
placement, page hosting and WPN geometry from scratch, and shares `NAV`, the
pages and `sendCommand` untouched. Nothing in the middle wanted to be
abstracted.

## Staged approach

Prove the seam with a pure refactor, then add the second layout as a
consumer.

### Stage 1 — the seam ✅ done

Extracted the navigation model out of `PAGES`: dropped `key`/`slot`, kept
`label`/`action`, routed the bezel through it. Zero behavior change, proven
by a data-equivalence check against the old tables before deleting them.

It answered two of the open questions below, and removed a live duplication
bug source: `label`/`action` had been declared twice for five pages.

### Stage 2 — the F-35 layout 🟡 in progress

Built (`src/web/shell/f35/`, served at `/f35`):

- borderless frame, no bezel; labels drawn *on* the page
- two placement modes: `edge` (the bezel's left key bank, minus the bezel)
  and `center` (MAIN's own 3-column block)
- every page hosted in full view, including WPN with layout-supplied rects
- MAP by revealing the always-running telemetry tap
- shared action dispatch; `NAV` unmodified

Remaining: **the 4-portal split model**. This is the genuinely hard part —
placement stops being derivable, and none of the bezel's split machinery
transfers.

### Stage 3 — selection 🟡 partial

The layout is chosen by URL today (`/` vs `/f35`), which is enough to
develop against but is not a user-facing setting. Smallest next step: a
BepInEx `ConfigEntry` (we already ship ConfigurationManager) or a `?layout=`
query param that makes `/` serve either shell. A softkey to switch live is a
later nicety — the F-35's MAIN carries a greyed `LYT` placeholder for it.

## Open questions

### Settled by building it

- **Are labels derivable from the ordered list, or do they need placement
  hints?** Both, split by view. **Full view is derivable** — item *i* → slot
  *i* down the left column, identically for both layouts (`fullViewSlot`,
  `cellOf`). **Split is not**: MAP deliberately groups its zoom rocker on the
  right, so the bezel needs `SPLIT_SLOTS`. Expect the portal model to need
  its own hints too.
- **Are HIDE SHELL / FULL / PIN / SWAP part of the navigation model?** No —
  layout-owned chrome. `nav-model.test.js` now enforces their absence.
- **One CSS bundle or two?** Two. `f35.css` shares no structure with
  `mfd.css`; only the `theme.css` tokens are common (`--no-label` was
  promoted there when both layouts needed the same off-white).

### Still open

- **Where does split state live once splits differ per layout?** Unchanged
  from the original plan: the navigation model shouldn't carry split
  geometry; each layout renderer should own its own.
- **Per-portal orientation.** The bezel treats orientation as *app-wide* on
  purpose: a media query inside an iframe evaluates against that iframe's own
  box, so a split pane (wide + short) would wrongly read landscape on a
  portrait device. The shell therefore reports the window's orientation as
  the single source of truth. **The F-35's portals invert this**: a
  quarter-width portal is *genuinely* portrait-shaped on a landscape screen,
  and WPN — which keys its weapon image off `body.landscape` — will be one of
  them. So the portal renderer likely needs per-portal orientation, diverging
  from the bezel's rule rather than reusing it.
- **Does a portal drive WPN's `compact` or `full` profile?** A quarter-width
  portal is close to the pane shape `compact` was written for, so it may need
  no rects at all — the opposite of the full-screen case above.
- **Connection status.** The bezel surfaces it (and the server URLs) on MAIN.
  The F-35's MAIN is navigation only, so it currently shows neither. The
  reference cockpit puts that class of readout in a master strip across the
  top of the glass.

## Out of scope

- Changing page content or the SSE/telemetry contract — both are
  layout-independent and stay as-is.
- Removing or restyling the bezel layout. Bezel stays the default.

## Where the code lives

Symbol names, not line numbers — this code is actively moving.

- **Shared:** `src/web/shell/nav-model.js` (+ its test) — `NAV`.
  `src/web/services/telemetry-source.js` — the one `EventSource`, inside the
  MAP page. `src/web/services/send-command.js` — `sendCommand`.
  `src/web/shared/theme.css` — the common tokens.
- **Bezel:** `src/web/shell/mfd.{html,css,js}`, `split-keymap.js`. Key
  symbols: `fullViewSlot`, `SPLIT_SLOTS`, `FRAME_PAGES`, `PAGE_URL`,
  `forwardAvnLayoutToFrame`, `forwardWpnLayoutToFrame`, `placeWpnNavLabels`.
- **F-35:** `src/web/shell/f35/f35.{html,css,js}`, `f35-wpn-paging.js` (+ its
  test). Key symbols: `F35_PAGES`, `PAGE_FEEDS`, `FEED_AS`, `FEED_DERIVE`,
  `NAV_LAYOUT`, `MAIN_EXTRAS`, `cellOf`, `forwardWpnLayout`.
- **Routes:** `/f35` is served by `TelemetryServer.cs` in-game and by
  `tools/serve_web.py` in the preview harness. Both need the entry.
