# TD — Target Designator

Hand off targets from your own [TGT](tgt.md) list to specific squad members. Only visible on TGT's
nav row while you're in a [squad](sqd.md); its own content depends on whether you're the leader or
a member.

## As the leader

The table mirrors your live TGT target list — the same rows, live-updating. It's a separate view
from TGT itself: selecting or assigning here never changes what's selected on TGT.

- **Tap a row** to select it (highlighted amber). Multiple rows can be selected at once.
- **Squad buttons** above the table, one per squad slot including yourself, labeled with each
  pilot's callsign designation (e.g. `TALON 1-1` is you, `TALON 1-2` the first member, and so on —
  same numbering as the [SQD](sqd.md) roster). With one or more rows selected, tap a squad button
  to assign them to that slot — the row's highlight clears and a small **→N** tag shows who has it
  (N is the plain slot number, not the full designation). A target can go to more than one slot;
  tapping the same button again for an already-assigned target un-assigns it. Assigning to your own
  button is just a personal marker — it's never actually sent anywhere.
- **DESIGNATE** sends each member their current assigned set. Sending again to a member who already
  has a pending designation replaces it entirely, rather than adding to it.
- **CLEAR** discards your own in-progress selection/assignment work without touching anything
  already sent.

Rows only ever leave this table because they left TGT (deselected or filtered out) — DESIGNATE and
CLEAR never remove a row themselves, only the highlight/tag state on top of it.

## As a member

A read-only table of whatever the leader last designated to you — never your own TGT selections.

- **Tap a row** to select that target in-game immediately.
- **AQUIRE** selects everything currently listed, all at once.
- **CLEAR** empties your own table. This doesn't notify the leader.

## Keybinds

Up to 9 keybinds (**Assign 1**-**Assign 9**, see [KEY](keybinds.md)) mirror the leader's squad
buttons — pressing one with targets selected on your own TD table is the same as tapping the
matching button. Only meaningful while your own TD display holds SOI.
