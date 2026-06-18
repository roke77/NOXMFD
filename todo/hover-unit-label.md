# Hover-to-Show Unit Label — Investigation & Plan

## Context

On the browser HUD (`src/ClientPage.cs`) the map shows the player and other units
as icons drawn on a `<canvas id="overlay">`. There is currently no way to tell
*what* a contact is — the icon alone carries no name. The goal: when the cursor
hovers over a unit's icon, show its name (e.g. `F-16C`) in a small label.

**Verdict: possible, purely client-side.** The contact's type name is already in
the SSE payload (`contacts[].t`, and the player's `name`), so no server / payload
changes are required.

## The core difficulty: icons are canvas pixels, not DOM elements

Because every icon is painted with `oc.drawImage` / `oc.fillRect` inside
`drawOverlay()` (`src/ClientPage.cs:296-314`), there are **no per-icon DOM nodes**
to attach a native `title`/hover to. So hovering requires:

1. **Recording** each icon's on-screen position + size as it is drawn (hit-test data).
2. **Hit-testing** the cursor against that list on `mousemove`.
3. **Rendering** a label for the topmost hit.

## How rendering works today (the pieces this touches)

- `src/ClientPage.cs:217-223` — `worldToOverlay(wx, wz)` returns the final screen
  pixel `{cx, cy}` for a world coord. This is exactly the hit-test anchor we need.
- `src/ClientPage.cs:276-293` — `drawIcon(...)` draws at `(cx, cy)` with size
  `basePx * scale` (image) or `FALLBACK_SIZE` (square). The drawn extent gives the
  hit radius.
- `src/ClientPage.cs:296-314` — `drawOverlay()` loops contacts (drawn first) then
  the player (drawn last = visually on top).
- `src/ClientPage.cs:67` — `#overlay` canvas is `position:absolute; top/left:0;
  width/height:100%`, sized in CSS pixels equal to the panel (no devicePixelRatio
  scaling, `resizeOverlay()` at `:195-200`), so client coords map 1:1 to canvas
  coords — hit-testing is straightforward.

## Recommended approach

### Data available for the label
Only the unit **type** name is in the payload (`UnitInfo.Type` → `contacts[].t`;
player → `lastData.name`). That is the label text. A friendlier display name would
require a server-side payload addition — out of scope; note it as a follow-up.

### Steps

1. **Build a hit-test list while drawing.** In `drawOverlay()`, push an entry for
   each icon as it is drawn:
   `hitTargets.push({ cx, cy, r, label })` where `r` is the icon's half-extent plus
   a few px of padding (`max(w,h)/2 + pad` for image icons, `FALLBACK_SIZE/2 + pad`
   for the square). Clear the array at the top of each `drawOverlay()` so it always
   matches what's on screen. Push the player last so it can be matched first.

2. **Hit-test on `mousemove`.** Add a `mousemove` listener on `#map-panel` (or the
   overlay). Convert the event to canvas coords via `overlay.getBoundingClientRect()`.
   Iterate `hitTargets` **from last to first** (topmost first) and pick the first
   whose distance to the cursor is `<= r`. Track the current hovered target.

3. **Render the label.** Recommended: a single reusable DOM tooltip element
   (`#unit-label`), absolutely positioned inside `#map-panel`, styled in the HUD
   idiom (monospace, HUD green, `rgba(6,10,6,0.78)` bg, thin `#1a3a1a` border, like
   `#mission-bar` at `:69-80`), `pointer-events:none`. On hover, set its text and
   position it near the cursor (small offset); hide it (`display:none`) when nothing
   is hit or the cursor leaves the panel (`mouseleave`).
   - DOM tooltip chosen over canvas text for crisp text and because it is
     independent of the 10 Hz canvas redraw. Anchoring to the **cursor** (not the
     icon) means it doesn't need repositioning when the icon moves between frames.
   - Alternative (canvas text): store a `hoveredId` and draw the label string in
     `drawOverlay()`; keeps everything on canvas but must redraw on `mousemove`.

4. **Cursor affordance (optional).** Set `#map-panel` cursor to `pointer` while a
   target is hovered, `default` otherwise.

## What's challenging / things to get right

1. **Hit data must match the drawn pixels exactly.** Record `cx/cy/r` from the same
   values passed into `drawIcon`, not recomputed — otherwise the hotspot drifts from
   the icon. Rebuild the list every `drawOverlay()` so it stays fresh as units move.

2. **Overlapping icons.** Units cluster (airbases, formations). Resolve by picking
   the **topmost** (last-drawn) hit; iterate the list in reverse.

3. **Moving targets vs a static cursor.** At 10 Hz a hovered unit slides out from
   under a stationary cursor. Anchoring the tooltip to the cursor (not the icon) and
   re-hit-testing each `mousemove` keeps behavior predictable; the label simply
   updates/clears as units pass under the cursor. (If we instead want the label to
   "stick" to a unit, we'd key on identity and re-find it each frame — but contacts
   have no stable id in the payload, only type+position, so cursor-anchored is the
   pragmatic choice.)

4. **Interaction with the planned map zoom (`todo/map-zoom.md`).** Both features add
   pointer handlers to `#map-panel` and both depend on final on-screen icon
   positions. Because hit-testing reads `cx/cy` straight from `worldToOverlay`
   output (post-transform once zoom lands), it stays correct under zoom/pan. Order
   of implementation doesn't matter, but when zoom adds drag-to-pan, ensure a drag
   gesture suppresses the hover label so it doesn't flicker mid-drag.

5. **Canvas vs DOM coordinate edge cases.** Use `getBoundingClientRect()` for the
   conversion (handles the panel's offset). No DPR math needed today since the
   canvas is sized in CSS pixels — but if a future change introduces DPR scaling for
   sharper icons, the hit-test conversion must divide by the scale factor.

6. **Player icon.** Decide whether hovering the player shows its name too (it's
   already named in the HUD's AIRCRAFT panel). Cheap to include; include it for
   consistency unless it feels redundant.

7. **Touch devices.** Hover doesn't exist on touch. Out of scope; a tap-to-label
   could be a later addition.

## Files to modify

- `src/ClientPage.cs` only — add the `#unit-label` element + CSS, populate a
  `hitTargets` array in `drawOverlay()` (and `drawIcon` callers), and add the
  `mousemove` / `mouseleave` handlers + hit-test helper.

## Verification

- Run the mod / server and open `http://localhost:5005`; load a mission.
- Hover over a contact icon → label with its type name appears near the cursor;
  move off → it disappears.
- Hover over clustered icons → the topmost unit's name shows.
- Move the cursor while units drift → label updates/clears correctly at 10 Hz.
- Resize the window, then hover → hotspots still line up with icons.
- (After zoom lands) hover at a non-default zoom → hotspots still align; dragging to
  pan does not leave a stuck label.
