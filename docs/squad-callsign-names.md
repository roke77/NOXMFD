# Pilot callsign names

## Goal

A player group asked for a way to fly under their callsigns instead of their Steam names, mainly on
the map (ATC and mission control) and in the kill feed. The mod they used, NO-ChangePlayerName,
patched `SteamFriends.GetPersonaName()`; since game 0.34 every client resolves every player's name
itself through `SteamFriends.GetFriendPersonaName(steamID)`, so that patch no longer reaches any
name another player sees, or even the one shown to yourself.

Each pilot running NOXMFD picks their own callsign, `"<CALLSIGN> <FLIGHT>-<NUMBER>"` (e.g.
`TALON 1-3`), on the SQD page ([self-callsign.md](self-callsign.md)). That callsign is their name
everywhere: the game's own map, kill feed, chat and scoreboard, and every NOXMFD page, whether or
not they are in a squad. A squad has its own name (`TALON 1`, [squadron-transport.md](squadron-transport.md)
"Squadron Callsign System"); its members fly under their own callsigns, which need not match it.

## Decisions

- **Replace, not append.** In the game's own UI a pilot's name is their callsign alone. NOXMFD pages
  add the Steam name in parentheses where there's room and it helps to tell who is who (table below).
- **One naming system.** No separate free-text display name: the callsign is the name. It is picked
  from the fixed `callsigns.js` list plus two numbers 1-9, never typed.
- **Pilot and squad are independent.** Joining, leaving or being kicked from a squad does not change
  a pilot's callsign. There are no member numbers, no reordering and no empty slots.
- **Persistence.** The callsign is kept in the BepInEx config and survives a restart. Squads stay
  in-memory and re-form after a restart.
- **Duplicates are allowed**, with an amber warning on the pilots involved.
- **Who sees the names.** Everyone running NOXMFD sees every pilot's callsign, whether they are in
  the same squad, another one or none (issues #106 and #107, [faction-broadcast.md](faction-broadcast.md)):
  each pilot broadcasts its own identity to the faction, and the rename is fed from that table plus
  the viewer's own callsign. Players without NOXMFD see Steam names. The rename stays keyed by
  SteamID.

## How the game resolves a name

| Step | Code (Assembly-CSharp) |
|---|---|
| Name lookup | `Player.GetPlayerName()`: returns `_playerNameCache` if set; else `UnitRegistry.cachedPlayerNames[steamID]`; else `TryGetNameFromSteam` → `GetFriendPersonaName`, sanitized, stored in both caches, then `_onNameResolved` fires. |
| Name object | `PlayerName(rawSteamName, sanitizedName)`. `RebuildCachedNames(playerIndex, serverTag)` builds the two display strings from `SanitizedName` (profanity filter, player-number prefix and server tag applied per the player's own settings). |
| Display | `Player.GetDisplayName(context)` → `GetPlayerName().GetDisplayName(context)`. `ChatOrLeaderboard` for chat/scoreboard/join messages, `Other` for everything else. |
| Aircraft label | `Aircraft.OwnerNameResolved` (an `OnNameResolved` listener, removed after its first call) sets the aircraft's `unitName` to `"<Other name> [<type>]"`, and the persistent unit's too. `NetworkunitName` is a plain local setter: every client builds this label itself. |

Consumers:

- Map icon labels (`UnitMapIcon.cs`), map order tooltip (`MapToolTip.cs`), chat, scoreboard, join
  messages, HUD target markers, vote-kick UI: `Player.GetDisplayName`.
- Kill feed (`MessageManager.RpcKillMessage`), "failed on impact", ejected-pilot units
  (`"<unitName> pilot"`): the aircraft's `unitName`.
- Vote-kick's own list: `RawSteamName` — stays the Steam name, which is right for a kick.

## Rename mechanism

For each SteamID with a callsign, on the local client:

1. Build `new PlayerName(rawSteamName, sanitize(callsign))` — the same `SanitizeRichText(32)`
   + `ReplaceCharactersNotInFont` the game applies — and `RebuildCachedNames(PlayerIndex,
   ServerTag)`. `RawSteamName` stays the real Steam name.
2. Store it in `Player._playerNameCache` and `UnitRegistry.cachedPlayerNames[steamID]`. A player
   who rejoins picks the renamed entry back up from `cachedPlayerNames`.
3. Fire `_onNameResolved` with it. Mirage's `AddLateEvent` replays its last value to every
   listener added afterwards, so an aircraft spawned later labels itself with the renamed object.
4. Rewrite `unitName` of that player's current aircraft (and its persistent unit) to
   `"<callsign> [<type>]"`, since `OwnerNameResolved` has already run and unsubscribed.
5. Keep the original `PlayerName` so a pilot who drops their callsign, or leaves the faction,
   restores or re-renames through the same steps.

`PlayerNameOverride.Reconcile(Squad.Designations())` runs these steps once a second from
`TelemetryReader`'s slow tick, right after `PlayerRoster.Refresh()`. `Squad.Designations()` is the
viewer's own callsign merged with every callsign the faction table holds. Reconcile compares every
player's cached name object with the rename it expects, so one pass covers a pilot changing their
callsign, someone joining or leaving the faction, a new aircraft, and `UnitRegistry.Reinitialize`
clearing `cachedPlayerNames`. A rename lands up to a second after the change.
`SquadDesignations.cs` holds the pure part: the format and the Steam-name insertion.

This needs **no Harmony patch**: steps 2–3 are writes to private fields through one cached
`FieldInfo` each, in a narrowly named reflection adapter like `CmReflection.cs`, failing safe with
a logged warning if a field is missing after a game update (the name then simply stays the Steam
name). Nothing in the game resets it: Steam's persona-change callback
(`Player.Steam_OnPersonaStateChanged`) only resolves a name while `_playerNameCache` is still null,
so a Steam name change mid-session doesn't undo the rename.

## NOXMFD's own names

Once the game's name is the callsign, every NOXMFD read of `GetDisplayName` returns it:
MAP pilot labels (`TelemetryReader`), TGP overlay pilot, the ATC extension. Two reads must switch to
the Steam name instead, or they'd show the callsign twice or feed it back into the squad:

- `PlayerRoster.cs` (invite list names) and the names `Squad.cs` carries in its roster and invites.
  They switch to the sanitized original (`GetPlayerName().RawSteamName` through the same
  sanitizer).
- Telemetry carries the Steam name next to the display name for the pages below.

Where each page shows what:

| Page | Shows |
|---|---|
| Game map, kill feed, chat, scoreboard | `TALON 1-3` |
| MAP pilot-name labels | `TALON 1-3` (label space) |
| AKF kill feed | `TALON 1-3 (SteamName) [<type>]` (`PlayerNameOverride.WithSteamName`) |
| TGP overlay pilot | `TALON 1-3` |
| SQD rows | callsign column + Steam name column |
| TD member columns, TGT's TD column | the member's callsign |
| ATC extension | `TALON 1-3` from `pn`; its SELECTED line can add `psn` in parentheses (ATC repo) |

A unit in the telemetry frame carries `psn`, the pilot's Steam name, only while `pn` shows their
callsign, for any page that wants the parenthesised form.

## Checks

- Pure logic gets xUnit tests in `tools/tests/`: the callsign rules (`PilotCallsign`), the identity
  record and table (`FactionIdentity`), and the format and Steam-name insertion (`SquadDesignations`).
- In game, with two or more NOXMFD clients: map labels, kill feed, chat and scoreboard show callsigns
  on both clients; changing a callsign renames live; joining or leaving a squad leaves it as it is;
  a client without a callsign, and a client without NOXMFD, still show Steam names.
