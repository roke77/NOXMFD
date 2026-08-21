# AKF

Part of the **MD** (Mission Data).

A live replica of the game's own kill-feed ticker, split into an ALL column (everyone's kills)
and a PLAYER column (yours — weapon name attached where resolvable, plus "incoming" lines when
you're the one shot down or your own ordnance gets intercepted), with session kill tally, funds
gained/spent, and your current rank below.

![AKF page](images/AKF.png)

## ALL / PLAYER split

The two feeds are read-only, but the space between them isn't: a pair of triangle grips sits on
the divider, and dragging it slides the split left or right so a crowded column can borrow room
from a quiet one.

Dragging a side down past a minimum width collapses it entirely, leaving only the arrow that
points back toward whichever column is still showing — a shortcut for "give the other feed the
whole page" without having to line the drag up by hand.

![AKF page with the ALL column collapsed](images/AKF_COLLAPSED.png)

Clicking that arrow restores the default split (ALL wider than PLAYER). The split (custom or
collapsed) isn't persisted — reloading the page, or navigating away and back, resets it to the
default.

## Density toggle

DETAILED/COMPACT, top right, switches how both feeds render their lines:

- **DETAILED** (default) — the full sentence, as above: `Attacker verb Victim with Weapon`.
- **COMPACT** — each unit name shortens to its first word only (`Hyperion Class Carrier` →
  `Hyperion`), the verb (`shot down`, `intercepted`, `destroyed`, …) collapses to a single `▸`
  arrow regardless of which one it was, and `with` collapses to a `•` dot. The weapon name itself
  is never shortened — it's the one piece of information COMPACT doesn't trade away.

![AKF page in COMPACT mode](images/AKF_COMPACT.png)

Density applies to both feeds at once and only affects how existing entries render — it doesn't
change what's tracked or how session stats below are computed.

## PAD cursor

The resizer and the density toggle are both drivable from a HOTAS
[PAD cursor](keybinds.md#pad-cursor): hovering the resizer and holding Select drags the split the
same way a mouse would, and a plain Select tap on the density toggle flips it. A tap on a
collapsed resizer's arrow restores the split, same as clicking it.
