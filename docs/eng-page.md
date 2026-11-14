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
                "pwr": -1, "pwrMax": -1, "hp": 0.380, "st": 1 }],
  "tanks": [{ "n": "wing_L", "x": -3.00, "kg": 310.0, "cap": 850.0, "leak": true, "fire": false }]
}
```

`st`: 0 OK, 1 FAULT, 2 INOP. `pwr`/`pwrMax` are -1 for engines that aren't power sources; `hp` is
-1 when the engine type has no `condition`.

## Layout

- Engine cards split either side of the pedestal (`ceil(n/2)` on the left); cards right of it
  mirror, HP tape on the outer edge.
- A wide, short card sets its two dials side by side; a tall, narrow card (three or more engines)
  lays the HP tape flat under the dials.
- In a tall pane the fuel band puts the tank bars on their own row.
- ENG joins the classic layout's `LABEL_INSET_PAGES`, so the bezel labels never cover a card.
- An engine that blows off keeps its card, reading INOP with zero output.

## Live-game verification

Not exercisable in the browser harness; check in a mission:

1. Twin-jet: L/R cards on the correct sides; RPM/thrust track the throttle; AB zone and lever.
2. Take engine damage: card goes FAULT with HP dropping; a kill goes INOP. Compare with the
   cockpit engine panel where the aircraft has one.
3. Helicopter: one ENG card showing PWR in kW.
4. Prop or ducted-fan aircraft: HP reads `--`, status still follows damage.
5. Tank bars: labels readable, masses sum to the fuel dial, a punctured tank shows LEAK, a burning
   one FIRE.
6. Burn/endurance/range settle within ~10 s of a throttle change; refuel/rearm clears and
   re-establishes the estimate; pausing leaves the values frozen.
7. Imperial setting switches to lb / nm / kt.
8. Log shows no `[NOXMFD]` warning about `condition`, `leakRate` or `onFire`.
