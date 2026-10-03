# WPT

Build waypoint routes or select a standalone steer point for in-flight guidance. WPT manages them;
**RTD** and **SPD** fly them. The left bezel keys switch between MAP, WPT, RTD and SPD.

![WPT page](images/WPT.png)

## Adding navigation points

Long-press a spot on [MAP](map.md). If a route is active, the point is appended to that route as a
waypoint. If no route is active, it is added to the steer points instead. A steer point can also be
added by typing its grid square (see below).

## Routes

The left column lists every route you've saved, plus the steer-point group. Tap a card to open it on
the right; the route you open doesn't have to be the active one.

- **+ NEW** creates a route, **IMPORT** pastes one in, **CLEAR** wipes every saved route at once.
- In the open route: **ACTIVATE / DEACTIVATE**, **RENAME**, **RESET** (progress back to the start),
  **EXPORT**, **SHARE** and **DELETE**.
- Tap a waypoint for **RENAME**, **FLY FROM HERE** (makes it the next point, activating the route if
  needed), **▲ / ▼** to reorder, and **DELETE**.

The strip at the top shows the next point's bearing, distance and the distance left on the route,
with a relative-bearing compass. Route progress auto-advances as you approach each waypoint.

## Steer points

Steer points are independent map positions rather than an ordered route. Open the **STEER POINTS**
card, then tap a point for **GUIDE TO**, **RENAME**, **SHARE** or **DELETE**. **+ NEW STEER POINT**
takes a grid square such as `Fd40` and adds a point at its centre.

The selected point stays active until you select another point or activate a route; flying over it
does not advance or deactivate it. An active route always has guidance priority. Deactivating that
route restores the previously selected steer point. While no route is active, MAP changes
**W+ / W−** to **S+ / S−** and those buttons cycle the steer-point selection.

## RTD — route details

The active route's guidance: a heading tape with a bug on the next waypoint's bearing, the next
waypoint with its distance, bearing and which way to turn, and the whole route as a timeline. Up to
six waypoints sit on a row; a longer route continues on the next row. Tap a waypoint on the timeline
to fly direct to it. Below it, the next two waypoints and the distance left.

![RTD page](images/RTD.png)

## SPD — steer-point details

The same heading tape and readout for the selected steer point, with every steer point as a button
beneath it. Tap one to guide to it.

![SPD page](images/SPD.png)

## Import / export

**IMPORT**, and each route's own **EXPORT**, turn a route into pasteable JSON.

## Where navigation data lives

Routes and steer points are stored by the mod itself, not any one browser: they show on every
connected display and survive a full game restart. Route progress keeps advancing whether or not
the WPT page is open; steer points remain static.

## Squad sharing

A squad leader (see [SQD](sqd.md)) can share a route or individual steer point with their squad. It
shows up for members as a SHARED entry with ACCEPT/DISMISS. Later edits by the leader re-broadcast
automatically, and accepted data unlocks for editing once the squad ends or the leader changes.

## In-game HUD cue

The effective navigation target also shows on the **in-game HUD**: an amber bug rides the game's
own heading tape, with distance and bearing beside it. Route guidance reads `WPT n · NAME`; steer-
point guidance reads `STPn · NAME` (`n` is the point's position in the steer-point list). Past
±45° of the nose, the bug becomes a sideways arrow pinned
at the edge it left, pointing the way to turn.

![In-game HUD waypoint cue](images/WPT_HUD.png)
