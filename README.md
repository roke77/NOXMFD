# NO XMFD

A live glass-cockpit display for [Nuclear Option](https://store.steampowered.com/app/2168680/).
While you fly, NO XMFD streams your aircraft's telemetry to a real-time
map and multi-function display you can open in any web browser — on the same
PC, or on a tablet or phone beside you as a second screen.

## Features

- **Live moving map** — the actual in-game map with your aircraft tracked on
  it in real time, complete with a trail.
- **Battlefield contacts** — other units shown with the game's own icons,
  coloured by faction, with your current targets highlighted.
- **Pan, zoom & follow** — move around the map freely or lock it to your
  aircraft.
- **Grid coordinates** — the in-game grid reference for your position.
- **Flight HUD** — aircraft name, airspeed, altitude (AGL), heading, gear
  state, and live countermeasure counts (IR flares, EW capacitor).
- **Multi-function display (MFD)** with dedicated pages:
  - **MAP** — the moving map.
  - **WPN** — current loadout and countermeasures.
  - **TGL** — your target list.
  - **TGP** — live targeting-pod camera feed.
  - **AVN** — airframe status with a live damage silhouette.
- **Targeting-pod video** — the real TGP camera feed, streamed to the display.
- **Second-screen ready** — open it on a tablet or phone on the same Wi-Fi
  and use it as a physical MFD next to your setup.
- **Fullscreen mode** — go edge-to-edge for a clean cockpit display.

## Requirements

- **Nuclear Option** (PC, via Steam).
- **BepInEx 5** (x64) installed into the game.
- A device with a modern web browser — the same PC, or a tablet/phone on the
  same local network.

### Required BepInEx setting

Set `HideManagerGameObject = true` under `[Chainloader]` in
`BepInEx/config/BepInEx.cfg`. Nuclear Option's scene cleanup destroys BepInEx's
manager GameObject on the boot → main-menu transition; hiding it keeps the
manager (and any plugin that lives on it) alive. NO XMFD has its own workaround
and runs either way, but **ConfigurationManager** (the in-game config menu used
for the HUD-declutter toggles) only survives — and therefore only opens — with
this set. Default config key is `Insert`; rebind it in that menu's *General*
section (avoid `F1`, which is a camera-view key in-game).

## Installing

> **🚧 Work in progress.** Packaged releases and step-by-step install
> instructions are coming. This section will be filled in once a proper
> release build is available.
