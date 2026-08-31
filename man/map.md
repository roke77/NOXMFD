# MAP

Full-screen tactical map showing friendly/hostile units and your own position. Click a unit to
target it. Your own plane always renders green; while you're in a [squad](sqd.md), every other
squad member's plane renders in the squad's teal instead of its plain friendly/hostile color, so
you can spot them at a glance.

![MAP page](images/MAP.png)

## Controls

- **FLW** — toggle follow: recenter the map on your own position as you fly, instead of staying
  wherever you last panned it.
- **CFG** — open [MAP's own settings page](mapcfg.md) (the telemetry refresh rate).
- **GRID** — toggle a coordinate grid overlay on the map. Off by default.
- **Z+ / Z−** — zoom in / out.
- **WPT** — open the [waypoint/route editor](wpt.md).
- **R+ / R−** — switch the active waypoint route to the next / previous one you've saved.
- **W+ / W−** — manually step to the next / previous waypoint on the active route, without
  waiting to fly to it.

## Status row

A row in the bottom-right corner, each item shown only while it applies:

- **CURSOR** — the grid square under your mouse or PAD cursor.
- **GRID** — the grid square you're currently in.
- **ROUTE** — the active route's name.

## HOTAS

Every control above can be driven without touching the screen — a HOTAS cursor works the map the
same way a mouse click does. See [PAD cursor](keybinds.md#pad-cursor).
