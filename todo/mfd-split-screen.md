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

## Pane→bezel key mapping (open question)

Splitting the screen vertically halves the available rows for line-
select labels. The bezel keys themselves don't move — they still
flank the full screen height — but the page renderers expect their
labels to land next to specific keys.

Options:

1. **Each pane addresses the full bezel.** Bezel keys 0..5 left and
   0..5 right are claimed by whichever pane is "focused." Player
   selects which pane the bezel drives via PIN/SWAP-like control or
   by clicking inside the pane. Other pane shows its labels in a
   dimmed/static way.
2. **Halve the bezel per pane.** Left keys 0..2 + right keys 0..2 →
   top pane; left keys 3..5 + right keys 3..5 → bottom pane. The
   bezel is physically split too. Most legible at-a-glance; but each
   page's renderer only has 3 left + 3 right slots instead of 6+6,
   which doesn't fit pages like MAIN (6 items) or WPN/TGL (5+ items
   plus PREV/NEXT).
3. **Hide line-select labels in split mode.** Pane navigation moves to
   the bottom bezel row (or to a header inside each pane). The pages
   render content-only.

Decision punted to implementation time; option 1 is the most likely
fit because it preserves the existing page renderers unchanged and
matches how real-aircraft MFDs handle multi-pane modes.

## Toggle behavior (2×1 button)

- First click on 2×1 from single-pane mode: split. The current page
  goes into the top pane. The bottom pane defaults to MAP
  (rationale: the map is the most universally useful "secondary"
  context — same default as the boot screen).
- Click 2×1 again from split mode: collapse back to single pane,
  keeping whichever pane was last interacted with (the "focused" one
  per option 1 above).
- The `data-action` for 2×1 should be `split` or `layout-2x1`.
- The generic icon already has class `ic-2x1` and renders correctly —
  no glyph changes needed.

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
- Bezel key mapping in split mode (option 1 / 2 / 3 above).
- PIN behavior with two panes — one shared pin, or per-pane pins?
- SWAP behavior in split mode.
- Should the bottom pane's default on first entry be MAP, or the
  last non-current page the player visited?
