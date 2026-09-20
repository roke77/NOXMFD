# ATC extension support — NOXMFD-side plan

## Status

Item 4 (aircraft classification flag) is built — `UnitInfo.IsAircraft`, wire key `"ac"`. Item 1
(fuel) was investigated and turned out infeasible as originally scoped — redesigned below as a
squad-only player-to-player broadcast, not a telemetry field; not started. Items 2–3 are still plan
only. The [ATC extension](https://github.com/roke77/NOXMFD-Extension-ATC) (its own
repo, own release cycle, [issue #89](https://github.com/roke77/NOXMFD/issues/89)) has its Phase 1
built against NOXMFD as it stands today — a traffic table, range presets, and ATC Status
assignment, all buildable without touching NOXMFD's own source. This document covers only the four
things Phase 2 needs NOXMFD itself to add; the extension's own design (its table, its status
bookkeeping, its UI) is that project's own plan (`docs/atc-mfd-plan.md` there), not this one.

## Goal

Issue #89 asks for two things NOXMFD's telemetry/extension API can't do yet: show fuel for aircraft
other than the local player's, and let an extension and MAP share a live selection/highlight — plus
one thing that already exists internally but isn't exposed (aircraft-vs-everything-else
classification). Each is its own independent addition; none depend on each other. Fuel turned out
to need a different mechanism than telemetry entirely — see item 1.

## NOXMFD's responsibilities

### 1. Per-unit fuel — squad-only, via squadron transport (not a `UnitInfo` field)

**Revised after investigation — this is not the small addition it first looked like.** The
original plan ("call the fuel accessor for every `Aircraft`, gate the result friendly-only") turned
out to rest on a false assumption about how fuel actually works over the network. Two findings from
the decompiled source:

- `Aircraft.GetFuelLevel()` sums each `FuelTank.GetLevel()` (`fuelMass`), a plain non-networked
  field. `FuelTank.Awake()`'s own `FuelTank_OnInitialize` sets `enabled = aircraft.LocalSim` — so
  the tank's `FixedUpdate` (the only place `fuelMass` ever decreases) simply never runs for a
  non-locally-simulated aircraft. `Unit.remoteSim` defaults to `!base.IsServer`
  (`_scratch/full/Unit.cs:754`), so on an ordinary player's client, `LocalSim` is true only for
  that player's *own* aircraft — every other unit's `fuelMass`, friendly or not, is frozen at
  whatever it was on spawn and never reflects real consumption. `GetFuelLevel()` on a remote
  aircraft is not "approximately right" — it is simply wrong.
- `Aircraft.fuelLevel` **is** a genuine `[SyncVar]`, networked to everyone — but it turns out to be
  a refuel *target*, not a live gauge: `SetFuelLevel`'s only use of it is `fuelTank.Refuel(fuelLevel)`,
  and nothing writes it on a per-tick cadence. It answers "what ratio should this tank refill to,"
  not "how much fuel is in it right now." A dead end for this purpose.

**Conclusion: there is no existing networked value that carries another aircraft's true current
fuel.** The only client that ever has an accurate fuel reading for a given aircraft is that
aircraft's own pilot's own NOXMFD instance (`TelemetryReader.cs:874`, still correct — it's the
local player's own plane, always `LocalSim`).

**Revised design: fuel travels player-to-player over the existing squadron transport
(`docs/squadron-transport.md`), not through the telemetry stream at all — the game itself has no
fuel data to give a third party.** This also means fuel is scoped to *squad*, not "friendly" as the
original ticket assumed (a real narrowing — a squad is a subset of the friendly faction, not all of
it; issue #89 didn't anticipate this constraint, and neither did the ATC extension's own plan doc,
which needs updating to match once this ships):

- Each NOXMFD instance already knows its own local player's real fuel every tick. Add a small,
  low-rate broadcast (piggybacking `Presence.cs`'s existing 5s beacon, or its own timer) of just
  that value to the player's current squad, reusing `Squad.cs`'s existing transport rather than a
  new one.
- The star topology (`docs/squadron-transport.md`'s "Implementation": members only ever talk to the
  leader) means a member's fuel reaching *other members* — not just the leader — needs the same
  two-hop shape `sqd.roster` already uses: each member sends its own fuel to the leader, the leader
  aggregates and re-broadcasts a `{steamId: fuel}` map to the whole squad, alongside (not instead
  of) the existing roster broadcast.
- New message types on `Squad.cs`'s existing envelope (mirroring `sqd.data`'s shape, not a new
  transport): a member→leader `sqd.fuel` push, and a leader→all `sqd.fuel-status` broadcast.
- A new small store, shaped like `PlayerRoster.AircraftFor` (SteamID-keyed, not persisted, cleared
  the same way `RouteStore.OnSquadEnded()` clears its own squad-scoped state).
- This is genuinely extension-agnostic — any NOXMFD page could read squad fuel once it exists, not
  just the ATC extension's own page — so it belongs as a first-party concept (e.g. surfaced the way
  `PlayerRoster.AircraftFor` already feeds SQD), with the ATC extension simply reading whatever
  slice already carries it, the same way it already reads `PilotName`/`Faction` off the main
  telemetry frame.

This is materially more work than a `UnitInfo` field — closer in shape to the target-designation
feature `docs/squadron-transport.md` already documents (new protocol messages, new state, a UI
surface) than to a one-line telemetry addition. Treat it as its own scoped task, not a quick follow-on
to items 2–4.

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

- **Item 1** — resolved as infeasible in its original shape; redesigned as a squad-only
  player-to-player broadcast over the existing squadron transport (see item 1 above). Still open:
  exact broadcast rate (piggyback `Presence`'s 5s beacon, or its own slower timer — fuel doesn't
  need to be fresh to the second), and whether the store lives in `Squad.cs` itself or a sibling
  file the way `PlayerRoster.cs` sits alongside it.
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
