# Self-assigned callsigns (issue #107)

A pilot picks their own callsign (`TALON 1-2`) and it is their designation everywhere, in or out of a
squad. A squad keeps its own name (`TALON 1`); its members fly under their own callsigns. Builds on
[faction-broadcast.md](faction-broadcast.md) (#106). Requirements, decisions and the SQD design (E1-E4)
live in the issue. This doc plans the implementation.

## Current state

- A designation is `SquadDesignations.Format(squadCallsign, flight, slot)`. The slot is the member
  number: 1 = leader, 2.. = members, holes kept after departures, reorderable with ▲/▼.
- `Squad.Designations()` builds the squad's designations from its roster; `Presence.Designations()`
  builds every other squad's from the faction table. `TelemetryReader` merges them (own squad wins)
  and feeds `PlayerNameOverride.Reconcile`.
- The identity record in the `presence` beat is `{v:1, c, f, s, l, fu}`: squad callsign, flight,
  the sender's slot, leader id, fuel.
- TD keys its matrix on slots (`TdStore`: target id -> slots, slot -> last-sent ids); TGT's TD column
  prints slot numbers.

## Wire format

Identity schema 2 splits the pilot's own callsign from the squad:

```json
{"v":2,"sv":2,"p":"VIPER","pf":1,"pn":2,"c":"TALON","f":1,"l":"76561198000000001","fu":0.734}
```

| Field | Meaning | Validation on receive |
|---|---|---|
| `v` | identity schema | 2 = this layout; 1 = an older client (fuel only is read, squad version counts as 0) |
| `sv` | squad protocol version | integer; absent = 0 = older |
| `p` `pf` `pn` | the pilot's own callsign, flight, number | `p` trimmed 1-20 chars, no control characters; `pf`, `pn` 1-9; all three or none |
| `c` `f` `l` | the squad's callsign, flight and leader's SteamID | as in #106; absent = not in a squad |
| `fu` | fuel | unchanged |

- The slot (`s`) is gone. A pilot's designation is `Format(p, pf, pn)`; the squad name is only a name.
- Callsign validation is the **shape**, not list membership: the fixed list can grow (#102) and the
  page only offers list entries.
- The old check "slot 1 iff leader == sender" goes with the slot. A record still describes only its
  sender, so the worst a forged `l` does is list the sender under another squad.
- Members of an other squad list: leader first, then in the order this client first heard them join.
- Older clients ignore a `v:2` record (they still count the beat) and send `v:1`, which is how a peer
  is recognised as older.

## Pieces

### 1. Pure core (`Squad/PilotCallsign.cs` new, `FactionIdentity.cs`, `SquadDesignations.cs`)

- `PilotCallsign`: `TryCreate(callsign, flight, number)` normalises (trim, upper-case) and validates;
  `Format`. Used for local input and for the received record.
- `FactionIdentity.Identity`: pilot callsign + flight + number, squad callsign + flight + leader,
  squad version, fuel. `Serialize` / `TryParse` per the table above.
- `Table`: `Designations` (everyone heard with a pilot callsign), `Squads` (join order), `SquadVersion`,
  `FuelFor`. `PilotsJson` marks duplicate pilot callsigns.
- `SquadDesignations` keeps `Format` and `InsertSteamName`; `Build`, `FirstFreeSlot`, `MoveTarget` go.
- xUnit: validation, round trip, v1 compat, table ordering, duplicates, the version check.

### 2. Own callsign (`Squad/SelfCallsign.cs`)

- Three hidden BepInEx config entries (callsign, flight, number), so it survives a restart.
- `Squad.SetSelfCallsign` validates through `PilotCallsign`, saves it, rebuilds state and makes
  `Presence` resend at once. Command `sqd.set-self-callsign` (`name`, `index` = flight, `n` = number).
- `Squad.CreateSquad` and `AcceptInvite` return false without one.

### 3. Squad without slots (`Squad.cs`)

- `Member` is `(Id, Name)`; the list order is join order and is what the roster broadcast carries.
  `MoveMember`, the slot parsing and `sqd.move-member` go.
- Invite envelope carries `sv`. An invite without it or below the current version is declined at once
  with a notice naming the sender. `Invite` refuses a target whose `sv` is lower.
- `SelfIdentity` fills the new fields. Leaving a squad no longer changes the pilot's designation.

### 4. TD and TGT keyed on SteamID

- `TdStore`: assignments target id -> member SteamIDs; sent lists keyed by SteamID;
  `ClearSlot` -> `ClearMember`, `SwapSlots` goes. JSON carries SteamIDs as strings (a 17-digit id
  overflows a JS number). The leader's own marker is their own SteamID.
- Dispatcher `td.cell` / `td.column` / `td.designate` take `peer` instead of a slot number.
- `td-matrix.js` / `td.js` compare ids as strings; column heads show the member's callsign.
- `tgt.js` prints each assigned member's callsign (Steam name when none) in its TD column.

### 5. State and roster to the page

- `/squad` gains `me` (own callsign, flight, number, `dup`) and `pilots` (SteamID -> designation, `dup`)
  for everyone with a callsign; members and faction squad members lose `slot`.
- `/server-players` rows gain `aircraft`, and `update` for a peer whose `sv` is below the current one.

### 6. SQD page (design E)

- YOUR CALLSIGN row (CHANGE -> three dropdowns + SET, amber while unset, amber note when duplicated).
- CREATE SQUAD starts on the pilot's own callsign and flight; CREATE and ACCEPT stay disabled with
  no callsign.
- Rows show the pilot's own callsign (or NO CALLSIGN), no ▲/▼, no OPEN rows. ⋮ menu: Promote to
  leader / Kick from squadron, usable with mouse, touch and the PAD cursor, closed by an outside
  click or Escape.
- Unassigned list: callsign, pilot, aircraft; UPDATE NOXMFD tag and no INVITE for an older pilot;
  SAME CALLSIGN tag on duplicates.

### 7. `serve_web.py` mock, docs, release notes

- Mock: self callsign states, a squad whose members' callsigns don't match its name, a duplicate, an
  older pilot, the ⋮ menu.
- Docs: `man/sqd.md`, `man/td.md`, `docs/squad-callsign-names.md`, `docs/faction-broadcast.md`,
  `SECURITY.md`, READMEs. Release notes: mixed-version groups must all update to squad together.

## Order of work

1. Pure core + tests.
2. Own callsign + squad without slots + version check.
3. TD / TGT re-keying.
4. State JSON, mock, SQD page, TD page, TGT.
5. Docs and screenshots, pre-release pass.
6. In-game test with two clients.
