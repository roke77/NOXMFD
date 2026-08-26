# TGP — CFG

[TGP](tgp.md)'s own settings page, reached from TGP's CFG key. It controls the feed refresh rate,
resolution, JPEG quality, and an optional cockpit-feed hide toggle.

![TGP CFG page](images/TGP_CFG.jpeg)

## TGP

The [targeting-pod camera feed](tgp.md)'s capture rate, independent of MAP's own telemetry rate.
Defaults to 15 Hz, adjustable up to 60 Hz — above 15 Hz the GPU readback can start dropping
captures on modest hardware, so raise it only if you have GPU headroom to spend. Higher rates cost
more CPU/GPU and network bandwidth; lower rates save it at the cost of smoothness and latency.
Changes apply immediately and persist across restarts.

## FEED RESOLUTION

Picks the feed's picture source — independent of FEED QUALITY below, and the same choice applies
whether [TGP](tgp.md) is showing a real unit lock or [manual camera control](tgp.md#manual-camera-control):

- **LOW** (default) — reads the game's own targeting-pod camera as-is, at its native ~360×240.
  Stat overlay and crosshair are already baked into the picture either way.
- **MID** — renders a separate camera at 720×480 instead: a sharper picture with real tree/grass
  detail, a stat overlay drawn on top of the page (target details when locked,
  [pointing details](tgp.md#manual-mode-overlay) in manual control), and a basic thermal-style
  black-and-white look when the pod is in IR mode.
- **HIGH** — the same separate camera at 1080×720 — roughly 2.25x MID's pixels for a sharper
  picture still.

MID and HIGH both render an extra camera every frame and cost more GPU time than LOW; HIGH also
reads back and encodes more pixels than MID. A warning appears under this row whenever either is
selected.

## FEED QUALITY

Controls JPEG compression only, independent of resolution — it does not change the camera or its
pixel count:

- **LOW** — JPEG quality 30. Smaller stream, more visible compression artifacts.
- **MID** (default) — JPEG quality 50. Today's shipped, unchanged-by-default behavior.
- **HIGH** — JPEG quality 90. Retains more fine detail; produces noticeably larger MJPEG frames.

A warning appears under this row when HIGH is selected, and a second, combined warning appears
whenever HIGH resolution and HIGH quality are both selected together — the maximum GPU, CPU, and
bandwidth setting available.

## HIDE COCKPIT FEED

When ON, NO XMFD hides the game's in-cockpit TGP picture while an external TGP page is open, so the
external MFD is the only moving TGP feed. The cockpit display falls back to its normal content
instead.

This applies at every resolution and quality combination. It is off by default and restores when
the toggle is turned OFF or the external TGP page is closed. A very brief cockpit-feed flash can
still happen during rapid target deselect/reselect transitions.

## RESET TO DEFAULTS

Restores the slider to 15 Hz, resolution to LOW, quality to MID, and HIDE COCKPIT FEED to OFF.
