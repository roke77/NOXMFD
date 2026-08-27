# MAP cursor — driving target selection from the HOTAS

**Status:** implemented and merged to `main`. Cursor transport, keyboard and HOTAS-axis sources,
contact selection, shared PAD-cursor behavior, hold handling, and MAP edge-panning are built and
covered by the harness/self-checks. A real in-game pass is still needed for MAP's visual rAF glide,
edge-panning, and physical HOTAS-axis input.

## Goal

When a MAP display is the SOI (sensor of interest), let the pilot move a cursor **over the map
itself** and select targets with the HOTAS — the same thing a mouse click or a touch tap already
does, but without touching the screen. Today SOI on a MAP reaches only the bezel keys (the zoom
rocker, FLW, the page keys); it cannot reach the contacts drawn on the canvas. This closes that gap.

New keybinds, on the **KEY** page, under a new **MAP** section — all act only on the focused MAP
display. Two groups:

*The cursor (new machinery):*

- **Cursor Select** — select the contact under the cursor (the click).
- **Cursor Up / Down / Left / Right** — move the cursor.
- two **axis** bindings — horizontal + vertical — as an analog alternative to the four direction
  keys, so a HOTAS mini-stick / hat can slew the cursor proportionally.

*The view controls (HOTAS binds for functions the bezel keys already do):*

- **Follow** — toggle FLW (what the bezel FLW key does).
- **Zoom In / Zoom Out** — what the bezel Z+ / Z− keys do.

**Decisions (locked for this branch):** keys **and** axes ship together (the analog slew is in from
the start), and **Cursor Select is its own bind** — separate from SOI Select — so there is no
context-overloaded key: SOI Select presses bezel keys, Cursor Select picks map contacts.

## What already exists to reuse (read before building)

This is mostly *wiring existing parts together*, not new machinery. `map.js` already owns everything
the selection half needs:

- **Hit-testing** — `hitTargets` (rebuilt every `drawOverlay()`: `{cx, cy, r, label, id, tg}` in
  canvas pixels, post-zoom/pan) is exactly what a cursor hit-tests against, same as the mouse does.
- **The select path** — the `overlay` `click` handler already picks the *nearest not-yet-selected*
  contact within reach, optimistically marks it (`pendingSel`), POSTs `target.select`, and
  flash-confirms (`flashSelect`). A cursor SELECT is that same handler with the cursor's position
  standing in for the click point. **Factor that body into `selectAt(px, py, pad)`** and call it from
  both.
- **A down-channel to the map iframe** — `map.js` already listens for `{ mfd:true, action:… }`
  messages (`toggle-follow`, `zoom-in`, `zoom-out`, `status-request`) that the shells post when a
  map's bezel keys are pressed (`mapSend` / `paneMapSend` in `mfd.js`, `mapSend` in `f35.js`). The
  cursor adds new `action`s on the **same channel** — no new plumbing between shell and map. And the
  **Follow / Zoom In / Zoom Out** binds reuse the `toggle-follow` / `zoom-in` / `zoom-out` actions
  that channel *already* carries — so those three need **no `map.js` change at all**, only a way for a
  bind press to reach the shell's existing `mapSend`.
- **The SOI focus model** — the server already tracks which surface is the SOI and broadcasts it
  every frame (`soiTarget` / `soiPane`), and each shell already knows when one of its surfaces is the
  focused MAP. The cursor input just needs to ride the same frame and be forwarded down only then.

The cursor itself (a crosshair sprite + its position) lives in `map.js`, drawn in `drawOverlay()`
over the contacts — one more thing on the canvas it already manages.

## The movement model — a velocity, integrated locally

The existing SOI action channel (`soiAct` + `soiSeq`, a counter the client acts on when it *changes*)
is built for **discrete** presses — NAV UP steps one key. A cursor needs **continuous** motion, and
an axis is an analog value, so the discrete counter is the wrong transport for movement. Two frames
per second of "nudge" (the frame rate is 10 Hz) would feel awful, and an axis has no "press."

Instead the cursor is driven by a **velocity vector** the client integrates itself:

1. The plugin folds the cursor binds into a vector `(cx, cy)`, each component in `[-1, 1]`:
   held keys give `±1` (or 0), axes give their analog deflection. It broadcasts this in **every**
   frame alongside the other SOI fields (`cursorX`, `cursorY`), plus a **select counter**
   (`cursorSelSeq`, edge, exactly like `soiSeq`) for the discrete Cursor Select press.
2. The shell forwards `(cx, cy)` and the select edge **down to the focused map iframe** — and only
   while the focused surface is a MAP (on any other page the cursor keys are simply inert; see
   *Coexistence* below).
3. `map.js`, while it holds the vector, runs a `requestAnimationFrame` integrator:
   `pos += vec * SPEED * dt`, clamped to the map image rect (`imgRect()`), redrawing the crosshair.
   Because it integrates at the browser's ~60 Hz against a `dt`, motion stays smooth even though the
   vector only refreshes at 10 Hz — the same reason the missile-flash and click-flash already use
   rAF loops. The loop self-stops when the vector is zero and no select is pending (like
   `ensureThreatAnimation`).

Digital keys and analog axes therefore **unify**: both are just a way to set `(cx, cy)`. The map
never knows or cares which one moved it.

`SPEED` is in map-pixels/second; a sensible default lets the cursor cross the canvas in ~1.5 s at
full deflection. `ponytail:` start with a constant; a config entry can come later if anyone wants to
tune it.

### Select

Cursor Select is a discrete edge — reuse the proven counter transport (`cursorSelSeq`, first-seen =
baseline, act only on change, exactly as `telemetry-source.js` already does for `soiSeq`). On the
edge, `map.js` calls `selectAt(cursor.x, cursor.y, CURSOR_HIT_PAD)` — the refactored select body.
Give the cursor a small reach pad (like touch's `TOUCH_HIT_PAD`) so it needn't be pixel-perfect on
an icon. Selection **only ever adds** targets, same rule as the tap: nearest unselected contact in
reach, no-op if none — deselection stays a TGT-page concern.

> `docs/page-cursor.md` later added a second, LIVE signal alongside this edge — `held` on the same
> `cursor` SSE event, sourced from the same bind's continuous (non-edge) press state
> (`Keybinds.Poll()`'s `Active(_cursorSelect, edgeOverride: false)`). MAP still only ever consumes
> the edge above (`cursorSelSeq`) and ignores `held` entirely; it exists for a page that needs to
> tell a tap from a hold, which MAP's plain "select the nearest contact" never does.

## Coexistence with the existing SOI nav

When a MAP is the SOI, the control sets are live together and don't overlap:

- **SOI Nav Up / Down / Select** — walk the **bezel keys** (zoom rocker, FLW, page navigation), as
  today. Unchanged.
- **Cursor Up / Down / Left / Right / Select** — drive the **in-map crosshair** and select contacts.
- **Follow / Zoom In / Zoom Out** — the map **view controls** directly, without walking the bezel to
  the FLW / Z± keys first.

Bezel chrome vs. map contacts vs. view controls — different targets, so the sets are coherent, not
redundant. (Follow/Zoom just give the pilot a direct bind for what SOI-nav-to-the-bezel-key can
already reach the slow way.) All of it is gated on a focused MAP: the crosshair only shows while a MAP
is focused, and the map binds no-op otherwise (`soiPane`/focused-page gating, the same signal that
already scopes the SOI ring).

## New keybinds (plugin, `Keybinds.cs`)

A new section `"MAP Keybinds"` → title **MAP**, with a section note explaining it all acts on the
focused MAP display. All are `DriveFree` (they drive the mod's display, not the aeroplane, so they
work at the main menu like the SOI binds).

| Id | Label | Driven | Effect |
|---|---|---|---|
| `cursor-up` / `cursor-down` | Cursor Up / Down | held | sets the vertical component of the cursor vector |
| `cursor-left` / `cursor-right` | Cursor Left / Right | held | sets the horizontal component |
| `cursor-select` | Cursor Select | edge | bumps `cursorSelSeq` |
| `map-follow` | Follow | edge | toggles FLW on the focused map |
| `map-zoom-in` | Zoom In | edge | zooms the focused map in |
| `map-zoom-out` | Zoom Out | edge | zooms the focused map out |

**Cursor vector — Poll integration.** The four direction binds are a *vector*, not four independent
one-shot actions, so they don't fit the existing `DriveFree()` "run one action" shape cleanly.
`Poll()` assembles the vector from the four binds' `ActiveNow` flags each frame and calls
`TelemetryServer.SetCursorVector(x, y)` once — including pushing `(0, 0)` on the frame the last
direction key releases (note: `Poll()` today early-returns when nothing is active, so the
release-to-idle transition needs a small guard so the final zero still ships). `cursor-select` stays
an ordinary edge `DriveFree` that calls a new `TelemetryServer.CursorSelect()` (bumps the counter,
like `SoiAction`).

**View controls — reuse the discrete action counter.** Follow / Zoom In / Zoom Out are ordinary edge
`DriveFree` binds that call `TelemetryServer.MapAction("follow" | "zoom-in" | "zoom-out")` — the SOI
`SoiAction` pattern exactly: a `mapAct` string + a `mapActSeq` counter the client acts on when it
changes. The shell maps that string straight onto the `mapSend` action it already sends for the
matching bezel key, so no new `map.js` code and no new down-channel message.

**Server (`TelemetryServer.cs`).** Add to the SOI frame state and emit every frame in `SoiJson()`:
`cursorX` / `cursorY` (floats), `cursorSelSeq` (counter), and `mapAct` / `mapActSeq`. Version the
frame cache on their change (as `_soiVersion` already does) so a moving cursor and a view action ship
even during the between-snapshot pings.

## Axis support

Analog axes are the *natural* fit for a cursor — proportional slew, fine control near a target.
Shipping in this branch alongside the keys (decided above). The plugin has no notion of an axis
today; it only reads buttons (`Keybinds.cs` `Active` / `JoyBtn`). What an axis source needs:

- **A new source on `BindDef`** — an axis index + joystick number (reusing the per-bind stick
  pinning) + an **invert** flag (axis polarity is arbitrary across devices). Deadzone can start as a
  shared constant.
- **A capture mode** — arm axis capture, then pick the axis whose deflection grows most past a
  threshold. Must snapshot each axis's **rest position** first (a throttle or slider rests at an
  extreme, not center) and measure deflection *from rest*, mirroring how button capture snapshots
  `_latched` held switches before accepting a press.
- **A live read** — `Joystick.GetAxis(index)` each frame → `[-1, 1]`, deadzoned, inverted, folded
  straight into the cursor vector (so the same `(cx, cy)` the keys would set).
- **`/keybinds` page UI** — the horizontal + vertical cursor rows get an axis-binding affordance
  (arm/clear, an invert toggle) alongside or instead of the button cell, plus the same in
  `serve_web.py`'s mock and `preview-mock.js` so it's verifiable without the game.

Because keys and axes both resolve to the cursor vector, they **coexist**: a bind can carry a key,
an axis, or both, and whichever moves wins that frame (axis when deflected past its deadzone, else
the key). `map.js` and the transport are identical either way — they only ever see `(cx, cy)`.

**Build order within the branch.** Even though both ship together, build the **key path first** (it
reuses the entire existing button infrastructure, so the cursor + select is testable end-to-end in
the harness almost immediately), then add the axis source underneath the same vector. That keeps a
working checkpoint before the larger axis-capture/read/UI change lands.

## Both layouts

The cursor logic lives once in `map.js` (shared by every map surface). Each shell only **forwards**
the vector + select down to its focused map:

- **Bezel (`mfd.js`)** — full-view base map (`mapFrame`), or a map **split pane** (`/map-view?bare`).
  Forward via the existing `mapSend` / `paneMapSend` when the focused surface (`soiPane`) is showing
  MAP.
- **F-35 (`f35.js`)** — the focused **portal** when it's a map portal; forward via its `mapSend`.

No per-layout cursor code — just the same "is my focused surface a MAP? then forward" gate in each.

## Steps

1. **Server + binds — built.** Cursor vector + select counter and the `mapAct`/`mapActSeq`
   view-action pair in `TelemetryServer` (broadcast every frame); the eight `DriveFree` binds + the
   `Poll()` vector-assembly in `Keybinds.cs`; `serve_web.py` + `preview-mock.js` gained the rows and
   hand-drive hooks (`window.__cursorVec`, `__cursorSelect`, `__mapAct`).
2. **map.js cursor — built.** `selectAt(px,py,pad)` factored out of the click handler; crosshair
   state, the rAF integrator, clamp-to-image, and the `cursor-focus`/`cursor`/`cursor-select` message
   handling; the crosshair draws in `drawOverlay()`. Follow/Zoom needed no `map.js` change — they
   reuse the existing `toggle-follow`/`zoom-in`/`zoom-out` handlers.
3. **Shell forwarding — built.** `telemetry-source.js` posts `cursor`/`cursor-select`/`map-act` up
   (gated on `focused`, same idempotent-counter shape as `soi`/`soi-act`); `mfd.js`/`f35.js` forward
   down via `focusedMapWindow()`/`syncCursorFocus()` — the surface that's both SOI-focused AND
   actually showing MAP. Two bugs surfaced and were fixed here:
   - The integrator used the rAF callback's own timestamp; switched to `performance.now()` so it
     doesn't depend on how a given embedder invokes `requestAnimationFrame`.
   - A pane/portal reloading while it holds cursor focus (e.g. entering a split) dropped the
     `cursor-focus` message — sent before the fresh document attached its listener, and a plain
     re-run of the sync wouldn't resend it since the window reference survives the reload. Fixed by
     resending directly on that pane/portal's own `load` event when it's still the eligible target.
4. **Verify in the `serve_web` harness — done, with one caveat.** Confirmed end-to-end: SOI focus
   reaching a full-view MAP shows a centered crosshair; Cursor Select and Follow/Zoom round-trip
   with no console errors; the split-pane reload bug reproduced and the fix confirmed there and in
   an F-35 portal. **Not confirmed here:** the actual rAF glide — this harness's Browser pane
   doesn't composite frames when not displayed, so `requestAnimationFrame` never fires and the
   crosshair never visibly moves in-harness. The integrate/clamp math is covered by
   `tools/map-cursor.test.js` instead. **Needs a real in-game/browser check for the visual motion**
   before calling this done.
5. **Axis source — built.** `BindDef` gained a nullable analog side (`AxisEntry`/`AxisJoyNumEntry`/
   `AxisInvertEntry`) alongside the nullable digital side, so a bind is either digital or axis-only
   (`AddAxis`, no Drive/DriveFree — `Poll()` reads it directly, like the four direction keys).
   `ArmAxisCapture`/`CaptureAxis` mirror the button-capture shape but measure deflection **from a
   snapshotted rest position** rather than an edge — a HOTAS axis is rarely centered at rest, so
   "moved" has to mean "moved from wherever it started," not "moved from zero." `ReadAxis` deadzones
   + inverts + folds straight into the cursor vector; a deflected axis overrides its two keys for
   that component (`Poll()`), so keys and axis coexist on the same two rows (**Cursor Horizontal** /
   **Cursor Vertical**) without conflict. `TelemetryServer.ServeKeybindsConfig` and `CommandDispatcher`
   (`keybind.arm-axis`/`cancel-axis`/`clear-axis`/`set-axis-invert`) extended to match; `keybinds.js`
   renders these two rows as one wide cell (arm/capture/invert/clear) instead of empty key/joy cells,
   guided by the JSON simply omitting `key` for an axis-only row. Verified end-to-end in the
   `serve_web` harness (arm → simulated capture → invert → clear, no console errors) — the server-side
   axis read itself needs a real HOTAS in-game to confirm, same caveat as step 4's rAF glide.

## Open questions / gaps

- **Cursor persistence** — reset to canvas center on focus, or remember its last spot? Lean center-
  on-first-focus (predictable), then leave it where the pilot parks it.
- **Follow/zoom while slewing** — the cursor is a screen-space point; under FLW the map pans beneath
  it each frame. Anchor the crosshair to screen space (simplest, matches a mouse) and let contacts
  slide under it, rather than pinning it to a world point.
- **Speed tuning** — constant to start; a config entry only if asked.
- **`ponytail:` non-trivial logic** — the rAF integrator + clamp and the vector-assembly in `Poll()`
  each get one runnable self-check (e.g. `tools/map-cursor.test.js`: integrate a vector over dt,
  assert clamp-to-rect and that select hit-tests the nearest unselected contact).
