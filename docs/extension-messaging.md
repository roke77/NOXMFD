# Extension faction, squad and messaging API

## Status

Built as extension API version 8. Asked for by the TAC extension
([NOXMFD-Extension-TAC](https://github.com/roke77/NOXMFD-Extension-TAC), its `docs/tac-plan.md`,
"NOXMFD API additions"), which needs one player per faction to coordinate targets with every squad
leader: it has to know who is in the faction, which squads exist, what its own squad standing is,
and send its own messages to other players' copies of the extension. The API stays generic:
nothing in it knows about TAC.

## What it adds

| Api member | Purpose |
|---|---|
| `LocalSteamId` | This pilot's SteamID (0 without Steam). |
| `GetFactionPilots()` | Faction-mates running NOXMFD right now, self excluded: SteamID, Steam name, designation, squad callsign/flight and leader. |
| `GetLocalSquad()` | This pilot's role (none/leader/member), squad callsign/flight/leader and own designation. |
| `SetMessageHandler(id, handler)` | Receives this extension's messages, on the main thread. |
| `SendMessage(id, to, payload)` | Sends one message to one faction-mate's copy of the same extension. |
| `SelectTarget(unitId)` | Selects a unit in this pilot's own targeting, the way a MAP tap does — for TAC's ACQUIRE. |

All of it is main-thread only, like the WPT reads (section 9 of `EXTENSIONS.md`).

## Design decisions

1. **The roster is what NOXMFD already knows.** `GetFactionPilots` joins `PlayerRoster` (who is in
   the local faction, their Steam name) with `Presence` (who runs NOXMFD) and the identity record
   every presence beat carries (`FactionIdentity`, docs/faction-broadcast.md: callsign, squad
   callsign/flight, leader). No new network traffic. A pilot on an older NOXMFD whose beat carries
   no record is listed with empty callsign and squad fields.
2. **Messages ride the squadron transport** (docs/squadron-transport.md) — same channel, same
   envelope, same 16 KB transport cap — under the wire type `ext.<id>`. NOXMFD's own types
   (`sqd.*`, `presence`, `fuel`) never start with `ext.`, so an extension can neither send nor
   receive them, and one extension's messages reach only the same extension id on the other side.
3. **Payloads are capped at 4096 UTF-8 bytes**, checked on send and again on receive. Enough for a
   target list; coordination messages, not bulk data.
4. **Sends go only to peers known to run NOXMFD** (`Presence.HasNoxmfd`, or a squadmate during a
   silence). The transport warns that sessions toward a player without the mod are never accepted
   and repeated connects stall their match traffic, so an extension must not be able to probe
   arbitrary SteamIDs. Discovery stays Presence's job.
5. **Receiving is unfiltered.** The sender's SteamID is authenticated by Steam; the payload is
   untrusted. Each extension decides whom to believe (TAC checks a claimed squad leader against
   `GetFactionPilots`). A message for an extension that isn't installed here, or has no handler, is
   dropped.
6. **Own inbox cursor.** `ExtensionMessages.Drain` reads `Squadron`'s shared inbox at its own pace,
   like `Squad`, `Presence` and `FuelBroadcast`, from `MissionLifecycle` every frame — so messages
   flow at the main menu too. A handler that throws is logged and doesn't stop the drain.
7. **Selecting goes through MAP's own path.** `SelectTarget` is `CommandDispatcher.SelectById`, the
   body of MAP's `target.select`: the unit must be live and detected by the player's side (the
   fog-of-war gate, docs/jamming-contact-telemetry-hardening.md F3), never no-faction, not excluded
   by the TGT filters, and it goes through `CombatHUD.SelectUnit` for the marker and beep. An
   extension gets the same rules instead of re-implementing them against the game.
8. **Rejections log once** per extension and reason (not registered, oversize, unknown recipient),
   so an extension retrying every frame doesn't flood the log. `SendMessage` returns false in each
   case, and when Steam refuses the send.

## Files

- `src/plugin/Extensions/Api.cs` — the public members.
- `src/plugin/CommandDispatcher.cs` — `SelectById`, shared by MAP's `target.select` and
  `Api.SelectTarget`.
- `src/plugin/Extensions/ExtensionMessageWire.cs` — wire type and size rules (pure, tested in
  `tools/tests/ExtensionMessageWireTests.cs`).
- `src/plugin/Extensions/ExtensionMessages.cs` — handlers, send checks, inbox drain.
- `src/plugin/Extensions/ExtensionSquadView.cs` — `FactionPilot`, `LocalSquad` and their builders.
- `FactionIdentity.Table.TryGet` / `Presence.TryGetIdentity` / `PlayerRoster.FactionSteamIds` — the
  per-pilot reads the roster needs.

## Needs a live check

Two NOXMFD clients in one faction, each with an extension that registers a handler:

- `GetFactionPilots` lists the other pilot within a few seconds of both entering the faction, with
  their callsign and squad once set, and drops them after they leave.
- `GetLocalSquad` follows creating, joining, leaving and handing over a squad.
- A `SendMessage` arrives at the other client's handler with the sender's SteamID; NOXMFD's squad
  features keep working alongside.
- A send to a player without NOXMFD is rejected and logs once.
- `SelectTarget` on a detected enemy selects it with the marker and beep; on an undetected or
  filtered-out unit it does nothing and logs why.
