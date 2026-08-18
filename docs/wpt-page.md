# WPT page — waypoints/route creator

A new NAV item, **WPT**, reached from MAP's own nav row (not MAIN — WPT is scoped to MAP, the same
way RTS is scoped to CFG). Lets the pilot plot custom waypoints on MAP and chain them into an
ordered route, with a distance/bearing readout to the next waypoint and edit/reorder/delete on the
WPT page itself (issue #38).

Client-side only: no BepInEx/C# changes. `world.x/z`, `hdg`, and the map's grid offset (`ox/oy`)
were already in every telemetry frame; this widens one already-piped-but-unconsumed slice
(`mapinfo`) and adds new pages/modules under `src/web/`, persisted in `localStorage`.

Automatic cross-device route sharing (the issue's stretch goal) is out of scope — routes are
per-browser-profile, matching the "client-side storage is enough for a first pass" framing. Manual
sharing exists (IMPORT/export, below): a route round-trips as pasteable JSON.

## Placement — long-press on MAP

A long-press (mouse hold, touch hold, or the PAD/HOTAS cursor's hold) on the map canvas drops a
waypoint at that world position into the active route. Plain click/tap keeps meaning "select
target" — the two gestures share the same pointer stream but never fire together (a fired
long-press suppresses the follow-up click).

Mechanics (`map.js`), following `tgt.js`'s existing long-press pattern for its filter cells:
- `WPT_LONG_MS = 500`. Armed on every single-pointer `pointerdown`, cancelled the moment real
  movement is detected (`gestureMoved`, >4px) or a second pointer joins (a pinch starting).
- The timer converts the down position via `overlayToWorld` (map-transform.js's exact pixel→world
  inverse) and calls `WaypointsStore.addWaypointToActive(x, z)`.
- A fired long-press is consumed by the `click` handler (not `pointerup`), since touch fires a
  synthetic `click` after `pointerup` and the suppression has to survive that hop.
- The PAD/HOTAS cursor gets the same placement for free via `pad-cursor.js`'s existing `onHold`
  arbitration — no new gesture code needed there.

No manual coordinate-entry add path exists — map long-press is the only way to add a waypoint.

## Data model + persistence

**`src/web/pages/wpt/wpt-route.js`** — pure, DOM/storage-free (Node-tested,
`wpt-route.test.js`), the same treatment `map-transform.js` gets:

```js
{
  version: 1,
  activeRouteId: "r_..." | null,
  routes: [{
    id: "r_...", name: "Strike Run",
    nextIndex: 0,   // progress cursor — which waypoint is "next"
    waypoints: [{ id: "w_...", name: "WP1", x: 1234.5, z: -6789.0 }, ...],
  }],
}
```

World units are meters. `distanceBearing(ownX, ownZ, wx, wz)` returns an **absolute compass
bearing** (`atan2(dx,dz)`, 0-360), not heading-relative — a pilot nav-aids off "fly heading X to
the waypoint," unlike RWR/MW's nose-relative plot. `advanceIfNear` bumps `nextIndex` once the
current next waypoint is within a threshold.

`activeRouteId` is a first-class `null` state, not just "unset": a route can be saved but
inactive, and `cycleRoute(routes, activeId, dir)` treats "no route active" as its own stop between
the last route and the first — R+/R- can step OUT of a route (parking on none) as well as into one,
rather than always landing on some route. Clicking WPT's already-active route deactivates it the
same way.

`waypointMarkerState(index, nextIndex)` (`'next' | 'reached' | 'pending'`) and
`segmentReached(index, nextIndex)` are the pure predicates behind MAP's route-line coloring (see
"Rendering on MAP" below) — pulled out of the canvas code and unit-tested after an off-by-one
(`i < nextIndex` instead of `i + 1 < nextIndex`) grayed the segment leading INTO the next waypoint
instead of only segments where both ends were already reached.

`nextIndex` is a **count of completed waypoints**, not a waypoint's identity — it names "how many
are done," not "which one." `removeWaypoint`/`reorderWaypoint` both follow that: reordering never
touches `nextIndex` (the plan ahead changed, not how much progress was made), so whichever waypoint
now sits at that index inherits "next," even if a different one carried the mark before the move.
Deleting a waypoint before `nextIndex` shifts it down by one; deleting the tracked waypoint itself
leaves the number as-is, which now names whatever slid up into its slot.

Waypoints get no default name — `addWaypointToActive` leaves `name` empty and the UI shows the
list position instead (`wpt.js`'s `waypointLabel` falls back to `WAYPOINT N`). Routes DO get a
default name: `freshRouteName()` generates a short `RT-XXXXX` code, pre-filled into the "+ NEW
ROUTE" input so the pilot can accept it or type over it before confirming.

**`src/web/pages/wpt/waypoints-store.js`** — the storage-coupled half: `localStorage` (key
`noxmfd.map.waypoints`), not MAP's `sessionStorage`-based view state — routes are pilot-authored
planning data meant to persist across reloads and be shared across every tab/display on the same
PC, the opposite tradeoff from per-tab pan/zoom/follow. Auto-creates a default route on the first
placement, so long-press works before any visit to WPT.

**Live cross-page sync comes for free**: `localStorage.setItem` in one document fires a `window
'storage'` event in every *other* same-origin document. MAP and WPT are separate iframes, so both
repaint immediately on the other's write — no postMessage plumbing needed for waypoint data itself.

## Rendering on MAP

`drawWaypoints()` (`map.js`) runs right after `drawGrid()` in `drawOverlay()`'s draw pass —
"under icons, above map image," the same layer as the grid, since waypoints are navigational
chrome the pilot plans against, not interactive contacts (no hit-testing/click on the map itself —
editing lives on the WPT page). Draws a dashed polyline through the active route in order, plus a
small numbered marker per waypoint.

Both the line and the markers are three-colored, driven by `wpt-route.js`'s pure
`waypointMarkerState`/`segmentReached`: the current "next" waypoint (`route.nextIndex`) draws
brightest so the map alone shows which one the readout is tracking; a waypoint already flown past
draws dimmer; a segment between two waypoints draws dim only when BOTH ends are already reached —
the segment leading INTO "next" stays the active color, the same leg the readout's bearing/distance
is tracking.

## The WPT page

`src/web/pages/wpt/{wpt.html,wpt.css,wpt.js}`, same shell convention as `obj.html`/
`keybinds.html`. Not self-driven like KEY/RTS — it receives the widened `mapinfo` slice via
`postMessage` (forwarded by `mfd.js`, since it's a separate document from MAP).

- **Route list**: create/rename (inline text input, no modal)/delete. Click a route to activate
  it; click the active one again to deactivate it — routes can sit saved but unassigned, and
  `activeRouteId: null` is a normal, reachable state, not just "before the first route exists."
  CLEAR (between "+ NEW ROUTE" and IMPORT) wipes every route at once, same no-confirmation style as
  the per-route delete button.
- **Waypoint list** (active route only): inline rename, ▲/▼ reorder (no drag-and-drop library
  anywhere in the repo, none needed here), delete, grid label per waypoint (via the imported
  `gridLabel()` from `telemetry-source.js`).
- **Readout**: `NEXT: <name>  BRG ddd°  DIST d.d km`, or `NO ACTIVE ROUTE`/`ROUTE COMPLETE`.
  Recomputed every `mapinfo` tick.
- **Auto-advance**: every `mapinfo` tick calls `WaypointsStore.advanceIfNear(..., 
  WPT_ADVANCE_RADIUS_M)`. `WPT_ADVANCE_RADIUS_M = 1000` (1 km) — combat-aircraft distances are
  km-scale, so this stays coarse on purpose; a single tuned constant, no settings UI.
- No PAD/HOTAS support for this page's own buttons (rename/delete/reorder/switch/CLEAR) — mouse/
  touch only, consistent with OBJ/BDF/MIS/PAL. Long-press placement on MAP itself does get full PAD
  parity (that's the one gesture this feature promises everywhere). The physical R+/R-/W+/W- HOTAS
  binds DO reach WPT, though (see "Nav wiring" below) — WPT is a `PAD_CURSOR_PAGES` page
  (`docs/page-cursor.md`), so `mfd.js` forwards those the same way it forwards them to MAP whenever
  WPT itself is the SOI-focused surface.

## Nav wiring

`NAV.map` (nav-model.js): `MAIN, GRID, FLW, WPT, R+, R-, W+, W-, Z+, Z-` — WPT sits right after
FLW; R+/R- (`rt-next`/`rt-prev`, a "ROUTE" word+triangle decorator between them, MAP's twin of the
existing ZOOM decorator between Z+/Z-) switch the active waypoint route, wrapping through a "none
active" stop; W+/W- (`wpt-next`/`wpt-prev`, a "WYPT" decorator) manually step the active route's
next waypoint; Z+/Z- stay the last pair. `NAV.wpt` is a single way-back to **MAP** (not MAIN — WPT
is reached from MAP's own nav row). `layout-pages.js`'s three tables, `mfd.js`'s full-view switch +
forwarders, and `f35.js`'s `PAGE_FEEDS.wpt = ['mapinfo']` carry the WPT destination;
`layout-coverage.test.js` is the backstop that catches an out-of-sync table automatically.

**R+/R- and W+/W- are dead keys under different conditions**, filtered independently
(`split-slots.js`'s `filterMapRouteActions`, `MAP_ROUTE_ACTIONS` vs `MAP_WAYPOINT_ACTIONS`): R+/R-
stay usable as long as ANY route is saved (there's something to cycle into, even with none active);
W+/W- need a route actually active, since they step ITS next waypoint. Every rendering below drops
each pair on its own rather than showing a key that does nothing.

**Ten items past the physical-key budget.** `NAV.map` growing to 10 pushed it past both a bezel
split pane's 6-key budget and F-35's single-column 6-row "edge" grid — three places render it, all
built from the same two lists (`split-slots.js`'s `MAP_FULL_LEFT`/`MAP_FULL_RIGHT`) so they can't
drift out of sync with each other:

- **Bezel full view**: a fixed 5-left/5-right split, not the generic 6-then-overflow sweep every
  other page uses (`mfd.js`'s `mapFullRight(hasRoutes, hasActiveRoute)`) — `MAP_FULL_LEFT` (MAIN,
  GRID, FLW, Z+, Z-) always fills the left bank; `MAP_FULL_RIGHT` (WPT, R+, R-, W+, W-) fills the
  right bank, R+/R- and W+/W- each dropped independently per the rule above. ZOOM sits between
  Z+/Z- (left3/left4); ROUTE between R+/R- and WYPT between W+/W- (right1/right2, right3/right4)
  each only draw once their pair is actually visible.
- **Bezel split pane**: paginated like MAIN (`mfd.js`'s `mapNavPaneSlice`/`paneMapNavPage`, reusing
  `ClassicPaging.mainPaneSlice` — MAP has no `SPLIT_SLOTS` entry, like MAIN), 5 items per page +
  PREV/NEXT. The item ORDER feeding that pagination is orientation-dependent
  (`split-slots.js`'s `mapSplitOrder(variant, hasRoutes, hasActiveRoute)`): an `'h'` (top/bottom)
  split's page 2 fills a physical left-then-right BANK boundary (2 slots then 3) that a pair must
  not straddle, so `MAP_SPLIT_ORDER` keeps R+/R- leading and W+/W- trailing the bare `wpt` entry;
  a `'v'`/`'vw'` (left/right) split has no such boundary — one column, no bank split — so
  `MAP_SPLIT_ORDER_V` leads with `wpt` instead, reading as "the page, then its controls." A pair
  that still lands split across two PAGES (rather than banks) simply doesn't get its decorator —
  `placeMapPaneDecorator` skips rather than draws it wrong, the same "rare pagination edge case"
  reasoning WPN's MASTER/MODE pane decorator already uses.
- **F-35 glass**: `mapNavItems()` places `MAP_FULL_LEFT` in column 1 and
  `SplitSlots.mapFullRight(hasRoutes, hasActiveRoute)` in column 2 via explicit cells, the same two
  shared lists the classic bezel's full view uses. The F-35 ZOOM/ROUTE/WYPT decorators find their
  pair by `data-action`, not position, so they render wherever the two buttons landed (or not at
  all if the pair was filtered out).

## Verification

- `node src/web/pages/wpt/wpt-route.test.js` — pure-logic self-check (distance/bearing, advance
  threshold, `cycleRoute`'s "none active" stop, `waypointMarkerState`/`segmentReached`, CRUD +
  reorder/delete preserving `nextIndex` by id).
- Full JS suite (`nav-model.test.js`, `layout-coverage.test.js`, `split-slots.test.js` — including
  its per-variant `mapSplitOrder`/`mapFullRight` checks for the R+/R- vs W+/W- and `h` vs `v`/`vw`
  cases, `map-transform.test.js`, `telemetry-source.test.js`, `server-route-coverage.test.js`) —
  all green.
- `dotnet build -c Release` — 0 errors; new/changed pages are picked up automatically by the
  existing `EmbeddedResource` glob, no csproj edit needed. Web assets are DLL-embedded, so this
  rebuild+redeploy is required before any in-game test, not just C# changes.
- `serve_web.py` harness: long-press places a waypoint (auto-creating a default route) and does
  not also select a target or pop a context menu; a short tap does neither. WPT renders the route/
  waypoint lists and readout, including the live `BRG`/`DIST` calc once `mapinfo` is forwarded
  through the full bezel shell (verified end-to-end, not just in isolation).
- In-game: R+/R-/W+/W- keybinds work with WPT itself SOI-focused, not only MAP; R+/R- stay usable
  with a route saved but none active; V_SPLIT/2:1 shows WPT before R+/R-/W+/W-, H_SPLIT keeps the
  bank-safe R+/R-/WPT/W+/W- order.

## Out of scope for this pass

- Automatic cross-device/server sync of routes (the issue's own stretch goal) — manual export/
  import already covers backing up or handing a route to another pilot.
- Settings UI for the proximity threshold — one named constant.
- Manual coordinate-entry waypoint creation — map long-press is the only add path.
- PAD/HOTAS support for WPT's own list-editing buttons (rename/delete/reorder/switch/CLEAR) — the
  physical R+/R-/W+/W- binds do reach WPT (see "Nav wiring"), just not WPT's on-page buttons.
- Drag-and-drop reordering.
