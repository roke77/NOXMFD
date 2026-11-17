# ENG page rework — per-engine diagnostics, tanks, endurance and range

## Goal

ENG becomes an engine-and-fuel status page: one card per engine with its RPM, thrust (or shaft
power), health and status; a centre pedestal with the throttle lever and IR heat tape; and a fuel
band with the total fuel dial, one bar per tank (leak and fire flagged), and a calculated burn rate,
endurance and range. It replaces the four aircraft-wide FUEL/HEAT/RPM/THRL dials. SYS is unchanged.

The layout is design **A1 · Dial cluster + throttle pedestal** from the ENG Page Variants canvas.

## Data the game provides

| Shown | Source | Notes |
|---|---|---|
| Engine list | `Aircraft.engineStates` (`List<IEngine>`) | Every propulsion type implements `IEngine`. |
| RPM | `IEngine.GetRPMRatio()` | Already normalized 0..1. |
| Thrust | `IEngine.GetThrust()` / `GetMaxThrust()` (N) | Turboshafts (`TurbineEngine`) return 0 for both. |
| Shaft power | `IPowerSource.GetPower()` / `GetMaxPower()` (W) | Shown instead of thrust when max thrust is 0, matching the cockpit `EngineTelemetry` widget's choice. |
| Status | `IEngine.OnEngineDamage` → FAULT, `OnEngineDisable` → INOP | The same events the cockpit widget listens to; no engine type has a public damaged flag. |
| Health (HP) | private `condition` (0..1) | Present on Turbojet, Turbofan, TurbineEngine, PropFan, RotorShaft. DuctedFan and ConstantSpeedProp have none: HP shows `--`. |
| L/R placement | engine/tank `transform` in aircraft-local space | Sorted by local x; labels are L/R for two engines, ENG for one, ENG 1..n otherwise. |
| Propellers | `ConstantSpeedProp` in `engineStates` | Driven by a turboshaft (Tarantula: 4 turboshafts + 4 props). `GetMaxThrust()` is always 0 and there is no `condition`, so a prop card shows thrust as a plain kN readout and HP `--`. Numbered apart from the engines: PROP 1..n. |
| Tanks | `Aircraft.GetFuelTanks()` | `fuelMass`, `GetCapacity()` public. |
| Leak / fire | private `FuelTank.leakRate > 0` / `onFire` | Reflection, logged once if the fields are renamed. |
| Ground speed | `Aircraft.rb.velocity`, horizontal | Unaffected by the floating origin. |
| Throttle, AB zone, heat | existing `thr`/`hasAb`/`abStart`/`heat`/`heatColor` | Unchanged sources. |

## What the game does not provide

- **Per-engine fire.** `Turbojet.engineFire` exists but nothing in the game ever sets it, and no
  other engine type has a fire state. The cockpit's `L ENG FIRE`-style warnings are the failure
  message an engine reports when it is disabled, not a separate fire condition. The A1 mockup's
  FIRE tag on an engine card is therefore dropped: a dead engine reads INOP. Tank fires are real
  (`FuelTank.onFire`) and show on the tank bar.
- **Fuel flow / burn rate.** No engine exposes fuel flow publicly, and leaks drain tanks directly.
  Burn is calculated (below).
- **Tank display names.** Tanks only have GameObject names. The bar label is derived from that name
  (each word cut to four letters, eight characters in all), so its readability depends on the
  game's naming.

## Calculated values

- **Burn rate** (`FuelBurnEstimator`, plugin): the drop in total fuel mass over game time, sampled
  over windows of at least 1 s and smoothed with a 10 s time constant. It includes leak loss, so
  endurance and range stay honest with a punctured tank. Paused game time adds no sample; a refuel
  (mass going up) or a clock rewind clears the estimate. Reads `--` until the first window.
- **Endurance** = fuel mass ÷ burn rate, shown h:mm. `--:--` with no estimate or zero burn.
- **Range** = endurance × ground speed, shown in km or nm. `--` below 10 m/s ground speed.
- Units follow the player's Imperial/Metric setting (`metric`): kg / km / km/h or lb / nm / kt.
  Thrust and power stay kN / kW, as on the game's own cockpit gauges.

## Wire format

A new top-level `eng` block (`TelemetryJson.EngBlock`), passed through the `avn` slice as `eng`
alongside `metric`:

```json
"eng": {
  "fuelKg": 3420.0, "fuelCapKg": 5100.0, "burn": 0.6833, "gs": 216.7,
  "engines": [{ "x": -1.20, "rpm": 0.580, "thr": 31000, "thrMax": 79000,
                "pwr": -1, "pwrMax": -1, "hp": 0.380, "st": 1, "prop": false }],
  "tanks": [{ "n": "wing_L", "x": -3.00, "kg": 310.0, "cap": 850.0, "leak": true, "fire": false }]
}
```

`st`: 0 OK, 1 FAULT, 2 INOP. `prop`: the entry is a propeller, fixed from its type when first
seen. `pwr`/`pwrMax` are -1 for engines that aren't power sources; `hp` is -1 when the engine type
has no `condition`.

## Layout

- Engine cards split either side of the pedestal (`ceil(n/2)` on the left); cards right of it
  mirror, HP tape on the outer edge.
- Each side is a grid: one column while a side holds up to two cards (stacked, so each card is wide
  and sets its two dials side by side), two columns from three a side (2x2 for eight entries).
- A tall, narrow card lays the HP tape flat under the dials.
- In a tall pane the fuel band puts the tank bars on their own row.
- ENG joins the classic layout's `LABEL_INSET_PAGES`, so the bezel labels never cover a card.
- An engine that blows off keeps its card, reading INOP with zero output.

## ECAM skin (F-35 layout)

The F-35 layout mounts ENG as `/avn?ecam` (`layout-pages.js`); the classic layout keeps the dials
above. Same structure, data and multi-engine grids, drawn ECAM-style (canvas artboard A2):

- Dials: an open 210deg white arc with ticks at 0, 50 and 100%, a coloured needle, the value boxed
  in grey at the lower right and the cyan label + unit at the lower left. No fill arc.
- RPM dials carry a cyan throttle-command index at the lever position.
- FUEL dial: fuel mass boxed, an amber tick at 25%, the fraction and capacity as a `67 % OF 5,100`
  sub-line.
- Grey rules replace the rounded panels; HP, heat and tank tapes are white outlines with a
  translucent fill under a solid top line; labels and units cyan, values green.
- Throttle lever: a white scale line, the AB zone red (a bar on the line plus a bracket, red AB tag),
  a translucent green bar from IDLE to an outlined handle.

`avn.js` reads the flag before its DOM refs and replaces the template and fuel gauges' inner markup
(`avnEcamGaugeHtml`); `body.ecam` scopes the skin's rules at the end of `avn.css`. The cyan and grey
are the page tokens `--no-ecam-cyan` / `--no-ecam-frame`, not the F-35 shell's `--no-teal`.

## Live-game verification

Not exercisable in the browser harness; check in a mission:

1. Twin-jet: L/R cards on the correct sides; RPM/thrust track the throttle; AB zone and lever.
2. Take engine damage: card goes FAULT with HP dropping; a kill goes INOP. Compare with the
   cockpit engine panel where the aircraft has one.
3. Helicopter: one ENG card showing PWR in kW. Tarantula: ENG 1..4 (PWR) and PROP 1..4 (THR kN
   readout) in a 2x2 grid each side.
4. Prop or ducted-fan aircraft: HP reads `--`, status still follows damage.
5. Tank bars: labels readable, masses sum to the fuel dial, a punctured tank shows LEAK, a burning
   one FIRE.
6. Burn/endurance/range settle within ~10 s of a throttle change; refuel/rearm clears and
   re-establishes the estimate; pausing leaves the values frozen.
7. Imperial setting switches to lb / nm / kt.
8. Log shows no `[NOXMFD]` warning about `condition`, `leakRate` or `onFire`.
9. F-35 layout: ENG draws the ECAM skin; the RPM dials' cyan index follows the throttle, the AB
   zone is red on an AB aircraft and absent otherwise.
