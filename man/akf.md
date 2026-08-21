# AKF

Part of the **MD** (Mission Data).

A live replica of the game's own kill-feed ticker, split into an ALL column (everyone's kills)
and a PLAYER column (yours — weapon name attached where resolvable, plus "incoming" lines when
you're the one shot down or your own ordnance gets intercepted), with session kill tally, funds
gained/spent, and your current rank below.

![AKF page](images/AKF.png)

Drag the divider between the two columns to resize them. Past a minimum width the shrinking side
collapses to just its arrow — click it to restore the default split.

![AKF page with the ALL column collapsed](images/AKF_COLLAPSED.png)

The DETAILED/COMPACT toggle at the top right switches both feeds to a condensed one-line style:
unit names shorten to their first word, the action collapses to an arrow, and "with" collapses to
a dot — the weapon name stays in full either way.

![AKF page in COMPACT mode](images/AKF_COMPACT.png)
