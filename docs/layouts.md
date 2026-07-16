# Layouts — swappable shell/navigation designs

## Status

**In progress.** Stage 1 (the seam) and Stage 2 (a second layout) are built
and on `feat/layouts`. The bezel remains the default and is unchanged.

- The **bezel** layout ships: a metallic 4/6/4/6 button frame, served at `/`.
- The **F-35** layout is a working prototype, served at `/f35`: borderless,
  no keys, labels drawn on the glass. Every page renders on it (MAIN, MAP,
  AVN, RWR, TGT, TGP, WPN). The glass is four independent portals, and the
  corner grips merge adjacent ones and split them back — five arrangements,
  never fewer than two portals. A fixed **master strip** runs across the top,
  carrying the aircraft-level chrome the navigation-only MAIN has no room for:
  the wordmark, the connection URLs and status, and the AVN avionics flags.

Both consume one shared navigation model. `NAV` has never been edited to
serve the second layout, and no page has changed — which was this plan's
central claim.

Outstanding: Stage 3, making the layout a user-facing setting rather than a
URL.

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
| **F-35**          | borderless       | clickable labels drawn on the page  | 1/2/4 vertical portals |

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
(HUD/PAL/BDF placeholders) beside `NAV` and merges the two when
rendering. Consequently ordering is a *rendering* choice: the F-35 sorts its
MAIN menu alphabetically (so TGP precedes TGT) while the bezel keeps `NAV`'s
order (TGT, TGP).

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

## Staged approach

Prove the seam with a pure refactor, then add the second layout as a
consumer.

### Stage 1 — the seam ✅ done

Extracted the navigation model out of `PAGES`: dropped `key`/`slot`, kept
`label`/`action`, routed the bezel through it. Zero behavior change, proven
by a data-equivalence check against the old tables before deleting them.

It answered two of the open questions below, and removed a live duplication
bug source: `label`/`action` had been declared twice for five pages.

### Stage 2 — the F-35 layout ✅ done

Built (`src/web/shell/f35/`, served at `/f35`):

- borderless frame, no bezel; labels drawn *on* the page
- two placement modes: `edge` (the bezel's left key bank, minus the bezel)
  and `center` (MAIN's own 3-column block)
- every page hosted, including WPN with layout-supplied rects
- the portal split, driven entirely by the corner grips
- shared action dispatch; `NAV` unmodified

#### Portals

A **portal** is an independent MFD: it owns everything two screens must not
share — which page is up, where its WPN list is paged to, whether its map
follows. The shell keeps only the telemetry cache and the tap.

The glass is **four slots** wide, and a portal fills one slot or two — never
three. So any two *adjacent* portals may merge, and nothing larger. `flex-grow`
carries the span, which is the whole resize arithmetic: with `flex-basis: 0`
every slot is the same width, so growing a portal to 2 gives it exactly two
and its neighbours keep theirs. No wrapper elements, no percentages.

Because a merge joins two and no more, at least two portals always remain:
**the glass is never one screen.** The real PCD isn't either.

##### The arrangement rule

Lives in `f35-glass.js`, pure and pinned by `f35-glass.test.js`. Every
arrangement is some set of adjacent merges that don't overlap, which is
exactly five:

```
1 2 3 4      (1 2) 3 4      1 (2 3) 4      1 2 (3 4)      (1 2) (3 4)
```

Nothing else is reachable: `(1 2)` and `(2 3)` would both want portal 2, and a
triple would need a merged portal to merge again. So a portal beside a merged
one has no grip at all, and simply waits — in `1 (2 3) 4`, portals 1 and 4
both do.

**Layout is not the whole story.** `(1 2)` merged from the left and from the
right occupy the same slots but keep different pages — the survivor is
whoever pressed. So the five layouts cover **six states**, and `ate` (which
side a portal swallowed) is what tells them apart. A split needs it to put the
newcomer back on the side that was eaten; getting that backwards silently
swaps the pilot's screens.

##### The grips

The corner grips are the only control: no URL, no presets. A grip sits in the
corner facing what it acts on, and its direction says what:

- **outward** — take the neighbour on that side.
- **inward** — give back the slot it took, splitting in two again.

An unmerged portal gets **one** grip, and it faces the **centre of the glass**:
portals 1 and 2 reach right, 3 and 4 reach left. Both neighbours of a divider
could offer to merge across it, but the two offers differ only in which page
survives — so one grip per divider costs a *choice*, never a layout, and all
five arrangements stay reachable.

The centre divider is the one place two grips meet, since the portals either
side both face it. That makes `(2 3)` the only merge reachable from either
direction — asymmetric, and deliberate.

The survivor of a merge keeps its page and everything on it, and just gets
wider. The absorbed portal is destroyed — its iframe and any map stream go
with it — and comes back fresh on MAIN.

##### MAP costs a stream here

The tap sits behind every portal, so it could only ever be *shown* to a portal
covering the whole glass, and no portal ever does. Every MAP portal therefore
mounts its own `/map-view?bare` and streams alongside the tap. The bezel pays
exactly the same in split mode, and ignores the duplicate telemetry the same
way (`event.source` must be the canonical map). `FLW`/`Z+`/`Z-` route to the
portal's own map, and `follow` is per-portal — it routes by source, as the
bezel's does.

#### A portal is not the glass

Two bugs, one mistake: something sized against the **viewport** while living
in a **portal**. Both were correct until the portal stopped being the whole
screen, and both only appeared at four.

- MAIN's label grid used a `6vw` column gap — 77px of the *glass* inside a
  320px portal, so seven of ten labels overflowed. The portal is now a CSS
  container (`container-type: size`) and the grid sizes in `cqw`/`cqh`.
  **There are no viewport units left in this layout**, deliberately.
- WPN's weapon image collapsed to a sliver. See "Per-portal orientation"
  below.

The rule for anything added here: measure the portal, never the window.

### Stage 3 — selection 🟡 partial

The F-35 can switch live: the strip's **LYT** replaces the portals with a
two-item chooser (CLASSIC / F-35, the current one marked), and CLASSIC
navigates to `/`. That is one direction only — the bezel has no way back, and
neither shell remembers the choice, so a reload lands on whatever the URL says.

So the layout is still chosen by URL (`/` vs `/f35`), which is enough to
develop against but is not a user-facing setting. What is left: a BepInEx
`ConfigEntry` (we already ship ConfigurationManager) or a `?layout=` query
param that makes `/` serve either shell — at which point LYT's CLASSIC sets
that preference rather than just navigating, and the bezel can grow the
matching control.

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
- **Avionics flags** — the eight annunciators the AVN page shows
  (GEAR / RADAR / GUNS / ENG / ASSIST / NVG / LIGHTS / TURRET), in one row, each
  a label + icon.
- **LYT** — the layout chooser, last, at the far right.

### LYT — why the strip and not MAIN

Choosing a layout is the whole glass's business, and MAIN is per-portal: on the
menu it would have offered the same choice up to four times, each press meaning
the same thing. The strip is the one place on this shell that speaks for the
aircraft rather than for a portal, so it is where the control belongs — and
moving it there took it out of `MAIN_EXTRAS`, where it had been sitting greyed.

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
not a portal, so it has no `PAGE_FEEDS` entry. The two slices it shows —
`status` and `avn` — are handed to it straight from the shell's message pump;
the URLs come from `/config` once (the same the bezel's MAIN reads). The flags
reuse `avn-status-policy`, so the GEAR-down-is-red rule stays in one place
shared with AVN, and their glyphs are the AVN page's own — inline SVGs, plus the
game-captured `gear-icon.png` mask for GEAR. `data-kind` / `data-field` on each
flag keep the update loop generic.

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

## Open questions

### Settled by building it

- **Are labels derivable from the ordered list, or do they need placement
  hints?** Both, split by view. **Full view is derivable** — item *i* → slot
  *i* down the left column, identically for both layouts (`fullViewSlot`,
  `cellOf`). **Split is not** *for the bezel*: MAP deliberately groups its
  zoom rocker on the right, so it needs `SPLIT_SLOTS`. The portal model turned
  out to need no hints at all — a portal is a whole MFD, so it places labels
  exactly as full view does. The problem was the bezel's, not the split's.
- **Are HIDE SHELL / FULL / PIN / SWAP part of the navigation model?** No —
  layout-owned chrome. `nav-model.test.js` now enforces their absence.
- **One CSS bundle or two?** Two. `f35.css` shares no structure with
  `mfd.css`; only the `theme.css` tokens are common (`--no-label` was
  promoted there when both layouts needed the same off-white).
- **Where does split state live once splits differ per layout?** In the
  portal. Everything two screens must not share (current page, WPN paging,
  follow) belongs to the portal; the shell keeps the telemetry cache and the
  tap. The navigation model carries no split geometry, as planned.
- **Per-portal orientation** — confirmed, and now built. A quarter portal is
  320×720: genuinely portrait on a landscape screen. Reporting the window's
  orientation left WPN's 2:1 weapon image unrotated in a tall narrow box,
  collapsed to a ~124×62 stripe. Each portal now measures its own box
  (`forwardOrientation`), and the image turns 90° to fill the column.

  **This is a deliberate divergence from the bezel, not a bug in it.** The
  bezel reports the window's orientation on purpose: its panes are
  wide-and-short, so a pane measuring itself would call a portrait device
  landscape. Portals are the opposite shape and need the opposite rule. Full
  view and halves are unaffected — a portal that *is* the window measures the
  same as the window.
- **Does a portal drive WPN's `compact` or `full` profile?** `full`, with
  rects, at every portal count. Once the portal reports its own orientation,
  `full` renders correctly at 320px wide, so `compact` isn't needed — the
  profile split turned out to be about *shape*, which orientation already
  carries, rather than about size.

### Still open

- **What else the master strip carries.** Connection status and the server
  URLs now live in the strip (see above), alongside the avionics flags — the
  layout's first chrome wanting telemetry, settled. What *else* it should carry
  (warnings, comms, IFF) is still open, as is the deferred collapse.
- **A portal's own page set.** Every portal currently offers all of `NAV`.
  Four portals showing four MAINs is a plausible default but not obviously
  the right one, and the reference shows each portal with a fixed role.
- **Uneven portals.** A portal is one slot wide or two, so the glass only ever
  divides on slot boundaries — no dragging a divider to 30/70. The reference
  suggests fixed roles rather than dragged widths, so this may never be
  wanted; noting it because `SLOTS` and the span are where it would go.
- **Triples.** Deliberately excluded: a merged portal offers no merge, so
  `(1 2 3)` and a full-width portal are unreachable. Allowing them would bring
  back full view, and with it the one case where a portal could show the tap
  instead of running its own map.

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
- **Bezel:** `src/web/shell/mfd.{html,css,js}`, `split-keymap.js`. Key
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
