# HUD compact layout

## Status

Implemented on `feature/hud-compact`, behind one F1-menu checkbox, **HUD → Compact HUD** (off by
default), which the **Toggle Compact HUD** keybind (KEY page, MFD SETUP → MISC, plus its
`compact-hud.toggle` remote-keybind command) also flips. Tuned in game on the FS-12 Revoker at 90° vertical FOV. The in-game checks at
the bottom are still open.

## Goal

Pull Nuclear Option's spread-out flight symbology into a tight cluster around the nose, close to
where an F-16 HUD puts it: speed left of the ladder, altitude right, heading tape above, all
within the area of a real HUD glass.

## How the game draws its HUD

Everything lives on one screen-space canvas, `HUDCanvas` (`FlightHud` + `CombatHUD`), at a
1080-unit reference height. Three layers matter:

- **HUD layer, fixed to the airframe.** `FlightHud.Update` moves `HUDCenter` every frame to the
  screen projection of a point 4 km ahead of the cockpit, and rolls it with the airframe. Its
  children ride along: the heading tape (`compass`), the pitch ladder (`pitchCompassCenter/
  pitchCompass`), the `waterline`, and the aircraft's `*_HUDExtras(Clone)` (a `HUDAppManager`)
  holding speed, AoA, Mach, G, altitude, climb rate, fuel and throttle arcs, gear icon and clocks.
  These are what you see looking forward.
- **HMD layer, fixed to the head.** `HeadMountedDisplay` (`HMDCenter`) holds boxed copies of speed,
  altitude and bearing at screen-fixed spots set by the game's own HMD sliders (side distance,
  side angle, top height). They hide when the nose marker comes near them, so they only matter when
  looking off the nose. The game's sliders can't compact the forward view.
- **World-projected and screen-corner elements**, such as the airbase/runway overlay, target box,
  minimap, status display and weapon pips. They stay as they are.

A one-shot dump of the canvas tree (names, components, local/anchored positions, text) mapped every
visible readout to its object. In the Revoker prefab, offsets from `HUDCenter`, in canvas units:

| Readout | Object | Offset |
|---|---|---|
| Heading tape / readout | `compass` / `BearingObject` | (0, 290) / (0, 250) |
| Speed, AoA, Mach, G | `speedGauge/Airspeed`, `AoAGauge/AOA`, `machIndicator/MachDisplay`, `Gindicator` | x −200, y +20…−65 |
| Altitude, radar alt, climb | `Altitude`, `Climbrate` | x +200, y +20…−20 |
| AoA indexer | `AoAIndexer` | (−324, 0) |
| Fuel / throttle arcs | `fuelGauge`, `throttleGauge_AB` | x ±315 |
| Gear/brake icon | `GearIndicator/gearIcon` | (267, −61) |
| Time / Local clocks | `GameTimeObject`, `OtherTimeObject` | (±250, 300) |
| Autopilot readout (another mod) | `AP_CombinedOverlay` | (−332, −110) |

No game code writes these positions or scales (only the self-positioning weapon pips, AoA bracket,
unit markers and status display do), so values the plugin sets stick.

## Design

### Blocks in degrees

`HudCompactLayoutMath.F16Preset` lists blocks. Each one has an anchor object, a target in degrees
from the nose, a size, and the members that move with it. Degrees convert to canvas units through the
camera's current vertical FOV (gnomonic: half the canvas height maps to `tan(fov/2)`), so the
cluster keeps its place against the ladder when zooming. Each member keeps its prefab offset from
the anchor, scaled with the block. Offsets are measured in `HUDCenter` space because some members
sit under offset parents (`MachDisplay` is under a `machIndicator` 25 units down). The fuel and
throttle arcs move as whole gauges, never scaled from inside, because each pointer rides a
pre-drawn arc image.

### Tuning

The preset values were tuned in game with temporary sliders: whole-cluster spread, offset and size
at first, then per-instrument offset and size for speed, altitude and heading, plus a ladder-window
size. Once tuned, they were baked into the preset and the sliders removed, leaving the single
checkbox. Tuned values (degrees from the nose, size):

| Block | Target | Size |
|---|---|---|
| Speed / AoA / Mach / G | (−9.5°, +0.5°) | 0.5 |
| Altitude / climb | (+9.5°, −0.5°) | 0.5 |
| Heading tape / readout | (0°, +12.75°) | 0.55 |
| Fuel arc + AoA indexer, throttle arc + gear icon | (∓24°, 0°) | 1 |
| Clocks | (∓13°, −14°) | 1 |

### Pitch ladder

The ladder is a 400×450 `RawImage` window onto a ladder texture spanning 180° of pitch.
`FlightHud` slides its `uvRect` with pitch and scales it by `50/fov` every frame. That scale is a
linear approximation that is exact near 60° FOV and about 15% too large at 90°. In `LateUpdate`,
after `FlightHud.Update`, the plugin replaces it with the conformal scale from the camera's
units-per-degree at screen center (`TrueLadderScale`), so the 10° rung sits 10° above the horizon.

The ladder can't be shrunk uniformly or moved off the nose: either would break its horizon
reference. During tuning, a ladder-size slider instead cropped the window evenly (smaller
`sizeDelta`, `uvRect` cropped about its center), which keeps rung spacing true. The tuned value was
1, so the crop was removed with the sliders.

### Lifecycle

`HudCompactLayout` rides the mission runner object like `HudDeclutter`. Originals are captured once
per `HUDAppManager` instance. The compass, ladder and waterline belong to `FlightHud` and survive a
respawn, so everything is restored before a re-capture. Turning Compact HUD off restores positions
and scales; the ladder scale comes back on `FlightHud`'s next frame.
An exception logs one warning, restores the native layout and disables the component.

## Known limits

- One preset, tuned on the Revoker. Other airframes use the same object names, but their prefab
  spacing may differ. A per-aircraft table is the upgrade path.
- `AP_CombinedOverlay` belongs to another mod, which may reposition it itself.
- The true ladder scale assumes the ladder texture is linear in pitch, as `FlightHud`'s uv mapping
  implies.

## In-game check

- Compact HUD on reproduces the tuned layout. The left block's rows stay separate.
- In level flight the horizon rung sits on the real horizon. Pitched up 10°, the 10 rung sits on
  the waterline. Check at the default FOV and zoomed.
- Toggling Compact HUD off restores everything, and the layout re-applies after a respawn.
- Try a second airframe.
