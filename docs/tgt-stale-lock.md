# TGT: flagging locked targets the game itself no longer trusts

## Goal

A target can sit locked on the TGT/TGP pages long after it's actually still where the last position
says — the aircraft went cold, or you lost line of sight, and nothing refreshed the fix. The game's
own TGP shows this by swapping the locked target's box for a "?" (`TargetScreenUI.outdatedSprite`).
NO XMFD only distinguished SENSOR (fresh) from DATALINK (relayed) — it had no equivalent of that "?"
state. This adds one: a third SRC value, STALE, plus a STALE button beside DATALINK that bulk-clears
just the stale locks.

## The game's own distinction (decompiled reference, `_scratch/full/`)

- **`FactionHQ.IsTargetPositionAccurate(target, threshold)`** ([FactionHQ.cs](../_scratch/full/FactionHQ.cs))
  is stricter than the freshness check `Datalink` already uses (`TrackingInfo.Observed()`, < 4s since
  last sensed). It returns `true` immediately while fresh, but once stale it falls back to comparing
  the target's **actual current position** (server-authoritative) against the last-known relayed one:
  still within `threshold` metres → still trusted; drifted past it → not trusted.
- **`TargetScreenUI`** ([TargetScreenUI.cs:207](../_scratch/full/TargetScreenUI.cs)) calls this with a
  20m threshold to decide whether a locked target's TGP box gets the normal sprite or `outdatedSprite`
  (the "?"). NO XMFD reuses the same 20m so its STALE flag lines up with what the pilot would see in
  the real TGP.
- Because `IsTargetPositionAccurate` short-circuits `true` while fresh, STALE can only ever be true
  for a contact that's already DATALINK — it's a strict subset, not a separate axis.

## The plan (as built)

1. **`UnitInfo.Stale`** ([TelemetrySnapshot.cs](../src/plugin/TelemetrySnapshot.cs)) — set in
   `BuildUnits()` ([TelemetryReader.cs](../src/plugin/TelemetryReader.cs)) right after `Datalink`:
   `datalink && !playerHQ.IsTargetPositionAccurate(u, 20f)`.
2. **Serialization** ([TelemetryJson.cs](../src/plugin/TelemetryJson.cs)) — one more terse key,
   `"st"`, on `UnitsArray`'s per-contact JSON, next to `dl`.
3. **Client derivation** ([telemetry-source.js](../src/web/services/telemetry-source.js)) — `st`
   rides along into each pushed target item the same way `dl` does.
4. **TGT page rendering** ([tgt.js](../src/web/pages/tgt/tgt.js), [tgt.css](../src/web/pages/tgt/tgt.css)) —
   SRC reads `STALE` (off-white, `--no-label`) when `st`, else `DATALINK` (purple) when `dl`, else
   `SENSOR`.
5. **STALE button** ([tgt.html](../src/web/pages/tgt/tgt.html)) — sits beside DATALINK in the footer,
   same dashed-border mod-only treatment, in `--no-label` instead of purple. **Tap** deselects just
   the stale-locked targets: **`tgt.clear-stale`** ([CommandDispatcher.cs](../src/plugin/CommandDispatcher.cs)),
   sharing `TgtClearBy(op, predicate)` with `tgt.clear-datalink` — both are now real, reachable
   callers, so the bulk-deselect loop went back to being one function instead of two copies.
6. **Preview mock** ([preview-mock.js](../tools/preview-mock.js)) — one mocked target flagged
   `st: true` (implying `dl: true`) so the harness exercises the SRC column's third state and the
   STALE button without the game.

## Design decisions along the way

- **STALE only shows for locked targets** — `Stale` is computed for every enemy contact in
  `BuildUnits` (matching how the game itself uses `IsTargetPositionAccurate` for non-target things
  like HUD markers), but the TGT page only ever renders items from the target list, so in practice
  the pilot only sees it on something they've actually locked. `tgt.clear-stale` only ever walks
  `weaponManager.GetTargetList()` for the same reason — clearing a "stale" unlocked contact isn't a
  thing that needs a button.
- **Tap-only, no hold** — DATALINK's own hold behaviour was removed for being an unpracticed gesture
  no one used; STALE was added afterward and never had one to begin with, for consistency.
