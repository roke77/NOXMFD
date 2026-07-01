# Nuclear Option eXternal MFD

NO XMFD is a BepInEx plugin for [Nuclear Option](https://store.steampowered.com/app/2168680/)
that reads live flight telemetry from the game and serves it over the local
network as a browser-based multi-function display (MFD) and moving map. The
display opens in any web browser, on the same PC or on another device on the
same network.

## Requirements

- **Nuclear Option** (PC, via Steam).
- **BepInEx 5** (x64) installed into the game.
- A device with a modern web browser — the same PC, or a tablet/phone on the
  same local network.

### Required BepInEx setting

Set `HideManagerGameObject = true` under `[Chainloader]` in
`BepInEx/config/BepInEx.cfg`. The plugin itself runs without it, but the in-game
**ConfigurationManager** menu — used to change settings live — will not open
unless it is set: Nuclear Option destroys BepInEx's manager GameObject on the
boot → main-menu transition, and ConfigurationManager lives on it. (Settings can
still be edited by hand in the `.cfg` file either way.) Its default open key is
`Insert`; rebind it in that menu's *General* section (avoid `F1`, which is a
camera-view key in-game).

## Installation

> **🚧 Work in progress.** Packaged releases and step-by-step install
> instructions are coming. This section will be filled in once a proper
> release build is available.

## Features

> **🚧 Work in progress.** A detailed feature list is coming.

## Security & privacy

NO XMFD is open source and runs entirely on your machine and local network — it
makes **no internet connections** and collects nothing. It does run a local web
server (so a tablet can connect) and can optionally add a Windows firewall rule
for its own port. Like all BepInEx mods it runs unsandboxed, so it's worth
knowing exactly what it can access: see **[SECURITY.md](SECURITY.md)** for the
full capability disclosure, the one network caveat (the LAN server is
unauthenticated), and how to verify the build yourself. Network/firewall setup
is covered in [docs/networking.md](docs/networking.md).
