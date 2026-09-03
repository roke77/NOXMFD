# PAD cursor for TGT/HUD — reusing MAP's crosshair on DOM pages

**Status:** implemented on `main` and `serve_web`-harness verified. The later manual-TGP work
exercises the shared PAD-cursor transport in game; MAP-specific visual glide/edge-panning and the
TGT/HUD page behavior still need their dedicated in-game checks below.

## Goal

MAP already has a HOTAS-drivable crosshair (`docs/map-cursor.md`, "the PAD cursor" from here on):
a velocity vector fed by held keys or an analog axis, integrated locally at ~60 Hz, plus a discrete
Select edge — all only live while that MAP is the SOI's focused surface. This extends the **same
mechanism** — same transport, same feel, same binds — to TGT and HUD, whose controls are ordinary
DOM elements inside a page iframe rather than canvas-drawn contacts. **Not** a discrete NAV-UP/DOWN
list-stepper (that was the wrong shape — rejected; this doc replaces that draft entirely).

**BDF/PAL stays out of scope** — `bdf.js` is read-only, no click handlers at all (confirmed by
reading it), so there is nothing for a cursor to hit-test against yet. Revisit only if/when BDF
grows an interactive control.

## What MAP already proves out (`map.js`, `docs/map-cursor.md`)

Everything on the transport side is generic and needs **no change** — it doesn't know or care that
it happens to be feeding a canvas today:

- **Plugin → server**: `Keybinds.cs`'s four cursor direction binds assemble a vector each `Poll()`,
  `cursor-select` is an ordinary edge bind. `TelemetryServer` broadcasts `cursorX`/`cursorY` +
  `cursorSelSeq` every frame, gated to nothing in particular — it's just "the SOI's cursor state."
- **`telemetry-source.js`**: posts `{type:'cursor', x, y}` / `{type:'cursor-select'}` up to the
  shell whenever this instance is SOI-focused, already generic (doesn't know what page is focused).
- **`mfd.js` forwarding**: `focusedMapWindow()` decides *whether* to forward — today it only
  returns non-null when the focused surface is showing MAP. **This is the one predicate that needs
  to widen**: it should return the focused surface's iframe window whenever that surface shows a
  page that opted in (MAP, TGT, HUD), not just MAP. `syncCursorFocus()`'s logic (tell the loser
  `cursor-focus:false`, the winner `cursor-focus:true`) is already fully generic — it just calls
  whatever `focusedMapWindow()`-equivalent predicate decides eligibility, so widening that one
  function/set is the whole shell-side change. (Likely rename to `focusedCursorWindow()` once it
  covers more than MAP, or keep the name and just widen the predicate — small naming call, not a
  design decision.)

What's MAP-specific and does **not** generalize as-is: `map.js`'s crosshair paint/integrate/clamp
code, and `selectAt()`'s canvas hit-test against `hitTargets`. Those are the two things TGT/HUD
need their own version of.

## What TGT/HUD need: a shared cursor module + a per-page hit-test

Rather than copy `map.js`'s ~60 lines of crosshair state into three files, pull the page-agnostic
half into one shared module, e.g. `src/web/services/pad-cursor.js`:

- **Shared (the module):** crosshair element creation, `cursorPos`/`cursorVec` state, `clampCursor`
  (parameterized by a rect-getter, since MAP clamps to `imgRect()` but TGT/HUD would clamp to their
  own panel bounds), `paintCursor` (transform-based, as today), the `performance.now()`-driven rAF
  integrator (`driveCursor`/`ensureCursorAnimation`), and the three message handlers
  (`cursor-focus`/`cursor`/`cursor-select`) — `cursor-select` calls a page-supplied `onSelect(x, y)`
  callback instead of a hardcoded `selectAt`.
- **Per page (the callback):**
  - **MAP** keeps its existing `selectAt(px, py, pad)` — nearest unselected contact within reach,
    unchanged. `map.js` migrates to import the shared module instead of owning its own copy, so
    there's exactly one crosshair implementation instead of three eventually.
  - **TGT/HUD** supply a **DOM hit-test**: `document.elementFromPoint(x, y)`, walk up to the
    nearest ancestor that's one of the page's already-existing clickable controls (`.tgt-cell`,
    `.tgt-veh`, `.tl-check`, `#datalink-btn`, `.tgt-action` for TGT; `.hud-dc`, `.hud-mode`,
    `.hud-max`, `.hud-sub` for HUD), and call `.click()` on it. No new command, no new handler —
    this is the same trick the DATALINK-button work leaned on: every control already has a working
    `click`/`pointerup` listener, so "select" is just a synthetic click at the cursor's point,
    exactly what a mouse or touch tap already does. If nothing clickable is under the point, no-op
    (same as MAP's "no contact in reach" case).
- **Clamp rect**: TGT/HUD clamp to their own panel's bounding rect (`#tgt-panel`/`#hud-...`
  equivalent) instead of `imgRect()` — the one page-specific parameter besides the hit-test.
- **Visual**: same crosshair sprite/element MAP already draws, reused as-is (consistent "this is
  the PAD cursor" look everywhere it appears, and zero new CSS/asset work).

## Binds — no new ones needed

Cursor Up/Down/Left/Right, the two axis rows, and Cursor Select already exist as HOTAS binds
(`docs/map-cursor.md`'s table) and are already the ones driving MAP; they simply also drive
TGT/HUD now, following the same "wherever the focused surface's iframe is a cursor-eligible page"
gate `focusedMapWindow()`/its replacement already applies. Follow/Zoom-In/Zoom-Out stay MAP-only —
they're MAP view controls, not applicable to TGT/HUD.

## Coexistence with the bezel SOI cursor

Unchanged from today for TGT/HUD: the bezel-level `soiCursor`/`soiKeys()` still gives that surface
just its one-item MAIN back-key (as now) — this doc doesn't touch that layer at all. The PAD cursor
is a second, independent control surface exactly the way it already is for MAP: bezel NAV UP/DOWN/
SELECT walk the (one-item) bezel key list, while Cursor Up/Down/Left/Right/Select drive the crosshair
inside the iframe. No overload, same split MAP already has between "SOI nav" and "the cursor."

## Scrolling TGT's list — reusing Zoom In/Out rather than new binds

Resolves the "clamp rect per page" open question above: rather than auto-scroll or leave rows
below the fold unreachable, reuse the **Zoom In / Zoom Out** binds. These already exist end-to-end
(bezel key, keybind capture, `DriveFree`, the `mapAct`/`mapActSeq` edge-counter transport,
`mapSend`/`paneMapSend` forwarding) purely as "an extra discrete action bound to the focused
surface" — MAP happens to interpret them as zoom, but nothing about the transport is zoom-specific.
On TGT (whose `.tgt-list-rows` is the one internally-scrolling region — `docs/page-cursor.md`'s
earlier open question), the same two binds scroll that list up/down by a step instead. RDR
repurposes them again — steps its selectable display range up/down (issue #40 follow-up,
`docs/rdr-page.md`), matching its own R+/R- bezel nav items. HUD has no internally-scrolling
region today, so they're simply inert there, same as Follow already is on a non-MAP page.

- **Shell side**: the existing `mapAct`/`mapActSeq` forwarding in `mfd.js` (`m.type === 'map-act'`)
  widens the same way `focusedMapWindow()` widens — forward to whichever cursor-eligible page is
  focused, not only MAP. The page decides what `'zoom-in'`/`'zoom-out'` means to it, exactly the
  parallel of "the shell forwards, the page decides" already established for cursor-select's
  DOM-hit-test above.
- **TGT side**: a `{mfd:true, action:'zoom-in'|'zoom-out'}` handler calls
  `tgtListRows.scrollBy({top: ±STEP})` (or the equivalent instant assignment) on `.tgt-list-rows`.
  Follow has no TGT equivalent and stays a MAP-only bind (nothing to "follow" on TGT/HUD).
- **Naming** — the bind labels stay "Zoom In"/"Zoom Out" (they're still that on MAP, the common
  case), rather than renaming to something generic like "Cursor Action A/B"; TGT's use is a
  repurposing of an existing bind's *meaning* on a specific page, not a new bind.

## Steps

1. **`pad-cursor.js` — built.** Extracted `map.js`'s crosshair state/paint/integrate/clamp/
   message-handling into `src/web/services/pad-cursor.js` (`createPadCursor({el, clampRect,
   onSelect, speed})`); `map.js` now imports it, passing `imgRect` as the clamp-rect getter and
   `(x,y) => selectAt(x, y, CURSOR_HIT_PAD)` as `onSelect`. The shared crosshair sprite/box CSS
   (`--map-crosshair`, `.pad-cursor`) moved from `map.css` into `shared/theme.css` so TGT/HUD reuse
   it without duplicating the base64 SVG.
2. **`mfd.js` forwarding — built.** `focusedMapWindow()` → `focusedCursorWindow()`, widened to a
   `PAD_CURSOR_PAGES` set — `{map, tgt, hud}` originally, `rdr` joined later (issue #40) when RDR
   got its own PAD cursor, `wpt` joined after that (issue #38 follow-up) so its own crosshair and
   the R+/R-/W+/W- `map-act` binds reach WPT the same way they reach MAP, and `sqd` joined after
   that (docs/squadron-transport.md) the same way `rdr`/`wpt` did — no data-forwarding branch of
   its own, since SQD polls `/squad`/`/server-players` directly rather than riding the shell's
   relay; branches on full-view MAP using its dedicated `mapFrame` vs. TGT/HUD/WPT/SQD using the
   shared `#page-frame`. The `cursor`/`cursor-select`/`map-act` message handlers, and both
   reload-resend fixups (the split-pane one that already existed, plus a new one for full-view
   `#page-frame`, which reloads on every TGT/HUD/WPT/SQD navigation the way a split pane already
   did), all route through this one predicate now.
3. **TGT/HUD — built.** Both import `pad-cursor.js`; `tgt.js` clamps to `.tgt-panel`'s own box
   (panel-local coordinates — the panel itself doesn't scroll) and its `onSelect` special-cases
   `.tgt-cell`/`.tgt-veh` (call `send('tgt.set', ...)` directly — the tap outcome, since a discrete
   Select press has no long-press "only" equivalent) and falls through to `.click()` for
   `.tl-check`/`.tgt-action`/`.tgt-mode`, which already have plain `click` listeners. `hud.js`'s
   cursor lives in viewport coordinates instead, because `.hud-panel` itself scrolls internally — a
   child positioned relative to a scrolling ancestor would drift with the content on every scroll,
   so `#pad-cursor` is a sibling of `.hud-panel`, not nested inside it; its `onSelect` is a plain
   `.click()` on the nearest `.hud-dc`/`.hud-mode`/`.hud-max`/`.hud-sub` ancestor (every HUD control
   already uses `click`, unlike TGT's press/hold cells).
4. **Zoom In/Out → scroll — built.** `mfd.js`'s `map-act` handler now routes through
   `focusedCursorWindow()` too, so it lands on whichever eligible page is focused; TGT scrolls
   `.tgt-list-rows`, HUD scrolls `.hud-panel` itself (the whole page is the scrolling region there).
5. **Verified in the `serve_web` harness.** Confirmed via `window.__setSoiTarget` +
   `window.__cursorVec`/`__cursorSelect`/`__mapAct`: cursor-focus centers the crosshair correctly on
   both full-view TGT and HUD (`display:block`, transform at the panel's centre); the DOM
   hit-test/selector logic correctly matches `.tgt-cell`/`.tl-check` and `.hud-max` at their real
   screen coordinates; Zoom In/Out moved both pages' scroll position through the real message
   pipeline (`__mapAct` → `mfd.js` → the page's own handler). **Not confirmed here, same caveat
   `docs/map-cursor.md` already names:** the rAF glide itself — this harness's Browser pane doesn't
   composite frames when not displayed, so a held vector never visibly moves the crosshair in
   harness. That math is unchanged from MAP's own (already covered by
   `tools/map-cursor.test.js`), just re-parameterized. **Needs a real in-game/browser check** for
   the visual glide and the actual click-through on TGT/HUD, same as MAP needed one for its own
   rollout.
6. **`f35.js` widened (docs/tgt-keybind-nav.md).** Originally scoped to classic-layout-only, since
   F-35 had its own MAP-only `focusedMapWindow()`. Generalized to `focusedCursorWindow()` + a
   `PAD_CURSOR_PAGES` set mirroring the bezel's, plus a `cursor-held` forwarder the bezel already had
   and F-35 was missing entirely (TGT/RDR's Select tap/hold arbitration lives there, not in the
   plain edge-driven `cursor-select`) — so a portal showing TGT/HUD/RDR gets the same cursor/
   cursor-select/cursor-held/map-act forwarding a MAP portal always did.

## Open questions

- **Hit-test precision on small controls** — HUD's subtype chips and TGT's vehicle-grid cells are
  small touch targets; MAP's `CURSOR_HIT_PAD` gives icons some slack because it's picking from a
  known contact list. `elementFromPoint` has no equivalent "nearest, within reach" fallback — it's
  exact-pixel only. Probably fine (these controls are already sized as touch targets, unlike a
  map icon), but worth confirming once it's testable in-game rather than assuming.
- **Scroll step size** — `SCROLL_STEP = 60` (px) in both `tgt.js`/`hud.js`, a flat constant tuned by
  guess, matching `pad-cursor.js`'s own `SPEED` — `ponytail:` no config entry unless asked.

## Round 2 — hold, hover, and edge-panning

Three follow-up requests after the initial rollout above landed and was verified:

1. **TGT's Select mirrors its own tap/long-press cells.** A filter cell's DOM press (`.tgt-cell`/
   `.tgt-veh`) has always had a secondary "hold = only this" action; the PAD cursor's Select
   couldn't reach it because Cursor Select only ever transported a discrete press *edge*
   (`cursorSelSeq`) — no way to tell a quick tap from a hold without a *continuous* held signal.
   Added one: `Keybinds.cs` now also reads the same `cursor-select` bind's LIVE state every frame
   (`Active(_cursorSelect, edgeOverride: false)`, using a new optional `edgeOverride` param on
   `Active()` so every other bind's behaviour is untouched) and reports it via
   `TelemetryServer.SetCursorSelectHeld(bool)`. It rides the existing `cursor` SSE event as a new
   `held` field (`CursorJson()`) — free to transport, since that event already ships ~60 bytes many
   times a second and only sends when its JSON actually changes.
   `telemetry-source.js` forwards a `held` change up as `{type:'cursor-held', held, pane}` (gated on
   focus, same idempotent-change-only shape as the vector); `mfd.js` forwards it down to whichever
   surface is focused, exactly like `cursor`/`cursor-select`.
   `pad-cursor.js` gained `onHold`/`holdMs` and a new `setSelectHeld(held)` method: rising edge arms
   a `holdMs` timer (matching `tgt.js`'s existing `LONG_MS` = 500), falling edge either found the
   timer already fired (the hold outcome already ran) or fires the tap outcome (`onSelect`) —
   the same tap-vs-hold arbitration `tgt.js`'s own `pointerdown`/`pointerup` handlers already do,
   just driven by the transported boolean instead of a real pointer event. **MAP is unaffected**: it
   never calls `setSelectHeld`/registers `onHold`, so its instant edge-driven `select()` (unchanged)
   is still the only thing that fires there. TGT's `onHold` fires `tgt.only` for `.tgt-cell`/
   `.tgt-veh` only — every other TGT control (list row, RESET/CLEAR, LASER/HUD) has no hold meaning,
   so holding over one is simply a no-op (same as holding a mouse down over a plain button already
   is). HUD doesn't register `onHold` either — none of its controls have a hold behaviour.
2. **The whole target-list row is now the click target**, not just the small `.tl-check` box — a
   bigger, easier touch/cursor hit area. `tgt.js`'s row-click delegation moved from
   `e.target.closest('.tl-check')` to `.closest('.tl-row')`; the checkbox mark (`.tl-check`) stays
   purely visual. **Hover feedback**, added everywhere on both pages via a new shared
   `.pad-hoverable`/`.pad-hover` class pair (`shared/theme.css`: a subtle
   `rgba(255,255,255,0.06)` overlay, doubling as a real mouse `:hover` too since neither page had
   one before) — `pad-cursor.js` gained an `onMove(x, y)` hook, called from `paint()` with the
   cursor's current point whenever it's visible (and `(null, null)` when hidden/unfocused); each
   page's `onMove` callback does the same `elementFromPoint` + `.closest()` lookup `onSelect`
   already does, toggling `.pad-hover` on whatever's currently under the crosshair and clearing it
   from whatever had it before. `pad-hoverable` was added to every clickable control's class list on
   both pages (TGT: filter cells, vehicle chips, list rows, RESET/CLEAR, LASER/HUD; HUD: declutter/
   mode/category/subtype).
3. **MAP edge-panning** — pushing the crosshair against `imgRect()`'s border, with more map to
   reveal past it (zoomed in) and FLW off, now pans the view that direction instead of just
   stopping the cursor at the edge — a 4-axis "scroll at the screen border," the same idiom an RTS
   map uses. `pad-cursor.js`'s `drive()` gained an optional `onEdge(ex, ey, dt)` hook, called with
   how far past `clampRect()` the pre-clamp position landed this tick (`ex`/`ey`, screen px,
   0 when within bounds) — the cursor is still clamped to the rect right after, so it stays visually
   pinned at the edge while `onEdge` reacts. `map.js`'s `onCursorEdge` pans `view.panX`/`panY` by
   `EDGE_PAN_SPEED * dt` toward the overflow direction, re-clamped by the existing `clampPan()`, and
   redraws. Explicitly gated on `!followPlayer`, per the request — under FLW, `drawOverlay()` is
   already re-centring the view on the player every frame, so edge-panning would just fight it.
   TGT/HUD still don't pass `onEdge` (nothing to reveal past their own panel edge). RDR does, for an
   unrelated purpose — pushing past its scope's top/bottom edge steps its displayed range, not the
   view (`rdr.js`'s own `onCursorEdge`); HSD carries the identical range-step behavior (issue #66),
   ported from RDR's. `clampPan()` can be nonzero even at `zoom === MIN_ZOOM` when the current
   mission's real reachable extent runs past the minimap image's own smaller extent (issue #65's
   per-mission margin) — edge-panning still just calls `clampPan()`, whatever it currently allows.

### Verification

Confirmed via `dotnet build` (0 errors) and the `serve_web` harness (`window.__cursorHold(true/false)`
added alongside the existing `__cursorVec`/`__cursorSelect`/`__mapAct` hooks). Unlike the original
rollout's rAF glide, the hold-timer arbitration and hover-toggle don't depend on frame compositing —
`setTimeout`/message-passing both run regardless — so these were provable live, not just by reading
the code:
- A quick press/release (`__cursorHold(true)` then `false` within ~150ms) on a target-list row fired
  `target.deselect` for that row — the tap outcome.
- A held press (>500ms) over a row with no hold meaning correctly no-opped, and the trailing release
  correctly did **not** also fire the tap (the hold already "used" the press) — confirms
  `holdFired` correctly suppresses the tap branch.
- Hover: focusing TGT/HUD immediately marked whatever was under the (centred) crosshair with
  `.pad-hover`, on both pages.
- The selector/hit-test logic for both `.tgt-cell`/`.tl-row` (TGT) and `.hud-max` (HUD) matches the
  right element at its real on-screen coordinates.

**Still not provable in this harness** (same caveat the original rollout already named): the rAF
glide itself and MAP's edge-panning, since both depend on the cursor's position actually moving
frame-to-frame, and this harness's Browser pane doesn't composite frames when not displayed. Needs a
real in-game/browser check for those two specifically.

## README + keybind description follow-ups

After this shipped, two small consistency passes landed on top:

- **README's "MAP cursor" section renamed to "PAD cursor"** and rewritten to describe all three
  pages (MAP/HUD/TGT) instead of MAP alone — it now covers Select's hold behaviour on TGT, Zoom
  In/Out's scroll repurposing on HUD/TGT, and MAP's edge-panning, plus updated the two anchor links
  that pointed at `#map-cursor`.
- **`Keybinds.cs`'s Zoom In/Zoom Out descriptions** (shown on the `/keybinds` page) now say
  "On a scrollable page, scrolls it up instead." / "...scrolls it down instead." — the up/down
  wording matches which bind does which, without naming TGT/HUD specifically (the binds' meaning is
  page-decided, per the "shell forwards, page decides" pattern above, so the description shouldn't
  hard-code which pages currently opt in).

## Round 3 — FCR/HSD cursor-anchored zoom

A DCS-style "TDC depress" magnifier for FCR (`rdr.js`) and HSD (`hsd.js`): holding Cursor Select —
no new bezel button — toggles a fixed 3x zoom centered on wherever the cursor currently sits, so an
overlapping cluster of contacts can be pulled apart without changing the selected range. Holding
again while zoomed returns to the normal view, from wherever the cursor is now (a plain toggle, not
a re-anchor-then-hold-to-restore gesture).

Both pages register a real `onHold` (`pad-cursor.js`) for the first time — previously neither passed
one, so `setSelectHeld`'s own fallback (`if (!onHold) { if (held) this.select(); return; }`) fired
Select's outcome on the PRESS edge. Registering `onHold` switches both pages onto the same
tap-vs-hold arbitration TGT/MAP already use: Select's target-lock outcome now fires on RELEASE
(before `holdMs`), and a press held past it fires the zoom toggle instead. This is an intentional,
unavoidable side effect of adding a hold gesture through the shared cursor primitive, not a
regression — it brings FCR/HSD's Select timing in line with the rest of the mod's PAD cursor pages.

**Implementation** — deliberately NOT baked into the pages' pure, already-tested geometry functions
(`bscopeXY`, `hsdXY`, `radarConePath`); those stay untouched so their existing unit tests keep
asserting exact, un-zoomed coordinates. Instead:

- A plain SVG `transform="translate(anchor) scale(3) translate(-anchor)"` is set directly on each
  page's existing content `<g>` groups (FCR: `rdr-grid`/`rdr-contacts`/`rdr-pitbull`/`rdr-sweep`;
  HSD: `hsd-grid`/`hsd-radar`/`hsd-threats`/`hsd-contacts`/`hsd-ownship`) from a shared-per-page
  `applyZoomTransform()`, called once at the end of `render()`. The crosshair (`*-cursor-g`), scope
  frame, and corner/readout `<text>` elements live outside those groups and stay screen-fixed —
  a magnifying glass held over the picture, not the whole panel. FCR's static ownship caret (fixed
  scope furniture, not a positioned entity) is deliberately left out of the zoomed groups too; HSD's
  `hsd-ownship` IS included, since it represents the aircraft's actual position on a spatial plan
  view where zooming away from ownship should be able to push it off-screen, same as any contact.
- `toContentSpace(vx, vy)` is the transform's exact inverse. `nearestContact` runs the cursor's raw
  screen/viewBox position through it before comparing against `plotted` — which itself is never
  touched, still holding exactly what `hsdXY`/`bscopeXY` computed — so hit-testing, hover, and
  Select all keep working unmodified regardless of zoom state.
- Zoom state (`zoomed`/`zoomAnchor`) persists across SOI focus changes and range/mode switches;
  nothing resets it automatically. Not asked for, and DCS's own TDC-depress zoom has no such
  auto-reset either.
- **Update:** the outer zoom transform scales the SPACING between contacts, which is the actual
  point of the magnifier, but it also blew up each icon's own drawn size by the same factor — a
  contact that overlapped another at scale 1 just became a bigger overlapping icon at scale 3. Each
  contact's markup (and FCR's pitbull dart) is now wrapped in its own nested `<g>` with a counter-
  scale, `iconTransform(px, py)`: `translate(px, py) scale(ICON_SHRINK/ZOOM_SCALE) translate(-px,
  -py)`, centered on the icon's own point so it shrinks in place instead of drifting toward the zoom
  anchor. Composed with the outer transform, the net on-screen size while zoomed is exactly
  `ICON_SHRINK` (0.5) x normal, independent of whatever `ZOOM_SCALE` happens to be. Identity (empty
  string) when not zoomed. The pitbull dart's line to its target is deliberately left OUTSIDE this
  wrapper — its two endpoints are two independently-zoomed points, not one icon's local geometry, so
  shrinking it around the dart's own center would wrongly pull the target end toward the dart.

Verified in `tools/serve_web.py`: toggling zoom via the console (`zoomed`/`zoomAnchor` + `render()`,
standing in for a real held Select press) visibly separated two previously-overlapping bricks/icons
on both pages, and `nearestContact` correctly resolved the right contact id at its new, magnified
screen position afterward. `rdr.test.js`/`hsd.test.js` cover `toContentSpace`'s inverse-transform
math directly (DOM-free, via a `toContentSpaceForTest` export). Not provable in this harness (same
caveat as Round 2's hold-timer/hover checks): a real held Cursor Select press end-to-end, since the
harness has no live HOTAS/keybind driving `cursor-held`.

## Round 4 — FCR/HSD Select never deselects; a new Cursor Deselect keybind

FCR/HSD's Cursor Select used to toggle a contact's lock (select if untargeted, deselect if already
targeted) — the same tap that drove Round 3's zoom hold. By request, both pages now mirror MAP's own
cursor select exactly: a tap always ADDS the nearest *unselected* contact in range to the target
set and never removes one, so repeated presses over a crowded area advance through the cluster
instead of re-toggling the first hit on and off. Deselecting is now a separate, dedicated action.

- **`Keybinds.cs`**: a new `cursor-deselect` bind in the existing "Cursor Keybinds" group, right
  after `cursor-select`. A plain one-shot `TelemetryServer.MapAction("cursor-deselect")` — same
  shape as `tgt-datalink`/`zoom-in`, no native/server-side effect of its own. No effect on MAP (no
  deselect concept there either) or TGT (already has its own dedicated deselect path via row tap /
  focused-lock Select, docs/tgt-cycle-focus.md) — the action reaches whichever page holds SOI, and
  only `rdr.js`/`hsd.js` listen for it.
- **`rdr.js`/`hsd.js`**: `padSelect` now calls a new `nearestContactBy(px, py, wantLocked)` — the
  same hit-test loop as `nearestContact`, but filtered to only-unlocked (Select) or only-locked
  (the new `padDeselect`) candidates, mirroring MAP's own `selectAt`'s "skip already-selected"
  filter. `padDeselect` is wired to the `'cursor-deselect'` message, reading the cursor's current
  position via `cursor.getPos()` (there's no separate transported x/y for this action, unlike the
  continuous cursor vector) and hit-testing exactly where a Select tap would.
- **`pendingSel`** (ported from MAP's own identically-named mechanism): a just-selected id is
  optimistically marked locked immediately, before the `target.select` request even resolves,
  expiring after 1.5s. Contacts refresh at 4 Hz (`TelemetryReader.ContactInterval`) — well slower
  than a HOTAS button can repeat — so without this, a rapid burst of Select presses would keep
  re-computing the SAME nearest-unselected contact (server confirms are still in flight) instead of
  advancing past it on each press, same bug MAP already solved.
- `nearestContact` itself (used by `padMove`'s hover highlight and `drawCursor`) is untouched —
  hover still highlights the nearest contact regardless of lock state.

Verified in `tools/serve_web.py`: with three contacts plotted inside one `HIT_PAD` cluster, calling
`padSelect` at that point four times in a row (mocking `sendCommand` to capture calls) produced
exactly three `target.select` calls, one per contact, in nearest-first order, then correctly
no-op'd on the fourth press with nothing left unselected — never once calling `target.deselect`.
Marking all three `tg:1` and calling `padDeselect` at the same point then correctly called
`target.deselect` for the nearest one. Confirmed on both FCR and HSD. `rdr.test.js`/`hsd.test.js`
cover `pendingSel`'s expiry semantics directly (DOM-free). Not provable in this harness: a real
Cursor Select/Deselect keypress end-to-end (same live-HOTAS caveat as Round 3).
