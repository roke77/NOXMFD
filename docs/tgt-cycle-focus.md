# Next/Previous Target: shared focus across TGT/FCR/HSD

[Issue #62](https://github.com/roke77/NOXMFD/issues/62).

## Goal

Next/Previous Target (`docs/tgt-keybind-nav.md`) steps which locked target TGT/FCR/HSD treat as
"focused" among however many are locked, reaching every open TGT/FCR/HSD page in every connected
browser regardless of SOI. On the SOI-focused TGT display, the same press also hands Cursor Select to
that focused row until the pilot moves the PAD cursor again.

This is the foundation `docs/rdr-fcr-hsd.md`'s "Focused lock vs. locked" section describes: FCR and
HSD draw a distinction between "the lock the bottom readout describes" and "any other simultaneous
lock," using this shared focus id as that distinction rather than each page guessing independently.

## Consolidated to one highlight (row-stepper removed)

TGT originally kept its own page-local row-stepper (`highlightIndex`, `docs/tgt-keybind-nav.md`)
alongside this shared `TargetFocus.Id` — two independent trackers both answering "which locked row is
current." Live testing found they routinely disagreed: locking a *new* target via the native in-game
keybind (not a Next/Prev press) reconciles `TargetFocus.Id` immediately (see `Reconcile` below), but
`highlightIndex` — a plain array index with no idea the locked set just changed — kept pointing at
whatever slid into that slot, often a different unit entirely. Sorting TGT's own target-list build to
match `weaponManager.GetTargetList()`'s order was tried first and fixed the *ordering* mismatch, but
not this one: the two trackers can drift for reasons that have nothing to do with list order,
since only one of them (`TargetFocus`) actually reacts to locks changing outside a Next/Prev press.

Fixed by removing `highlightIndex` outright rather than teaching it to watch for every event that can
desync it: `focusedTargetId` (this doc's `TargetFocus.Id`) is now TGT's *only* "which row is current"
state. `tgt.js`'s `.tl-row.tgt-focused` class (`tgt.css`) draws the outline that used to mean
"row-stepper here" — the previous inset accent bar is gone, since there's only one highlight concept
left to draw. See "Select arbitration" below for what decides its color and what Cursor Select does
with it.

## Select arbitration: Next/Previous vs. the PAD cursor

Removing `highlightIndex` also removed the mutual exclusivity `docs/tgt-keybind-nav.md` originally
built between the row-stepper and the free crosshair — and having only one tracker left made that
regress visibly: with a lock always focused (`focusedTargetId` is populated the moment anything is
locked, `Reconcile` below, not only after a Next/Prev press), Cursor Select unconditionally deselected
the focused lock even while the pilot had moved the crosshair over an unrelated control (a filter
cell, DATALINK, ...) to act on that instead. Found live: SOI-focused TGT with two locks from HSD,
moved the crosshair over FRIENDLY, pressed Select — the focused lock got deselected, FRIENDLY never
toggled.

Fixed with a small piece of *local, UI-only* state in `tgt.js` — `crosshairActive` — that decides
which control Cursor Select acts on, independent of `focusedTargetId` itself (so there's no second
"which target" tracker to desync, only a "who does Select listen to" flag):

- **Next/Previous Target** (`'tgt-next'`/`'tgt-prev'` actions) sets `crosshairActive = false` and
  hides the crosshair (`cursor.setHidden(true)`, `pad-cursor.js`) — Select now deselects the focused
  lock directly, same as tapping its row, with no aiming needed. The focused row's outline is amber.
- **Moving the crosshair** (a real deflection on the `'cursor'` action, not the `(0,0)` a key release
  reports) sets `crosshairActive = true` and un-hides it — Select now hit-tests the crosshair's
  position like any other PAD-cursor page. The focused row's outline turns grey (`.tgt-focused-inactive`,
  `tgt.css`) so it's visually clear Select won't act on it right now, without hiding that it's still
  the shared focus other pages (FCR/HSD, the HUD TTI cue) are reading.
- **Gaining SOI focus** (`'cursor-focus'` with `on:true`) always resets to `crosshairActive = true` —
  no stale mode carries across a focus loss.

This mirrors `docs/tgt-keybind-nav.md`'s original row-stepper/crosshair exclusivity almost exactly,
just re-targeted at the single surviving tracker instead of a second one.

## Shared state: TargetFocus

`src/plugin/Http/TargetFocus.cs` holds one id (a `Unit.persistentID.Id`, 0 = none) — no version
counter, unlike `SoiFocus.cs`'s own idempotent fields: `FocusedTargetId` rides inside
`TelemetrySnapshot` itself (`TelemetryReader.PushSnapshot`), so it's already covered by the
snapshot's own version, whereas SOI state is serialized live off `SoiFocus` at request time
(`TelemetryServer.GetFrameBytes`) and genuinely needs its own. `TargetFocus` is a different concept
from `SoiFocus` regardless: SoiFocus tracks which *surface* (display/pane) is focused; TargetFocus
tracks which *target* is focused. The two are independent by design — a pilot cycles target focus
without touching SOI.

- **`Cycle(dir, lockedIds)`** — steps to the next/previous id in `lockedIds`' own order, wrapping at
  both ends. Called from `Keybinds.cs`'s new
  `CycleTargetFocus(dir)`, which reads `GameManager.GetLocalAircraft`'s
  `weaponManager.GetTargetList()` directly at press time — no SOI gate, and a DefFree bind, so it
  still runs with no aircraft at all (simply a no-op; nothing is locked yet either).
- **`Reconcile(lockedIds)`** — called once per contact scan
  (`TelemetryReader.RefreshContactSnapshotIfNeeded`, ~4 Hz) with the same list's current contents, to
  handle focus changing for reasons other than a Next/Prev press:
  - 0 locks remaining clears focus;
  - exactly 1 lock remaining always focuses it (matches "first locked" being the always-true case
    when there's only one target locked);
  - nothing focused yet but 2+ are already locked (locked several before ever touching
    Next/Previous) defaults to the first one — matches `WeaponManager.Fire()`'s own `targetList[0]`
    convention. Found missing during in-game testing (issue #67's HUD TTI report): locking two
    targets from the MAP without ever pressing Next/Prev left focus stuck at "none" indefinitely;
  - losing the focused lock specifically (but others remain) drops focus to none, rather than
    silently jumping the pilot's attention to some other target they didn't choose.

Both use the game's own `weaponManager.GetTargetList()` order — the same list `tgt.set`,
`target.select`/`target.deselect`, and the native TGP camera all already share
(`CommandDispatcher.cs`, `TelemetryReader.cs`'s `BuildRdr`/`BuildHsd`) — so cycling order matches
whatever order the game itself considers the locks to be in, not an order any one page's rendering
happens to walk them in.

## Display order matches cycle order

TGT's own selected-target list is built client-side from the contact scan (`Units`/`BuildUnits`),
which walks an order that has nothing to do with lock order — so without help, the table could show
locks in one sequence while Next/Previous stepped focus through a different one. By request, TGT now
sorts its own list to match: `TelemetrySnapshot.LockedTargetIds` carries `weaponManager.GetTargetList()`'s
order (the same list `Reconcile`/`Cycle` above already use) on the wire as `lockedTargetIds`, and
`telemetry-source.js` sorts the contact-derived `targets` array by each row's index in it before
handing it to `tgt.js`. This is purely a display nicety now, not a correctness fix — since
`docs/tgt-cycle-focus.md`'s "Consolidated to one highlight" section removed TGT's own row-stepper,
nothing depends on the table's row order for correctness anymore, only on `focusedTargetId` matching
the right row wherever it sits.

## Transport

`TelemetrySnapshot.FocusedTargetId` is a new top-level field, broadcast the same way `MapReachW`/`H`
(issue #65) are — a plain `StringBuilder.Append` in `TelemetryJson.AppendFrameHeader`, not a
numbered `{N}` slot, so it doesn't force renumbering the rest of the format string. It travels in
the ordinary shared telemetry frame, which every connected display already receives identically
regardless of SOI focus (`TelemetryServer.GetFrameBytes`'s cached frame) — no new SOI plumbing
needed for "reach every open browser."

`telemetry-source.js` forwards it as `focusedTargetId` on the `'targets'`, `'rdr'`, and `'hsd'`
postUp messages (the three feeds that already carry per-contact `tg`/lock membership). The classic
shell (`mfd.js`) and F-35 glass (`f35.js`) both pass it straight through their existing forwarding —
`f35.js`'s generic `forwardSlice` already relays messages verbatim, so only its one hand-built
message (`forwardHsd()`, which merges `hsd`/`mapinfo`/`rdr` into a single custom payload) needed an
explicit `focusedTargetId` addition.

## Page changes

- **`tgt.js`** — every row in the `'tgt-targets'` list is already locked (it's the TGT panel's
  selected-target list), so "is this row focused" is a plain `t.id === focusedTargetId` check. Which
  class it draws (`.tl-row.tgt-focused` amber, or `.tgt-focused-inactive` grey) and what Cursor
  Select does with it both depend on `crosshairActive` — see "Select arbitration" above.
- **`rdr.js`** — `renderContacts()`'s `focused` check changed from `locked && !first` to
  `locked && c.id === state.focusedTargetId`; the bottom readout (`renderReadout`) now describes
  the actually-focused contact, not whichever locked one the loop reached first.
- **`hsd.js`** — the same swap in its own `renderContacts()`/`renderReadout()`.

## Native TGP camera — explicitly out of scope for now

Investigated as part of this ticket (see the issue body): the game's own targeting-pod camera
(`TargetCam.decompiled.cs`, wired up via `TgpFeed.cs`/`TgpManualControl.cs`) has no "cycle which
lock is focused" concept at all. With one locked target it points straight at it
(`SingleTargetPositionAndSize`); with two or more it aims at the bounding-box center of *all* of
them and zooms out to fit (`MultipleTargetPositionAndSize`) — there's no "point at just one of
several" behavior to reuse. Making the camera track `TargetFocus.Id` instead of the averaged group
would need a Harmony patch over `TargetCam`'s own aim-point math, not just reading this new state —
left as a clearly-scoped-out follow-up; tracking focus centrally here is what would make that patch
straightforward to add later without re-plumbing anything.

## Verification

`dotnet build` (0 errors). `tools/tests/TargetFocusTests.cs` covers `Reconcile`'s four rules (clear
on zero, auto-focus on exactly one, default to the first lock when nothing was focused yet and 2+
appear together, drop focus when the specifically-focused lock is lost) and `Cycle`'s wraparound in
both directions. `tools/tests/TelemetryJsonTests.cs` covers `focusedTargetId`'s round trip through
`TelemetryJson.Serialize`. `telemetry-source.test.js` covers sorting TGT's target list to match
`lockedTargetIds`. `rdr.test.js`/`hsd.test.js`/`pad-cursor.test.js` still pass unchanged. Full
`tools/ci-check.ps1` green.

**Confirmed in-game**, including the row-stepper removal above: found live while locking targets from
HSD that adding a fresh lock via the native in-game keybind desynced TGT's old row-stepper from
`focusedTargetId`, which is what prompted removing the row-stepper rather than patching it further.
Also found live, immediately after that fix landed: with the row-stepper gone, Cursor Select
unconditionally deselected the focused lock even after the pilot moved the crosshair over an
unrelated control — the "Select arbitration" section above's `crosshairActive` fix. Not yet
re-tested in-game since that fix; `pad-cursor.test.js` covers `setHidden`/`getPos`'s hidden-state
behavior it depends on.

## Related documents

- [TGT keybind navigation](tgt-keybind-nav.md)
- [RDR hub — FCR and HSD pages](rdr-fcr-hsd.md)
- [Page cursor](page-cursor.md)
