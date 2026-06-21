# MFD top and bottom generic buttons

## Status

Planning only. The current MFD implementation has six generated generic
line-select buttons on the left and six on the right. This document sketches
the work to add matching generated button banks across the top and bottom of
the screen.

## Current implementation

The MFD shell lives in `src/MfdPage.cs`.

- The bezel is a three-row CSS grid: top strip, middle row, bottom strip.
- The middle row is a three-column grid: left key column, screen, right key
  column.
- The left/right generic keys are generated in JavaScript from:

```js
const COUNTS = { 'keys-left': 6, 'keys-right': 6 };
```

- Each generated side bank uses a separator/key/separator pattern so labels
  and page content can align to the physical key slots.
- The generated side buttons are cached as `leftKeys` and `rightKeys`.
- Page actions are assigned through `dataset.action`, and a single delegated
  click handler on `.mfd` sends all `.key` clicks through `mfdButton()`.
- Static page labels are driven by `PAGES[page].items`, but those items
  currently imply the left side. Dynamic pages such as WPN and TGL directly
  use `leftKeys`, `rightKeys`, `sepEls`, and `rightSepEls`.

## Goal

Add six generated generic buttons to the top strip and six to the bottom strip
using the same model as the left/right banks:

- generated from one count map;
- rendered as physical bezel keys, not overlay-only UI;
- clickable through the existing delegated `.mfd` handler;
- assignable through `dataset.action`;
- able to display visual labels inside the screen overlay aligned to the
  corresponding top or bottom key.

## Layout approach

Use the existing `.strip .center` cells, because they already line up with the
screen column.

- Add `<div class="keys h" id="keys-top"></div>` to the top strip center.
- Add `<div class="keys h" id="keys-bottom"></div>` to the bottom strip center.
- Keep the corner controls as corner controls. If the fullscreen button needs
  to remain in the top strip, either keep it in a small corner cluster or make
  an explicit decision to map fullscreen to one of the new top keys.
- Add horizontal key CSS:
  - `.keys.h` uses row direction and fills the center strip width.
  - `.keys.h .key` uses the rotated side-key proportions, likely `46px x 36px`.
  - `.keys.h .key::before` draws a vertical white tick mark.
  - `.keys.h .sep::before` draws a vertical engraved ridge.
- Keep top/bottom button spacing tied to the screen width rather than the full
  bezel width, so labels and controls feel like part of the same MFD surface.

## JavaScript approach

Generalize the current side-only button handling just enough to support four
banks.

1. Expand the count map:

```js
const COUNTS = {
  'keys-left': 6,
  'keys-right': 6,
  'keys-top': 6,
  'keys-bottom': 6,
};
```

2. Cache all four key banks:

```js
const keyBanks = {
  left: document.querySelectorAll('#keys-left .key'),
  right: document.querySelectorAll('#keys-right .key'),
  top: document.querySelectorAll('#keys-top .key'),
  bottom: document.querySelectorAll('#keys-bottom .key'),
};
```

3. Keep `leftKeys` and `rightKeys` aliases at first so WPN, TGL, and AVN do
   not need to be rewritten as part of the same change.

4. Clear actions across every key bank in `showPage()` instead of only left
   and right.

5. Extend `PAGES[page].items` with an optional `side` field:

```js
{ label: 'MAP', side: 'left', key: 1, action: 'map' }
{ label: 'FLL', side: 'top', key: 5, action: 'fll' }
```

Default `side` to `left` for the current page definitions. This keeps the
first implementation mostly backwards-compatible.

6. Replace the repeated label-placement code with a helper:

```js
function placeOverlayLabel(bank, keyIndex, label, action) { ... }
```

The helper should:

- choose `keyBanks[bank][keyIndex]`;
- set `dataset.action`;
- create an `.overlay-item`;
- apply a bank class such as `.overlay-item.left`, `.right`, `.top`, or
  `.bottom`;
- position the label from the key's `getBoundingClientRect()` relative to
  `overlayEl`.

## Overlay label behavior

Left and right labels already sit inside the screen near the side they control.
Top and bottom should follow the same idea:

- top labels are centered horizontally under their physical top key and placed
  near the top edge of the screen overlay;
- bottom labels are centered horizontally above their physical bottom key and
  placed near the bottom edge of the screen overlay;
- top/bottom labels need `white-space: nowrap` and centered transforms so
  short labels like `FLL`, `NAV`, or `SYS` do not shift visually;
- if longer labels are introduced later, clamp or shrink them rather than
  allowing overlap with adjacent labels.

Suggested CSS classes:

```css
.overlay-item.left { left: 16px; transform: translateY(-50%); }
.overlay-item.right { right: 16px; transform: translateY(-50%); }
.overlay-item.top { transform: translate(-50%, 0); }
.overlay-item.bottom { transform: translate(-50%, -100%); }
```

## Dynamic page considerations

WPN, TGL, and AVN currently depend on side separators for content layout.
Those should remain side-based in the first pass.

- `renderWpn()` should continue to use left key slots for weapon rows and
  right key 0 for NEXT.
- `renderTgl()` should continue to use left/right key slots for targets.
- `renderAvn()` should continue to align the aircraft name and frame against
  the left-side key geometry.
- Top/bottom keys can initially be no-op unless a page explicitly assigns
  actions to them.

After the generic support is in place, future page-specific work can decide
which commands belong on top/bottom without forcing a layout refactor.

## Implementation checklist

1. Add `#keys-top` and `#keys-bottom` containers to the top and bottom strip
   center cells.
2. Add horizontal `.keys.h` CSS for layout, button size, tick marks, and
   separator ridges.
3. Extend the generated key count map to include top and bottom.
4. Introduce `keyBanks` plus compatibility aliases for `leftKeys` and
   `rightKeys`.
5. Clear `dataset.action` across every bank in `showPage()`.
6. Add a reusable overlay-label placement helper.
7. Update static `PAGES` item rendering to support `side`, defaulting to
   left.
8. Optionally move fullscreen from its current standalone top-center button to
   a top generic key, but only after deciding the desired physical mapping.
9. Build the preview with `tools/build_preview.py --mfd`.
10. Open the preview and verify desktop and mobile/portrait layouts:
    - 6 left keys, 6 right keys, 6 top keys, 6 bottom keys;
    - no overlap with corner controls;
    - overlay labels align with their assigned keys;
    - existing MAIN, MAP, WPN, TGL, TGP, AVN, and fullscreen behavior still
      work.

## Acceptance criteria

- Top and bottom generic key banks are generated by the same JavaScript path as
  the side banks.
- All four banks can receive `dataset.action` and use the existing click
  handler.
- Existing left/right page behavior is unchanged.
- The top and bottom keys visually align with the screen, not merely the outer
  bezel.
- The implementation is small enough to keep `MfdPage.cs` maintainable until
  the planned React client split happens.
