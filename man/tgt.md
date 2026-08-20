# TGT

A clickable replica of the in-cockpit TARGET SELECTION panel — control which units can be
targeted, and see what you currently have selected.

![TGT page](../docs/images/TGT.png)

## Filters

Rows of toggles for faction (friendly/enemy), category, and vehicle type, plus LASER and HUD mode
buttons:

- **Tap** a filter to turn it on or off.
- **Hold** a filter to isolate it — turns everything else in its row off, leaving just that one on.
- **RESET FILTER** turns every filter back on.

## Target list

Every target you currently have selected, one row per target:

- **NAME** — the unit's name.
- **SRC** — where the lock is coming from: **SENSOR** (your own live sensors), **DATALINK**
  (relayed by your faction, still trustworthy), or **STALE** (relayed, but the game no longer
  trusts the position — the same check behind the TGP page's own "?" marker).
- **RNG** — range to the target.
- **GRID** — its grid position.

**Tap anywhere on a row** to deselect that target. **CLEAR TARGETS** deselects everything at once.

## Bulk-clearing by source

Two buttons below the list clear targets by *why* they're selected, without touching the rest:

- **DATALINK** — deselects every datalink-only lock.
- **STALE** — deselects every stale lock.

## Keybinds and PAD cursor

Next/Previous Target, Clear Datalink, and Clear Stale all have dedicated keybinds, and this page
is fully drivable from a HOTAS [PAD cursor](keybinds.md#pad-cursor) — see [KEY](keybinds.md) for
both.
