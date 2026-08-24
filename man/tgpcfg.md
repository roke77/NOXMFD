# TGP — CFG

[TGP](tgp.md)'s own settings page, reached from TGP's CFG key. One live-adjustable slider plus a
picture-quality toggle.

## TGP

The [targeting-pod camera feed](tgp.md)'s capture rate, independent of MAP's own telemetry rate.
Defaults to 15 Hz, adjustable up to 60 Hz — above 15 Hz the GPU readback can start dropping
captures on modest hardware, so raise it only if you have GPU headroom to spend. Higher rates cost
more CPU/GPU and network bandwidth; lower rates save it at the cost of smoothness and latency.
Changes apply immediately and persist across restarts.

## LOW / HIGH

Picks the feed's picture source:

- **LOW** (default) — reads the game's own targeting-pod camera as-is.
- **HIGH** — renders a separate, higher-resolution camera every frame instead: a sharper picture,
  real tree/grass detail, a mag/range/grid/target-detail overlay with a per-target lock box, and a
  basic thermal-style black-and-white look when the pod is in IR mode. Costs an extra render pass —
  still an early-pass feature, expect rough edges.

## RESET TO DEFAULTS

Restores the slider to 15 Hz and the quality to LOW.
