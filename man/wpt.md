# WPT

Plot custom waypoints and chain them into an ordered route that guides you in-flight.

![WPT page](images/WPT.png)

## Adding waypoints

Long-press a spot on [MAP](map.md) to drop a waypoint into the active route.

## Routes

The WPT page lists every route you've saved:

- **Create / rename / delete** a route.
- **Click a route** to activate it. Click the active one again to deactivate it — it stays saved,
  just no longer the one you're navigating.
- **CLEAR** wipes every saved route at once.

## Waypoints (within the active route)

- **Rename, reorder, or delete** any waypoint.
- **Reset progress** back to any waypoint, if you've already passed it.
- A **distance/bearing readout** and a **relative-bearing compass** point to the next waypoint,
  and auto-advance to the one after it as you approach.

## Import / export

**IMPORT**, and each route's own export button, turn a route into pasteable JSON — back one up, or
hand it to another pilot.

## Where routes live

Routes are stored by the mod itself, not any one browser: the same routes show on every connected
display (PC, tablet, phone), survive a full game restart, and keep advancing whether or not the
WPT page is even open.

## In-game HUD cue

The active waypoint also shows on the **in-game HUD**: an amber bug rides the game's own heading
tape at the waypoint's bearing, with a `WPT n · NAME` / distance · bearing readout beside it. Past
±45° of the nose — the tape only spans 90° — the bug becomes a sideways arrow pinned at the edge
it left, pointing the way to turn. It shows whenever a route is active.

![In-game HUD waypoint cue](images/WPT_HUD.png)
