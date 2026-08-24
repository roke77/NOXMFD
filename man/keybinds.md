# KEY

Optional dedicated keybinds for cockpit functions the game has no native bind for. Each function
takes a keyboard/mouse key, a joystick/HOTAS button, or both — click a cell on the page and press
the key or button to bind it. Multi-stick HOTAS setups are supported; each bind remembers which
stick it came from.

![Extended keybinds page](images/KEY.png)

## Weapons

- **Flares** — select + deploy IR flares (tap to pop, hold to keep popping).
- **Jammer** — select + activate the radar jammer (hold to jam).
- **Jamming Pod** — select + activate a weapon-mounted radar jamming pod (e.g. the Medusa's), hold
  to keep jamming.
- **Cycle guns / missiles / bombs** — select the last soft-selected weapon of that type, or the
  first in the list; repeated presses cycle to the next one, skipping depleted weapons. Cycling to
  a different type leaves the current one soft-selected.
- **Gun trigger / Weapon release** — per-class fire keys (hold for continuous fire). If the active
  weapon is from the other class, the first press only switches — bringing up the right reticle —
  and the next press fires. Current gun and missile/bomb choices show as outlines on
  [WPN](wpn.md).

## Gear

- **Gear up / Gear down** — dedicated raise/lower (the stock bind is a single toggle).

## Map & waypoints

Direct binds for what [MAP](map.md)'s FLW/Z+/Z−/R+/R− keys and [WPT](wpt.md)'s W+/W− keys already
do on the focused display:

- **Follow**, **Zoom In**, **Zoom Out**
- **Next Route**, **Previous Route** — stay usable to switch INTO a route as long as one is saved,
  even with none currently active.
- **Next Waypoint**, **Previous Waypoint**

## Target list

- **Next Target / Previous Target** — step a highlighted row through the focused [TGT](tgt.md)
  display's locked-target list, without aiming the PAD cursor at it. Mutually exclusive with the
  cursor: stepping a row hides the crosshair and hands Cursor Select to that row instead (Select
  deselects it); moving the crosshair hands Select back.
- **Clear Datalink / Clear Stale** — the keybind equivalents of tapping [TGT](tgt.md)'s own
  DATALINK/STALE buttons.

## Cursor

**Cursor Up / Down / Left / Right / Select**, plus two HOTAS axis binds (horizontal/vertical) —
drive the [PAD cursor](#pad-cursor) below.

## Other settings

An **Input When Game Unfocused** toggle sits above the bind table (not a bind itself) — turn it on
if you run the display in a browser on the same PC as the game, so your HOTAS stays live while
that browser window has focus. Off by default; leave it off for a tablet or phone, where the game
keeps focus anyway.

**Listen for Keybinds (Remote)** lets this browser send the keyboard binds configured on this page
back to the game, so a tablet, laptop, or WSO station can operate the same MAP/TGT/SOI/weapon
actions without physical access to the game PC. It is off by default and stored per browser. Keep it
enabled only on the browser you intend to use for input; if the page detects that it is running on
the game PC, it warns that the game may also receive the same physical keypress and double-fire an
action.

## Sensor of Interest (SOI)

Operate a display from your HOTAS without touching it. One screen at a time is selected — it is
outlined in white — and the SOI keys move a cursor over its buttons and press them. In a split the
selection is a single pane; on the [F-35 layout](layouts.md#f-35) it is a single portal. Nothing
is selected until you press a SOI key.

- **SOI Next / SOI Prev** — select the next / previous screen (every open display, and each F-35
  portal).
- **Nav Up / Nav Down** — move the cursor over the selected screen's buttons.
- **Nav Select** — press the button under the cursor.

![SOI-selected screen](images/SOI1.png)

![SOI-selected screen](images/SOI2.png)

## PAD cursor

When a display's focused page is interactible — [MAP](map.md), [HUD](hud.md), [TGT](tgt.md), or
[RDR](rdr.md) — a crosshair can move over it and act on whatever's underneath, the same thing a
mouse click or touch tap already does, but from the HOTAS, without touching the screen.

- **Cursor Up/Down/Left/Right** (or a bound analog axis) slews it.
- **Cursor Select** picks whatever it's over: a contact on MAP, a toggle on HUD, a filter or
  target row on TGT (holding it also mirrors that page's own long-press action, where it has one),
  or a contact on RDR (locking/unlocking it). On TGT specifically, Select instead deselects
  whichever row Next/Previous Target highlighted, if one is — see [Target list](#target-list)
  above.
- **Zoom In/Out** zoom the MAP view as usual, or scroll the page up/down on HUD/TGT.
- On MAP, pushing the cursor against the edge with FLW off pans the view to reveal more terrain.

It only acts on whichever display currently has both SOI focus and one of these pages open.

## Layout

- **Save Layout** — save the current split/portal arrangement and which page each pane or portal
  shows, under a name.
- **Load Layout** — pick a saved arrangement by name and apply it immediately.

No joystick/HOTAS for these two — whichever browser window has keyboard focus when the key is
pressed is the one that acts, and the key you set here applies to every connected browser. The
same two actions are also available as **SAVE**/**LOAD** buttons on [LYT](layouts.md)'s own menu,
for a tablet with no keyboard.

![Layout section](images/LYT_KEY.png)

## HUD Presets

- **HUD Preset 1** through **HUD Preset 5** — five ordinary binds (keyboard and joystick/HOTAS
  both work, unlike Layout's keyboard-only pair above). Pressing one instantly recalls that
  numbered preset's saved filters onto [HUD](hud.md#presets) and makes it the current one shown at
  the bottom of that page — the same thing clicking it in HUD's own LOAD list does.

## Immersion Options

Optional cold-start behavior and dedicated binds for radar, engine, and weapons safety,
configured in their own section at the bottom of this page.

- **Enable Radar / Engine / Master Arms on start** — three toggles, all ON by default (matching
  the game's own behavior). Turn any of them off for more immersion: that system starts off when
  you spawn into a new aircraft, and you arm/start it yourself.
- **Radar ON / Radar OFF**, **Engine ON / Engine OFF**, **Master Arms ON / Master Arms OFF** —
  dedicated binds for each, on top of the on-start toggles, so you can flip them mid-flight.
  Master Arms OFF blocks guns, missiles, and bombs until it's back on — [WPN](wpn.md) shows a
  full-screen SAFE warning while it's off, and its ARM/SAFE controls mirror and drive the same
  state.
- **A/A mode / A/G mode** — restrict missile cycling to air-to-air or air-to-ground weapons; guns
  fire in either mode, bombs only cycle in A/G. Tap to set the mode; hold either bind to reset to
  ALL (unrestricted) — there's no dedicated ALL bind, since neither showing lit already means ALL.
  [WPN](wpn.md)'s A/A · A/G controls mirror and drive the same state.
- **Force HUD filters on combat mode** — off by default. Turn it on to have switching to A/A or
  A/G automatically force [HUD](hud.md)'s matching preset (the same NAV/GUN/A2A/A2G/EW/LOG tabs
  HUD's own mode row applies), restoring whatever you had set yourself on returning to ALL.
  Pressing an already-active A/A or A/G again re-forces that preset, discarding any HUD tweaks
  made since it last applied — a quick way to reset back to it without leaving WPN.

![Immersion Options section](images/IMM.png)
