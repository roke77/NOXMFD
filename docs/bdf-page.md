# BDF — faction forces MFD page

## Status

**On `feat/bdf-page`, up to date with `main`, `dotnet build`-verified and
deployed — not yet merged, no in-game pass yet.** Replicated the game's
in-cockpit faction/HQ status panel as a new MFD page: header (faction name,
WARHEADS, SCORE, FUNDS) + a forces breakdown (SHIPS / BUILDINGS / VEHICLES /
AIRCRAFT), always for **the player's own faction**. The enemy-faction view
(the game's `SelectFaction.Other`) is explicitly out of scope — a separate
branch/page later. Renders full-view in `#page-frame` and, like TGT, as a
split pane in both the CLASSIC bezel and the F-35 layout. Verified end-to-end
in the `serve_web` harness; a real in-game pass (live `FactionHQ` data, real
captured icons) is still outstanding before merge.

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

1. **[done] Telemetry snapshot** (`src/plugin/TelemetrySnapshot.cs`) — add a `Bdf*`
   block: `BdfPresent` (bool), `BdfFaction` (string), `BdfFunds` (float),
   `BdfScore` (float), `BdfWarheads` (int), and four arrays of a small
   `{Name, Count}` struct (mirrors `TgtToggleInfo`'s `{Name, On}` shape) —
   `BdfShips`, `BdfVehicles`, `BdfBuildings` in enum order, `BdfAircraft` in
   `Encyclopedia.i.aircraft` order (`Name` = `unitName`, doubling as the
   `/icon?type=` key). No separate section-total fields — since every type
   is enumerated, the section total is just the sum of its array, derived
   client-side (one less thing to keep in sync).

2. **[done] TelemetryReader** — on the reader's slow tick (this doesn't need
   60 Hz), resolve `aircraft.NetworkHQ`; if null, emit `BdfPresent = false`
   (mirrors TGT's `present:false` path) and skip the rest. Otherwise read
   the header scalars and bucket `missionStatsTracker.currentUnits` into the
   three type arrays via each definition's type enum, plus the aircraft list
   filtered by `IsAllowed(MissionManager.AllowEventContent)`.

3. **[done] AssetCapture** — added `TryCaptureShipTypeIcons()`, a straight
   mirror of `TryCaptureVehicleTypeIcons()` (`src/plugin/AssetCapture.cs:337`)
   over `Encyclopedia.i.shipTypes[i].typeSprite`. For aircraft, **no new
   capture method** — reuses the existing generic
   `TryCaptureIcon(UnitDefinition)` (`AssetCapture.cs:420`), called
   proactively over every `Encyclopedia.i.aircraft` definition (not just
   units the world-scan has sighted this mission), so the grid has an icon
   for airframes never spotted yet. Also added `TryCaptureFactionLogo(hq)`
   (not in the original plan, added for visual fidelity with the
   screenshot) — captures `Faction.factionColorLogo` once per faction name,
   sharing the `/bdf-icon` store (no collision risk with the short
   ship-type codes).

4. **[done] TelemetryServer** — serializes the `bdf` block into the SSE
   frame (mirrors `tgt`'s serialization at `TelemetryServer.cs:861`). Added
   `SetBdfIcon` / `/bdf-icon?type=` for ship-type icons (and the faction
   logo), mirroring `SetTgtIcon` / `/tgt-icon` exactly
   (`TelemetryServer.cs:281,354`). Aircraft icons keep using the existing
   `/icon?type=<unitName>`.

5. **[done] Web frontend** — new `src/web/pages/bdf/{bdf.html,bdf.css,bdf.js}`,
   styled like AVN/TGT (green monospace theme, matches the screenshot).
   Header block (faction name + logo + WARHEADS/SCORE/FUNDS), a static
   "FORCES" label (no dropdown — the other six `DisplayType` modes are out
   of scope and not rendered at all for this pass), then the four rows
   exactly as laid out in the screenshot: SHIPS (icon + label + count, no
   section total shown), BUILDINGS / VEHICLES (label + count, bold section
   total), AIRCRAFT (icon + count only, bold section total). `present:false`
   shows an UNAVAILABLE placeholder (same pattern as TGT).

6. **[done] telemetry-source.js** — maps the new `bdf` SSE block into a
   page-level slice, same pattern as `tgt`.

7. **[done] Wire nav** — `NAV.main` (`nav-model.js`) is pinned at exactly 6
   items (enforced by `nav-model.test.js`) and already full (AVN/MAP/RWR/
   TGP/TGT/WPN), so BDF couldn't slot in there. Added as a `BEZEL_EXTRAS`
   entry (`mfd.js`) instead, down the right bank after HUD and LYT:
   `{ label: 'BDF', action: 'bdf', bank: 'right', index: 2 }`. **Also wired
   the F-35 layout** (`src/web/shell/f35/f35.js` — `F35_PAGES.bdf`,
   `PAGE_FEEDS.bdf`), which wasn't in the original plan: BDF was already a
   greyed-out `MAIN_EXTRAS` placeholder there (alongside HUD/PAL), so it lit
   up once given a page. Both shells needed the same top-left collision fix
   TGT already has — BDF's own WARHEADS readout sits where the bezel's/F-35's
   MAIN back-label lands, so BDF joins the shell's shared vertical-MAIN
   treatment (`isVmainPage` + `.overlay.vmain` in `mfd.js`/`mfd.css`, and
   `f35.css`'s `[data-page]` selector) — found by clicking through the
   rendered page in the browser, not by inspection.

   **Split panes** (added after the `main` merge, mirroring TGT): `SPLIT_SLOTS.bdf`
   (a single MAIN back-key), `PAGE_URL.bdf = '/bdf?bare'`, and a
   `forwardBdfToPanes()` twin of the full-view forwarder, wired into the
   pane-load and telemetry handlers. In a split the pane's MAIN back-label
   stands upright via the per-label `vlabel` class (BDF is in `isVmainPage`),
   and the page carries a symmetric side inset so that label clears the
   header on whichever edge it lands (left/top pane vs. a V-split's right
   pane). BDF is reached in full view (its `BEZEL_EXTRAS` key is full-view
   only, like LYT/HUD) and carried into a split by splitting from there.

8. **[done] tools/preview-mock.js + serve_web.py** — added a mock `bdf`
   block matching the reference screenshot's numbers 1:1, plus a
   `/bdf-icon` placeholder route mirroring `/tgt-icon`.

9. **[partially done] Verify** — `serve_web` harness pass complete: rendered
   and clicked through both the CLASSIC bezel and F-35 layouts (full view and
   split), confirmed against the reference screenshot's numbers, caught and
   fixed the label collision above. `dotnet build -c Release` clean (0 errors)
   and deployed. **Not done**: an in-game pass (real `FactionHQ` data, real
   captured icons, bezel key reachable on the physical/virtual MFD).

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
