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
- **WPT** — open the [route and steer-point editor](wpt.md).
- **R+ / R−** — switch the active waypoint route to the next / previous one you've saved.
- **W+ / W−** — with a route active, manually step to its next / previous waypoint.
- **S+ / S−** — the same controls relabel automatically when no route is active; they select the
  next / previous saved steer point instead.

Long-press the map to append a waypoint to the active route. With no route active, the same gesture
creates a standalone steer point. Both kinds of navigation point are managed on [WPT](wpt.md).

## Status row

A row in the bottom-right corner, each item shown only while it applies:

- **CURSOR** — the grid square under your mouse or PAD cursor.
- **GRID** — the grid square you're currently in.
- **ROUTE** — the active route's name.

## HOTAS

Every control above can be driven without touching the screen — a HOTAS cursor works the map the
same way a mouse click does. See [PAD cursor](keybinds.md#pad-cursor).
