# Layouts — swappable shell/navigation designs (planning)

## Status

Planning only. No code yet. The one shell that ships today (a metallic
bezel with a 4/6/4/6 button frame) is the only "layout." This document
explores what it would take to support a second, structurally different
layout — e.g. a borderless F-35-style widescreen split into quadrants,
with clickable labels along each page's edges instead of bezel keys —
without touching page content.

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

| Layout            | Frame            | Nav labels                          | Split model            |
|-------------------|------------------|-------------------------------------|------------------------|
| **Bezel** (today) | metallic bezel   | 4/6/4/6 physical keys around screen | H/V bezel-key splits   |
| **F-35** (future) | borderless       | clickable labels along page edges   | fixed 4-quadrant grid  |

## What's already decoupled

Page *content* is already independent of the shell. Every page —
AVN / MAP / RWR / TGT / TGP / WPN — is its own iframe under
`src/web/pages/*`, served "bare" and mounted into `#screen`
(`FRAME_PAGES` in `src/web/shell/mfd.js:61`; the map is a separate
`/map-view?bare` iframe). The shell hosts the frame and streams SSE
data; it does not know what's inside a page.

That covers *what* a page renders. It does **not** cover *where* a page
renders it — some pages are handed bezel geometry. See the next section.

## What's coupled to the current (bezel) layout

Three files, coupling concentrated in `mfd.js`:

- **`src/web/shell/mfd.html`** — hardcodes the bezel structure: four key
  banks (`#keys-top` 4, `#keys-left` 6, `#keys-bottom` 4, `#keys-right`
  6) framing `#screen`.
- **`src/web/shell/mfd.css`** (~56KB) — the metallic bezel look and its
  geometry.
- **`src/web/shell/mfd.js`** (~79KB) — the real coupling:
  - `PAGES` (`mfd.js:79`) declares navigation as `{ label, key, action }`,
    where **`key` is a physical bezel-slot index**. The `label`/`action`
    pair is layout-independent; the `key` slot is bezel-specific.
  - `SPLIT_PAGES` + `SplitKeymap` (`mfd.js:191`, `split-keymap.js`)
    resolve labels to physical bezel keys per split orientation
    (top/bottom vs left/right). This whole mechanism is written around
    bezel-key geometry.

- **Page placement geometry (shell → page).** The exception to "pages are
  decoupled": three pages are handed *bezel geometry*, not just data.
  `forwardAvnLayoutToFrame` / `forwardWpnLayoutToFrame` /
  `forwardTglLayoutToFrame` (`mfd.js:474`, `:687`, `:736`) read the bezel
  key-separator rects (`sepEls`) and post `{avn,wpn,tgl}-layout` messages
  so each page's rows align to the physical key bands:

  ```js
  // mfd.js:736 — forwardTglLayoutToFrame()
  function bot(i) { return sepEls[i].getBoundingClientRect().bottom - frameTop; }
  w.postMessage({ mfd: true, type: 'tgl-layout', layout: 'full', slots: slots }, '*');
  ```

  This is not theoretical. HIDE SHELL already has to keep the `.keys.v`
  columns in the layout — zero-width and invisible — *purely* so those
  separator rects stay valid; drop them and the AVN/WPN full-view geometry
  collapses. That workaround exists today because of this coupling.

  **Escape hatch (already built):** each of those pages also has a
  `compact` profile that needs no bezel geometry at all (written for split
  panes — AVN reverts to compact when `layout !== 'full'`; TGL falls back
  to `fallbackY()`). A non-bezel layout can drive them in compact mode from
  day one rather than inventing a new placement contract. TGT / RWR / TGP
  need no geometry at all (TGT is fully clickable, RWR is a responsive SVG,
  TGP is a video feed).

## The seam

Split `PAGES` into two layers:

1. **Navigation model** — layout-independent data. Per page, an ordered
   list of `{ label, action }`. No `key`, no `side`, no `slot`. This is
   "what a pilot can do from this page," and it's identical on an F-35.
2. **Layout renderer** — swappable. Consumes the navigation model plus
   the active page and split state, and decides *where and how* labels
   render and how the frame looks. The bezel layout maps labels to
   4/6/4/6 physical slots; the F-35 layout renders them along the four
   screen edges as clickable hotspots.

`action` dispatch — what actually happens on click — stays shared. Both
layouts call the same `send-command` handlers; only the label placement
and the frame differ.

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

Note the fourth item: a layout also owns the **page placement geometry**
it feeds pages (the `*-layout` messages). The bezel renderer supplies
key-band rects; a quadrant renderer would supply its own — or just drive
the pages' existing `compact` profile and supply none.

## The honest caveat: a layout is not a stylesheet

The F-35's 4-quadrant screen is a *different layout engine* than the
bezel's H/V splits. The current split logic (`SplitKeymap`, top/bottom
vs left/right resolution) is written around bezel-key geometry and
**will not carry over** — a quadrant layout needs its own split/placement
behavior.

So a layout owns: **frame + label placement + split behavior + page
placement geometry**, sharing only (a) page content and (b) action
dispatch. The abstraction is "a
layout owns the shell and navigation rendering," *not* "a layout is a
skin." Trying to make one parametric shell serve both a physical bezel
and a borderless quadrant grid would be worse than two focused shell
implementations sharing the content and action layers.

## Staged approach

Do **not** build a layout engine first. Prove the seam with a pure
refactor, then add the second layout as a consumer.

### Stage 1 (refactor, zero behavior change)

Extract the navigation model out of `PAGES`: drop `key`/`slot`, keep
`label`/`action`. Route the existing bezel shell through it — the bezel
renderer re-attaches the physical slot mapping. Nothing the user sees
changes. This proves the seam holds before anything is rewritten. If the
extraction turns out ugly, we've learned the coupling is deeper than it
looks, cheaply.

### Stage 2 (second layout)

Add the F-35 layout as a second renderer consuming the same navigation
model: its own frame markup/CSS, its own edge-label placement, its own
quadrant split behavior. Shared: page iframes, SSE, action dispatch.

### Stage 3 (selection)

Let the user pick the active layout. Smallest first: a BepInEx
`ConfigEntry` (we already ship ConfigurationManager) or a `?layout=`
query param on the shell URL. A softkey to switch live is a later nicety,
not a Stage-3 requirement.

## Open questions to settle while implementing (not now)

- Where does split state live once splits differ per layout? Today it's
  bezel-shaped (`SplitKeymap`). The navigation model shouldn't carry
  split geometry; each layout renderer should own its own.
- Do the F-35 edge labels need per-page placement hints (which edge a
  label sits on), or is edge assignment derivable from the ordered list?
  Prefer derivable; add hints only if a page needs them.
- HIDE SHELL, FULL, PIN/SWAP softkeys are function controls, not page
  navigation. Are they part of the navigation model, or layout-owned
  chrome? Leaning layout-owned chrome.
- Asset/CSS split: does each layout get its own CSS bundle, or one file
  gated by a layout class on `<body>`? Two focused files is probably
  cleaner given how different the geometry is.

## Out of scope

- Actually building any of this. Plan-only.
- Changing page content or the SSE/telemetry contract — both are
  layout-independent and stay as-is.
- Removing or restyling the bezel layout. Bezel stays the default.

## Pre-flight before implementing

- Read `src/web/shell/mfd.js` — `PAGES` (`:79`), `SPLIT_PAGES` (`:191`),
  `FRAME_PAGES` (`:61`), and `src/web/shell/split-keymap.js`. These are
  the coupling points Stage 1 has to tease apart.
- Read the geometry forwarders too — `forwardAvnLayoutToFrame` (`:474`),
  `forwardWpnLayoutToFrame` (`:687`), `forwardTglLayoutToFrame` (`:736`).
  They're the coupling the "pages are decoupled" story misses, and they
  decide whether Stage 2 needs a new placement contract or can just use
  the pages' existing `compact` profile.
- Do Stage 1 (navigation-model extraction) and confirm the bezel shell is
  visually unchanged before writing a single line of the F-35 layout.
