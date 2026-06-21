# MFD — 2×1 split-screen layout (planning)

## Status

Planning only. No code yet. The 2×1 bezel icon is rendered today but
has no `data-action` wired, so clicking it is a no-op.

## Goal

Let the player run two MFD component pages at once, stacked vertically
inside the same screen recess: one page on top, another on the bottom,
separated by a 3px white horizontal divider. The split is toggled by
the existing 2×1 button at the bottom of the bezel (currently the 4th
button, `bottomIcons[3]`, class `ic-2x1`).

Both halves are fully live: switching a half to MAP renders an active
map there, switching it to AVN renders the live damage silhouette, and
so on for TGP / TGL / WPN / MAIN.

The bezel itself (left/right/top/bottom button banks, the indicator
stack at the top-right, the PIN/SWAP/2×1 generic controls) is **NOT**
split — it stays at the shell level and keeps its current behavior.

## Current architecture (what we'd be modifying)

The MFD shell today renders only ONE page at a time, in a hybrid way:

- `.screen` contains a single `<iframe src="/map-view?bare">` for the
  MAP page (lives as a real iframe so the map's interactive canvas,
  follow logic, SSE stream and postMessage broadcasts work in isolation).
- `.screen > .overlay` contains all the other pages as stacked
  absolutely-positioned `<div>` panels (`.tgp-panel`, `.wpn-panel`,
  `.tgl-panel`, `.avn-panel`, `.info-box` for MAIN). `showPage(name)`
  toggles their `.show` class and hides everything else.
- Per-page line-select labels are positioned by JS into the same
  overlay against the physical bezel keys.

Important consequence: the user's request says "any MFD component page
iframe can be rendered on each part," but **most pages are not iframes
today** — only MAP is. The split-screen feature has to pick an
implementation strategy that handles this.

## Two implementation strategies

### Strategy A — All pages become iframes

Refactor each page to have its own URL (`/map-view?bare`,
`/avn?bare`, `/tgl?bare`, …) and render it via `<iframe>`. The
split-screen then becomes "two iframes stacked vertically."

- **Pros**
  - Symmetric and conceptually clean. The split layout is just two
    iframes.
  - Each pane is fully isolated — independent scripts, independent
    state, no cross-pane DOM interference.
  - The MAP page already works this way; extending the pattern
    completes the model.
- **Cons**
  - Large refactor. Every page (`MfdPage.cs` panels + their renderers,
    label placement, message routing) has to be split out into its own
    holder file with its own served URL.
  - The MFD↔page postMessage protocol needs to fan out: today the map
    iframe broadcasts to its `window.parent` (the shell). With four
    iframes, broadcasts have to be routed to the right pane, and the
    shell→pane "action" messages (pin/swap/follow) become per-pane.
  - Per-page key-label placement currently reaches into the same
    overlay DOM as the shell. Moving each page into its own iframe
    means labels render INSIDE each iframe — that probably looks fine
    (overlay-item.left/right/top/bottom CSS still works), but the
    label-to-bezel-key alignment math currently reads bezel key rects
    from the shell DOM and has to be replaced with size-aware
    relative coordinates or postMessage-driven coordinates.
  - Static assets (the AVN silhouette layout, the WPN icons, the TGP
    MJPEG) get fetched per iframe instance. Cheap on a single instance,
    but if both panes show the same page it's redundant. We can dedupe
    via HTTP caching headers — the server already serves these with
    URL-keyed caches.

### Strategy B — DOM-only split inside the existing overlay

Keep the current panel model. Add a CSS layer that, when split mode is
on, halves each panel's vertical space and stacks two panel sets in
the screen. The shell tracks a second `currentPage` (per pane) and
runs the existing render path twice — once per pane — into the right
half of the overlay.

- **Pros**
  - No new served URLs. The shell stays as one document.
  - Per-pane state lives next to the existing single-pane state
    (`currentPage` becomes `pages: ['avn', 'wpn']` or similar).
  - No cross-iframe message routing complexity for shell↔page
    communication on the non-MAP pages.
- **Cons**
  - The MAP page is still an iframe (and there's no good reason to
    de-iframe it — it owns a heavy canvas + SSE stream). So MAP
    becomes a special case: showing MAP in one pane requires
    instantiating a second `<iframe src="/map-view?bare">` for that
    pane.
  - Two MAP iframes means two SSE connections to `/stream`. The server
    side has to be OK with that (it broadcasts to all listeners today,
    so this should already work — but worth confirming).
  - Per-page renderers (`renderAvn`, `renderWpn`, `renderTgl`, …) all
    read/write a single set of DOM nodes today. They have to be
    parameterised by "which pane's DOM" so the same renderer can run
    twice into different DOM trees.
  - Label placement against bezel keys was designed for one screen
    half. The renderers that place per-page labels against key rects
    (`placeOverlayLabel`, `renderWpn`/`renderTgl` PREV/NEXT, AVN name
    against key[0]) need to be redefined for split mode: which bezel
    keys map to which pane, and where labels land vertically.

**Recommendation:** Strategy A. The refactor is bigger up-front but
the resulting model is straightforward (two iframes inside a flex
column), and it's the cleaner foundation for any future "2×2" or
"swap panes" enhancements. Strategy B accumulates per-renderer
duplication and special-casing.

## Visual layout

```
┌──────────────────────────────────────────┐
│ ░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░  │  ← top bezel keys (shell)
├───┬──────────────────────────────┬───────┤
│ ▒ │ ┌──────────────────────────┐ │ ▒     │  ← left + right bezel keys
│ ▒ │ │      TOP PANE  (iframe)  │ │ ▒     │
│ ▒ │ │      e.g. /avn?bare      │ │ ▒     │
│ ▒ │ ├══════════════════════════┤ │ ▒     │  ← 3px white divider
│ ▒ │ │      BOT PANE  (iframe)  │ │ ▒     │
│ ▒ │ │      e.g. /map-view?bare │ │ ▒     │
│ ▒ │ └──────────────────────────┘ │ ▒     │
├───┴──────────────────────────────┴───────┤
│ ░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░  │  ← bottom bezel keys (shell)
└──────────────────────────────────────────┘
```

- The 3px white divider spans the FULL width of the recess. Pure
  `#ffffff` (or near-white off the existing CSS palette — `#d4d8dc` is
  what `.overlay-item` uses, but the divider is heavier and probably
  wants full white for clear separation).
- Both panes share the existing `.screen` background and the recessed
  `box-shadow` look. The divider lives **between** them, not on top.

## Pane→bezel key mapping (resolved)

The bezel is physically halved per pane: each pane is driven by the
6 line-select keys that flank it. Both vertical key columns
(left and right) each have 6 keys; on split, the top 3 of each
column drive the top pane (6 keys total) and the bottom 3 of each
column drive the bottom pane (6 keys total).

```
left col       right col
─────────      ─────────
L0  ──┐        ┌──  R0     ┐
L1  ──┤  TOP   ├──  R1     │  top pane: 6 slots
L2  ──┘  PANE  └──  R2     ┘
       ── DIVIDER ──
L3  ──┐        ┌──  R3     ┐
L4  ──┤  BOT   ├──  R4     │  bottom pane: 6 slots
L5  ──┘  PANE  └──  R5     ┘
```

Consequence: every page must define a **split-mode layout** that
fits within 6 slots (vs the 12 available in single-pane mode). The
single-pane layout is unchanged; the split-mode layout is a new,
explicit configuration the page renderer reads when its pane is in
split context. We'll work out the split-mode layout for each page
together — see the interview section below.

The bottom-bezel and top-bezel rows (generic buttons including
PIN/SWAP/2×1) are NOT split — they stay at the shell level and keep
their current behavior.

## Toggle behavior (2×1 button)

- First click on 2×1 from single-pane mode (entering split):
  - **Top pane** = whatever page was being displayed at the moment
    2×1 was clicked.
  - **Bottom pane** = the currently PINNED page.
  - **Bottom pane fallback** = if no page is pinned, render `MAIN`.
- Click 2×1 again from split mode: collapse back to single pane,
  keeping whichever pane was last interacted with (the "focused" one
  per option 1 above).
- The `data-action` for 2×1 should be `split` or `layout-2x1`.
- The generic icon already has class `ic-2x1` and renders correctly —
  no glyph changes needed.

### Edge cases for the entry rule

- **Current page IS the pinned page** (top == bottom would be a
  duplicate): same rules still apply, but bottom falls through to
  `MAIN` since rendering the same page twice on entry is unhelpful.
  After entering split, the player can manually move either pane to
  any page via the bezel — duplicates ARE allowed at that point
  (Strategy A makes that work for free).
- **Entering split from MAIN**: top is `MAIN`, bottom is pinned (or
  `MAIN` again with the duplicate fallback above → in that combo
  case, default bottom to `MAP` since MAIN+MAIN with no pin is the
  only state where neither rule gives us anything useful).

## Implementation sequence

To keep each step small and verifiable, the work is staged:

1. **Wire the `split` action.** Give `bottomIcons[3]` (the 2×1 icon)
   a `data-action="split"` and add a `case 'split'` to the bezel
   click dispatcher in `MfdPage.cs`. No layout yet — just toggle a
   shell-level `splitMode` flag and log it.
2. **Implement the split layout.** Update `.screen` to render two
   stacked panes with a 3px white horizontal divider between them
   when `splitMode` is on. Use Strategy A: each pane is its own
   `<iframe>`. Single-pane behavior is preserved when `splitMode` is
   off — the existing `<iframe src="/map-view?bare">` becomes the
   single-pane case of the same iframe shell.
3. **Seed both panes with MAIN.** As a dev checkpoint, on split
   entry render the MAIN page in BOTH the top and bottom panes,
   regardless of the eventual entry rule. This proves the split
   layout works and surfaces every constraint the 6-slot bezel
   imposes on the MAIN renderer.
4. **Remap MAIN for split mode** (first interview entry below).
   Once MAIN renders cleanly in 6 slots, this becomes our reference
   for how per-page remap is structured.
5. **Apply the real entry rule.** Replace the "MAIN on both" seed
   with the actual rule from the Toggle-behavior section: top =
   current page, bottom = pinned page (or `MAIN` fallback).
6. **Remap each remaining page** in the order defined by the
   interview section. Ship each one as a small, independent change.
7. **Collapse-back semantics + indicator chips.** Carry the focused
   pane's page back to single mode; resolve PIN/SWAP/FOLLOW in
   split mode per the section below.

## Per-page line-select remap (interview)

Each page's single-pane line-select layout is defined in `PAGES`
(in `MfdPage.cs`) plus, for the dynamic pages (WPN/TGL/AVN), in
their `render*` functions. We need a parallel **split-mode layout**
per page that fits within the 6 slots the pane controls (3 left +
3 right, indexed `L0..L2` and `R0..R2` against the pane's bezel
half).

The remap is decided page by page as an interview with the user.
Each page gets a short discussion covering:

- Which actions on the page can stay (highest-value navigation +
  controls).
- Which actions get dropped, hidden, or moved behind a
  page-internal control.
- How PREV/NEXT (for WPN/TGL) is preserved with fewer slots.
- Whether the page needs to grow a new "more" / overflow control.

### Interview order

The order is implementation order — earlier entries unblock later
entries (MAIN unblocks all navigation across the split panes; AVN's
remap will inform similar layouts for static-content pages).

1. **MAIN** (6 items today: AVN, MAP, RWR, TGL, TGP, WPN — slots
   L0..L5). Perfect candidate to start: it's the navigation hub
   and exactly fills 6 single-pane slots, so the question becomes
   how to fit 6 items into the new 3L + 3R split layout. Resolved
   here first because both panes need MAIN to be navigable in
   split mode for any later remap to be reachable.
2. **MAP** (4 items today: MAIN, FLW, Z+, Z−). Fewest items —
   should fit naturally. Confirm slot assignment and whether
   FOLLOW chip placement needs to change inside a pane.
3. **AVN** (1 item today: MAIN). Trivial in terms of items, but
   the silhouette + side bars need to render correctly at half
   the vertical space — a layout-only conversation.
4. **TGP** (1 item today: MAIN). MJPEG viewport at half vertical
   space — confirm aspect-ratio behavior and NO LOCK placement.
5. **TGL** (dynamic; 5 entries per page + PREV/NEXT/MAIN). The
   single-pane layout uses 10 slots (5 left + 5 right) for entries
   plus side keys for paging. Split mode forces fewer entries per
   page or a different pagination scheme.
6. **WPN** (dynamic; 5 entries per page + PREV/NEXT/MAIN +
   countermeasures panel). Same constraint as TGL plus the CM
   panel sizing question.

### Interview entry template

For each page, when we get to it, we'll fill out:

- **Single-pane items today**: list, with their current
  key/side/action.
- **Split-mode items kept**: list, with new
  key/side/action.
- **Items dropped or moved**: list, with rationale.
- **Page-internal UI changes**: layout tweaks needed to fit half
  the vertical space.
- **Notes / open follow-ups**.

We append a filled-in entry here as each interview completes.

### MAIN — resolved

**Single-pane items today** (6 items, all on left bezel):

| Slot | Label | Action |
|------|-------|--------|
| L0   | AVN   | navigate → AVN |
| L1   | MAP   | navigate → MAP |
| L2   | RWR   | navigate → RWR (stub — page not built yet) |
| L3   | TGL   | navigate → TGL |
| L4   | TGP   | navigate → TGP |
| L5   | WPN   | navigate → WPN |

**Split-mode items kept** (6 items, 3 left + 3 right per pane —
mirrors the single-pane order, split in half):

| Slot | Label | Action |
|------|-------|--------|
| L0   | AVN   | navigate this pane → AVN |
| L1   | MAP   | navigate this pane → MAP |
| L2   | RWR   | navigate this pane → RWR (stub) |
| R0   | TGL   | navigate this pane → TGL |
| R1   | TGP   | navigate this pane → TGP |
| R2   | WPN   | navigate this pane → WPN |

**Navigation scope:** clicking a destination on MAIN-in-a-pane
navigates ONLY that pane to the destination. The other pane is
untouched. So both panes have independent "what page am I showing"
state, and MAIN is each pane's local navigation hub.

**Items dropped or moved:** none. All 6 destinations carry over.
RWR remains as a stub (matching single-pane behavior) — it'll be
fleshed out as its own future work and the slot is reserved for it.

**Page-internal UI changes:**

- The `.info-box` card (the "NO ROKS MFD" / URL / connection-status
  card) stays centered in the pane in split mode, but shrinks to
  fit the half-height vertical space. Keep the existing portrait
  scaling logic; layer the split-mode case on top of it so the
  card scales down further when its pane is half the screen's
  height.
- Empty / unconnected-state visuals (the disconnected status
  pill) remain visible — the card is the primary signal of MFD
  health and shouldn't disappear in split mode.

**Notes / open follow-ups:**

- The renderer will need to know which pane it's drawing into so
  `placeOverlayLabel` resolves the right physical bezel key (L0
  in the top pane = the actual L0 key; L0 in the bottom pane =
  the actual L3 key). The pane→physical-key offset is the cleanest
  way to keep the per-page item lists (`L0..L2 / R0..R2`)
  pane-agnostic.
- Confirm during implementation that the info-box's shrunken
  layout still reads at the smallest expected pane size (portrait
  viewport split in half is the tightest case).

## Interaction with PIN / SWAP / indicator chips

- The PIN chip is per-page today. In split mode it should track the
  focused pane's page (or render once per pane if both pages are
  pinned — open design question).
- SWAP semantics in split mode: probably swap the TWO panes (top↔bot)
  rather than the focused pane↔pinned page. The current single-pane
  meaning of SWAP no longer applies cleanly. This needs its own pass
  during implementation.
- The FOLLOW chip is MAP-specific. In split mode it should appear
  attached to whichever pane is showing MAP — if neither pane is MAP,
  no chip; if both panes are MAP, the chip applies to the focused one.

## Non-goals

- 2×2 / 4-quadrant layout (the `ic-square` and `ic-split` bezel icons
  hint at future modes; out of scope here).
- Per-pane independent themes/colours.
- Resizable divider — the split is exactly 50/50.
- Swapping panes via drag.

## Testing checklist (for when we implement)

- Toggle from each starting page (MAIN/MAP/AVN/TGP/TGL/WPN) and
  confirm split layout renders cleanly.
- Both panes showing MAP simultaneously: both render live, both react
  to FOLLOW, SSE stream still routes correctly.
- Both panes showing the same non-MAP page: independent renderers
  don't trample each other's DOM.
- Resize the window during split: divider stays 3px and centered.
- Portrait viewport: confirm split still reads as "top + bottom"
  (it should — the split axis is vertical relative to the screen
  recess, not absolute screen orientation).
- Collapse back: focused pane's page is preserved; PIN / SWAP /
  FOLLOW indicators carry over correctly.
- The bezel generic buttons (PIN / SWAP / 2×1 / others) keep their
  shell-level behavior — they aren't intercepted by either pane.

## Open questions

- Strategy A vs B (recommend A, but worth one more pass before
  starting the refactor).
- PIN behavior with two panes — one shared pin, or per-pane pins?
- SWAP behavior in split mode.
