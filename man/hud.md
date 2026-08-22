# HUD

A clickable replica of the game's in-cockpit HUD OPTIONS screen, plus a declutter strip for the
mod's own HUD-hiding toggles.

![HUD page](images/HUD.png)

## Declutter

Toggles for the mod's own native-HUD hiding — the weapon panel, the corner minimap, and the boxed
flight readouts. Independent of everything below; these aren't part of the game's own HUD OPTIONS.

## Mode tabs

NAV / GUN / A2A / A2G / EW / LOG — clicking one applies that mode's saved preset, switching every
category and type toggle below to whatever that mode has configured.

## Categories

FRIENDLY UNITS, ENEMY UNITS, AIRCRAFT, MISSILES, VEHICLES, BUILDINGS, SHIPS — each can be
maximized or minimized independently. Minimized enemy icons shrink to a small dot; minimized
friendly icons disappear entirely. Two things to know:

- **Aircraft always show at full size**, regardless of this toggle — the game itself exempts
  them.
- **A newly-spotted unit stays maximized for a few seconds** before this setting applies to it,
  so a toggle only affects units already established on the HUD, not ones just appearing.

## Vehicle & building types

Within VEHICLES and BUILDINGS, individual type chips (e.g. TRUCK, MBT, AAA for vehicles; FAC,
HGR, DEF for buildings) toggle independently of their parent category.

## Presets

Up to 5 named presets of your own, saved server-side so any connected browser can save or load
one. A label at the bottom of the page reads **PRESET N: name** — whichever of the 5 is current —
followed by **SAVE** and **LOAD**.

- **SAVE** — opens a name prompt; submitting it captures the page's current filters (every
  category, vehicle, and building toggle above) into the current preset under that name.
- **LOAD** — opens a list of all 5 presets. Clicking one applies its saved filters and makes it
  the current preset; a pencil icon renames it in place, and a **×** clears it back to empty
  (name and data both) — the slot itself stays, only its contents are gone.
- The 5 numbered keybinds on [KEY](keybinds.md#hud-presets) recall a preset directly without
  opening LOAD, and also become the current preset.
- Presets are separate from the mode tabs above: a mode tab applies one of the game's own built-in
  presets (NAV/GUN/A2A/A2G/EW/LOG); these 5 are your own, named by you.
- See [KEY](keybinds.md#immersion-options)'s **Force HUD filters on combat mode** setting for how
  switching weapons mode to A/A or A/G can automatically apply the matching mode tab here.

## PAD cursor

This page is fully drivable from a HOTAS [PAD cursor](keybinds.md#pad-cursor).
