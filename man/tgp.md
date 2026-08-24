# TGP

Targeting-pod camera feed zoomed on the locked target, with range and bearing.

![TGP page](images/TGP.png)

## Picture quality

Two picture sources, picked on the [TGP CFG](tgpcfg.md) page:

- **LOW** (default) — the game's own targeting-pod camera, exactly as it renders in the cockpit.
- **HIGH** — a separate, sharper camera with real tree/grass detail, plus everything below.

## HQ overlay

Only shown in HIGH quality, while a target is locked — LOW quality already has this baked into the
game's own picture. Reads:

- Target type/name, top-left, with a pilot callsign line underneath when known.
- **[JAM]** / **[LASE]** / **[OLD]** tag next to the name when the target is jammed, actively
  laser-designated, or its tracked position has gone stale.
- Range, altitude, speed (top-left) and heading, relative altitude, relative speed (top-right) —
  the last three blank out for a target too far/stale to read in detail.
- A bearing compass with a needle, bottom-left, pointing at the target relative to the pod's own
  aim, plus the raw bearing in degrees.
- Grid square, IR/COLOR mode, and magnification, bottom-right.
- A lock box drawn at the target's own screen position, one per locked target:
  - White solid — a normal hostile lock.
  - Blue solid, with an X through it — a friendly.
  - Yellow solid — jammed.
  - White dashed — the tracked position is stale.
  - Amber, with a small red cross in the middle — actively laser-designated.

## IR mode

When the pod is in IR (thermal) mode, HIGH quality shows a black-and-white picture instead of
color — a basic simulated thermal look, not a full radiometric simulation.

## Refresh rate

The feed's update rate is a live-adjustable slider on [TGP CFG](tgpcfg.md) — higher costs more
CPU/GPU and network bandwidth, lower saves it at the cost of smoothness and latency. Defaults to
15 Hz.
