# RDR

A radar scope (an F-16-style B-scope) showing your own ownship at the bottom, bearing spread
left-right, and range stretching upward toward the top of the screen.

![RDR page](images/RDR.png)

## Reading the scope

- **Top-left label** — always reads `RWS`. Cosmetic, matching the game's own radar display; it
  doesn't reflect an actual mode.
- **Sweep needle** — mirrors the game's own in-cockpit radar sweep; only animates while your radar
  is on.
- **Contacts** — colored bricks with a short velocity-vector stub showing which way they're
  heading:
  - **Green** — detected by your own radar.
  - **Purple** — datalink-only: shared by your faction, not currently painted by your own radar.
  - **Amber**, with a ring around it — locked.
- **Your own missiles** — once one of your AA missiles goes pitbull (active seeker, no longer
  needing your radar), it shows as a blue dart pointed at its target, with a flickering line
  connecting the two.

## The numbers on screen

- **Top-right** — the selected display range (see [Changing range](#changing-range) below), in
  nautical miles or kilometers depending on your game's unit setting.
- **Bottom-left and bottom-right, next to your ownship position** — the radar's azimuth cone, in
  degrees left/right off the nose (e.g. `-30` / `+30`). Everything plotted on the scope falls
  within this cone; a contact outside it isn't shown at all.

## When a target is locked

The bottom of the screen fills in with your **first** locked contact (if you have several locked
at once, this always shows the same one — not whichever you locked most recently):

- **Name** — the contact's identifying label, large and centered.
- **RNG** — its range from you.
- **ALT** — its altitude.
- **HDG** — its heading, as a 3-digit compass bearing (e.g. `270`), not relative to your own nose.
- **LOCK** — a count of every contact currently locked, not just the one shown above.

Range and altitude use the same unit setting as the rest of the game (nm/ft or km/m).

## Locking a target

Click or tap directly on a contact to lock it; click again to unlock. The same works with a HOTAS
[PAD cursor](keybinds.md#pad-cursor) aimed between its two acquisition bars and Select pressed —
that needs the PAD cursor's binds (Cursor Up/Down/Left/Right/Select, and the two axis binds) set
up first on the [KEY](keybinds.md) page.

## Changing range

**R+ / R−** step the display range in and out — a fraction of the radar's true max range, not a
fixed distance, so it always fills the scope regardless of which radar you're flying with. Your
choice is remembered across page reloads. Pushing the PAD cursor past the scope's top or bottom
edge steps the range the same way.
