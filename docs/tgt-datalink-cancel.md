# TGT: telling datalink-only locks apart — [issue #29](https://github.com/roke77/NOXMFD/issues/29)

**Branch:** `tgt-datalink-cancel`. **Status:** built, `serve_web`-harness verified. Not yet tested in-game.

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

## The plan (as built)

1. **`UnitInfo.Datalink`** ([TelemetrySnapshot.cs](../src/plugin/TelemetrySnapshot.cs)) — set in
   `BuildUnits()` ([TelemetryReader.cs](../src/plugin/TelemetryReader.cs)) alongside the existing
   `Faction`/`Targeted` fields, using the lookup above. Enemy contacts only; friendly/neutral always
   false.
2. **Serialization** ([TelemetryJson.cs](../src/plugin/TelemetryJson.cs)) — one more terse key,
   `"dl"`, on `UnitsArray`'s per-contact JSON, next to the existing `tg`.
3. **Client derivation** ([telemetry-source.js](../src/web/services/telemetry-source.js)) — the TGT
   page's target list is *already derived client-side from `contacts`*, filtered by `tg` (see the
   comment there: "derive from contacts... the mod flags each targeted unit on its contact"). The new
   flag rides along into each pushed target item as `dl` alongside `id`/`n`/`g`/`r`/`f`.
4. **TGT page rendering** ([tgt.js](../src/web/pages/tgt/tgt.js), [tgt.css](../src/web/pages/tgt/tgt.css)) —
   the target list gained a fifth column, SRC (`SENSOR` / `DATALINK`, purple-tinted when datalink),
   toggled in the existing per-row refresh loop.
5. **DATALINK button** ([tgt.html](../src/web/pages/tgt/tgt.html)) — sits below the target list,
   dashed purple border (distinct from the real `TargetListSelector` filter buttons above it, which
   also gate future selection — this one doesn't): **tap** deselects just the datalink-only targets.
   A new bulk server-side command:
   - **`tgt.clear-datalink`** ([CommandDispatcher.cs](../src/plugin/CommandDispatcher.cs)) — mirrors
     the existing `tgt.clear` ("deselect everything") pattern, scoped by the same datalink/observed
     check as `UnitInfo.Datalink`.
6. **Preview mock** ([preview-mock.js](../tools/preview-mock.js)) — two mocked targets flagged
   `dl: true` so the harness exercises the SRC column and both button directions without the game.

Turned out to need two small new commands after all (see "Open question", below, for how the design
moved there) — everything else is exactly the existing `tgt.clear`/`ForceDeselect`/ `target.deselect`
machinery, just scoped by one extra boolean.

## Design decisions along the way

- **"Sensor" over "radar"** for the live-side label — the game's own distinction (`Observed()`) is
  about *freshness*, not which instrument painted the contact (could be radar, IRST, visual ID, a
  laser designation), so "radar" would overclaim.
- **The button isn't a real filter, and looks like it isn't** — dashed border + a dedicated purple
  accent (`--no-purple` / `--no-purple-rgb`, added to [theme.css](../src/web/shared/theme.css)) mark
  it as a mod-only control, so a pilot who's learned "these buttons gate future selections too"
  (see docs/tgt-page.md) doesn't wrongly assume that about this one.
- **A bulk deselect, not a display filter** — an earlier pass had tap hide datalink rows from view
  (client-side only). Revised per direct feedback: tap deselects the datalink-only targets for real,
  no view-only filtering.
