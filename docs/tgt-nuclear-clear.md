# TGT: isolating in-flight nuclear ordnance among locked targets

## Goal

[Issue #91](https://github.com/roke77/NOXMFD/issues/91): a way to isolate in-flight nuclear
ordnance — a missile or bomb that's nuclear-type and already launched/released, by an aircraft or
a land-based unit — among the targets currently locked on TGT. Faction-agnostic (any faction's
nuclear ordnance counts) and about the ordnance itself, never the launch platform that fired it.

## The game's own data (decompiled reference, `decompiled/` + `_scratch/full/`)

- **`WeaponInfo.nuclear`** (`decompiled/WeaponInfo.decompiled.cs:63`) is a plain `bool` field,
  sibling to the `missile`/`bomb`/`glideBomb` flags `WeaponSelectors.cs` already classifies against
  for the player's own loadout.
- **`Missile.GetWeaponInfo()`** (`_scratch/full/Missile.cs:603-606`) exposes that `WeaponInfo`
  publicly on any live in-flight instance.
- **No separate `Bomb` class exists.** `OpticalSeekerBomb` (a glide bomb's seeker component)
  attaches to the same `Missile` GameObject as a guided missile — confirmed against the full
  decompile. One check, `unit is Missile m && m.GetWeaponInfo()?.nuclear == true`, covers both
  missiles and bombs.
- Confirmed against [nuclearoption.wiki.gg](https://nuclearoption.wiki.gg/wiki/Category:Nuclear_Weapon):
  4 nuclear weapons exist as of this writing (GPO-N, ALND-4, Piledriver TBM, AIR-2 Genie).
  **Piledriver TBM has both a High Explosive and a nuclear variant on the same airframe/name** —
  proof that classification has to read the live fired instance's `WeaponInfo`, never infer
  nuclear-ness from a unit type name.

## Why a bulk-deselect button, not a toggle or a selectability gate

TGT's FACTION/CATEGORY/VEHICLE rows mirror the game's own `TargetListSelector` toggle groups
(`CommandDispatcher.cs`'s `TgtGroup`, `TelemetryReader.cs`'s `ReadToggles`) — what the sensor/lock
system is willing to consider a target *before* a lock happens. "Nuclear" isn't a property the
sensor system classifies pre-lock; it's only readable off a specific already-locked `Missile`
instance. There's no native toggle group to mirror the same way, and nothing to intercept before a
lock occurs — so NUCLEAR can't gate selectability like the other three.

A DETAILED/COMPACT-style client-side display filter (hide non-nuclear rows, change nothing in the
game) was the other candidate. Rejected in favor of an actual bulk-deselect, matching what the
requester wanted: isolating nuclear locks is meant to change what's actually selected, not just
what's shown.

## The plan (as built)

1. **`CommandDispatcher.IsNotNuclearOrdnance(FactionHQ, Unit)`** — the predicate, inverse of
   `IsDatalinkOnly`/`IsStale`: `!(unit is Missile m && m.GetWeaponInfo() != null &&
   m.GetWeaponInfo().nuclear)`. No `NetworkHQ == playerHQ` guard (unlike the other two) — nuclear
   ordnance from any faction should be isolated, not just the enemy's.
2. **`ClearNonNuclearTargets()`** — `TgtClearBy("tgt.clear-non-nuclear", IsNotNuclearOrdnance)`,
   reusing the exact bulk-deselect helper `ClearDatalinkTargets`/`ClearStaleTargets` already share.
3. **`tgt.clear-non-nuclear` dispatcher entry** (`CommandDispatcher.cs`).
4. **NUCLEAR button** (`tgt.html`, `tgt.css`, `tgt.js`) — sits beside DATALINK/STALE in the footer,
   same dashed-border mod-only treatment, in `--no-red` (the theme's alert/danger token — this one
   isolates a threat, unlike DATALINK/STALE's data-quality framing). Wired through `send()`, the
   PAD cursor's tap path, and the map-act keybind relay, identically to DATALINK/STALE.
5. **`tgt-nuclear` keybind** (`Keybinds.cs`) — `Clear Non-Nuclear`, same shape as `tgt-datalink`/
   `tgt-stale`: fires `CommandDispatcher.ClearNonNuclearTargets()` directly (works regardless of
   which display, if any, is focused) and a `MapAction` broadcast for a focused TGT display.
6. **No telemetry change.** Unlike `Stale` (a new `UnitInfo` field, serialized, carried through
   `telemetry-source.js`), nuclear-ness is only ever read at the moment of deselect, server-side,
   off the live `Missile.GetWeaponInfo()`. Nothing about it reaches the browser — the button is a
   pure fire-and-forget command, same as DATALINK/STALE's `ForceDeselect` loop.

## Design decisions along the way

- **Isolates, doesn't clear — the one place this diverges from DATALINK/STALE's naming
  convention.** DATALINK and STALE each *clear what they name*. NUCLEAR does the opposite: tapping
  it *keeps* nuclear locks and clears everything else. Flagged deliberately (see issue #91) since a
  player used to the other two buttons' convention could reasonably expect the inverse. No renaming
  was done — "NUCLEAR" is the label the feature was requested under — but it's called out here and
  in `tgt.html`'s header comment for whoever next touches this footer.
- **Faction-agnostic**, unlike `IsDatalinkOnly`/`IsStale` which both explicitly exclude the
  player's own faction (`unit.NetworkHQ == playerHQ` guards to `false`). Nuclear ordnance from any
  faction should be kept when isolating.
- **In-flight ordnance only, never a platform.** A launch platform (bomber, TEL, silo) is never
  itself tagged nuclear — only what it's actually fired, and only once that's a locked target in
  its own right.
- **No unit test for `IsNotNuclearOrdnance`**, matching `IsDatalinkOnly`/`IsStale` — all three
  depend on live game types (`Unit`, `FactionHQ`) not unit-testable outside the game runtime.
