# TGT: telling datalink-only locks apart — [issue #29](https://github.com/roke77/NOXMFD/issues/29)

**Branch:** `tgt-datalink-cancel`. **Status:** planning.

## Goal

A user request, referencing [NO_Tactitools](https://github.com/clumzy/NO_Tactitools) (NOTT), which
apparently has the same control already: a way to cancel a **datalink-only** lock on the TGT page —
a target your own aircraft isn't actually sensing, just relayed to you by a friendly unit's radar via
the faction's shared tracking database — without disturbing a target you're genuinely tracking
yourself. Today the TGT page's selected-target list shows every locked target the same way; there's
no way to tell which ones are stale/relayed vs. actively sensed.

- **Primary ask:** a dedicated control to cancel a datalink-only lock specifically.
- **Fallback ask:** at minimum, show which entries are datalink-sourced so the pilot can tell them
  apart.

## What already exists to reuse (read before building)

Turns out the primary ask is **already there** — this is almost entirely the fallback ask. TGT's
selected-target list already supports canceling any one lock:

- **`target.deselect {id}`** ([CommandDispatcher.cs](../src/plugin/CommandDispatcher.cs)) — deselects
  a single unit by persistentID, via `CombatHUD.DeSelectUnit` (falling back to the bare
  `weaponManager` op when the HUD isn't tracking the contact). No-ops if it isn't currently a target.
- **The TGT page already wires this** ([tgt.js](../src/web/pages/tgt/tgt.js):133-141) — tapping a
  row's checkbox sends `target.deselect` for that row's id. Nothing new needed here: canceling a
  datalink-only lock is *exactly* canceling any other lock, once the pilot can see which row is which.

So the only real gap is **visibility**: which rows in that list are datalink-only. Once that's shown,
the existing tap-to-deselect already cancels the right one.

### The game's own distinction (decompiled reference, `_scratch/full/`)

- **`FactionHQ.trackingDatabase`** (`Dictionary<PersistentID, TrackingInfo>`) is how *any* enemy
  unit's position reaches you at all unless it's your own faction — confirmed by
  `FactionHQ.GetKnownPosition()`: own-faction units return their live transform directly, everything
  else resolves *only* through this dictionary. Our own `TelemetryReader.BuildUnits()` already calls
  `playerHQ.TryGetKnownPosition(u, out gp)` for exactly this reason — every enemy contact we already
  show has necessarily gone through this path.
- **`TrackingInfo.Observed()`** — `Time.timeSinceLevelLoad - lastSpottedTime < 4f`. True only if
  *some* friendly sensor (not necessarily yours) painted it within the last ~4 seconds. False means
  the entry is sitting in the shared database from an older sighting — a stale, datalink-only
  position, which is exactly the case this ticket is about.
- **`FactionHQ.GetTrackingData(id)` / `IsTargetBeingTracked(unit)`** — the accessor to read a given
  unit's `TrackingInfo` and check `Observed()` without duplicating the 4s constant ourselves.

So **"datalink-only" = `!playerHQ.GetTrackingData(u.persistentID).Observed()`** for a targeted enemy
unit. (Own-faction targets, if they can even be targeted, are never datalink-only — their position
always comes from `NetworkHQ == this`, not the tracking database.)

## The plan

1. **`UnitInfo`** ([TelemetrySnapshot.cs](../src/plugin/TelemetrySnapshot.cs)) gains one field —
   `Stale` (or similar) — set in `BuildUnits()` alongside the existing `Faction`/`Targeted` fields,
   using the lookup above. Enemy contacts only; friendlies default false.
2. **Serialization** ([TelemetryServer.cs](../src/plugin/TelemetryServer.cs)) — one more terse key on
   `UnitsArray`'s per-contact JSON (`"dl"` or similar), next to the existing `tg`.
3. **Client derivation** ([telemetry-source.js](../src/web/services/telemetry-source.js):225-241) —
   the TGT page's target list is *already derived client-side from `contacts`*, filtered by `tg`
   (see the comment there: "derive from contacts... the mod flags each targeted unit on its
   contact"). Carry the new flag through into each pushed target item alongside `id`/`n`/`g`/`r`/`f`.
4. **TGT page rendering** ([tgt.js](../src/web/pages/tgt/tgt.js):102-131) — one more element per row
   (a small "DATALINK" badge or dimmed styling), toggled in the existing per-row refresh loop that
   already updates name/grid/range each frame. The checkbox next to it is already wired to
   `target.deselect` — no new interaction needed.
5. **Preview mock** ([preview-mock.js](../tools/preview-mock.js)) — flag one or two of the mocked
   targets as datalink-only so the harness can show/verify the badge without the game.

No new server command, no new client message type, no change to the deselect path at all — this is
one boolean threaded through plumbing that already exists end to end, plus a badge in the existing
row template.

## Open question

Whether a *dedicated* "cancel datalink locks" bulk action (beyond the existing per-row deselect) is
still wanted once the badge exists — the ticket's primary ask reads like it assumed no per-target
cancel existed yet. Worth checking with the user once the badge is in, rather than building a bulk
action nobody asked for after finding out the granular one already works.
