# Layouts — swappable shell/navigation designs

## Status

**All three stages done and merged to `main` (shipped in 0.14.0).** Stage 1 (the
seam), Stage 2 (a second layout), and Stage 3 (remembering the choice) are in. The
bezel remains the default, and every page renders on it exactly as before.

- The **bezel** layout ships: a metallic 4/6/4/6 button frame, served at `/`.
  It has gained one thing from this branch: an **LYT** key on MAIN opening a
  LAYOUT page, so the two layouts can reach each other (see Stage 3).
- The **F-35** layout is a working prototype, served at `/f35`: borderless,
  no keys, labels drawn on the glass, and framed in teal. Every page renders
  on it (MAIN, MAP, AVN, RWR, TGT, TGP, WPN, BDF, PAL, HUD, KEY, RDR) — one
  exception below (AFM). The glass is four independent
  portals, each framed as a whole box, and the corner grips merge adjacent
  ones and split them back — five arrangements, never fewer than two portals.
  A fixed **master strip** runs across the top, carrying the aircraft-level
  chrome the navigation-only MAIN has no room for: the wordmark, the
  connection URLs and status, the mission name and ownship grid, the THRL and
  FUEL gauges, the AVN avionics flags, and the fullscreen toggle.

Both consume one shared navigation model. `NAV` has never been edited to serve
the second layout, and **no page has a layout-specific implementation** — which
was this plan's central claim, and it holds. Two pages now render *differently*
under the F-35, which is a different thing: AVN and MAP each grew a `?nochrome`
option of their own, and the F-35 is a host that asks for it (see
`docs/layouts-f35-build-log.md`'s "Decluttering"). The page decides what its
own option means; the layout only picks.
Nothing under `pages/` knows a layout exists.

AVN and AFM are two separate pages today: AVN dropped its damage silhouette
and failure labels to AFM (see `src-architecture.md`), and moved from FUEL/
THROTTLE bars to circular RPM/FUEL/HEAT/THRL gauges; its status tiles are
bezel-actuated toggles, not just annunciators. F-35 hasn't picked up either
change yet — **AFM has no F-35 entry at all** (unreachable from the F-35's
MAIN), and **AVN's `?nochrome`, which the F-35's master strip sets on every
AVN portal, now blanks the whole panel** rather than leaving a silhouette
behind, since AVN carries no silhouette to leave anymore (see
`docs/layouts-f35-build-log.md`'s "Decluttering"). The bezel's `SCR` MAIN item
is also now named `MD` (Mission Data);
F-35's `MAIN_EXTRAS` still lists `BDF`/`PAL` separately with no equivalent
single entry, so it's inconsistent with the bezel on that point too.

See `docs/layouts-f35-build-log.md` for how the F-35 layout was staged and
built, and the open-questions log from that work.

What the F-35 cost outside its own `shell/f35/` directory, precisely: three
tokens in `theme.css` (`--no-teal` and its rgb source; plus `--no-label`, which
was promoted out of a hardcoded `#d4d8dc` in `mfd.css` when this layout wanted
the same off-white — a one-line change to the bezel's stylesheet, same value);
and one added telemetry slice, `mapinfo`, for chrome that shows no map. Stage 1
is the other half of the branch's diff: extracting `NAV` out of `mfd.js` into
`nav-model.js` moved ~200 lines of the bezel shell, which is the refactor the
seam *is*, not a cost of the second layout.

Stage 3 is now complete: both layouts reach each other live (LYT on MAIN, in
either layout), and the choice sticks across loads via
`localStorage` + a before-paint redirect guard in each shell's `<head>`. A
BepInEx-config default was consciously not built — see Stage 3 for that call.

See [issue #8](https://github.com/roke77/NOXMFD/issues/8) for the F-35
reference screenshots that motivated this.

## Terminology

We call this **layout**, not "theme", on purpose. A theme implies a skin
(colors, textures) over the same structure. What changes here is
structural: the shell frame, where navigation labels live, and how
splits work. Colors are the smallest part of it.

A layout does own its colours — the F-35 frames itself in teal where the bezel
uses off-white — but that turned out to be the cheap half: recolouring the
entire frame was one token and a handful of `var()` swaps, while the portals,
the grips and the arrangement rule are the layout. The naming holds: had this
been a theme, the teal would have been all of it.

## Goal

Let a user pick a different shell/navigation design while every MFD page renders
from one implementation. The page *is* fixed; the *surrounding shell*, the
navigation-label placement, and the split behavior are what a layout owns.

"One implementation" is the claim, not "one appearance" — a page may offer
options and let its host choose between them, which it already did before there
was a second layout: `avn-layout` carries a `full` or `compact` profile, and the
bezel picks `compact` for a split pane. What a layout may never do is fork a
page, or reach inside one.

| Layout            | Frame            | Nav labels                          | Split model            |
|-------------------|------------------|-------------------------------------|------------------------|
| **Bezel** (today) | metallic bezel   | 4/6/4/6 physical keys around screen | H/V bezel-key splits   |
| **F-35**          | borderless       | clickable labels drawn on the page  | 1/2/4 vertical portals |

The F-35's split model is **four side-by-side vertical portals**, not a 2×2
quadrant grid — see the reference screenshots on issue #8. Each portal is
effectively an independent MFD with its own page and its own edge labels.

## What's already decoupled

Page *content* is independent of the shell. Every page — AVN / AFM / MAP / RWR /
TGT / TGP / WPN / BDF / PAL / HUD / KEY / RDR — is its own iframe under
`src/web/pages/*`, served "bare" and mounted into a host frame. The shell hosts
the frame and feeds it data; it does not know what's inside a page. The F-35
layout renders all of them without a single page edit — except AFM, which the
F-35 doesn't reach yet (see Status above).

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
  around bezel-key geometry, and **none of it carried over** to the portal
  model: the F-35's split shares no code with it.
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
(HUD/KEY/LYT/PAL/BDF/RDR — no AFM yet, see Status above) beside
`NAV` and merges the two when rendering. Not all of
them are pages: `KEY` leaves the document for `/keybinds` (`LINKS`) and `LYT`
opens the layout chooser over the whole glass (`GLASS_ACTIONS`), so `canDo`
counts those tables too — an action in none of them renders dimmed.
Consequently ordering is a *rendering* choice: the F-35 sorts its MAIN menu
alphabetically, interleaving those extras among the NAV items, while the bezel
shows `NAV`'s six in their given order (itself alphabetical: AVN, MAP, RWR,
TGP, TGT, WPN).

The same asymmetry applies to placement. `NAV` items may never carry a cell,
but a layout's own items may — WPN's `NEXT` names its top-right cell,
because the shell built it.

## The honest caveat: a layout is not a stylesheet

The F-35's portal screen is a *different layout engine* than the bezel's H/V
splits. The bezel's split logic (`SplitKeymap`, top/bottom vs left/right
resolution) is written around bezel-key geometry and **did not carry over** —
the portal model needed its own split and placement behavior, and got it.

So a layout owns: **frame + label placement + split behavior + page
placement geometry**, sharing only (a) page content and (b) action dispatch.
The abstraction is "a layout owns the shell and navigation rendering," *not*
"a layout is a skin." Trying to make one parametric shell serve both a
physical bezel and a borderless portal grid would be worse than two focused
shell implementations sharing the content and action layers.

Building the second layout supports this. `f35.js` reimplements label
placement, page hosting, split behavior and WPN geometry from scratch, and
shares `NAV`, the pages and `sendCommand` untouched. Nothing in the middle
wanted to be abstracted. The split prediction held exactly: not one line of
`SplitKeymap` or `SPLIT_SLOTS` was reusable, because both resolve labels to
physical keys and the F-35 has none.

## The master strip

A fixed full-width bar across the top of the glass — the home for what belongs
to the aircraft rather than to any one portal, which the navigation-only MAIN
has nowhere to put. The reference cockpit has one.

### Shape

**Two containers, stacked, never overlapping.** `.pcd` is a flex column: the
strip, then `#portals`. The portals are *pushed down* to make room rather than
sliding under the strip, so nothing the strip holds can reach a portal — no
z-order or inset to reason about. `#map-tap` stays `inset: 0` full-size behind
both; it is a data source, never displayed, so its box only drives the map
view's own layout.

- The strip is full width, **one ninth of the glass tall**, at the top.
- The portals take the rest, in their own container (`flex: 1`), and keep their
  widths and arrangement — only their height changes. At 1280×720 they drop
  from 720 to 640 tall.

### Content

Left to right:

- **Wordmark** — `NO XMFD`, styled text (there is no served logo asset yet).
- **Connection block** — the local and LAN URL and the live connection status,
  stacked. The bezel shows these on MAIN; the F-35's MAIN is navigation only,
  so they live here instead.
- **Mission block** — the mission name and the ownship's grid, stacked, each an
  `.mfd-chip` (the theme's boxed readout, the same class the map page's GRID
  chip uses, so the two render identically). Hidden entirely while no mission is
  loaded, as the map page hides its own mission bar. The name is the only string
  in the strip whose length the game decides, so it ellipsises rather than push
  the flags and FULLSCREEN off a bar that clips them.
- **Gauges** — THRL and FUEL, stacked, horizontal, in the slack between the
  mission block and the flags. See below.
- **Avionics flags** — the eight annunciators the AVN page shows
  (GEAR / RADAR / GUNS / ENG / ASSIST / NVG / LIGHTS / TURRET), in one row, each
  a label + icon. Click-to-toggle (issue #35): `data-kind` names the `avn.toggle`
  group 1:1, so a tap here sends the same command a tap on AVN's own tile does
  (2026-08-15 — AVN's tiles became directly clickable too, needed for the F-35
  layout since it has no bezel keys to wire the toggle externally). Both paths
  dispatch identically; neither is the sole way in.
- **FULLSCREEN** — last, at the far right. The only thing in the strip you
  press, so it is drawn as a button — bordered, square and icon-only, the same
  toggle the bezel carries on a top-bank key.

### LYT — the layout chooser

`LYT` sits on MAIN, in `MAIN_EXTRAS`, where the bezel also keeps it: a pilot
finds the same name in the same place in either layout. Being per-portal costs
nothing — every portal offers the same choice and any of them answers for the
whole glass, exactly as the bezel offers LYT in each split pane.

Pressing it swaps the **container**, not the portals' contents: `#portals` and
`#layout-picker` are siblings in the `.pcd` column and take the same slot below
the strip, and the `hidden` attribute is the whole of the state. The portals
keep their pages, their arrangement and their map streams while they wait, so
coming back costs nothing and loses nothing.

Which layout is current needs no state either — this document *is* the F-35, so
its item is marked in the HTML, and CLASSIC is simply somewhere else (`/`).

One thing the swap does owe the portals: hidden, a portal's box is 0×0, and the
shell's resize listener still fires into it — handing WPN a zero-height rect per
row. So restoring the glass reruns `relayoutAll()`. WPN is the only page that
needs it, for the same reason it is the only one that ever needed it: it derives
its geometry from a box instead of reflowing itself with CSS.

### Telemetry — the first chrome that wants it

The strip is the layout's first piece of chrome that needs live data, and it is
not a portal, so it has no `PAGE_FEEDS` entry. The slices it shows — `status`,
`avn` and `mapinfo` — are handed to it straight from the shell's message pump;
the URLs come from `/config` once (the same the bezel's MAIN reads). The flags
reuse `avn-status-policy`, so the GEAR-down-is-red rule stays in one place
shared with AVN, and their glyphs are the AVN page's own — inline SVGs, plus the
game-captured `gear-icon.png` mask for GEAR. `data-kind` / `data-field` on each
flag keep the update loop generic.

`mapinfo` is new, and it is the first slice `TelemetrySource` emits that no page
consumes. Every other one exists because a dedicated MFD page renders it; the
mission name and the ownship grid were the map page's private HUD, derived from
the raw frame and drawn on itself. A strip that shows no map has no way to reach
them, so the tap now derives the pair once more and posts it up. The map page is
untouched and still renders its own from `d` — the alternative, having it emit
what it draws, would have made a page a data source for the shell hosting it.

The stream also carries a `mapName` ("PREVIEW ISLAND") distinct from `mission`.
Nothing renders it, here or on the map page.

### The gauges

THRL and FUEL live in the master strip, sized from the same
`avn-throttle-policy.js` MIL/AB math AVN's own gauge uses — `fuel` and
`throttle` are already in the `avn` telemetry slice, so the strip needs no
separate feed. See `docs/layouts-f35-build-log.md` for how the widget's
specific styling was derived (originally ported from AVN's own vertical-bar
look, since replaced by AVN's circular gauges — the historical comparison
lives there now, not here, since the names it compares against no longer
exist on the AVN page).

### Boot

The connection block boots like the bezel's MAIN info box: a `LOADING…` bar
(ported from `runBootLoading`) fills 0→100% over ~1s, then the URL lines type
out character by character with a blinking caret (a standalone port of
`typewriterUrls`, minus the bezel-only boot-loader coupling). The strip starts
`.booting` from the HTML with the connection block hidden and the URL nodes
empty, so a fully-formed URL never flashes before the animation. The reveal is
gated on *both* the bar finishing and `/config` landing, whichever is last.

### Collapse — deferred

The strip was first planned collapsible, retracting to a thin band with the
portals growing into the space. That is **not built**: it is a fixed bar for
now. Whenever collapse returns, the design question it raised stands — a bar
that vanishes entirely leaves the expand control homeless, and the only place
left for it is over portal 1's `edge` nav labels, where it would eat their
clicks (as TGT's horizontal label once ate `RESET FILTER`). Retracting to a
thin band rather than to nothing is one answer; a control elsewhere is another.

### What it cost the portals

The strip spends glass: the portals are shorter (720→640 at 1280×720). A merged
portal's *shape* — and with it its orientation and WPN's derived rects — comes
from that smaller box, handled by the existing `resized()` path when the glass
is built. Because the bar is fixed there is no per-toggle recompute; the nav
grids need nothing (`edge` is rows of the portal's height, `center` is
`cq`-sized), and the corner grips sit at the portal's own bottom.

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
  `src/web/pages/avn/avn-status-policy.js` — the state→colour rule for the
  avionics flags, an AVN-page module the F-35 strip also loads.
- **Bezel:** `src/web/shell/classic/mfd.{html,css,js}`, `split-keymap.js`. Key
  symbols: `fullViewSlot`, `SPLIT_SLOTS`, `FRAME_PAGES`, `PAGE_URL`,
  `forwardAvnLayoutToFrame`, `forwardWpnLayoutToFrame`, `placeWpnNavLabels`.
- **F-35:** `src/web/shell/f35/f35.{html,css,js}`, plus two pure modules with
  their tests — `f35-glass.js` (the arrangement rule: `gripsFor`, `merge`,
  `split`, `SLOTS`) and `f35-wpn-paging.js`. Key symbols in `f35.js`:
  `makePortal` (everything per-screen lives in its closure), `onGrip`
  (merge/split, and the only thing that changes the glass), `refreshGlass`,
  `buildGlass`, `cells`, `F35_PAGES`, `PAGE_FEEDS`, `FEED_AS`, `DERIVED`,
  `NAV_LAYOUT`, `MAIN_EXTRAS`, `cellOf`, `forwardWpnLayout`,
  `forwardOrientation`. The master strip is the same file: `runStripBoot`
  (the loading bar), `typeStripUrls` (the URL typewriter), `updateStripFlags` /
  `updateStripStatus` (fed from the message pump), and `loadStripUrls`.

  The split is worth knowing: `f35-glass.js` is *policy* (which grips exist,
  what a merge would produce) and `f35.js` is *mechanism* (portals, iframes,
  the DOM). Changing the grip rule touched only the module and its test —
  `f35.js` asks rather than knows.
- **Routes:** `/f35` is served by `TelemetryServer.cs` in-game and by
  `tools/serve_web.py` in the preview harness. Both need the entry.
