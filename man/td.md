# TD — Target Designator

Hand off targets from your own [TGT](tgt.md) list to specific squad members. Only visible on TGT's
nav row while you're in a [squad](sqd.md); its own content depends on whether you're the leader or
a member.

## As the leader

The table mirrors your TGT target list, but unlike TGT it's deliberately static rather than
live-updating — see **REFRESH** below for why. It's a separate view from TGT itself: selecting or
assigning here never changes what's selected on TGT.

- **Tap a row** to select it (highlighted amber). Multiple rows can be selected at once.
- **Squad buttons** above the table, one per squad slot including yourself, labeled with each
  pilot's callsign designation (e.g. `TALON 1-1` is you, `TALON 1-2` the first member, and so on —
  same numbering as the [SQD](sqd.md) roster). With one or more rows selected:
  - **Tap** a squad button to assign them to that slot — the row's highlight clears and a small
    tag (the plain slot number, not the full designation) shows who has it.
  - **Long-press** a squad button instead to assign without clearing the highlight, so you can
    designate the same selection to several slots in a row without re-selecting each time.

  A target can go to more than one slot; assigning again for an already-assigned target/slot pair
  un-assigns it. Assigning to your own button is just a personal marker — it's never actually sent
  anywhere.
- **DESIGNATE** sends each member their current assigned set, then returns you to TGT — where the
  new **TD** column shows the same slot numbers you just assigned. Sending again to a member who
  already has a pending designation replaces it entirely, rather than adding to it.
- **REFRESH** pulls in the current TGT list and re-applies it to the table. The table only updates
  on its own when a target is actually selected or deselected in-game (not on every range/grid
  tick) — REFRESH is the manual way to bring grid/range up to date, or to pick up a target that
  changed without a fresh select/deselect.
- **ALL** selects every row currently in the table, same as tapping each one individually.
- **CLEAR** discards your own in-progress selection/assignment work without touching anything
  already sent.

## As a member

A read-only table of whatever the leader last designated to you — never your own TGT selections.

- **Tap a row** to select that target in-game immediately.
- **AQUIRE** selects everything currently listed, all at once, then returns you to TGT.
- **CLEAR** empties your own table. This doesn't notify the leader.

## HUD marks

Whenever a unit is targeted by someone else in the squad, its native in-game HUD icon gets a small
teal mark — visible in the cockpit HUD itself, without opening TD or TGT:

- **`*`** — the squad leader currently has this unit targeted.
- **`⌃`** — at least one member (not the leader) currently has it targeted. One mark regardless of
  how many members.

This is purely informational and works for every squad member, leader included — it shows what the
rest of the squad is doing at a glance, separate from your own local target lock (the amber `+`).
Marks update live and clear automatically once the squad ends.

## Keybinds

Up to 9 keybinds (**Assign 1**-**Assign 9**, see [KEY](keybinds.md)) mirror the leader's squad
buttons exactly, tap and hold both: a tap assigns your current selection and clears it, a hold
assigns without clearing so you can designate the same selection to several slots in a row. Only
meaningful while your own TD display holds SOI.
