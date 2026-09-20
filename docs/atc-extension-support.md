# ATC extension support — NOXMFD-side plan

## Status

Item 4 (aircraft classification flag) is built — `UnitInfo.IsAircraft`, wire key `"ac"`. Items 1–3
are still plan only. The [ATC extension](https://github.com/roke77/NOXMFD-Extension-ATC) (its own
repo, own release cycle, [issue #89](https://github.com/roke77/NOXMFD/issues/89)) has its Phase 1
built against NOXMFD as it stands today — a traffic table, range presets, and ATC Status
assignment, all buildable without touching NOXMFD's own source. This document covers only the four
things Phase 2 needs NOXMFD itself to add; the extension's own design (its table, its status
bookkeeping, its UI) is that project's own plan (`docs/atc-mfd-plan.md` there), not this one.

## Goal

Issue #89 asks for two things NOXMFD's telemetry/extension API can't do yet: show a locked-down
per-unit value (fuel) only to its own faction, and let an extension and MAP share a live
selection/highlight — plus one thing that already exists internally but isn't exposed
(aircraft-vs-everything-else classification). Each is its own independent addition; none depend on
each other.

## NOXMFD's responsibilities

### 1. Per-unit fuel, friendly-only

`TelemetryReader.cs:874` reads fuel (`aircraft.GetFuelLevel()`) only inside the single-aircraft
object initializer for the local player's own plane; `TelemetrySnapshot.Fuel` is one top-level
`float`, and `UnitInfo` has no fuel field for any other unit at all.

Add a `Fuel` field to `UnitInfo` (`TelemetrySnapshot.cs:522-578`), populated in `BuildUnits`
(wherever that loop currently builds each `UnitInfo` — same loop `HasDetail`/`SpeedReading` come
from) by calling the aircraft's own fuel accessor for every `Aircraft` unit, not just the local
player's. First confirm `GetFuelLevel()` (or whatever the real accessor turns out to be) is
actually readable against a non-local `Aircraft` component — `TelemetryReader.cs:874` has only ever
called it on the player's own aircraft, so this needs verifying against the decompiled game source
before committing to the field, the same way any other new telemetry read here would.

**Deliberately not reusing `HasDetail`.** Speed/altitude/heading are things an active radar lock
plausibly reveals — fuel quantity isn't, the same reasoning the ATC extension's own plan doc
recorded (`docs/atc-mfd-plan.md` there, "Data-visibility model"). So `Fuel` needs its own gate,
friendly-only regardless of lock/stale state: only serialize it (or serialize a real value instead
of a sentinel) when `Faction == 1`. Enemy/neutral rows always get whatever "no data" sentinel the
JSON encoding already uses elsewhere (`TelemetryJson.cs`'s `UnitsArray` — match its existing
convention for an absent field rather than inventing a new one).

### 2. Per-instance icon color/ring override

`Api.cs`'s only coloring surface is per unit **type** (`SetFactionColorOverride`/
`SetUnitTypeColorOverride`, backed by `IconColorRegistry.cs`'s `_typeOverrides`, keyed by
`unitType` string ± an optional faction filter). There's no per-unit-**id** override anywhere, and
the ATC extension needs one to ring a specific aircraft with its assigned status color without
recoloring every aircraft of that type.

Extend `IconColorRegistry` with a second copy-on-write dictionary alongside `_typeOverrides`,
keyed by unit id (`uint`, matching `UnitInfo.Id`) instead of type string:

```csharp
private static volatile Dictionary<uint, TypeOverride> _idOverrides = new Dictionary<uint, TypeOverride>();

internal static bool SetIdOverride(uint id, string hex, bool ringOnly = true) { ... }
internal static void ClearIdOverride(uint id) { ... }
internal static IReadOnlyDictionary<uint, TypeOverride> IdOverridesSnapshot() => _idOverrides;
```

Reuses `TypeOverride`'s existing `IsValidHex` validation and the same "one warning per rejected key"
guard `WarnRejected` already provides — no new validation concept, just a new key space. `ringOnly`
(or an equivalent flag) matters because this is additive to the existing faction/type color, not a
replacement: the ticket (issue #89, section 5) wants "the aircraft's normal faction/team/aircraft
identification" to stay visible, with the status color as a ring/marker layered on top, not a
palette swap.

`Api.cs` gains:

```csharp
public static void SetUnitColorOverride(uint id, string hex) => IconColorRegistry.SetIdOverride(id, hex);
public static void ClearUnitColorOverride(uint id) => IconColorRegistry.ClearIdOverride(id);
```

`TelemetryJson.cs`'s `"colors"` block (already extended once for type overrides per
`docs/vanilla-icons-plus-extension.md`) gains a third, additive sub-object for id overrides. Exact
key shape is an implementation detail; the constraint carried over from that same doc still holds
— additive only, every existing page and extension parsing `colors` today keeps working unchanged.

`map.js`'s color resolution (`map.js:639`) needs a new draw step, not just a new color lookup: an
id override, if present for `u.id`, draws as a ring/outline around the existing icon (already drawn
in its existing faction/type color) rather than substituting into the same `hex` variable the
faction/type lookup already produces — substituting would violate the ticket's own "identification
stays visible" requirement above.

### 3. A shared, extension-writable selected-unit concept

MAP's click-to-select (`map.js:1200`, `selectAt`) is local-only client state — it's never sent to
the server and never broadcast to any other page or extension. The one existing "focused unit"
concept, `TargetFocus.cs`, is a different thing entirely: it tracks the player's current *weapon
lock* focus (reconciled from `weaponManager.GetTargetList()`, `TargetFocus.Reconcile`/`Cycle`), is
read-only from JS, and has no `Api` method to set it externally. Repurposing it would conflate "the
target my weapons are tracking" with "the row an ATC controller clicked," which are unrelated
concepts that happen to share the word "target."

Add a new, independent static store — same shape as `TargetFocus.cs` (a `uint`, `0` = none,
`Volatile`/lock-guarded), but writable from `Api.cs`, not just from `TelemetryReader`'s own contact
scan:

```csharp
public static void SetSelectedUnit(uint id) => SharedSelection.Set(id);
public static uint GetSelectedUnit() => SharedSelection.Id;
```

Carried in the telemetry frame (`TelemetrySnapshot`/`TelemetryJson`, alongside the existing
`FocusedTargetId`) so every connected page/pane sees the same value every tick — this is what makes
it *shared* rather than per-tab. `map.js` needs a new branch in its own click-select handling: a
click still sets local state as it does today, but should also call `Api`-driven update (via
whatever command path MAP already uses for a server round trip) so an extension's own next frame
picks up the new selection, and conversely MAP needs to read the shared value each frame and treat
it the same way it treats its own local selection when it changes from outside. This is the one
item here that touches a first-party page's own behavior, not just new API surface — worth its own
design pass on exactly how "local click" and "externally-set selection" reconcile without fighting
each other (e.g. a click always wins locally and pushes up; an external change only applies when it
doesn't contradict a click made more recently).

### 4. Aircraft classification flag on `UnitInfo` — built

No per-contact tag distinguishes an aircraft from a ground vehicle, ship, or building in the
`contacts` array today — the AIR/MSL/GND/BLD/SHP buttons on TGT reflect the game's own filter-panel
state (`d.tgt.category`), not a tag on each contact. But the classification already exists
server-side, unexposed: `BuildHsd` (`TelemetryReader.cs:1352-1353`) already filters its own
contact list with `u.definition.typeIdentity.air <= 0.5f` — the game's own native "how aircraft-like
is this" score, not something NOXMFD invented.

Add an `IsAircraft` (or similarly named) `bool` to `UnitInfo`, set in `BuildUnits` using that exact
same check. Not new classification work — just exposing a condition NOXMFD's own code already
trusts and uses today for HSD, on the one array that doesn't currently carry it.

### 5. Versioning

All four items are new public surface (1, 2, and 3 add to `Api.cs`; 4 only extends the JSON shape,
no new method). Bump `Api.ApiVersion` (`Api.cs:10`, currently `3`) once landed — to `4` if shipped
together, or incrementally per item if they ship across separate releases. The ATC extension pins
whichever version(s) it actually depends on via `BepInDependency MinimumVersion`, same as every
other extension already does.

### 6. Documentation

Once built, extend `docs/extensions-api.md` (today's five numbered surfaces, matching `EXTENSIONS.md`'s
"six surfaces" — icon color overrides is already the sixth) with the new per-unit color override and
selected-unit methods, and add the equivalent walkthrough section to `EXTENSIONS.md` for a modder
writing against them. Item 4 (the classification flag) needs no new *surface* documentation — it's
just a new field on the existing `contacts` payload — but its presence and meaning belongs in
whichever doc already describes `UnitInfo`'s wire shape for extension authors.

## What stays out of NOXMFD

- ATC Status assignment/bookkeeping (the `unitId → status` map, its 12-value enum, its command
  handler) — entirely extension-owned, already built via `PublishSlice`, no core involvement needed
  or wanted.
- Range presets and the distance-to-reference-position calculation — client-side in the extension,
  reusing the same `telemetry-source.js`/`d.world` pattern TGT's own RNG column already uses.
- The ATC page's own table, layout, and rendering — none of it needs anything from NOXMFD beyond
  the four items above plus the telemetry/asset surfaces every extension already has.

## Open questions

Left for whoever implements each item, not answered here:

- **Item 1**: the actual accessor for a non-local aircraft's fuel — confirm against decompiled
  source before assuming `GetFuelLevel()` generalizes.
- **Item 2**: exact wire shape for id overrides in the `colors` block (an object keyed by numeric
  id as a string, same convention `TypeOverride`'s dictionary already uses server-side, is the
  obvious default — but worth confirming against how the existing `types` sub-object encodes its
  own keys before adding a second, inconsistent shape).
- **Item 3**: the reconciliation rule between a local MAP click and an externally-set selection
  arriving the same tick — needs a real design pass, not just a field.
- **Item 4** — resolved: shipped with BuildHsd's exact same `> 0.5f` threshold, no new tuning. If
  the ATC extension's own aircraft-only filter later turns out wrong at the margins (something HSD
  never surfaced because it only ever showed aerial contacts anyway), revisit then rather than
  guessing at a second threshold up front.
