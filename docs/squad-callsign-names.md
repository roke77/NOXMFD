# Squad callsign names

## Goal

A player group asked for a way to fly under their callsigns instead of their Steam names, mainly on
the map (ATC and mission control) and in the kill feed. The mod they used, NO-ChangePlayerName,
patched `SteamFriends.GetPersonaName()`; since game 0.34 every client resolves every player's name
itself through `SteamFriends.GetFriendPersonaName(steamID)`, so that patch no longer reaches any
name another player sees, or even the one shown to yourself.

NOXMFD already gives every squad member a designation, `"<CALLSIGN> <FLIGHT>-<MEMBER>"` (e.g.
`TALON 1-3`, [squadron-transport.md](squadron-transport.md) "Squadron Callsign System"), but only
the SQD roster and TD's squad buttons show it. This feature makes the designation the pilot's name
everywhere: the game's own map, kill feed, chat and scoreboard, and every NOXMFD page.

## Decisions

- **Replace, not append.** In the game's own UI a squad member's name is their designation alone.
  NOXMFD pages add the Steam name in parentheses where there's room and it helps to tell who is who
  (table below).
- **One naming system.** No separate free-text display name: the squad designation is the name.
- **Leader reorders members.** SQD gets up/down buttons per member row so the leader can set who is
  `-2`, `-3`, … instead of living with join order.
- **Numbers stick.** A member leaving, being kicked or dropping out leaves their number empty
  instead of renumbering everyone below them, so nobody's name changes under them mid-flight. Only
  the leader leaving renumbers the squad.
- **Callsign list.** Stays the fixed `callsigns.js` list; more entries get added separately.
- **No persistence.** Squads stay in-memory and re-form after a restart, like today.
- **Who sees the names.** Squad members see each other's designations (they already hold the
  roster). Players outside the squad — ATC, mission control — see Steam names until the planned
  AWACS/Overlord role, which receives every squad's roster and designations. That role is a
  separate feature; this one only has to leave the rename keyed by SteamID so it can feed more
  names in later.

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

For each SteamID with a designation, on the local client:

1. Build `new PlayerName(rawSteamName, sanitize(designation))` — the same `SanitizeRichText(32)`
   + `ReplaceCharactersNotInFont` the game applies — and `RebuildCachedNames(PlayerIndex,
   ServerTag)`. `RawSteamName` stays the real Steam name.
2. Store it in `Player._playerNameCache` and `UnitRegistry.cachedPlayerNames[steamID]`. A player
   who rejoins picks the renamed entry back up from `cachedPlayerNames`.
3. Fire `_onNameResolved` with it. Mirage's `AddLateEvent` replays its last value to every
   listener added afterwards, so an aircraft spawned later labels itself with the renamed object.
4. Rewrite `unitName` of that player's current aircraft (and its persistent unit) to
   `"<designation> [<type>]"`, since `OwnerNameResolved` has already run and unsubscribed.
5. Keep the original `PlayerName` so leaving the squad (or a kick, disband, or a member moving to
   another number) restores or re-renames it through the same steps.

`PlayerNameOverride.Reconcile(Squad.Designations())` runs these steps once a second from
`TelemetryReader`'s slow tick, right after `PlayerRoster.Refresh()`. It compares every player's
cached name object with the rename it expects, so one pass covers every roster change (join,
leave, kick, reorder, EDIT callsign/flight, leadership transfer), a new aircraft, and
`UnitRegistry.Reinitialize` clearing `cachedPlayerNames`. A rename lands up to a second after the
roster change. `SquadDesignations.cs` holds the pure part: the format, the SteamID → designation
map, and the member swap.

This needs **no Harmony patch**: steps 2–3 are writes to private fields through one cached
`FieldInfo` each, in a narrowly named reflection adapter like `CmReflection.cs`, failing safe with
a logged warning if a field is missing after a game update (the name then simply stays the Steam
name). Nothing in the game resets it: Steam's persona-change callback
(`Player.Steam_OnPersonaStateChanged`) only resolves a name while `_playerNameCache` is still null,
so a Steam name change mid-session doesn't undo the rename.

## NOXMFD's own names

Once the game's name is the designation, every NOXMFD read of `GetDisplayName` returns it:
MAP pilot labels (`TelemetryReader`), TGP overlay pilot, the ATC extension. Two reads must switch to
the Steam name instead, or they'd show the designation twice or feed it back into the squad:

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
| SQD roster | designation column + Steam name column (unchanged layout) |
| TD squad buttons | `TALON 1-3` (unchanged) |
| ATC extension | `TALON 1-3` from `pn`; its SELECTED line can add `psn` in parentheses (ATC repo) |

A unit in the telemetry frame carries `psn`, the pilot's Steam name, only while `pn` shows their
designation, for any page that wants the parenthesised form.

## Member reordering

- Each member carries its own slot (`Squad.Member.Slot`, `slot` in every roster, invite and
  transfer message; a peer that sends none gets its list position). Joining takes the lowest free
  slot from 2 up (`SquadDesignations.FirstFreeSlot`), so an empty slot fills before the squad grows.
- New leader-only command `sqd.move-member {id, dir: -1|1}`: moves the member into the neighbouring
  slot — swapping with whoever holds it, or moving into it when it's empty — within 2 up to the
  highest held slot (`SquadDesignations.MoveTarget`), then `BroadcastRoster()` as every other
  roster change does, so the new numbers reach every squadmate with the next roster.
- The leader stays `-1`; up/down only reorder members `2..n` (the top member row has no up button,
  the last no down button). Making someone else `-1` is the existing leadership transfer: the old
  leader leaves the squad, the successor becomes `-1`, and the remaining members keep their order.
- SQD renders ▲/▼ on each member row for the leader only, beside the existing ★/× buttons.
- A member leaving, being kicked or dropping out leaves their slot empty; nobody else's number
  changes. SQD shows the empty slot as an `OPEN` row, and TD drops assignments to it
  (`TdStore.ClearSlot`). The next pilot to join takes it, or the leader moves a member into it.
- The leader leaving hands off to `_members[0]` (the lowest-numbered member) unless they pick a
  successor with ★. The successor becomes `-1` and everyone else is renumbered `-2..` in slot
  order, closing any empty slots.

## Checks

- Pure logic gets xUnit tests in `tools/tests/`: the move target (bounds, leader fixed), the
  first free slot, and the designation-per-SteamID map built from slots.
- `serve_web.py` mock: `sqd.move-member`, and the Steam name field for the pages above.
- In game, with two or more NOXMFD clients in a squad: map labels, kill feed, chat and scoreboard
  show designations on both clients; reorder and EDIT rename live; leaving restores the Steam
  name; a non-squad client still sees Steam names; a member leaving leaves their number OPEN on
  every client while everyone else keeps theirs, and the next join fills it.
