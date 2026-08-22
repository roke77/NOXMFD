# RWR

A circular radar-warning scope: a nose-up display (whatever's ahead of you is always toward the
top, regardless of your actual heading) showing who's got a radar on you, by bearing.

![RWR page](images/RWR.png)

## Reading the scope

- **Rim and range rings** — the outer edge and the two dashed inner rings are evenly spaced, not
  labeled in any real distance — this page has no adjustable range, unlike [RDR](rdr.md). Closer
  to center means closer to you.
- **Cardinal ticks** — four short marks at the very top/bottom/left/right of the rim, marking
  straight ahead / directly behind / left / right of your nose.
- **Nose marker** — the small triangle at the top of the rim. Always points up, since the whole
  display rotates with you.
- **Ownship caret** — the chevron near the center marks your own position.

## Contacts

Each emitter tracking you shows as a small diamond at its bearing and relative distance, with a
short label:

- **Grey** — searching (their radar is sweeping toward you, not locked on).
- **Yellow** — tracking.
- **Red**, with a bracket around it — locked on you.

A contact fades as its last update ages, so a stale return looks fainter than a fresh one.

## Incoming missiles

A missile actually in flight toward you shows as a flickering red/yellow dart, pointing inward
from its launch bearing — the line connecting it to you shortens as it closes the distance. Its
label shows the seeker type (when known) and range in kilometers. If the missile carries a radar
seeker, a dashed yellow line is also drawn straight through your position along its beam axis —
the notch line — so you can see the actual line of the threat, not just its origin bearing.
