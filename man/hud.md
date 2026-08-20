# HUD

A clickable replica of the game's in-cockpit HUD OPTIONS screen, plus a declutter strip for the
mod's own HUD-hiding toggles.

![HUD page](../docs/images/HUD.png)

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

## PAD cursor

This page is fully drivable from a HOTAS [PAD cursor](keybinds.md#pad-cursor).
