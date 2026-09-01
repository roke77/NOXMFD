# WPT steer points

## Status

Implemented on `steer-points` for issue #73. The branch is based on `map-squad-styling`; it is not
merged to `main` yet.

## Requirements

The WPT page has a **STEER POINTS** table below the active route's waypoint table. A steer point is
a persistent, named map position with no route ordering or automatic state changes:

- Long-press MAP with an active route to append a waypoint, as before.
- Long-press MAP with no active route to create and select a steer point.
- Selecting another steer point changes guidance immediately. Flying over one does not advance it.
- An active route always owns navigation guidance. Deactivating it restores the prior steer-point
  selection; a completed-but-still-active route does not silently fall through to a steer point.
- With an active route, the navigation bezel pair is **W+ / W−** and steps route progress.
- With no active route, the pair is **S+ / S−** and cycles saved steer points with wraparound.
  The pair is hidden only when there is neither an active route nor any steer point to cycle.
- The KEY page names the physical actions **Next/Previous Waypoint / Steer Point** and explains the
  same route-dependent behavior.
- Steer points are persistent, exportable/importable, and individually shareable by a squad leader.

## Data ownership and navigation priority

`RouteStore` remains the single source of truth. The existing routes JSON file now also stores
`activeSteerPointId` and `steerPoints`; `/wpt-options` serves those fields to every display alongside
the route library. The browser does not keep a second writable copy.

`TryGetActiveNavigationPoint` is the authoritative resolver used by `HudWaypointCue`. The matching
`navigationTarget` helper drives WPT's compass/readout. Both enforce the same order:

1. If a route is active and has a next waypoint, use that waypoint.
2. If a route is active but complete, expose no navigation target.
3. Only when no route is active, use `activeSteerPointId`.

Route proximity advance remains route-only. Steer points never participate in `AdvanceIfNear`.

## Commands and page behavior

The browser uses context-level commands for actions whose meaning depends on route state:

- `wpt.add-navigation-point` chooses waypoint versus steer point in `RouteStore`.
- `wpt.step-navigation` chooses route progress versus steer-point cycling in `RouteStore`.

Keeping that decision server-side prevents an out-of-date browser cache from sending the wrong
mutation during a route activation change. Direct steer-point CRUD/select/import commands remain
available for the WPT editor: `add-steerpoint`, `rename-steerpoint`, `delete-steerpoint`,
`set-active-steerpoint`, `cycle-steerpoint`, and `import-steerpoints`.

MAP draws steer points as diamonds over the map image. The effective steer point uses amber; other
points use cyan. Route waypoint/line rendering is unchanged. The native HUD uses `STP · NAME` for
steer-point guidance and keeps `WPT n · NAME` for route guidance.

## Import/export

The portable collection format intentionally excludes storage ids and selection:

```json
{
  "steerPoints": [
    { "name": "INGRESS", "x": 7526.1, "z": 8584.3 }
  ]
}
```

Import validates the whole non-empty collection before mutation, appends every point with a fresh
local id, and selects the first imported point. Existing steer points are not replaced.

## Squadron sharing

Steer points follow the existing route-sharing lifecycle with separate payload types:

- `wpt.steerpoint` carries one point with the leader-owned id.
- `wpt.steerpoint-deleted` is the id-only delete tombstone.
- A received point is pending until ACCEPT or REJECT.
- Accepted points are read-only while the sender remains the squad leader.
- Repeated payloads update the matching pending/accepted point instead of duplicating it.
- After the first SHARE, leader edits auto-rebroadcast and deletion sends the tombstone.
- Ending the squad or changing leader clears pending entries and unlocks accepted points.

Individual sharing matches the UI's per-row share action and avoids replacing a member's unrelated
local steer-point library.

## Verification

- `dotnet test tools/tests/NOXMFD.Tests.csproj -c Release` covers navigation priority, completed
  routes, wraparound, static behavior, import validation, and the squad lifecycle.
- `wpt-route.test.js` covers browser target resolution and portable JSON.
- `split-slots.test.js` covers route/steer action filtering and W+/W− to S+/S− labels.
- The full JS suite and repository CI check cover the remaining frontend and build contracts.
- The preview harness at `/wpt`, classic full/split MAP, and F-35 MAP verifies the table, symbols,
  effective readout, and dynamic labels in each shell.

## Remaining validation

An in-game flight test should confirm that a steer point selected on WPT drives both the native HUD
cue and MAP/WPT guidance, survives a restart, and resumes after an active route is deactivated. A
two-player squad test should confirm accept/update/delete behavior over the real Steam transport.
