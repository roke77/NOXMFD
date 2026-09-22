# TGT: a TYPE column for the target list, GRID moved into DETAILED

## Goal

Player request, a follow-on to [issue #91](https://github.com/roke77/NOXMFD/issues/91)'s NUCLEAR
button: the target list had no way to tell what a locked target actually *is* at a glance — a
squadron lead scanning a long list of locks couldn't distinguish an aircraft from a SAM site from
an in-flight missile without reading the name. Two changes:

1. **GRID moves out of the always-visible columns into DETAILED-only**, alongside SPD/ALT/HDG
   (issue #88). COMPACT (the default) drops it.
2. **A new TYPE column, visible in both COMPACT and DETAILED**, showing what kind of thing each
   locked target is — and, for in-flight nuclear ordnance specifically, **NUCLEAR** in red instead
   of the generic MISSILE label.

## Classification

Five labels, one per known lockable `Unit` runtime subclass (`_scratch/full/`) — `Aircraft`,
`Missile`, `Ship`, `Building`, `GroundVehicle`:

| Label | Unit type |
|---|---|
| `AIRCRAFT` | `Aircraft` |
| `MISSILE` | `Missile`, unless nuclear (see below) |
| `NUCLEAR` | `Missile` where `GetWeaponInfo().nuclear` is true — overrides MISSILE |
| `GROUND` | `GroundVehicle` |
| `BUILDING` | `Building` |
| `SHIP` | `Ship` |

Empty string (renders `—` client-side) for anything else — in practice unreachable for a real
locked target, but defensive rather than assumed.

This is a **runtime-type check on the live `Unit`** (`u switch { Missile m => ..., Aircraft => ...,
... }`), the same idiom `TelemetryReader.cs`'s existing `hasDetail = (u is Aircraft || u is
Missile)` already uses — not the game's own `TargetListSelector_ToggleButton.CheckUnitTypes`, which
classifies by `u.definition.GetType()` against `UnitDefinition` subclasses instead. Both approaches
should agree in practice (an `Aircraft` unit has an `AircraftDefinition`, etc.), but the `is`-based
check matches this codebase's existing style rather than introducing a new classification pattern
for one column.

The NUCLEAR override reuses the exact check `docs/tgt-nuclear-clear.md` already established:
`WeaponInfo.nuclear`, read off the live `Missile.GetWeaponInfo()` — not duplicated as a separate
helper, since it's a one-line check done in the same per-contact loop either way.

## The plan (as built)

1. **`UnitInfo.TargetKind`** (`TelemetrySnapshot.cs`) — new `string` field, computed in
   `TelemetryReader.cs`'s per-contact loop (the same loop building `HasDetail`/`SpeedReading`/
   `IsAircraft` etc. — `TelemetryReader.cs:1637` area) right alongside the existing `hasDetail`
   check, so it costs one more switch expression per contact, not a second pass.
2. **Serialization** (`TelemetryJson.cs`) — one more terse key, `"ty"`, on the contacts array, next
   to `"ac"`.
3. **Client derivation** (`telemetry-source.js`) — `ty` rides along into each pushed target item the
   same way `hd`/`sp`/`al`/`h` (issue #88) already do.
4. **TGT rendering** (`tgt.html`, `tgt.css`, `tgt.js`) — a new `.tl-type` cell, positioned right
   after SRC (before RNG), always visible. `data-type="NUCLEAR"` on the cell drives the red accent
   (`--no-red`, the same token the footer's NUCLEAR button already uses for this concept), the same
   `data-*`-attribute-driven coloring pattern the ATC extension's per-row STATUS dropdown uses.
5. **GRID regrouped** — `.tl-grid`/`.tgt-flight-head` joins the existing `display:none` / `.has-
   flight-col { display:block }` toggle SPD/ALT/HDG already used, instead of being permanently
   visible. The four grid-template-columns combinations (base / `.has-td-col` / `.has-flight-col` /
   both) were all re-derived — see `tgt.css`'s own floor-sum comments for the arithmetic.
6. **Preview mock** (`tools/preview-mock.js`) — every existing mock target got a matching `ty`; two
   new entries (`Piledriver TBM (HE)` / `Piledriver TBM (Nuc)`, real weapon names from
   nuclearoption.wiki.gg) exercise MISSILE and NUCLEAR specifically — the same same-name-different-
   nuclear-status pair `docs/tgt-nuclear-clear.md` already picked as the sharpest test case.
7. **C# tests** (`TelemetryJsonTests.cs`) — `TargetKind` added to the existing full round-trip test,
   plus a dedicated default-empty test, matching every other `UnitInfo` field's coverage shape.

## Why TYPE stays visible in COMPACT (unlike GRID/SPD/ALT/HDG)

GRID/SPD/ALT/HDG are supplementary flight data — useful detail, not something every glance at the
list needs. TYPE is different: it's the answer to "what is this lock," which matters just as much
scanning quickly in COMPACT as it does in DETAILED, and is the whole point of surfacing NUCLEAR at
all — a nuclear lock needs to read as NUCLEAR whether or not DETAILED happens to be on.

## Verification

- `tools/ci-check.ps1` — full pass: `dotnet build -c Release` (0 errors), all `*.test.js` (51 files,
  including the existing `telemetry-source.test.js`), `dotnet test` (400/400, including the two new
  `TargetKind` cases), `serve_web.py` route smoke.
- `serve_web` harness, live browser check: TYPE renders for every mock row (AIRCRAFT/GROUND/SHIP/
  BUILDING/MISSILE/NUCLEAR all present), NUCLEAR reads in red/bold and MISSILE in the ordinary dim
  color despite both being `Piledriver TBM`-named rows — confirming the column reflects the live
  per-target classification, not a name heuristic. Toggling DETAILED/COMPACT confirmed GRID now
  hides/shows with SPD/ALT/HDG (`display:none`/`block` verified directly via computed style, not
  just visually) while TYPE stays visible either way.
- Not yet checked in-game.
