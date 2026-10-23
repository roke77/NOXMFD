# ADI and HSI pages

## Status

**Planning; designs approved, open questions answered** (see Decisions). Nothing built yet.
Branch `feature/adi-hsi-pages`.

## Request

A player asked: *"Could we get an attitude indicator and gyrocompass page?"*

A first mockup (`_scratch/claude_designs/ADI page mockup.html`, local only) stacked an attitude
ball above an HSI. The plan splits that into two independent MFD pages:

- **ADI** (attitude director indicator): the attitude ball and the flight-data readouts around it.
- **HSI** (horizontal situation indicator): the gyrocompass the player asked for, plus navigation
  to the active waypoint or steer point.

Each page fills its own pane, so a pilot can put them in split view or show only one.

The approved layouts are the "ADI B · Side tapes" and "HSI" boards on the Claude Design canvas
[ADI and HSI pages](https://claude.ai/artifact/4q4peWLtXDajDKnLwZ1cY1), drawn in the classic bezel
(890 px glass). Both keep a 64 px inset on **both** sides of the glass: the left one clears the
shell's vertical MAIN label, and the right one mirrors it so the instrument sits centred. Labels are
20 px near-white (`--no-label`) and values 44 px, at that glass size. The canvas's "ADI A · Classic
ball" board was the runner-up.

## What the game provides

From decompiling `Assembly-CSharp.dll`. The game draws its own instruments as `HUDApp`s, and every
value they show is a plain transform or rigidbody read, so the plugin can reproduce each one
exactly. No reflection needed; all members are public.

| Value | Game source (the `HUDApp` it mirrors) |
|---|---|
| Pitch | `aircraft.transform.eulerAngles.x`, wrapped to ±180 and sign-flipped so nose-up is positive (`ArtificialHorizon`, `BasicFlightInstruments`) |
| Roll | `aircraft.transform.eulerAngles.z`, wrapped to ±180 and sign-flipped so right-wing-down is positive (`ArtificialHorizon`) |
| Heading | `aircraft.transform.eulerAngles.y` (`Bearing`, `FlightHud.compass`) |
| Vertical speed | `Vector3.Dot(aircraft.CockpitRB().velocity, Vector3.up)` (`Climbrate`) |
| G load | `Vector3.Dot(aircraft.pilots[0].GetAccel() + Vector3.up, aircraft.transform.up)` (`GIndicators`) |
| AoA | `atan2(v.y, v.z)` of `cockpit.transform.InverseTransformDirection(cockpit.rb.velocity)`, shown above 10 m/s (`AoADisplay`) |
| Sideslip | the same local velocity's x component, `atan2(v.x, v.z)` (no game widget) |
| Mach | `aircraft.speed / LevelInfo.GetSpeedOfSound(GlobalPosition().y)` (`MachIndicator`) |
| Turn rate | `rb.angularVelocity` projected on world up (no game widget) |

The game has **no magnetic model**: every compass in it shows true heading, 0 = +Z. The HSI shows
true heading, untagged since there is no other reference to tell it apart from; it doesn't
invent variation or gyro drift.

Near ±90° pitch, Euler roll and heading flip (gimbal lock). The game's own horizon has the same
behaviour, and the ADI matches it.

## Telemetry today

The own-ship frame already carries `hdg`, `tas`, `agl` and `world.y`. The shell already derives the
`mapinfo` slice (position, heading, map meta, `metric`) and relays it to the WPT family
(`WPT_PAGES = ['wpt', 'rtd', 'spd']`), and route/steer-point data reaches pages through
`WaypointsStore` + `wpt-routes`. **The HSI needs no new plugin data**; the ADI does.

## ADI page (`pages/adi/`)

Layout ("ADI B · Side tapes"):

- **Attitude ball** in the centre, between the tapes: sky/ground split, pitch ladder (5° rungs,
  labels every 10°), fixed bank scale (10/20/30/45/60) with a sky pointer that rolls with the
  ball, fixed amber aircraft symbol, slip ball under the ball.
- **Airspeed tape** down the left side and **altitude tape** down the right, each scrolling past a
  fixed boxed pointer at horizon level that shows the exact value; the pointer has a notch facing
  the ball. Airspeed: a mark every 5 kt, a number every 10 kt, ±50 kt visible. Altitude: a mark
  every 50 ft, a number every 100 ft, ±500 ft visible. Tape headers: TAS KT and ALT FT.
- **Top corners**: Mach (left), vertical speed (right).
- **Bottom row**: four equal tiles: G, max G (session), AoA, radar altitude.

### Plugin changes

New `TelemetrySnapshot` fields, read in `TelemetryReader`'s own-ship block next to `heading`, and
appended in `TelemetryJson` with `sb.Append` (as `mapReach` is) rather than new `{N}` slots:

| JSON key | Field | Notes |
|---|---|---|
| `pitch` | pitch, degrees, nose-up positive | |
| `roll` | roll, degrees, right-wing-down positive | |
| `vs` | vertical speed, m/s | |
| `g` | G load | session max is kept client-side |
| `aoa` | AoA, degrees | `null` (or a sentinel) below 10 m/s, as the game hides it |
| `beta` | sideslip, degrees | drives the slip ball |
| `mach` | Mach | |
| `turn` | turn rate, degrees/second, right positive | `rb.angularVelocity` around world up; read by the HSI |

Eight floats per frame, about 80 bytes. The math that turns raw Euler angles and vectors into these
numbers (pitch wrap and sign, AoA, sideslip) goes in a Unity-free static helper so `tools/tests/`
can cover it.

### Web changes

- `telemetry-source.js` derives a new `adi` slice from the frame (the eight fields plus `tas`,
  `agl`, `world.y`, `metric`) and posts it up; the shells relay it like the other slices.
- The page interpolates pitch, roll, heading and both tapes between frames (see "Update rate").
- Units follow the game's unit setting (`metric`), as RTD does.
- The ball's sky and ground are two new theme tokens, `--no-sky` and `--no-ground` (blue and brown
  defaults), editable on the UI page like every other colour.

## HSI page (`pages/hsi/`)

Layout ("HSI"):

- **Compass rose** filling the centre, rotating under a fixed lubber line, with a boxed heading
  readout above it and a fixed own-ship symbol in the middle.
- Bearing pointer (magenta) to the current navigation target.
- Course arrow and course deviation bar (amber), with deviation dots.
- Turn-rate trend arc: where the heading will be in 6 s.
- **Corners**, two readouts each: target and bearing (top-left), course and cross-track
  (top-right), distance and ETE (bottom-left), turn rate and TAS (bottom-right). Navigation
  readouts are magenta, course readouts amber.
- **No navigation target**: the pointer, course arrow and deviation bar hide, the rose shows
  NO NAV TARGET, and the navigation readouts show a dash. Heading, the trend arc and turn rate keep
  working.

### Data

The HSI takes the WPT family's data (it joins `WPT_PAGES`) plus the `adi` slice:

- Navigation target: `WptRoute.navigationTarget(WaypointsStore.load())`, the same one WPT, RTD and
  the HUD cue use (active route's next waypoint, else the active steer point).
- Bearing and distance: `WptRoute.distanceBearing`.
- ETE: distance / `tas` (from the `adi` slice).
- Turn rate and the 6 s trend arc: the plugin's `turn` field (from the `adi` slice).
- Course: the bearing of the active leg (previous waypoint → next waypoint). A target with no leg
  gets a **direct-to course**: the bearing from where the aircraft was when the target became
  active. That covers an active steer point and a route's first waypoint. The plugin's
  `RouteStore` records that position on activation and serves it with the route/steer-point data,
  so every display draws the same course.
- Cross-track: own position against the course line. **One dot is 1 nm** (2 km when metric), two
  dots each side; past that the bar pins at the edge.

New pure helpers (leg course, direct-to course, cross-track, deviation clamping, the trend span)
go in `wpt-route.js` next to the existing route math, with cases in `wpt-route.test.js`. The
activation-position bookkeeping in `RouteStore` gets xUnit cases in `tools/tests/`.

## Update rate

Own-ship telemetry arrives at `FastHz`: 10 Hz by default, 1–30 Hz on MAP CFG. At 10 Hz a horizon
visibly steps. Both pages interpolate pitch, roll, heading and the tapes toward the latest frame on
`requestAnimationFrame`, wrapping heading across 0/360, and stop the loop when the page is hidden.
The telemetry rate itself stays as it is. The cost is one small SVG transform per element per
display frame, per [performance.md](performance.md).

## Navigation: the Avionics hub

MAIN's AVN key becomes the **Avionics** hub for four pages, with a left-bank switch between them
(the pattern the WPT and MD families use):

| Code | Page |
|---|---|
| ADI | attitude (new) |
| AFM | airframe (moves here from MAIN) |
| HSI | navigation compass (new) |
| SYS | today's AVN page, renamed: fuel, throttle, heat and RPM gauges and the status lamps |

The switch lists them alphabetically, and the AVN key opens the first one, ADI. AFM leaves
MAIN's extras (`BEZEL_EXTRAS.main`, `f35.js`'s `MAIN_EXTRAS`), which frees a MAIN key.

The AVN → SYS rename touches the page folder (`pages/avn/` → `pages/sys/`), its route, `NAV`
entries, the embedded resources, `man/avn.md` and the docs that name the page.

## Wiring checklist

Per [src-architecture.md](src-architecture.md) → "Shell hooks", for each of `adi` and `hsi`, and for
the renamed `sys`:

- `NAV` entries (`mfd.js`, `f35.js`, `nav-model.js`): the hub's switch row on each of the four
  pages, and the AVN key's action landing on ADI.
- `layout-pages.js` `CLASSIC_FULL`, `split-slots.js`.
- `mfd.js`: `showPage` + `mfdButton` cases (`classic-button-wiring.test.js` catches a missing one),
  and a relay entry (`adi` slice; HSI added to `WPT_PAGES`).
- `f35.js` slice map: `adi: ['adi']`, `hsi: ['mapinfo', 'wpt-routes', 'adi']`.
- `TelemetryServer` routes and `NOXMFD.csproj` embedded resources for the new files
  (`server-route-coverage.test.js`).
- A `man/` page for each, and `man/avn.md` → `man/sys.md`.

## Testing

- `wpt-route.test.js`: leg course, cross-track sign and scale, trend span.
- `tools/tests/`: the plugin's attitude math helper (pitch wrap/sign, AoA, sideslip, at zero speed).
- Harness: both pages in `serve_web.py` with mock frames, including roll through ±180, heading
  through 0/360, and no navigation target.
- **In game:** ball direction and sign for pitch and roll, G and AoA against the game's own HUD
  readouts, slip ball direction, HSI bearing pointer against the WPT compass, and behaviour on
  respawn and aircraft change.

## Decisions

The planning questions and their answers:

1. **Placement**: AVN becomes the Avionics hub (ADI, AFM, HSI, SYS), opening on ADI; codes `ADI`
   and `HSI`. See "Navigation: the Avionics hub".
2. **ADI readouts**: all of them, as in the approved design.
3. **Ball colours**: themeable tokens `--no-sky` / `--no-ground`.
4. **Course with no leg** (steer point, a route's first waypoint): direct-to course from the
   activation position, stored in `RouteStore`.
5. **Turn rate**: a plugin field, `turn`.
6. **Deviation scale**: 1 nm per dot (2 km when metric), two dots each side.
7. **Update rate**: interpolate on the page; the telemetry rate doesn't change.
8. **Combined page**: none; split view covers it.
9. **In-cockpit MFD**: web only for now.
