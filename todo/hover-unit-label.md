# Hover-to-Show Unit Label — Investigation & Plan

## Context

On the browser HUD (`src/ClientPage.cs`) the map shows the player and other units
as icons drawn on a `<canvas id="overlay">`. There is currently no way to tell
*what* a contact is — the icon alone carries no name. The goal: when the cursor
hovers over a unit's icon, show its name (e.g. `T/A-30 Compass`) in a small label.

**Verdict: possible, purely client-side.** The contact's type name is already in
the SSE payload (`contacts[].t`, and the player's `name`), so no server / payload
changes are required.

> Note: this plan was refreshed after the map zoom/pan/follow feature landed.
> Those features changed how positions are computed (see below), which actually
> makes hover *easier* — hit-testing now reads already-transformed pixels.

## The core difficulty: icons are canvas pixels, not DOM elements

Because every icon is painted with `oc.drawImage` / `oc.fillRect` inside
`drawOverlay()` (`src/ClientPage.cs:331-372`), there are **no per-icon DOM nodes**
to attach a native `title`/hover to. So hovering requires:

1. **Recording** each icon's on-screen position + size as it is drawn (hit-test data).
2. **Hit-testing** the cursor against that list on `mousemove`.
3. **Rendering** a label for the topmost hit.

## How rendering works today (the pieces this touches)

- `src/ClientPage.cs:253-258` — `worldToOverlay(wx, wz)` returns the final on-screen
  pixel `{cx, cy}` for a world coord. It now runs the base pixel through
  `viewTransform`, so its output is **already post-zoom/pan/follow** — exactly the
  hit-test anchor we need, with no extra transform work.
- `src/ClientPage.cs:311-328` — `drawIcon(type, hex, cx, cy, hdg, orient, basePx, scale)`
  draws at `(cx, cy)`. Image icons are sized `h = basePx*(scale||1)`,
  `w = h*(iconAspect)`; iconless units fall back to a `FALLBACK_SIZE` (5px) square.
  Icon **size is constant in screen px regardless of zoom** (only positions are
  transformed), so the hit radius is a simple screen-space value.
- `src/ClientPage.cs:358-371` — `drawOverlay()` loops contacts first (`:358-366`,
  size `UNIT_BASE`=15 × `u.s`) then the player last (`:368-371`, `ICON_BASE`=15 ×
  `iconScale`), so the player draws on top.
- `src/ClientPage.cs:203-209` — `resizeOverlay()` sizes `#overlay` in CSS pixels
  equal to the panel (no devicePixelRatio scaling), so client coords map 1:1 to
  canvas coords — hit-testing is straightforward.
- `src/ClientPage.cs:576-623` — existing pointer handlers (wheel-zoom, drag-pan,
  dbl-click reset) and a module-level `dragging` flag we can reuse to suppress the
  label mid-drag.

## Recommended approach

### Data available for the label
Only the unit **type** name is in the payload (`UnitInfo.Type` → `contacts[].t`;
player → `lastData.name`). That is the label text. A friendlier display name would
require a server-side payload addition — out of scope; note it as a follow-up.

### Steps

1. **Record hit targets while drawing.** Add a module-level `let hitTargets = [];`.
   Clear it at the top of `drawOverlay()` (after the early `return`s) so it always
   matches what's on screen. The cleanest place to capture the true drawn extent is
   `drawIcon` itself, since only it knows whether an image or the square fallback was
   drawn and at what size: have `drawIcon` **return its half-extent** `r`
   (`max(w,h)/2` for an image, `FALLBACK_SIZE/2` for the square). Then in
   `drawOverlay` push `{ cx, cy, r: r + PAD, label }` for each icon (a few px of
   `PAD` makes small icons easier to hit). Push the player **last** so it is matched
   first. `cx/cy` come straight from the `worldToOverlay` result already used to
   draw — never recompute them.

2. **Hit-test on `mousemove`.** Add a `mousemove` listener on `#map-panel` (or
   `#overlay`). Convert the event to canvas coords via
   `overlay.getBoundingClientRect()`. Iterate `hitTargets` **from last to first**
   (topmost first) and pick the first whose distance to the cursor is `<= r`. If a
   drag is in progress (`dragging === true`), skip hit-testing and hide the label so
   it doesn't flicker while panning.

3. **Render the label.** Use a single reusable DOM tooltip (`#unit-label`),
   absolutely positioned inside `#map-panel`, styled in the HUD idiom (monospace,
   HUD green, `rgba(6,10,6,0.78)` bg, thin `#1a3a1a` border — like `#mission-bar` /
   `#follow-btn`), `pointer-events:none`. On a hit, set its text and position it near
   the cursor (small offset); hide it (`display:none`) when nothing is hit or the
   cursor leaves the panel (`mouseleave`).
   - DOM tooltip chosen over canvas text for crisp text and because it is
     independent of the 10 Hz canvas redraw. Anchoring to the **cursor** (not the
     icon) means it doesn't need repositioning when the icon moves between frames.
   - Alternative (canvas text): store a `hoveredId` and draw the label string in
     `drawOverlay()`; keeps everything on canvas but must redraw on `mousemove`.

4. **Cursor affordance (optional).** The drag code already toggles the panel cursor
   between `grab`/`grabbing`; leave that as-is and don't fight it while a target is
   hovered (hovering doesn't need its own cursor).

## What's challenging / things to get right

1. **Hit data must match the drawn pixels exactly.** Record `cx/cy` from the same
   `worldToOverlay` result passed into `drawIcon`, and take `r` from `drawIcon`'s
   own size math — don't recompute either, or the hotspot drifts from the icon.
   Rebuild the list every `drawOverlay()` so it stays fresh as units move and as the
   view zooms/pans.

2. **Overlapping icons.** Units cluster (airbases, formations). Resolve by picking
   the **topmost** (last-drawn) hit; iterate the list in reverse.

3. **Moving targets vs a static cursor.** At 10 Hz a hovered unit slides out from
   under a stationary cursor (and in follow mode the whole field re-pans each frame).
   Anchoring the tooltip to the cursor and re-hit-testing each `mousemove` keeps
   behaviour predictable; the label simply updates/clears as units pass under the
   cursor. (Contacts have no stable id in the payload — only type+position — so
   cursor-anchored is the pragmatic choice; we can't reliably "stick" to one unit.)

4. **Interaction with zoom/pan/follow (now implemented).** Because hit-testing reads
   `cx/cy` straight from `worldToOverlay` — which is post-transform — hotspots stay
   aligned with icons at any zoom/pan/follow state automatically; no transform math
   in the hover code. The one explicit hook needed: **suppress the label while
   `dragging`** (the flag at `:576`) so it doesn't flicker during a pan.

5. **Canvas vs DOM coordinate edge cases.** Use `getBoundingClientRect()` for the
   conversion (handles the panel's offset). No DPR math needed today since the canvas
   is sized in CSS pixels — but if a future change introduces DPR scaling for sharper
   icons, the hit-test conversion must divide by the scale factor.

6. **Player icon.** Decide whether hovering the player shows its name too (it's
   already named in the HUD's AIRCRAFT panel). Cheap to include; include it for
   consistency unless it feels redundant.

7. **Touch devices.** Hover doesn't exist on touch. Out of scope; a tap-to-label
   could be a later addition.

## Files to modify

- `src/ClientPage.cs` only — add the `#unit-label` element + CSS, a module-level
  `hitTargets` array populated in `drawOverlay()` (with `drawIcon` returning its
  half-extent), and the `mousemove` / `mouseleave` handlers + hit-test helper.

## Verification

Two ways: the in-game build, or the game-free preview
(`python tools/build_preview.py --open`, ideally after a real
`tools/capture_assets.py` so the contacts/icons are real).

- Hover over a contact icon → label with its type name appears near the cursor;
  move off → it disappears.
- Hover over clustered icons → the topmost unit's name shows.
- Move the cursor while units drift (live game) → label updates/clears at 10 Hz.
- Resize the window, then hover → hotspots still line up with icons.
- Zoom in / pan / enable follow, then hover → hotspots still align with icons; a
  drag-to-pan does not leave a stuck or flickering label.
- (Preview note) the static preview doesn't move units, so test "moving target"
  behaviour against the live game.
