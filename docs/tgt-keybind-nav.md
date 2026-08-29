# TGT: keybind-driven target-list navigation

## Goal

row and press Select to deselect it — but aiming a 2D crosshair at a specific row in a scrolling list
is fussier than it needs to be. This doc originally added a discrete row-stepper (`highlightIndex`)
walked by Next/Previous Target binds, plus two more keybinds that mirror the DATALINK/STALE buttons
(`docs/tgt-datalink-cancel.md`, `docs/tgt-stale-lock.md`). The row-stepper itself was later removed
in favor of the shared `TargetFocus.Id` (see "Superseded by TargetFocus" below) — Next/Previous still
drives a page-side effect on TGT today, just a different one: which control Cursor Select acts on
(`docs/tgt-cycle-focus.md`'s "Select arbitration"), not a separate stepped index. This doc now covers
the two DATALINK/STALE binds in full, and points to `tgt-cycle-focus.md` for Next/Previous.

## The four binds

All four are `TGT Keybinds` (`src/plugin/Input/Keybinds.cs`), placed right after `MAP Keybinds` —
DefFree like MAP/SOI, since they drive the mod's own display rather than the aeroplane:

- **Next Target** / **Previous Target** — see `docs/tgt-cycle-focus.md` (both the shared
  `TargetFocus.Id` they step, and TGT's own "Select arbitration" reaction to the press).
- **Clear Datalink** / **Clear Stale** — fire `tgt.clear-datalink`/`tgt.clear-stale` directly, the
  keybind equivalents of tapping those buttons.

## Transport: reusing map-act, not building new plumbing

Zoom In/Out already prove out a generic channel for "an extra discrete action bound to whatever's
SOI-focused; the page decides what it means" (`Keybinds.cs` → `TelemetryServer.MapAction("...")` →
`mfd.js`'s `map-act` forwarding, gated on `focusedCursorWindow()`, → the focused page's `message`
handler as `{mfd:true, action:'...'}`). All four binds above ride that exact channel with their own
action strings (`'tgt-next'`, `'tgt-prev'`, `'tgt-datalink'`, `'tgt-stale'`) — no shell or server
changes beyond the `MapAction(...)` calls. This also means they're automatically scoped to only reach
TGT while it's the SOI-focused display (the same gate Zoom In/Out already have) — nothing extra
needed for that. Next/Previous also fire `Keybinds.CycleTargetFocus(dir)` directly, no SOI gate,
since `TargetFocus.Id` reaches every open TGT/FCR/HSD browser regardless of which one has SOI
(`docs/tgt-cycle-focus.md`).

`tgt-datalink`/`tgt-stale` need nothing plugin-side beyond the bind itself: `tgt.clear-datalink` and
`tgt.clear-stale` already exist as commands (`CommandDispatcher.cs`); `tgt.js` just calls the same
`send(...)` its own buttons call.

## Superseded by TargetFocus (row-stepper removed)

Next/Previous Target originally drove two independent effects on this page: a page-local row-stepper
(`highlightIndex`, walking `tgt.js`'s own copy of the target list) *and* the shared, server-side
`TargetFocus.Id` that FCR/HSD also read (`docs/tgt-cycle-focus.md`). In practice these two trackers
disagreed with real frequency — anything that changed the locked-target set *other than* a Next/Prev
press (a fresh lock added via the native in-game keybind, a lock lost to a kill or range) moved
`TargetFocus.Id` through `TargetFocus.Reconcile` immediately, but left `highlightIndex` — a plain
array index with no knowledge locks had changed — pointing at whatever slid into that slot, often a
completely different unit. The result: TGT's amber outline (the row-stepper) and its amber accent bar
(the focused lock) routinely landed on two different rows even though both were meant to answer the
same question, "which locked target is current."

Rather than patch the row-stepper to watch for every event that can desync it from `TargetFocus`,
`highlightIndex` was removed outright: `TargetFocus.Id` (`focusedTargetId` on the wire) is now the
*only* "which row is current" state `tgt.js` keeps. Cursor Select's old row-stepper shortcut and its
mutual exclusivity with the free crosshair both moved onto this single tracker too — see
`docs/tgt-cycle-focus.md`'s "Select arbitration" section for the current shape of that (which control
owns Select, and the amber/grey outline that shows which one).

## F-35 layout: everything above rode a channel it didn't reach

First in-game test found the (then four) binds inert, and separately that RDR's crosshair never moved
either — both symptoms of the same pre-existing gap, not this feature: `f35.js` had its own MAP-only
`focusedMapWindow()` (`docs/page-cursor.md` step 6 named this as a known, deliberately scoped-out
limitation), so `map-act` — the channel every bind here rides — silently never reached a portal
showing TGT, HUD, or RDR. Widened it (`docs/page-cursor.md`'s step 6 update has the details) — also
found `cursor-held` wasn't forwarded by F-35 *at all*, which the bezel already did: TGT/RDR's Select
tap-vs-hold arbitration lives entirely in that held state, not the plain edge-driven `cursor-select`
MAP/HUD use, so Select never fired any outcome for either page under F-35, unrelated to this feature
but caught by the same testing pass.

## Verification

`dotnet build` (0 errors). `serve_web` harness, both layouts: `window.__mapAct('tgt-datalink')`/
`'tgt-stale'` post the same `/command` bodies the buttons do. On F-35 specifically: confirmed a TGT
portal's crosshair shows on SOI focus and a separate RDR portal's own cursor element receives real
`cursor` vector messages with the posted x/y — both of which reached nothing before the `f35.js`
widening. Next/Previous Target's current page-side effect (Select arbitration) is covered by
`docs/tgt-cycle-focus.md`'s own verification, including the in-game finding that prompted it.
