# TGP map and grid slew (issue #103)

Point the TGP at a ground position chosen on the MAP page: a spot under the PAD cursor or a
clicked spot (map slew), or the centre of a typed grid square (grid slew).

## Design

- One plugin command, `tgp.slew` (`wx`, `wz` = floating-origin-corrected world X/Z), handled by
  `TgpManualControl.SlewTo`. It engages manual control if needed and locks Point Track on the
  ground point through the same `LockPointTrack` the TRK path uses, so a slew behaves like any
  Point Track: it holds as the aircraft moves, pan/tilt nudges redesignate it, and a real lock
  ends manual control.
- Ground height comes from a downward raycast against world geometry from 10 km above sea level.
  Where no terrain collider answers the point sits at sea level, which still gives the right
  bearing for a coarse slew (the log line says so).
- Manual mode enforces no aim-angle limits, so the pod always reaches the point and nothing is
  clamped. There is no line-of-sight check.

## Controls

| Control | Effect |
|---|---|
| Keybind **TGP Slew to Cursor** (`map-slew`) | `MapAction("slew-cursor")`; MAP slews to the PAD cursor at once |
| Bezel **SLEW** tap | `slew-toggle`; arms the map (label alternates between its normal colour and bordered amber); the next click, tap or Cursor Select slews |
| Bezel **SLEW** hold, keybind **TGP Slew Grid Entry** (`map-slew-grid`) | `slew-keypad`; opens the grid keypad |

The armed state is MAP's; it reports it up as the `slew` message, routed by source like `grid`
(one state per map context), and both shells light the label.

The grid keypad is a MAP overlay in NOAutopilot's keypad style: it shows one tile set per step (the
map's own major letters via `gridEntryRange`, then A-J, then digits), BACKSPACE, CANCEL and ENTER.
Opening it while it is open is a no-op. Cursor Select while it is open
presses the tile under the PAD cursor. `gridToWorld` (telemetry-source.js) is the inverse of
`gridLabel` and returns the square's centre, or null for a malformed or off-map reference.

SLEW is the last item of MAP's nav list: last on the right bank in full view, last on the final
page in a split pane.

## Decisions

- A slew engages manual control without claiming SOI (`Toggle(claimSoi: false)`). Claiming it moved
  focus to the camera, so the PAD cursor started panning the pod and the next slew keybind reached no
  MAP window.

- Two keybinds rather than one tap/hold pair: `PollTapHold` fires the tap on press, so a hold
  would slew first and then open the keypad.
- The keypad is an overlay, not a page like WPT: it is a modal that needs MAP's grid metadata.

## Needs an in-game check

- Ground-height raycast resolves on real terrain; the sea-level fallback where it does not.
- Pod holds the point as the aircraft moves; a real lock still ends manual control.
- Both keybinds on a HOTAS; keypad presses with the PAD cursor.
- SLEW tap/hold on the classic bezel and the F-35 glass, including split panes.
