# Next/Previous Target: shared focus across TGT/FCR/HSD

[Issue #62](https://github.com/roke77/NOXMFD/issues/62).

## Goal

Next/Previous Target (`docs/tgt-keybind-nav.md`) drives TGT's own row-stepper, and — the effect this
doc covers — also steps which locked target TGT/FCR/HSD treat as "focused" among however many are
locked, reaching every open TGT/FCR/HSD page in every connected browser regardless of SOI.

This is the foundation `docs/rdr-fcr-hsd.md`'s "Focused lock vs. locked" section describes: FCR and
HSD draw a distinction between "the lock the bottom readout describes" and "any other simultaneous
lock," using this shared focus id as that distinction rather than each page guessing independently.

## What stays untouched

TGT's row-stepper (`highlightIndex`, `docs/tgt-keybind-nav.md`) is a different mechanism aiming
Cursor Select at an arbitrary row — locked or not — and remains exactly as it was: SOI-gated, local
to TGT, unaffected by anything here. Next/Previous now does both things on every press.

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
  both ends (matches TGT's `navHighlight` wrap). Called from `Keybinds.cs`'s new
  `CycleTargetFocus(dir)`, which reads `GameManager.GetLocalAircraft`'s
  `weaponManager.GetTargetList()` directly at press time — no SOI gate, and a DefFree bind, so it
  still runs with no aircraft at all (simply a no-op; nothing is locked yet either).
- **`Reconcile(lockedIds)`** — called once per contact scan
  (`TelemetryReader.RefreshContactSnapshotIfNeeded`, ~4 Hz) with the same list's current contents, to
  handle focus changing for reasons other than a Next/Prev press:
  - 0 locks remaining clears focus;
  - exactly 1 lock remaining always focuses it (matches "first locked" being the always-true case
    when there's only one target locked);
  - losing the focused lock specifically (but others remain) drops focus to none, rather than
    silently jumping the pilot's attention to some other target they didn't choose.

Both use the game's own `weaponManager.GetTargetList()` order — the same list `tgt.set`,
`target.select`/`target.deselect`, and the native TGP camera all already share
(`CommandDispatcher.cs`, `TelemetryReader.cs`'s `BuildRdr`/`BuildHsd`) — so cycling order matches
whatever order the game itself considers the locks to be in, not an order any one page's rendering
happens to walk them in.

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
  selected-target list), so "is this row focused" is a plain `t.id === focusedTargetId` check. A new
  `.tl-row.tgt-focused` class (`tgt.css`) draws an inset amber accent bar, kept visually distinct
  from `.nav-highlight`'s outline (a different action — aiming Select at a row — that can coexist
  with focus on the rare frame both apply to the same row).
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

`dotnet build` (0 errors). `tools/tests/TargetFocusTests.cs` covers `Reconcile`'s three rules (clear
on zero, auto-focus on exactly one, drop focus when the specifically-focused lock is lost) and
`Cycle`'s wraparound in both directions. `tools/tests/TelemetryJsonTests.cs` covers
`focusedTargetId`'s round trip through `TelemetryJson.Serialize`. `rdr.test.js`/`hsd.test.js`/
`pad-cursor.test.js` still pass unchanged (the page-side change is a one-line comparison swap, no
new pure logic to unit-test there). Full `tools/ci-check.ps1` green. Not yet tested in-game.

## Related documents

- [TGT keybind navigation](tgt-keybind-nav.md)
- [RDR hub — FCR and HSD pages](rdr-fcr-hsd.md)
- [Page cursor](page-cursor.md)
