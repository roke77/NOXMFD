# WPT rework — route manager plus route and steer-point detail pages

**Status:** built on `feature/wpt-rework`; verified in the `serve_web` harness, not yet in game.
The layout follows the approved designs saved in `_scratch/claude_designs/WPT page redesigns/`
(gitignored): "WPT A · Routes" for `/wpt`, "WPT B / B1 · Route Details" for `/rtd`, and "WPT C ·
Steer Point Details" for `/rtd?spd`.

## Pages and navigation

WPT, RTD and SPD are one family, reached from MAP's WPT key. Each carries the same left-bank switch,
`MAP / WPT / RTD / SPD`, with `mark` on the current page (`NAV.wpt`, `NAV.rtd`, `NAV.spd`), the
pattern the CFG group uses. All three stand their bezel labels upright (`isVmainPage`) and pad both
sides like TGT so the labels never cover content.

- **WPT** (`/wpt`) manages routes and steer points.
- **RTD** (`/rtd`) flies the active route.
- **SPD** (`/rtd?spd`) flies the selected steer point.

RTD and SPD are one document with two views, the way `/bdf` serves PAL. All three read the
`mapinfo` slice (the `RELAY_MESSAGES.mapinfo` entry now feeds the whole `WPT_PAGES` family) and the
navigation library the shell already pushes to every frame.

## WPT — route manager

1. Header: WAYPOINTS, with the hint opposite it and the green-dark rule underneath (TGT's header).
2. Next-point strip: relative-bearing compass, NEXT/STEER, the point, grid, BRG, DIST and the distance
   left on the route.
3. Left column: ROUTES with **+ NEW / IMPORT / CLEAR**, incoming shared routes (ACCEPT / DISMISS),
   one card per route (lamp lit on the active one, points and length, ACTIVE or SHARED with the
   sender), then the fixed **STEER POINTS** card (lock icon, GUIDING while it guides).
4. Right pane: whichever card is open. The open route need not be the active one.

**Route pane.** Title and status, then ACTIVATE/DEACTIVATE, RENAME, RESET, EXPORT, SHARE (squad
leader with members only) and DELETE (REMOVE on a received route). Points list as `#`, name, NEXT,
grid and leg; tapping a point expands RENAME, FLY FROM HERE, ▲ ▼ and DELETE. A received route keeps
only the progress actions (FLY FROM HERE, RESET), the same read-only rule `RouteStore` enforces.

**Steer-point pane.** The group itself can't be renamed, shared or deleted. **+ NEW STEER POINT**
takes a grid square (`Fd40`) and adds its centre through `gridToWorld`; the hint says whether the
text is a grid, on this map, and which STP it becomes. Each point expands to GUIDE TO, RENAME, SHARE
(leader only) and DELETE. Incoming shared steer points list first with ACCEPT / DISMISS. Steer-point
IMPORT/EXPORT is gone from the page; the store and plugin commands remain.

## Editing a route that isn't active

`wpt.rename-waypoint`, `wpt.reorder-waypoint` and `wpt.remove-waypoint` take an optional route id in
`bind`; without one they act on the active route, so MAP, the keybinds and the extension API are
unchanged. `RouteStore.RouteOrActive` resolves it; `RouteStoreTests` covers that an id-targeted edit
leaves the active route alone. FLY FROM HERE activates the route first, then `wpt.reset-waypoint`.

## Route and steer point are exclusive

A route and a steer point never guide at once. `RouteStore.ActivateRoute` / `ActivateSteerPoint`
are the only way either gets selected, and selecting one clears the other: activating, creating,
importing or cycling to a route drops the steer point; GUIDE TO (`wpt.set-active-steerpoint`) ends
the route. A steer point added or imported while a route guides is saved unselected, so adding one
never ends route guidance. Deactivating a route leaves nothing selected; the earlier behaviour of
restoring the previous steer point is gone. A routes file saved with both selected loads with the
route kept, which had guidance priority before.

## RTD and SPD — details

1. Header: `ROUTE <name>` (or NO ACTIVE ROUTE) / STEERPOINTS, with `WPT n OF m` / `STP n OF m`.
2. Heading tape: 120° centred on the heading, an amber bug on the target bearing, or an edge arrow
   with the bearing when it's off the tape.
3. Readout: the point, its name and grid; DIST in the player's unit, BRG, and a TURN LEFT/RIGHT n° /
   ON COURSE cue (`WptRoute.signedTurn`).
4. RTD: the timeline, then THEN / AFTER / ROUTE LEFT cards. Up to six connected points per row; a
   longer route wraps, and the leg that wraps leaves its row at the right edge and re-enters the next
   from the left, labelled on the second half (`WptRoute.timeline`). Flown legs dim, the leg into the
   next point is lit, and ▼ YOU rides that leg. Tapping a point flies direct to it.
5. SPD: every steer point as a button, three per row, growing downward; tapping one guides to it.

The timeline and tape are laid out in `calc()` of their own width, so they reflow with the pane.

## Shared pieces

- `wpt-route.js` (unit-tested): `signedTurn`, `legLengths`, `routeLength`, `remainingDistance`,
  `timeline`, and the km/nm and bearing formatters both pages print with (`distanceParts`,
  `formatDistance`, `formatBearing`).
- The RTD/SPD heading tape is built once and slid each tick (docs/performance.md, 2026-10-03).

## Sizing

- All three pages inset their sides by `clamp(50px, 5.5vw, 56px)`, wider than TGT's
  `clamp(34px, 5vw, 48px)`: here the page's own bezel label is the lit one, and its outlined box
  reaches about 46px in, past 5vw in a half-width pane.
- WPT's base size is `clamp(12px, 2.1vmin, 19px)`, RTD/SPD's `clamp(11px, 2vmin, 18px)`. Both set
  `button { font-size: inherit }`, since the route cards, point rows and steer-point buttons are
  buttons and would otherwise stay at the browser's fixed 13.3px.
- The open route's six actions are equal-width grid columns, at least as wide as DEACTIVATE.
- The RTD/SPD readout takes the height the page leaves (up to a cap) and its big figures scale with
  it through container query units, capped by its width so `WPT 12` and `123.4 nm` still fit.
- `telemetry-source.js` adds the map size (`w`, `h`) to `mapinfo`, which `gridToWorld` needs.
- WPT no longer uses `shared/page-chrome.css`; the page is a fixed panel like TGT, its two columns
  stacking in a narrow pane.

## Still to check in game

- Every WPT/RTD/SPD action against the real plugin (the harness swallows `wpt.*` writes): editing a
  non-active route, FLY FROM HERE on a non-active route, the typed-in steer point's position, SHARE
  and ACCEPT/DISMISS in a squad.
- The live tape, cue, ▼ YOU and ROUTE LEFT while flying a route.
- RTD/SPD in split panes and the F-35 portal.
