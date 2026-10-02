# Faction-wide identity broadcast (issue #106)

Every pilot running NOXMFD sees every squad member's designation (`TALON 1-3`): in the game's own
map, kill feed, chat and scoreboard, on NOXMFD pages, and in the ATC extension. That holds whether
the viewer is in the same squad, another squad or no squad at all. Today the rename only covers the
viewer's own squad, because `PlayerNameOverride.Reconcile` is fed `Squad.Designations()` alone.

Requirements and decisions live in the issue. This doc plans the implementation.

## Current state

- `Presence.cs` sends a `presence` beat with an **empty payload** to the whole faction roster every
  5 s. Receivers record a last-seen time per SteamID. Older clients ignore the payload.
- `FuelBroadcast.cs` sends a `fuel` ratio faction-wide every 15 s, TTL 45 s.
- Both ride `Squadron.cs` (Steam P2P, versioned `{v,type,payload}` envelope, 16 KB cap) and are
  ticked from `PlayerRoster.Refresh` at 1 Hz with the faction peer list.
- `SquadDesignations.cs` (Unity-free, under xUnit) formats `"<CALLSIGN> <FLIGHT>-<MEMBER>"`.
- `PlayerNameOverride.Reconcile(wanted)` applies a SteamID → designation map once a second from
  `TelemetryReader`'s slow tick and restores anyone no longer in it.
- SQD gets its own squad from `GET /squad` / the `sqd-state` push, and the match roster from
  `GET /server-players` / the `server-players` push.
- `Squad.CreateSquad` returns false while incoming invites are undecided.

## Wire format

The identity rides the `presence` payload: one faction message on the existing 5 s beat, no new
message type. Older clients keep treating the beat as "running NOXMFD" and ignore the payload.

```json
{"v":2,"sv":2,"p":"VIPER","pf":1,"pn":2,"c":"TALON","f":1,"l":"76561198000000001","fu":0.734}
```

| Field | Meaning | Validation on receive |
|---|---|---|
| `v` | identity schema version | 2 = this layout; 1 = an older client (only its fuel is read, squad version counts as 0); anything else → ignore identity, still count the beat |
| `sv` | squad protocol version | integer 0–99; absent = 0 = older |
| `p` `pf` `pn` | the pilot's own callsign, flight and number ([self-callsign.md](self-callsign.md)) | `p` trimmed 1–20 chars, no control characters; `pf`, `pn` 1–9; all three or none |
| `c` | squad callsign | trimmed, 1–20 chars (`CreateSquad`'s limit); absent = not in a squad |
| `f` | squad flight | 1–9 |
| `l` | squad leader's SteamID (string: a ulong overflows JSON numbers) | non-zero; groups squads, since two squads can share `c`+`f` |
| `fu` | fuel ratio | clamped 0–1, NaN dropped; absent = no aircraft |

- The receiver builds a pilot's callsign itself with `SquadDesignations.Format(p, pf, pn)`, then the
  rename's sanitizer. No free-text designation field travels, so a record can't carry arbitrary
  text beyond a 20-char callsign.
- A record only ever describes its sender. It renames `m.From`, which Steam authenticates.
- Not in a squad: the `c`, `f` and `l` fields are absent. That is also the explicit clear: a record
  without `c` drops the sender from its squad at once.
- The whole payload is capped at 320 B on receive; anything longer or unparseable is dropped,
  and the beat still counts for presence.
- A peer whose record has `v:1`, or none, counts as `sv` 0: SQD lists it as UPDATE NOXMFD, without
  INVITE, and an invite from it is declined on arrival.

## Pieces

### 1. `Squad/FactionIdentity.cs`: pure core (new, Unity-free, under xUnit)

- `Identity` record: the pilot's own callsign, flight and number; the squad's callsign, flight and
  leader id; squad protocol version; fuel.
- `Serialize(Identity)` / `TryParse(string, out Identity)` with the validation above.
  Uses `JsonLite` (already Unity-free and tested).
- `Table`: SteamID → (Identity, last seen). Time is passed in, so tests drive it.
  - `Note(from, identity, now)`, `Designations(now, inFaction)` → SteamID → designation.
  - Expiry (requirement 7, no flapping): an entry's designation holds while its pilot is still in
    the faction roster and has beaten within 120 s, the same allowance `Presence.IsLost` gives
    a pilot still loading. A record without `c` clears at once.
- `Merge(own, faction)`: the viewer's own callsign wins, the faction table for everyone else.
- `Squads(table, now)`: groups entries by leader id → callsign, flight, members (the leader first,
  then in the order this client first heard them). Feeds SQD.
- `PilotsJson(designations)`: SteamID → callsign and whether another pilot flies the same, for
  `state.pilots`.
- `Duplicates(...)`: the set of `(callsign, flight)` pairs flown by more than one leader id.

### 2. Sender (`Presence.cs`)

- `Presence.Tick` sends `FactionIdentity.Serialize(current)` instead of `string.Empty`.
  `current` comes from `Squad.SelfIdentity` (the pilot's own callsign, the squad's callsign,
  flight and leader id, the squad protocol version) and the local aircraft's fuel.
- Immediate send on change (requirement 3): `Squad.RebuildState()` already runs on every squad
  change. It calls a new `Presence.MarkDirty()`, which zeroes `_nextBroadcast`, so the next 1 Hz
  `Refresh` sends without waiting for the 5 s beat. A change reaches other squads within about 1 s.
- `FuelBroadcast.Tick` stops sending (requirement 15). Its receive side stays for older peers, and
  `FuelFor` prefers the identity table's fuel when present.

### 3. Receive and apply

- `Presence.Drain` parses the payload with `FactionIdentity.TryParse` and notes it in the table.
  It already reads every `presence` message.
- `TelemetryReader`'s slow tick calls
  `PlayerNameOverride.Reconcile(Squad.Designations())`: the viewer's own callsign merged with the table.
  `Reconcile` stays the only apply path, so restores, aircraft labels and `psn` keep working.
- Telemetry `pn` / `psn` keep their meaning: MAP, TGT, AKF and the ATC extension need no change.

### 4. Faction squads to SQD

- The faction's other squads ride `/squad`'s state as `state.faction`, built in
  `Squad.BuildStateJson` by `FactionIdentity.FactionJson` (pure, unit-tested) from
  `Presence.Squads` plus `PlayerRoster`'s Steam name and aircraft per SteamID. That state already
  refreshes at 1 Hz and rides the `sqd-state` push, so there is no new endpoint, SSE event or shell
  relay wiring:

  ```json
  {"squads":[{"leader":"7656…","callsign":"VIPER","flight":2,"dup":false,
              "members":[{"id":"7656…","leader":true,"name":"DeckJockey","aircraft":"Tarantula"}]}],
   "selfDup":false}
  ```

  It excludes the viewer's own squad, which SQD already has. The page marks the pairs in use in
  its pickers from `squads` itself, and `selfDup` says whether the viewer's own pair clashes.
- `/server-players` keeps its shape. SQD's unassigned list is `/server-players` minus everyone
  in any squad, so INVITE is never offered to a pilot already in another squad.

### 5. `Squad.CreateSquad` declines pending invites (requirement 11)

- Remove the `_pendingReceived.Count > 0` guard. On success, decline every pending invite the way
  `AcceptInvite` declines the others: send `sqd.decline` to each sender and clear the queue.

### 6. SQD page: designs D and E

Mockups: `docs/images/sqd-d1-leader.png` to `sqd-d4-no-squad.png`; design E
([self-callsign.md](self-callsign.md), `sqd-e1-leader.png` to `sqd-e4-no-callsign.png`) builds on D.
Canvases are linked from the issues.

- Header: `SQUADRONS` with TGT's `tgt-head` separator, squad and pilot counts on the right.
- Not in a squad: the CREATE SQUAD row first (enabled whatever the invites, but it waits for the
  pilot's own callsign), then invite cards.
- The squad list: own squad first (green border, not collapsible, EDIT/LEAVE/DISBAND or LEAVE
  in its header), then every other squad (teal border, collapsible, read-only). Rows keep the
  current grid: the pilot's own callsign, Steam name, aircraft icon, then the amber LEADER label or,
  on the leader's view of a member, the ⋮ menu (Promote to leader, Kick from squadron). Only the
  own squad carries CALLSIGN / PILOT / AIRCRAFT column titles.
- Duplicates: amber SAME DESIGNATION text on each clashing squad's header, own included; the
  picker (CREATE and EDIT) marks used pairs amber with a dot, still selectable, plus one amber
  "<CALLSIGN> <FLIGHT> ALREADY FLYING" line.
- Amber labels have no border; buttons do.
- Unassigned players stay docked at the bottom (open with no squad, collapsed when leading,
  hidden for a plain member).
- Other squads start collapsed. What the pilot opens or closes (other squads by leader id, and the
  unassigned list once toggled) is kept in `sessionStorage` for the browser session.
- The page's narrow-width layout (two-line rows below 640 px) carries over to every squad.

### 7. `serve_web.py` mock

- `state.faction` with a duplicate `TALON 1`.
- `sendCommand('sqd.mock', {name: 'leader' | 'member' | 'invited' | 'none'})` switches between the
  four D states, so each can be checked in the `hud-web` preview (reload after switching).
  ACCEPT in the invited state joins that squad.

## Order of work

1. `FactionIdentity.cs` and its xUnit tests (parse, validation, merge, expiry, grouping,
   duplicates).
2. Sender and receiver wiring (pieces 2 and 3). Two NOXMFD clients in different squads should
   now see each other's designations, with no UI change yet.
3. `CreateSquad` change (piece 5).
4. `state.faction` in `/squad` (piece 4).
5. SQD page and mock (pieces 6 and 7).
6. Docs: `docs/squad-callsign-names.md` ("Who sees the names"), `docs/squadron-transport.md`
   (Implementation), `SECURITY.md` (designation and fuel shared faction-wide), `src/plugin/README.md`,
   `src/web/README.md`, the SQD manual in `man/`. Note in #94 that AWACS reads this table.
7. `tools/ci-check.ps1`, then the in-game checks in the issue.

## Decisions

- **Order of other squads:** by callsign, then flight (then leader id), so squads sharing a
  designation sit next to each other.
- **Not in a squad, invited:** the squad list keeps its normal order; the invite cards above it
  already name the inviting squads.
- **Heartbeat:** 5 s on every server size. About 100 B per faction-mate per beat stays negligible,
  and a squad change goes out within a second regardless.
- **AWACS (#94):** reads this faction table for names; no separate roster feed.
