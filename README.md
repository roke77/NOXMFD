# <img src="docs/images/icon.svg" alt="" width="30" height="30" align="absmiddle"> Nuclear Option eXternal MFD

[![Nuclear Option](https://img.shields.io/badge/Game-Nuclear%20Option-green)](https://store.steampowered.com/app/2168680/Nuclear_Option/)
[![BepInEx 5](https://img.shields.io/badge/Loader-BepInEx%205-orange)](https://docs.bepinex.dev/)
[![Release](https://img.shields.io/github/v/release/roke77/NOXMFD?label=Release&color=blue)](https://github.com/roke77/NOXMFD/releases/latest)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)

![NO XMFD running on a flight-sim rig](docs/images/SIM.png)

NO XMFD is a BepInEx plugin for [Nuclear Option](https://store.steampowered.com/app/2168680/)
that reads live flight telemetry from the game and serves it over the local
network as a browser-based multi-function display (MFD). The display opens in
any web browser, on the same PC or on another device on the same network.

## Contents

- [Requirements](#requirements)
- [Installation](#installation)
- [Features](#features)
- [Reporting & collaboration](#reporting--collaboration)
- [Mod compatibility](#mod-compatibility)
- [Extensions](#extensions)
- [Security & privacy](#security--privacy)
- [License](#license)

## Requirements

- **Nuclear Option** (PC, via Steam). Currently tested against game version 0.34.2.
- **BepInEx 5** (x64) installed into the game.
- A device with a modern web browser — the same PC, or a tablet/phone on the
  same local network.

## Installation

NO XMFD ships as a single BepInEx plugin (the web display is bundled inside the DLL).

<details>
<summary><strong>With NOMM (recommended)</strong></summary>

[NOMM](https://github.com/Combat787/NOMM) (Nuclear Option Mod Manager) installs BepInEx and
NO XMFD for you, and keeps it up to date.

1. **Install NOMM** from its [latest release](https://github.com/Combat787/NOMM/releases/latest)
   — on Windows, the `portable.exe` or the `.msi` installer.
2. **Find "Nuclear Option eXternal MFD"** in NOMM's mod list and install it. NOMM pulls in
   BepInEx automatically if it isn't already present.
3. **Launch the game** and open `http://localhost:5005/` in a browser. To reach it from a
   tablet or phone on your network, see [NETWORKING.md](NETWORKING.md).

</details>

<details>
<summary><strong>Manually</strong></summary>

1. **Install BepInEx 5** (x64) into Nuclear Option — grab it from the
   [BepInEx releases](https://github.com/BepInEx/BepInEx/releases). Run the game
   once so BepInEx creates its folders.
2. **Download the latest NO XMFD release** — the `NOXMFD_x.y.z.zip` asset — from
   the [Releases page](https://github.com/roke77/NOXMFD/releases).
3. **Extract it** into a subfolder of BepInEx's plugins directory:

   ```
   BepInEx/plugins/NOXMFD/
   ```

4. **Launch the game.** Open `http://localhost:5005/` in a browser to see the
   display. To reach it from a tablet or phone on your network, see
   [NETWORKING.md](NETWORKING.md).

</details>

## Features

NO XMFD's features are built around flight immersion. It declutters Nuclear
Option's in-game HUD instruments and relocates those readouts onto external
displays — a second monitor, tablet, or phone — the way a physical flight-sim
rig spreads its instrumentation across dedicated screens and panels around the
pilot, with HOTAS-friendly keybinds to match.

### MFD pages

- **[AFM](man/afm.md)** — airframe status and damage.
- **[AVN](man/avn.md)** — avionics gauges and system toggles.
- **CFG** — configuration and settings hub.
  - **[HUD](man/hud.md)** — in-cockpit HUD config.
  - **[KEY](man/keybinds.md)** — extended keybinds.
  - **[LYT](man/layouts.md)** — layout chooser.
- **[EXT](man/ext.md)** — third-party extension pages.
- **[MAIN](man/main.md)** — landing page.
- **[MAP](man/map.md)** — tactical map.
  - **[CFG](man/mapcfg.md)** — MAP's own refresh-rate setting.
  - **[WPT](man/wpt.md)** — waypoint/route editor.
- **MD** — mission data hub.
  - **[AKF](man/akf.md)** — kill feed.
  - **[BDF / PAL](man/bdf.md)** — faction forces.
  - **[MIS](man/mis.md)** — mission info.
  - **[OBJ](man/obj.md)** — objectives.
- **RDR** — radar hub.
  - **[FCR](man/rdr.md#fcr)** — Fire Control Radar
  - **[HSD](man/rdr.md#hsd)** — Horizontal Situation Display
- **[RWR](man/rwr.md)** — radar warning receiver.
- **[SQD](man/sqd.md)** — squadron membership and shared waypoint routes.
- **[TGP](man/tgp.md)** — targeting-pod camera.
  - **[CFG](man/tgpcfg.md)** — feed rate, resolution, and quality settings.
- **[TGT](man/tgt.md)** — target-selection table.
- **[WPN](man/wpn.md)** — weapon loadout.

## Reporting & collaboration

Found a bug, or want a feature? Open an issue on the
[issue tracker](https://github.com/roke77/NOXMFD/issues) — include your game and
NO XMFD versions, and steps to reproduce for bugs. It's also where planned and
in-progress work is tracked.

Pull requests are welcome — see [CONTRIBUTING.md](CONTRIBUTING.md) for dev setup,
testing, and what's expected of a change before it's ready for review. For anything
non-trivial, open an issue first so we can agree on the approach before you write
code. Security issues have their own process — see
[Verifying and reporting](SECURITY.md#verifying-and-reporting).

## Compatibility

Mixed BepInEx installations have not been systematically tested. If you hit a conflict, please
[open an issue](https://github.com/roke77/NOXMFD/issues) with the names and versions of every
installed plugin involved.

The main shared surfaces to check are:

- **Keybinds** — extended keybinds (flares, gear, weapon cycling, Master Arm,
  radar/engine, combat mode) are read directly from raw keyboard/joystick state. Two plugins bound
  to the same physical key/button will both fire; NO XMFD cannot detect that collision.
- **Harmony patches** — a handful of game methods are patched to enforce Master
  Arms and set radar/engine spawn defaults (see
  [SECURITY.md](SECURITY.md#what-no-xmfd-does) for the exact list). Additional patches on the same
  methods can change behavior according to Harmony's patch order.
- **HUD declutter** — hiding native HUD elements (weapon panel, minimap, boxed
  readouts) works by directly toggling those elements' visibility. A second writer can conflict
  with NO XMFD over the same on/off state.

Temporarily disabling plugins one at a time is the fastest way to isolate which component owns a
conflict.

## Extensions

NO XMFD supports third-party extensions that add their own MFD pages — see [man/ext.md](man/ext.md)
for how they show up in-app, and [EXTENSIONS.md](EXTENSIONS.md) for the full guide to building one.

## Security & privacy

NO XMFD is open source and runs entirely on your machine and local network — it
makes **no internet connections** and collects nothing. It does run a local web
server (so a tablet can connect) and can optionally add a Windows firewall rule
for its own port. Like all BepInEx mods it runs unsandboxed, so it's worth
knowing exactly what it can access: see **[SECURITY.md](SECURITY.md)** for the
full capability disclosure, the one network caveat (the LAN server is
unauthenticated), and how to verify the build yourself. Network/firewall setup
is covered in [NETWORKING.md](NETWORKING.md).

## License

[MIT](LICENSE) © Roque Alejandro Cuello
