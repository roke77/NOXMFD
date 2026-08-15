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
  - [MFD pages](#mfd-pages)
  - [MFD shell](#mfd-shell)
  - [MFD layouts](#mfd-layouts)
  - [Extended Keybinds](#extended-keybinds)
  - [Immersion Options](#immersion-options)
- [Reporting & collaboration](#reporting--collaboration)
- [Mod compatibility](#mod-compatibility)
- [Security & privacy](#security--privacy)
- [License](#license)

## Requirements

- **Nuclear Option** (PC, via Steam). Currently tested against game version 0.34.
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
   tablet or phone on your network, see [docs/networking.md](docs/networking.md).

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
   [docs/networking.md](docs/networking.md).

</details>

<details>
<summary><strong>Changing settings (ConfigurationManager)</strong></summary>

NO XMFD's own settings — the extended keybinds — are changed either in the
in-game **ConfigurationManager** menu or by hand in
`BepInEx/config/com.roque.NOXMFD.cfg`. The plugin runs fine without any of this;
you only need it to adjust those settings.

To use the in-game menu, install **ConfigurationManager** — the settings editor,
much friendlier than editing the config file by hand. Download the **BepInEx 5**
build from its
[releases](https://github.com/BepInEx/BepInEx.ConfigurationManager/releases) and
extract the DLL into `BepInEx/plugins/`. It also needs `HideManagerGameObject =
true` under `[Chainloader]` in `BepInEx/config/BepInEx.cfg`: Nuclear Option
destroys BepInEx's manager GameObject on the boot → main-menu transition, and
ConfigurationManager lives on it, so the menu will not open unless the setting is
on. Its default open key is `F1`, rebindable in that menu's *General* section.

Either way the settings work — skip ConfigurationManager and edit the `.cfg` by
hand.

</details>

## Features

NO XMFD's features are built around flight immersion. It declutters Nuclear
Option's in-game HUD instruments and relocates those readouts onto external
displays — a second monitor, tablet, or phone — the way a physical flight-sim
rig spreads its instrumentation across dedicated screens and panels around the
pilot, with HOTAS-friendly keybinds to match.

### MFD pages

- **MAIN** — landing page: connection status and the URL(s) to open the display.

  <details>
  <summary>$\color{green}\textsf{Show screenshot}$</summary>

  ![MAIN page](docs/images/MAIN.png)

  </details>

- **AFM** — airframe status: aircraft name, a nose-on view of your loadout's armed/exhausted hardpoints (mirroring the cockpit's own weapon-station panel), and a top-down silhouette that darkens per-part as it takes damage, with engine-fire and other critical failure callouts.

  <details>
  <summary>$\color{green}\textsf{Show screenshot}$</summary>

  ![AFM page](docs/images/AFM.png)

  </details>

- **AVN** — avionics at a glance: circular gauges for engine RPM, fuel, IR heat signature, and throttle (with an afterburner range on the dial), plus a bank of system toggles — gear, radar, guns, engine, flight assist, night vision, nav lights, turret — that show live state and double as bezel-actuated switches.

  <details>
  <summary>$\color{green}\textsf{Show screenshot}$</summary>

  ![AVN page](docs/images/AVN.png)

  </details>

- **HUD** — a remote for the game's in-cockpit HUD OPTIONS: mode tabs (NAV/GUN/A2A/…) and per-category / per-type toggles for which unit icons show on the HUD, plus a declutter strip that hides native HUD widgets — the weapon panel, corner minimap, and boxed flight readouts.

  <details>
  <summary>$\color{green}\textsf{Show screenshot}$</summary>

  ![HUD page](docs/images/HUD.png)

  </details>

- **KEY** — extended-keybinds editor: bind a keyboard key and a joystick/HOTAS button to each of
  the mod's own cockpit functions. See [Extended Keybinds](#extended-keybinds) below for the
  functions themselves.

- **LYT** — layout chooser: switch the display to a different shell layout.

  <details>
  <summary>$\color{green}\textsf{Show screenshot}$</summary>

  ![LYT page](docs/images/LYT.png)

  </details>

- **MAP** — full-screen tactical map with friendly/hostile units and your own position; click a unit to target it, FLW toggles follow, Z+/Z− zoom. A HOTAS cursor can drive all of this without touching the screen — see [PAD cursor](#pad-cursor) below.

  <details>
  <summary>$\color{green}\textsf{Show screenshot}$</summary>

  ![MAP page](docs/images/MAP.png)

  </details>

- **MDT** (Mission Data Table) — read-only replicas of the game's faction Forces panel, one per fixed identity: BDF for BOSCALI, PAL for PRIMEVA, a switch away from each other. Warheads, score, and funds, plus a ships/buildings/vehicles/aircraft breakdown.

  <details>
  <summary>$\color{green}\textsf{Show screenshot}$</summary>

  ![BDF page](docs/images/BDF.png)

  </details>

- **RDR** — a radar scope showing air contacts from your own radar (green) and the faction's shared datalink picture (purple), with a PAD cursor to slew between the bars and lock/unlock a target.

  <details>
  <summary>$\color{green}\textsf{Show screenshot}$</summary>

  ![RDR page](docs/images/RDR.png)

  </details>

- **RWR** — radar threats around you by bearing, with incoming-missile warnings.

  <details>
  <summary>$\color{green}\textsf{Show screenshot}$</summary>

  ![RWR page](docs/images/RWR.png)

  </details>

- **TGP** — targeting-pod camera feed zoomed on the locked target, with range and bearing. (Low quality for now. Follow high quality development [here](https://github.com/roke77/NOXMFD/issues/10))

  <details>
  <summary>$\color{green}\textsf{Show screenshot}$</summary>

  ![TGP page](docs/images/TGP.png)

  </details>

- **TGT** — target-selection filter mirroring the in-cockpit TARGET SELECTION panel: toggle which factions, categories, and vehicle types can be targeted (plus LASER/HUD), with RESET and CLEAR, above your live selected-target list. A DATALINK button bulk-deselects the datalink-only locks; a STALE button bulk-deselects locks whose relayed position the game no longer trusts (the same check behind the TGP's own "?" marker).

  <details>
  <summary>$\color{green}\textsf{Show screenshot}$</summary>

  ![TGT page](docs/images/TGT.png)

  </details>

- **WPN** — weapon loadout and rounds remaining, plus IR-flare count and jammer charge. ARM/SAFE and
  A/A · A/G controls reflect and drive Master Arms and combat mode — see
  [Immersion Options](#immersion-options).

  <details>
  <summary>$\color{green}\textsf{Show screenshot}$</summary>

  ![WPN page](docs/images/WPN.png)

  </details>

### MFD shell

The shell frames the active page with dedicated bezel buttons — function
controls along the top, layout presets along the bottom.

- **HIDE** — hide the bezel so the screen fills the viewport.
- **FULL** — fullscreen toggle.
- **PIN** — pin a page.
- **SWAP** — jump to/from pin.
- **F_VIEW** — single page.
- **H_SPLIT** — top/bottom split.
- **V_SPLIT** — left/right split.
- **V_WIDE_SPLIT** — left/right 2:1 split.

<details>
<summary>$\color{green}\textsf{See screenshots}$</summary>

![V_SPLIT (left) and H_SPLIT (right)](docs/images/H_V_SPLIT.png)

![V_WIDE_SPLIT](docs/images/V_WIDE_SPLIT.png)

</details>

### MFD layouts

NO XMFD can render in more than one shell layout — a different frame,
navigation, and split model over the same pages. Two are supported for now:
**CLASSIC** (the metallic bezel above) and **F-35**.

#### F-35

A borderless, touch-driven layout modelled on the real F-35's panoramic cockpit
display: there are no bezel keys — the navigation labels are drawn on the glass
and tapped directly, and the screen divides into side-by-side portals, each an
independent MFD, that you merge and split with corner grips. A fixed strip
across the top carries the aircraft-level readouts — connection, throttle and
fuel, and the avionics flags.

<details>
<summary>$\color{green}\textsf{See screenshots}$</summary>

![F-35 layout — MAIN](docs/images/F-35%20MAIN.png)

![F-35 layout — 1-2-1 portal split](docs/images/F-35%201-2-1.png)

![F-35 layout — 2-2 portal split](docs/images/F-35%202-2.png)

</details>

### Extended Keybinds

Optional dedicated keybinds for cockpit functions the game has no native bind for,
configured on the **KEY** page, reached from MAIN in either layout (or opened directly at
`http://localhost:5005/keybinds`). Each function takes a keyboard/mouse key, a
joystick/HOTAS button, or both — click a cell and press the key or button to bind it.
Multi-stick HOTAS setups are supported; each bind remembers which stick it came from.

- **Flares** — select + deploy IR flares (tap to pop, hold to keep popping).
- **Jammer** — select + activate the radar jammer (hold to jam).
- **Jamming Pod** — select + activate a weapon-mounted radar jamming pod (e.g. the Medusa's). Hold
  to keep jamming.
- **Gear up / Gear down** — dedicated raise/lower (the stock bind is a single toggle).
- **Cycle guns / missiles / bombs** — select the last soft-selected weapon of that type, or
  the first in the list; repeated presses cycle to the next one, skipping depleted weapons.
  Cycling to a different type leaves the current one soft-selected.
- **Gun trigger / Weapon release** — per-class fire keys (hold for continuous fire). If the
  active weapon is from the other class, the first press only switches — bringing up the
  right reticle — and the next press fires. The current gun and missile/bomb choices show
  as outlines on the WPN page.
- **Follow / Zoom In / Zoom Out** — direct binds for what the bezel's FLW and Z+/Z− keys already
  do on the focused MAP display.
- **Cursor Up / Down / Left / Right / Select**, plus two HOTAS axis binds (horizontal/vertical) —
  drive the [PAD cursor](#pad-cursor) below.
- **Next Target / Previous Target** — step a highlighted row through the focused TGT display's
  locked-target list, without aiming the PAD cursor at it. The two are mutually exclusive: stepping
  a row hides the crosshair and hands Cursor Select to that row instead (Select deselects it);
  moving the crosshair hands Select back.
- **Clear Datalink / Clear Stale** — the keybind equivalents of tapping TGT's own DATALINK/STALE
  buttons.

An **Input When Game Unfocused** toggle sits above the bind table (not a bind itself) — turn it on
if you run the display in a browser on the same PC as the game, so your HOTAS stays live while
that browser window has focus. Off by default; leave it off for a tablet or phone, where the game
keeps focus anyway.

<details>
<summary>$\color{green}\textsf{Show screenshot}$</summary>

![Extended keybinds page](docs/images/KEY.png)

</details>

#### Sensor of Interest (SOI)

Operate a display from your HOTAS without touching it. One screen at a time is selected — it is
outlined in white — and the SOI keys move a cursor over its buttons and press them. In a split the
selection is a single pane; on the F-35 it is a single portal. Nothing is selected until you press
a SOI key. Five binds, on the **KEY** page:

- **SOI Next / SOI Prev** — select the next / previous screen (every open display, and each F-35
  portal).
- **Nav Up / Nav Down** — move the cursor over the selected screen's buttons.
- **Nav Select** — press the button under the cursor.

<details>
<summary>$\color{green}\textsf{See screenshots}$</summary>

![SOI-selected screen](docs/images/SOI1.png)

![SOI-selected screen](docs/images/SOI2.png)

</details>

#### PAD cursor

When a display's focused page is interactible — MAP, HUD, TGT, or RDR — a crosshair can move over
it and act on whatever's underneath, the same thing a mouse click or touch tap already does, but
from the HOTAS, without touching the screen. Cursor Up/Down/Left/Right (or a bound analog axis)
slews it, Cursor Select picks whatever it's over: a contact on MAP, a toggle on HUD, a filter or
target row on TGT (holding it also mirrors that page's own long-press action, where it has one), or
a contact on RDR (locking/unlocking it). On TGT specifically, Select instead deselects whichever row
Next/Previous Target highlighted, if one is — the two are mutually exclusive, and moving the
crosshair hands Select back to it (see the Extended Keybinds list above). Zoom In/Out zoom the MAP
view as usual, or scroll the page up/down on HUD/TGT. On MAP, pushing the cursor against the edge
with FLW off pans the view to
reveal more terrain. It only acts on whichever display currently has both SOI focus and one of
these pages open.

### Immersion Options

Optional cold-start behavior and dedicated binds for radar, engine, and weapons safety —
configured in their own section at the bottom of the **KEY** page.

- **Enable Radar / Engine / Master Arms on start** — three toggles, all ON by default (matching
  the game's own behavior). Turn any of them off for more immersion: that system starts off when
  you spawn into a new aircraft, and you arm/start it yourself.
- **Radar ON / Radar OFF**, **Engine ON / Engine OFF**, **Master Arms ON / Master Arms OFF** —
  dedicated binds for each, on top of the on-start toggles, so you can flip them mid-flight.
  Master Arms OFF blocks guns, missiles, and bombs until it's back on — the WPN page shows a
  full-screen SAFE warning while it's off, and its ARM/SAFE controls mirror and drive the same
  state.
- **A/A mode / A/G mode** — restrict missile cycling to air-to-air or air-to-ground weapons; guns
  fire in either mode, bombs only cycle in A/G. Tap to set the mode; hold either bind to reset to
  ALL (unrestricted) — there's no dedicated ALL bind, since neither showing lit already means ALL.
  The WPN page's A/A · A/G controls mirror and drive the same state.

<details>
<summary>$\color{green}\textsf{Show screenshot}$</summary>

![Immersion Options section](docs/images/IMM.png)

</details>

## Reporting & collaboration

Found a bug, or want a feature? Open an issue on the
[issue tracker](https://github.com/roke77/NOXMFD/issues) — include your game and
NO XMFD versions, and steps to reproduce for bugs. It's also where planned and
in-progress work is tracked.

Pull requests are welcome. For anything non-trivial, open an issue first so we
can agree on the approach before you write code. Security issues have their own
process — see [Verifying and reporting](SECURITY.md#verifying-and-reporting).

## Mod compatibility

NO XMFD hasn't been systematically tested against other mods — that's real
investigation work, tracked but not yet underway. If you hit a conflict, please
[open an issue](https://github.com/roke77/NOXMFD/issues) with both mods' names
and versions.

Some ways two mods can step on each other, so you know what to check first if
something breaks:

- **Keybinds** — extended keybinds (flares, gear, weapon cycling, Master Arms,
  radar/engine, combat mode) are read directly from raw keyboard/joystick state,
  the same way most mods do it. Two mods bound to the same physical key/button
  will both fire; NO XMFD doesn't and can't detect that for you.
- **Harmony patches** — a handful of game methods are patched to enforce Master
  Arms and set radar/engine spawn defaults (see
  [SECURITY.md](SECURITY.md#what-no-xmfd-does) for the exact list). Another mod
  patching the same methods can change behavior depending on patch order, which
  isn't something either mod controls.
- **HUD declutter** — hiding native HUD elements (weapon panel, minimap, boxed
  readouts) works by directly toggling those elements' visibility. A mod that
  also touches them can end up fighting NO XMFD over the same on/off state.

None of this is unique to NO XMFD — it's the standard risk profile of any
BepInEx mod that reads input or patches game code. Uninstalling one of the two
mods is the fastest way to confirm which side a conflict is coming from.

## Security & privacy

NO XMFD is open source and runs entirely on your machine and local network — it
makes **no internet connections** and collects nothing. It does run a local web
server (so a tablet can connect) and can optionally add a Windows firewall rule
for its own port. Like all BepInEx mods it runs unsandboxed, so it's worth
knowing exactly what it can access: see **[SECURITY.md](SECURITY.md)** for the
full capability disclosure, the one network caveat (the LAN server is
unauthenticated), and how to verify the build yourself. Network/firewall setup
is covered in [docs/networking.md](docs/networking.md).

## License

[MIT](LICENSE) © Roque Alejandro Cuello
