# MAP: squad-member styling

Issue #48 — a squadmate's aircraft icon on MAP renders in the squad's teal (`--no-squad`,
theme.css) instead of its plain faction color, so a glance at MAP shows who in the squad is where
without opening SQD. Depends on the squad feature (docs/squadron-transport.md).

## Where the flag comes from

No new correlation was needed — `PlayerRoster.Refresh()` (docs/squadron-transport.md's
"Membership picker") already walks `FactionHQ.GetPlayers()` once per 1 Hz slow tick and reads each
player's live `Player.Aircraft`; this just reads one more field off the same object:

- `PlayerRoster.cs` now also keeps `_aircraftIdBySteamId` (SteamID → the aircraft's
  `persistentID.Id`, 0 when there is none) alongside the existing aircraft-name dictionary.
- `Squad.SquadmateSteamIds()` — leader + members, **never** including this pilot's own SteamID
  (own-ship is a wholly separate draw call on MAP, never part of the contacts array at all, so
  there's nothing for this flag to override there even without the exclusion — added anyway for
  correctness rather than relying on that coincidence).
- `PlayerRoster.Refresh()` combines the two into `_squadAircraftIds` (a `HashSet<uint>`), rebuilt
  from scratch every tick — the same "no dedicated invalidation path" approach `RouteStore`/`Squad`
  use elsewhere: a squad ending, a member switching aircraft, or one despawning all just fall out of
  the next rebuild on their own.
- `TelemetryReader.BuildUnits` (4 Hz) reads `PlayerRoster.IsSquadAircraft(u.persistentID.Id)` per
  visible unit and stores it as `UnitInfo.SquadMember`, serialized as the contact's `sq` flag
  (`TelemetryJson.cs`).

Scoped to the current faction automatically, not by any extra check: `_aircraftIdBySteamId` is only
ever populated from the local player's own faction roster in the first place (a squad only spans
one faction — same constraint `PlayerRoster.cs`'s own header comment already documents for the
aircraft-name lookup).

## Rendering

`map.js`'s icon-color lookup gets one check ahead of the plain faction lookup:
`u.sq ? SQUAD_COLOR : (factionColors[u.f] || factionColors[0])` — same tint/glow pipeline every
other icon already uses, just a different source color. `SQUAD_COLOR` is `--no-squad`'s literal hex
(`#4ec9c9`), same reasoning `PLAYER_COLOR` already has for why canvas can't read a CSS var().

## Verification

`dotnet build` (0 errors), `dotnet test` (`TelemetryJsonTests`' `Unit_contact_round_trips_every_field`
covers the new `sq` field round-tripping through `TelemetryJson.Serialize`, plus a default-false
case). `tools/serve_web.py`'s mock has no squad-aware unit generation, so this needs in-game
verification (or an in-mission mock update) to see the actual tint on real contacts.
