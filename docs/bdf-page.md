# BDF — faction forces MFD page

## Status

**Planned — not started.** Branch `feat/bdf-page`. Replicate the game's
in-cockpit faction/HQ status panel (screenshot supplied by the user) as a new
MFD page: header (faction name, WARHEADS, SCORE, FUNDS) + a forces breakdown
(SHIPS / BUILDINGS / VEHICLES / AIRCRAFT), always for **the player's own
faction**. The enemy-faction view (the game's `SelectFaction.Other`) is
explicitly out of scope — a separate branch/page later.

## What the in-game BDF page is

The game panel is `InfoPanel_Faction` (decompiled in
`_scratch/full/InfoPanel_Faction.cs`). It has seven display modes
(`DisplayType`: Forces, Players, Airbases, Reserves, Losses, Value,
Manpower), picked from the panel's own dropdown. **Only `Forces` is in
scope** — that's the one in the screenshot. The dropdown itself is cosmetic
for this pass (see "Open questions").

## Data model (traced through the decompiled sources)

| Screenshot element | Source |
|---|---|
| Faction name ("BOSCALI") | `Faction.factionName` |
| Faction logo | `Faction.factionColorLogo` |
| WARHEADS | `FactionHQ.GetWarheadStockpile()` (int) |
| SCORE | `FactionHQ.factionScore` (float) |
| FUNDS | `FactionHQ.factionFunds` (float, **in millions**) |
| BUILDINGS / VEHICLES / SHIPS / AIRCRAFT section totals | `FactionHQ.missionStatsTracker.units.{buildings,vehicles,ships,aircraft}.current` |
| Per-type breakdown (CV, LHA, … / TRUCK, UGV, … / CIV, FAC, …) | `FactionHQ.missionStatsTracker.currentUnits` — a `SyncDictionary<UnitDefinition,int>`, bucketed by each definition's type enum (below) |
| Aircraft icon grid | `Encyclopedia.i.aircraft`, one `AircraftDefinition` per icon (no grouping), filtered by `IsAllowed(MissionManager.AllowEventContent)` |

Bucketing enums (each confirmed byte-for-byte against the screenshot's
abbreviations):

- `ShipDefinition.shipType` (`ShipType`): `CV, LHA, LFD, DDG, FFG, FFL, LC`
- `VehicleDefinition.vehicleType` (`VehicleType`): `TRUCK, UGV, LCV, AFV, MBT, ART, AAA, IR_SAM, R_SAM, RDR`
- `BuildingDefinition.buildingType` (`BuildingType`): `CIV, FAC, RDR, DEP, HGR, DEF, AMMO`

`InfoPanel_ItemPrefab.RefreshDefinition` (Forces mode) sums
`missionStatsTracker.GetCurrentUnits(def)` over every definition in a type
bucket — same thing as summing `currentUnits` ourselves, just done client
side instead of per-definition lookups.

**Icons**: `InfoPanel_Faction.SetupList` only ever sets a sprite for the
**Ships** row (`Encyclopedia.i.shipTypes[i].typeSprite`) and the aircraft
grid (`AircraftDefinition.mapIcon`) — Vehicles and Buildings rows get text
labels only, no icon. That matches the screenshot (boat silhouettes over
SHIPS, plane silhouettes over AIRCRAFT, plain text elsewhere).

## Access path from the plugin

Same pattern already used in `TelemetryReader.cs:376`
(`GameManager.GetLocalAircraft(out Aircraft aircraft)`): `aircraft.NetworkHQ`
**is** the player's `FactionHQ` (inherited from `Unit` — no
`FactionRegistry` name lookup needed). Null-guard the same way other
per-aircraft blocks do when there's no local aircraft yet.

## Formatting

- **Funds**: stored in millions; the `$-13,1m` look is
  `UnitConverter.ValueReading`'s scale-by-magnitude format (raw $ / k / m / b
  / t breakpoints, see `_scratch/full/UnitConverter.cs`). The comma decimal
  is a locale artifact of the dev's game install, not something to match —
  format with a period, same breakpoints.
- **Score**: one decimal (`N1`-equivalent).
- **Warheads**: plain int.

## Plan

1. **Telemetry snapshot** (`src/plugin/TelemetrySnapshot.cs`) — add a `Bdf*`
   block: `BdfPresent` (bool), `BdfFaction` (string), `BdfFunds` (float),
   `BdfScore` (float), `BdfWarheads` (int), and four arrays of a small
   `{Name, Count}` struct (mirrors `TgtToggleInfo`'s `{Name, On}` shape) —
   `BdfShips`, `BdfVehicles`, `BdfBuildings` in enum order, `BdfAircraft` in
   `Encyclopedia.i.aircraft` order (`Name` = `unitName`, doubling as the
   `/icon?type=` key). No separate section-total fields — since every type
   is enumerated, the section total is just the sum of its array, derived
   client-side (one less thing to keep in sync).

2. **TelemetryReader** — on the reader's slow tick (this doesn't need
   60 Hz), resolve `aircraft.NetworkHQ`; if null, emit `BdfPresent = false`
   (mirrors TGT's `present:false` path) and skip the rest. Otherwise read
   the header scalars and bucket `missionStatsTracker.currentUnits` into the
   three type arrays via each definition's type enum, plus the aircraft list
   filtered by `IsAllowed(MissionManager.AllowEventContent)`.

3. **AssetCapture** — add `TryCaptureShipTypeIcons()`, a straight mirror of
   `TryCaptureVehicleTypeIcons()` (`src/plugin/AssetCapture.cs:337`) over
   `Encyclopedia.i.shipTypes[i].typeSprite`. For aircraft, **no new capture
   method** — reuse the existing generic `TryCaptureIcon(UnitDefinition)`
   (`AssetCapture.cs:420`), but call it proactively over every
   `Encyclopedia.i.aircraft` definition (not just units the world-scan has
   sighted this mission), so the grid has an icon for airframes never
   spotted yet.

4. **TelemetryServer** — serialize the `bdf` block into the SSE frame
   (mirrors `tgt`'s serialization at `TelemetryServer.cs:861`). Add
   `SetBdfIcon` / `/bdf-icon?type=` for ship-type icons, mirroring
   `SetTgtIcon` / `/tgt-icon` exactly (`TelemetryServer.cs:281,354`).
   Aircraft icons keep using the existing `/icon?type=<unitName>`.

5. **Web frontend** — new `src/web/pages/bdf/{bdf.html,bdf.css,bdf.js}`,
   styled like AVN/TGT (green monospace theme, matches the screenshot).
   Header block (faction name + logo + WARHEADS/SCORE/FUNDS), a static
   "FORCES" label (no dropdown — the other six `DisplayType` modes are out
   of scope and not rendered at all for this pass), then the four rows
   exactly as laid out in the screenshot: SHIPS (icons only, no section
   total shown), BUILDINGS / VEHICLES (text labels, bold section total),
   AIRCRAFT (icons, bold section total). `BdfPresent:false` shows an
   UNAVAILABLE placeholder (same pattern as TGT).

6. **telemetry-source.js** — map the new `bdf` SSE block into a page-level
   slice, same pattern as `tgt`.

7. **Wire nav** — `NAV.main` (`nav-model.js`) is pinned at exactly 6 items
   (enforced by `nav-model.test.js`) and already full (AVN/MAP/RWR/TGP/TGT/
   WPN), so BDF can't slot in there. It gets a `BEZEL_EXTRAS` entry
   (`mfd.js`) instead, right after LYT: `{ label: 'BDF', action: 'bdf',
   bank: 'right', index: 1 }` (LYT keeps `index: 0`).

8. **tools/preview-mock.js** — add a mock `bdf` block + `/bdf-icon`
   responses so the page can be iterated on in the `serve_web` harness
   without the game running (same as TGT's mock).

9. **Verify** — `serve_web` harness pass (renders, mock counts match the
   screenshot's layout) + in-game pass (real `FactionHQ` data, icons
   actually captured, bezel key reachable).

## Open questions

- ~~Bezel placement.~~ **Resolved: `BEZEL_EXTRAS.main` right bank, right
  after LYT** — `{ label: 'BDF', action: 'bdf', bank: 'right', index: 1 }`.
- ~~The FORCES dropdown.~~ **Resolved: omitted entirely** — only a static
  "FORCES" label, no control, since the other six `DisplayType` modes are
  out of scope for this pass.
- **Ship-type icon endpoint.** Plan above says a dedicated `/bdf-icon?type=`
  mirroring TGT's `/tgt-icon`, rather than reusing the generic `/icon`
  namespace — cleaner key space, no risk of a ship-type name (`CV`, `LC`,
  …) ever colliding with an aircraft `unitName`. Flagging in case a simpler
  option is preferred.
- **Refresh rate.** The game's own panel refreshes once a second
  (`InfoPanel_Faction.refreshRate = 1f`). Matching that (rather than
  per-frame) is likely fine since forces counts change slowly, but worth
  confirming against how other slow-tick blocks (e.g. TGT) are paced.
