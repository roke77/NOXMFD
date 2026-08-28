# TGT: keybind-driven target-list navigation

## Goal

The PAD cursor (`docs/page-cursor.md`) already lets a HOTAS pilot aim a free crosshair at a target
row and press Select to deselect it — but aiming a 2D crosshair at a specific row in a scrolling list
is fussier than it needs to be. [NO_Tactitools](https://github.com/clumzy/NO_Tactitools) proves out a
simpler shape for exactly this: a discrete row-stepper (`TargetListController.cs`'s
`targetIndex`) walked by two dedicated Next/Previous binds, with a third bind acting on whichever row
is current. This adds the same shape to NO XMFD's TGT page, plus two more keybinds that mirror the
DATALINK/STALE buttons (`docs/tgt-datalink-cancel.md`, `docs/tgt-stale-lock.md`).

Next/Previous Target also drive a second, independent effect: which locked target TGT/FCR/HSD treat
as "focused," everywhere those pages are open — see docs/tgt-cycle-focus.md. This doc covers only
the row-stepper (`highlightIndex`) described below.

## The four binds

All four are `TGT Keybinds` (`src/plugin/Input/Keybinds.cs`), placed right after `MAP Keybinds` — DefFree
like MAP/SOI, since they drive the mod's own display rather than the aeroplane:

- **Next Target** / **Previous Target** — step a highlighted row through the locked-target list,
  wrapping at both ends.
- **Clear Datalink** / **Clear Stale** — fire `tgt.clear-datalink`/`tgt.clear-stale` directly, the
  keybind equivalents of tapping those buttons.

## Transport: reusing map-act, not building new plumbing

Zoom In/Out already prove out a generic channel for "an extra discrete action bound to whatever's
SOI-focused; the page decides what it means" (`Keybinds.cs` → `TelemetryServer.MapAction("...")` →
`mfd.js`'s `map-act` forwarding, gated on `focusedCursorWindow()`, → the focused page's `message`
handler as `{mfd:true, action:'...'}`). The four binds above just ride that exact channel with new
action strings (`'tgt-next'`, `'tgt-prev'`, `'tgt-datalink'`, `'tgt-stale'`) — no shell or server
changes beyond the four `MapAction(...)` calls. This also means they're automatically scoped to only
reach TGT while it's the SOI-focused display (the same gate Zoom In/Out already have) — nothing
extra needed for that.

`tgt-datalink`/`tgt-stale` need nothing plugin-side beyond the bind itself: `tgt.clear-datalink` and
`tgt.clear-stale` already exist as commands (`CommandDispatcher.cs`); `tgt.js` just calls the same
`send(...)` its own buttons call.

## Mutual exclusivity with the PAD cursor

The row-stepper and the free crosshair are two different ways to say "this one" and shouldn't both be
live at once — explicit design requirement, not an assumption:

- **Next/Previous → hides the crosshair.** `pad-cursor.js` gained `setHidden(bool)`: forces the
  crosshair invisible without touching its `pos`/`vec`, so un-hiding resumes exactly where it was
  left (matches the "parked, not forgotten" contract focus-loss already has). `tgt.js`'s
  `navHighlight(dir)` calls `cursor.setHidden(true)` every time it moves `highlightIndex`.
- **Moving the crosshair → clears the highlight.** `telemetry-source.js` only posts a `'cursor'`
  message when the vector actually *changes* (`docs/page-cursor.md`), so every message TGT receives
  represents a real deflection or a release-to-zero. `tgt.js`'s handler clears the highlight (and
  un-hides the crosshair) only on `m.x || m.y` — the deflection, not the release that immediately
  follows it — so tapping a direction key and letting go doesn't re-clear anything on its own trailing
  zero.
- **Cursor Select's outcome depends on which mode is active.** `padCursorSelectAt` checks
  `highlightIndex` first: if a row is highlighted, Select deselects it (`deselectHighlighted`) and
  returns before ever reaching the crosshair's DOM hit-test; otherwise Select behaves exactly as
  `docs/page-cursor.md` already documents.

## Rendering

`highlightIndex` (-1 = none) lives in `tgt.js` next to `targets`/`targetsKey`. `renderTargets()`
reclamps it (`>= targets.length` → last valid index) before checking whether to rebuild the DOM — the
same list-shrink case `docs/tgt-stale-lock.md` already had to handle for `targets` itself — then
toggles a `.nav-highlight` class on the row at that index in its existing per-row refresh loop
(alongside `.datalink`/`.stale`), so the highlight survives both the 10 Hz text refresh and a full row
rebuild without separate bookkeeping.

`.tl-row.nav-highlight` (`tgt.css`) is a persistent 2px `--no-amber` outline + a faint amber wash —
persistent rather than `.pad-hover`'s transient overlay, since it has to stay legible while the
crosshair (and its hover feedback) is hidden. `--no-amber` matches its existing "selected/waiting
state" meaning elsewhere in the theme.

## F-35 layout: everything above rode a channel it didn't reach

First in-game test found the four binds inert, and separately that RDR's crosshair never moved
either — both symptoms of the same pre-existing gap, not this feature: `f35.js` had its own
MAP-only `focusedMapWindow()` (`docs/page-cursor.md` step 6 named this as a known, deliberately
scoped-out limitation), so `map-act` — the channel every bind here rides — silently never reached a
portal showing TGT, HUD, or RDR. Widened it (`docs/page-cursor.md`'s step 6 update has the details) —
also found `cursor-held` wasn't forwarded by F-35 *at all*, which the bezel already did: TGT/RDR's
Select tap-vs-hold arbitration lives entirely in that held state, not the plain edge-driven
`cursor-select` MAP/HUD use, so Select never fired any outcome for either page under F-35, unrelated
to this feature but caught by the same testing pass.

## Verification

`dotnet build` (0 errors). `serve_web` harness, both layouts: `window.__mapAct('tgt-next')`/
`'tgt-prev'` step the highlight and hide the crosshair; `window.__cursorVec(x, y)` with a nonzero
component clears it and un-hides the crosshair again; `window.__mapAct('tgt-datalink')`/`'tgt-stale'`
post the same `/command` bodies the buttons do; `window.__cursorHold(true)` then `false` within
300ms deselects the highlighted row. On F-35 specifically: confirmed a TGT portal's crosshair shows
on SOI focus, `tgt-next` reaches it and hides it, held-then-released reaches it as a deselect, and a
separate RDR portal's own cursor element receives real `cursor` vector messages with the posted
x/y — all of which reached nothing before the `f35.js` widening. Not yet tested in-game.
