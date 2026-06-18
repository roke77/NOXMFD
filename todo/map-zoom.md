# Map Zoom for the Client HUD — Feasibility & Plan

## Context

The browser HUD (`src/ClientPage.cs`) currently renders the map at a single fixed
scale: the game's map sprite is shown in an `<img id="map-img">` with
`object-fit: contain`, and a `<canvas id="overlay">` on top draws the player and
unit icons. There is **no zoom or pan** today. The user wants map zoom comparable
to the in-game map (scroll to zoom, drag to pan, icons stay readable).

**Verdict: yes, this is possible, and it is a purely client-side change.** No
changes to the C# telemetry server, the SSE payload, or `TelemetryReader.cs` are
required — the data needed (`world.x/z`, `map.w/h`) is already streamed, and the
floating-origin correction already happens server-side (`TelemetryReader.cs:251`),
so a zoom is just a view transform layered on top of already-absolute world
coordinates.

## How rendering works today (the pieces zoom must touch)

- `src/ClientPage.cs:49-55` — `#map-img`, the DOM image fitted with `object-fit: contain`.
- `src/ClientPage.cs:67` — `#overlay`, the canvas drawn on top.
- `src/ClientPage.cs:203-212` — `imgRect()`: computes where the contain-fitted
  image actually sits in the canvas (letterbox-aware). This is effectively the
  "zoom = 1, no pan" rectangle.
- `src/ClientPage.cs:217-223` — `worldToOverlay(wx, wz)`: world → normalized [0,1]
  → pixel inside `imgRect()`. Single projection function; every icon goes through it.
- `src/ClientPage.cs:276-293` — `drawIcon(...)`: draws icons at a screen pixel with
  a fixed `basePx` size (`ICON_BASE`/`UNIT_BASE` = 15px).
- `src/ClientPage.cs:296-314` — `drawOverlay()`: clears and redraws all icons.

The key structural fact: **the map lives in the DOM (`<img>`) while the icons live
in the canvas.** They are two coordinate systems kept in sync only because both
derive from `imgRect()`. Zoom has to keep them in sync under an arbitrary
scale+pan transform — that is the crux of the difficulty.

## Recommended approach: draw the map into the canvas

Rather than CSS-transforming the `<img>` and separately transforming the canvas
math (two systems that must agree to sub-pixel precision), **render the map sprite
into the overlay canvas with `oc.drawImage`, using the same transform as the
icons.** Make `#map-img` an off-screen source image (kept for loading/`/map`
refresh, hidden via CSS), and let the canvas be the single source of truth.

This collapses two coordinate systems into one, so map and icons can never drift.

### Steps

1. **View state.** Add `let view = { zoom: 1, panX: 0, panY: 0 }` (pan in screen
   px at zoom=1), plus a `followPlayer` flag (default on, like the game centering
   on you).

2. **Single transform.** Introduce a helper that maps a base `imgRect()` pixel to
   the final on-screen pixel: `screen = (base - focal) * zoom + focal + pan`.
   Route both the map blit and `worldToOverlay()` through it so they share one
   transform. Keep `imgRect()` as the zoom=1 base rectangle.

3. **Blit the map in `drawOverlay()`.** Before drawing icons, `oc.save()`, apply
   the scale/translate transform, `oc.drawImage(mapImg, dx, dy, dw, dh)` using the
   `imgRect()` rectangle, `oc.restore()`. Hide the DOM `#map-img`.

4. **Keep icons constant size.** Transform only icon *positions* (via
   `worldToOverlay`), not `basePx`. Icons stay 15px regardless of zoom — matches
   the in-game map, where terrain scales but unit symbols don't.

5. **Input handlers.**
   - `wheel` on `#map-panel`: zoom toward the cursor (cursor as focal point);
     clamp `zoom` to e.g. `[1, 8]`. Disables `followPlayer`.
   - pointer drag: pan; clamp pan so the map can't be dragged off-screen.
   - optional: double-click or a key to reset to `zoom=1` / re-enable follow.

6. **Clamping.** Clamp pan against the scaled `imgRect()` so empty letterbox isn't
   exposed; clamp `zoom >= 1` so you can't zoom out past the full map.

7. **No payload changes.** `gridLabel()` and the HUD panels use world coords and
   are unaffected.

## What's challenging

1. **Map ↔ icon synchronization (the main risk).** Today they agree only because
   both use `imgRect()`. Under zoom+pan they must agree exactly or icons drift off
   terrain. The drawing-into-canvas approach above eliminates this by construction;
   the CSS-transform-the-`<img>` alternative is simpler to start but fragile —
   `transform-origin` interacts awkwardly with `object-fit: contain`'s letterbox.

2. **Source resolution ceiling.** The map is a fixed-resolution sprite extracted
   from the game (`TelemetryReader.cs:307-323`). Zooming past its native pixel
   density gives a blurry/pixelated image — the in-game map likely uses higher-res
   or vector terrain we don't have. Detail when zoomed in is capped by the sprite;
   this is a genuine limitation, not fully fixable client-side. (Mitigation: cap
   max zoom, or investigate extracting a higher-res map image server-side — a
   larger, separate effort.)

3. **Focal-point / clamping math.** Zoom-toward-cursor and edge-clamping are the
   usual fiddly bits (keeping the point under the cursor fixed while clamping pan).
   Standard but easy to get subtly wrong.

4. **Follow-player vs free-pan interaction.** Deciding when panning breaks "follow
   player" and how/whether to re-center is a UX decision (mirror the game: centered
   by default, free-look on drag, reset to re-center).

5. **`resizeOverlay()` interaction.** Window resize recomputes canvas size and
   `imgRect()`; pan/zoom state must be re-clamped on resize so the view stays valid.

6. **Touch/pinch (optional).** If pinch-zoom is wanted, add pointer-event gesture
   handling — extra but well-trodden.

None of these are blockers. The honest headline: **the feature is straightforward
to wire up; the two real caveats are keeping the DOM image and canvas overlay in
lockstep (solved by drawing the map into the canvas) and the fixed resolution of
the extracted map sprite limiting zoomed-in sharpness.**

## Files to modify

- `src/ClientPage.cs` only — CSS (`#map-img`, `#overlay`), the projection helpers
  (`imgRect`/`worldToOverlay`), `drawOverlay()`, and new input handlers + view state.

## Verification

- Run the mod / server and open `http://localhost:5005`.
- Load a mission; confirm scroll zooms toward the cursor and drag pans.
- Confirm unit/player icons stay pinned to terrain features across zoom levels and
  stay constant pixel size.
- Confirm the grid label and HUD readouts are unchanged.
- Resize the window at a non-default zoom; confirm the view re-clamps without
  exposing letterbox or losing alignment.
