# KEY

Reached from MAIN via **CFG** in either layout, alongside [HUD](hud.md), [LYT](layouts.md), and
[RTS](rates.md) — or opened directly at `http://localhost:5005/keybinds`.

## Extended Keybinds

Optional dedicated keybinds for cockpit functions the game has no native bind for, configured on
the **KEY** page. Each function takes a keyboard/mouse key, a joystick/HOTAS button, or both —
click a cell and press the key or button to bind it. Multi-stick HOTAS setups are supported; each
bind remembers which stick it came from.

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
- **Follow / Zoom In / Zoom Out / Next Route / Previous Route / Next Waypoint / Previous Waypoint** —
  direct binds for what the bezel's FLW, Z+/Z−, R+/R− and W+/W− keys already do on the focused MAP
  or WPT display. Next/Previous Route stay usable to switch INTO a route as long as one is saved,
  even with none currently active.
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

![Extended keybinds page](../docs/images/KEY.png)

</details>

## Sensor of Interest (SOI)

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

![SOI-selected screen](../docs/images/SOI1.png)

![SOI-selected screen](../docs/images/SOI2.png)

</details>

## PAD cursor

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

## Immersion Options

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

![Immersion Options section](../docs/images/IMM.png)

</details>
