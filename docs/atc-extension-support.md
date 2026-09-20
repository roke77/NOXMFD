# ATC extension support — NOXMFD-side plan

## Status

Items 1, 2, and 4 are built. Item 4: `UnitInfo.IsAircraft`, wire key `"ac"`. Item 1 (fuel) was
investigated, turned out infeasible in its original shape, and shipped as a faction-wide
player-to-player broadcast instead of a telemetry field (`FuelBroadcast.cs`, wire key `"pf"`) —
see item 1 below for why, including a mid-investigation correction (an initial squad-only design
was revised again once `Presence.cs`'s own faction-wide broadcast turned out to already prove the
simpler shape works). Item 2 (per-instance icon color/ring override): `Api.SetUnitColorOverride`/
`ClearUnitColorOverride`, `ApiVersion` bumped to `4`, wire key `"ids"` under `colors`, drawn by
`map.js`'s new `drawStatusRing`. Item 3 is still plan only. The [ATC extension](https://github.com/roke77/NOXMFD-Extension-ATC) (its own
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
to need a different mechanism than telemetry entirely (a player-to-player broadcast, not a game
read) — see item 1 — but still lands the original "friendly" scope the ticket asked for.

## NOXMFD's responsibilities

### 1. Per-unit fuel — built, faction-wide peer broadcast (not a `UnitInfo` game read)

**Revised twice during investigation — not the small addition it first looked like, and not the
squad-scoped fallback first drafted either.** The original plan ("call the fuel accessor for every
`Aircraft`, gate the result friendly-only") rested on a false assumption about how fuel works over
the network:

- `Aircraft.GetFuelLevel()` sums each `FuelTank.GetLevel()` (`fuelMass`), a plain non-networked
  field. `FuelTank.Awake()`'s own `FuelTank_OnInitialize` sets `enabled = aircraft.LocalSim` — so
  the tank's `FixedUpdate` (the only place `fuelMass` ever decreases) simply never runs for a
  non-locally-simulated aircraft. `Unit.remoteSim` defaults to `!base.IsServer`
  (`_scratch/full/Unit.cs:754`), so on an ordinary player's client, `LocalSim` is true only for
  that player's *own* aircraft — every other unit's `fuelMass`, friendly or not, is frozen at
  whatever it was on spawn. `GetFuelLevel()` on a remote aircraft is not "approximately right" — it
  is simply wrong.
- `Aircraft.fuelLevel` **is** a genuine `[SyncVar]`, networked to everyone — but it turns out to be
  a refuel *target*, not a live gauge: `SetFuelLevel`'s only use of it is
  `fuelTank.Refuel(fuelLevel)`, and nothing writes it on a per-tick cadence. A dead end.

**No existing networked value carries another aircraft's true current fuel.** The only client that
ever has an accurate reading for a given aircraft is that aircraft's own pilot's own NOXMFD instance
(`TelemetryReader.cs:874`, still correct — it's the local player's own plane, always `LocalSim`).

**Built: fuel travels player-to-player over the existing squadron transport
(`docs/squadron-transport.md`, `Squadron.SendToAll`/`Since`), not through a game-state read at
all — the game itself has nothing to give a third party.** The first redesign scoped this to
*squad* (reasoning: the transport's leader/member protocol is star-topology, so reaching every
squad member looked like it needed a leader-relay hop). That undersold the transport itself:
`Presence.cs` already broadcasts its own "I'm running NOXMFD" beacon to the **whole faction
roster**, not just a squad, over this exact same transport, every 5s, already shipped and working.
Fuel reuses that identical shape — no leader relay needed, no squad membership required — which
restores the *original* "friendly" scope from issue #89 instead of narrowing it.

**What's built:**

- `src/plugin/Squad/FuelBroadcast.cs` — new file, structured identically to `Presence.cs`: a
  `Tick(peers, myFuel)` that broadcasts the local player's own current `GetFuelLevel()` to the
  faction roster every 15s (fuel changes slowly; no need for Presence's 5s cadence), and a
  `Drain()` that clamps and stores each peer's last-reported ratio with a 3× TTL, same as
  `Presence.HasNoxmfd`'s own staleness model. `FuelFor(steamId)` returns `null` once stale.
- `src/plugin/Squad/PlayerRoster.cs`'s `Refresh()` calls `FuelBroadcast.Tick(peerIds, myFuel)` right
  alongside its existing `Presence.Tick(peerIds)` — same peer list, same 1 Hz caller, no new scan.
- `src/plugin/MissionLifecycle.cs` drains it alongside `Presence.Drain()`.
- `UnitInfo` gained `HasPeerFuel`/`PeerFuelRatio` (`TelemetrySnapshot.cs`), set in `BuildUnits`
  from `FuelBroadcast.FuelFor(ac.pilots[0].player.SteamID)` — bool-gated rather than a sentinel
  float, since a plain float defaults to `0.0` under `default(UnitInfo)`/`default(TelemetrySnapshot)`
  (used throughout `tools/tests`), which would misread as "empty tank" instead of "no data."
  `TelemetryJson.cs` collapses it to a single wire field, `"pf"` (`-1` sentinel for "no data"),
  matching the `avn` block's existing client-side `-1` convention rather than adding a second key.
- Tests: `TelemetryJsonTests.cs`'s round-trip test now also covers `pf`, plus a dedicated test that
  the *default* (no broadcast heard) serializes as `-1`, not `0.0` — the exact bug the bool gate
  exists to prevent.

**Trust note, carried from `docs/squadron-transport.md`'s own model:** this value is peer-reported,
not server-authoritative. A modified client could broadcast a fake number. `Drain()` clamps to
`0..1` so a malformed or malicious payload can't smuggle `NaN`/`Infinity`/out-of-range values into
the UI, but nothing stops a peer from lying that they're full. Low stakes — a display convenience,
not something game-integrity-critical.

This is genuinely extension-agnostic — any NOXMFD page could read `pf` once it's on the wire, not
just the ATC extension's own page. The extension just reads it off the main telemetry frame the
same way it already reads `PilotName`/`Faction`.

### 2. Per-instance icon color/ring override — built

`Api.cs`'s only coloring surface was per unit **type** (`SetFactionColorOverride`/
`SetUnitTypeColorOverride`, backed by `IconColorRegistry.cs`'s `_typeOverrides`, keyed by
`unitType` string ± an optional faction filter). There was no per-unit-**id** override, and the ATC
extension needs one to ring a specific aircraft with its assigned status color without recoloring
every aircraft of that type.

**Shipped as designed below**, with one simplification: `ringOnly` (originally sketched as a flag)
turned out unnecessary — `map.js` always draws the id override as a ring (`drawStatusRing`), never
folds it into the icon's own fill color the way the type/faction overrides do, so there was nothing
for a flag to toggle. `Api.SetUnitColorOverride(uint id, string hex)` /
`Api.ClearUnitColorOverride(uint id)`, `IconColorRegistry._idOverrides`, wire key `"ids"` under
`colors` (`TelemetryJson.IdColorOverridesJson`), `ApiVersion` bumped `3` → `4`. Tests:
`IconColorRegistryTests.cs` (round-trip, invalid id/hex rejection, snapshot immutability) and
`TelemetryJsonTests.cs` (wire shape, empty-by-default). Documented in both `EXTENSIONS.md` and
`docs/extensions-api.md`'s section 6.

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

- **Item 1** — built. Shipped as a faction-wide player-to-player broadcast
  (`FuelBroadcast.cs`, 15s interval, own file alongside `Presence.cs`/`PlayerRoster.cs`) rather than
  the squad-only design first drafted — see item 1 above for why the simpler shape works.
- **Item 2** — built. Wire shape ended up exactly the obvious default: `"ids"` is an object keyed
  by the numeric id as a JSON string, `{"hex":"#..."}` per entry — same shape `"types"` already
  used, minus the unused faction filter.
- **Item 3**: the reconciliation rule between a local MAP click and an externally-set selection
  arriving the same tick — needs a real design pass, not just a field.
- **Item 4** — resolved: shipped with BuildHsd's exact same `> 0.5f` threshold, no new tuning. If
  the ATC extension's own aircraft-only filter later turns out wrong at the margins (something HSD
  never surfaced because it only ever showed aerial contacts anyway), revisit then rather than
  guessing at a second threshold up front.
