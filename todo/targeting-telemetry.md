# Targeting Telemetry — Color Targeted Unit Icons

## Context

The web HUD draws every contact in its faction color (friendly green, enemy red,
neutral grey). There is no way to see which unit the player has **targeted** in the
game. Goal: when a unit is the player's current in-game target, recolor its map
icon — **friendly → light blue, enemy → orange** — so the locked contact stands out.

**Unlike the zoom and hover plans, this is NOT purely client-side.** Whether a unit
is targeted is game state that lives only in the mod process, so it has to be
detected in `src/TelemetryReader.cs`, added to the payload, and then rendered by the
client. The chain is: read target (server) → flag the unit (snapshot) → serialize →
recolor (client).

## Phase 0 — Investigation (the real unknown): find the targeting API

The mod references the game's `Assembly-CSharp.dll` directly and keeps a local,
gitignored `decompiled/` copy of the game source for reference (see `.gitignore`
and `NOTelemetryReader.csproj`). **The decompiled source is not on this machine —
it lives on the Windows build box**, so this investigation must happen there.

Find a reliable accessor for the player's current target, reachable from the local
`Aircraft`. Search the decompiled source for likely owners and members:

- `WeaponManager` (already used at `TelemetryReader.cs:261` via
  `aircraft.weaponManager` / `currentWeaponStation`) — check for `target`,
  `currentTarget`, `lockedTarget`, `selectedTarget`.
- `Aircraft` itself, and any radar / sensor / targeting component
  (`grep -riE "Unit .*[Tt]arget|[Tt]arget *\{ *get" decompiled/`).
- The HUD / target-box code — whatever the in-game reticle reads is exactly the
  "target" the user means; trace back from there to its source field.
- Missile/seeker lock vs radar track vs designated ground target — **decide which
  notion of "targeted" to mirror.** The user almost certainly means the single
  primary target the HUD boxes; prefer that one accessor. (Design for a *set* of
  targeted units anyway, so multi-lock cases degrade gracefully.)

Expected shape of the result: a `Unit` reference (or a small set). If the field is
non-public, reflect into it once and cache the `FieldInfo` — reuse the exact pattern
already in `GetSelectedCmCategory` (`TelemetryReader.cs:126-160`), including the
`try/catch` fallback so a game update that renames the field degrades to "no target"
rather than crashing.

**Identity matching:** match the target `Unit` against the existing `_units` scan by
reference (`ReferenceEquals`), not by position — positions are fuzzy under fog of
war and floating origin, references are exact.

## Phase 1 — Server: flag the targeted unit(s)

1. **Snapshot model** (`src/TelemetrySnapshot.cs`): add `public bool Targeted;` to
   `UnitInfo` (struct at `:60-68`). Defaults to `false`, so untouched paths are safe.

2. **Resolve the target once per push** (`src/TelemetryReader.cs`, in `PushSnapshot`
   at `:246`, before `BuildUnits`): get the player's target `Unit` via the accessor
   from Phase 0; pass it (or a `HashSet<Unit>`) into `BuildUnits`.

3. **Set the flag** in `BuildUnits` (`:345-376`): when adding each `UnitInfo`, set
   `Targeted = ReferenceEquals(u, target)` (or `targetSet.Contains(u)`). The targeted
   enemy is by definition tracked, so it already passes `TryGetKnownPosition` — no FOW
   special-casing needed.

   Note the cadence: `_units` is refreshed at 1 Hz (`ScanWorld`) but the target is
   read at 10 Hz in `PushSnapshot`. A just-acquired target that isn't yet in `_units`
   simply won't highlight for up to ~1 s — acceptable; reference matching is correct
   once it appears.

## Phase 2 — Serialize

In `src/TelemetryServer.cs`, `UnitsArray(...)` (around `:305`), add `"tg":true/false`
to each contact object alongside `t/x/z/h/f/o/s`. Keep it boolean-cheap (only emit on
true, or always emit `0/1`) — match the existing compact style.

## Phase 3 — Client: recolor targeted icons

In `src/ClientPage.cs`:

1. Add target color constants near the faction colors (`:183-184`):
   `const TARGET_FRIENDLY = '#5cc8ff';  // light blue`
   `const TARGET_ENEMY    = '#ff8000';  // orange`
   (Both are distinct from friendly green / enemy red / neutral grey and from the
   selected-weapon `#ffaa00` used in the loadout panel.)

2. In `drawOverlay()` (`:301-307`), when a contact's `u.tg` is true, choose the color
   by faction: `f===1 → TARGET_FRIENDLY`, `f===2 → TARGET_ENEMY` (neutral targeted →
   fall back to enemy orange or leave as-is — minor decision). Pass that as the `hex`
   into `drawIcon`; the existing per-`type|hex` tint cache (`:255-272`) handles the new
   colors automatically. The icon's glow (`shadowColor`) follows the same hex, so the
   highlight reads clearly.

3. Leave the player's own icon untouched (it stays HUD green).

## What's challenging

1. **Finding the right game API (main risk/unknown).** There may be several
   target-like concepts (radar lock, IR seeker lock, designated ground target, HUD
   reticle target). Pick the one the HUD boxes so it matches what the user sees.
   Requires the decompiled source on the build machine.
2. **Brittleness across game updates.** Any private-field access must use the cached
   reflection + `try/catch` fallback pattern so a rename can't crash the push loop.
3. **One vs many targets.** Decide and document the semantics; the per-unit boolean
   keeps the wire format simple regardless.
4. **Scan-vs-push cadence (1 Hz vs 10 Hz).** Brief highlight lag on a freshly
   acquired target; acceptable, noted above.
5. **Color clarity.** Ensure light blue / orange are distinguishable from existing
   colors at small icon sizes and against the dark map (suggested hexes chosen with
   that in mind; tune after seeing it).
6. **Interaction with the hover-label plan.** Independent — the label shows the name,
   this changes the color; no conflict. Both read the same `contacts[]`.

## Files to modify

- `src/TelemetryReader.cs` — resolve target + set `UnitInfo.Targeted` (+ reflection helper).
- `src/TelemetrySnapshot.cs` — add `Targeted` to `UnitInfo`.
- `src/TelemetryServer.cs` — serialize `tg` in `UnitsArray`.
- `src/ClientPage.cs` — target color constants + recolor in `drawOverlay`.

## Verification

Requires the game (Windows build machine) — cannot be verified from this dev host.

- Build (the csproj auto-deploys the DLL to `BepInEx/plugins`), launch a mission,
  open `http://localhost:5005`.
- Lock an **enemy** unit in-game → its map icon turns **orange**; clear the lock →
  it returns to red.
- Target a **friendly** unit → its icon turns **light blue**; clear → back to green.
- Switch targets → the highlight follows the new unit and the old one reverts.
- Confirm the player icon and all non-targeted contacts are unchanged.
- Confirm that with no target (and after a game update that might rename the field)
  the push loop keeps running — no crash, just no highlight.
