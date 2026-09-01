# HUD: squad target marks

[Issue #49](https://github.com/roke77/NOXMFD/issues/49).

## Status

Implemented, not yet tested in-game — expect the same size/offset tuning pass every other native
HUD cue here has needed after its first live look (`docs/hud-tti-estimate.md`, `docs/hud-focus-
mark.md`).

## Goal

Mark a unit's native in-game HUD icon (`HUDUnitMarker` — the square on ground units, the triangle on
aircraft, both parented under `CombatHUD.iconLayer`) whenever that unit is also being targeted by
someone else in the squad, so a glance at the HUD shows what the squad is already working without
switching to TGT/TD.

Two independent glyphs at the marker's top-right corner (`HudFocusMark.cs`'s own "+" sits top-left,
so the two cues never collide), stacked vertically when both are active:

- **`*`** — the squad **leader** currently has this unit targeted.
- **`⌃`** (chevron up) — **any** non-leader member currently has it targeted. One chevron regardless
  of how many members — a "someone else has it" flag, not a count.

Both render in `--no-squad` teal (`rgb(78, 201, 201)` — `theme.css`; `Color(78/255, 201/255,
201/255)` on the plugin side), not the unit's own faction colour, so a squad mark reads as its own
thing regardless of what it's stacked on. Shows regardless of whether the *local* pilot has also
locked the unit — the amber "+"/lock-ring belongs to that separate, pre-existing feature
(`docs/hud-focus-mark.md`, issue #68); this one is purely about what the rest of the squad is doing.

## Data path (the actual new work, per the ticket)

Neither the squad transport (#42) nor Target Designator (#47) gave every squad member visibility
into what *every other* member is targeting — #42 is roster/route-share only, and #47 is
one-directional (leader designates *to* a member; a member's own targeting never flows back). This
adds the missing piece: a small, change-driven broadcast, star topology like everything else in
`docs/squadron-transport.md`.

- **`SquadTargets.cs`** — the live-game glue, ticked once per second from `TelemetryReader`'s slow
  scan (same cadence as `Presence.Tick`/`PlayerRoster.Refresh`, right next to both). Reads the local
  aircraft's `weaponManager.GetTargetList()` (the same target-set reference RDR/TGT already reuse —
  see `docs/rdr-page.md`), and only acts when that set actually *changed* versus last tick
  (`SquadTargetsStore.SetSelfIds`) — a lock only changes on select/deselect, so this is the same
  "small text payload, a few per minute" tier the transport doc already ships target designation for,
  not a continuous stream.
  - **Member**: sends its own id set to the leader (`Squad.SendLocks` → `sqd.locks`).
  - **Leader**: relays the freshly-aggregated whole picture to every member (`Squad.
    RelayLocksAggregate` → `sqd.locks-aggregate`) whenever ITS OWN set changes.
- **`Squad.cs`** additions: `HandleLocks` (leader-side inbound — accepts only from a current member,
  same guard every other member-sourced handler here uses; aggregates via `SquadTargetsStore.
  SetMemberIds`, then immediately relays if that changed anything, same "push on change" shape
  `BroadcastRoster` already follows) and `HandleLocksAggregate` (member-side inbound — accepts only
  from the current leader). `Squad.cs` stays the one place that actually calls `Squadron.SendTo/
  SendToAll`; `SquadTargets.cs` never touches the transport directly, same split `CommandDispatcher.
  TdDesignate`/`Squad.SendDataTo` already keep for Target Designator.
- **`SquadTargetsStore.cs`** — pure BCL aggregation/lookup, no Squad/Unit/CommandDispatcher
  touchpoint, same testability seam `TdStore.cs`/`RouteStore.cs` keep (`tools/tests/
  NOXMFD.Tests.csproj` compiles it standalone). Holds this instance's own lock set, the leader's
  per-member aggregate (leader-only), and the last-relayed leader/other-member id sets (member-only).
  A leader instance answers `IsLeaderTargeting`/`IsOtherMemberTargeting` from its own live state
  directly (it IS the source, never stale); a member answers from the last relayed aggregate.
  `ApplyAggregate` takes the receiving pilot's own Steam id specifically to exclude their own entry
  from "other member" — a member never sees their own targets flagged as someone else's.

### Wire shapes

Both reuse the existing `sqd.data`-style pattern of a bare JSON payload in the envelope's `text`
field — no new `CommandEnvelope` fields, matching every other squad payload in this codebase:

- `sqd.locks` (member → leader): a bare array of ids, `[123,456]`.
- `sqd.locks-aggregate` (leader → every member): `{"leader":[ids],"members":{"<steamId>":[ids],...}}`.

## HUD rendering

**`Hud/HudSquadTargetMark.cs`** — a `MonoBehaviour`, added alongside `HudFocusMark`/`HudTgpCue`/
`HudTtiCue` in `MissionLifecycle.StartReader`. Same "ride `CombatHUD`'s own marker instead of
reprojecting world position" approach as `HudFocusMark.cs`, scaled up: rather than one always-alive
mark for a single focused lock, this is a pool of mark pairs keyed by persistentID, since any number
of units can be squad-targeted at once.

- **Refresh**: every frame, walks `CombatHUD.markerLookup` via `CombatHudMarkerLookup.cs` — the
  reflected-private-field lookup this cue and `HudFocusMark.cs` both need for the identical field,
  pulled into one shared cache rather than each keeping its own, so a game update that renames the
  field needs fixing in one place. For each visible marker (`image != null && image.enabled` — an
  off-screen edge-arrow-pinned lock has nothing sensible to sit next to, same reasoning
  `HudFocusMark.cs` uses), asks the store whether the leader and/or any other member is targeting
  that unit's id. Neither → skip. Either → build (if new) or reposition (if already built) that
  unit's mark pair as a sibling of the marker's own `Image`, inheriting its screen tracking and
  distance scaling for free.
- **Teardown**: anything not seen this frame (no longer targeted by anyone in the squad, or its
  marker left view) gets its `GameObject`s destroyed, not just hidden — unlike `HudFocusMark`'s one
  permanent mark, this pool's size is unbounded by nothing but "how many units the squad is
  currently working," so stale entries actually need to go.
- **Rebuild**: a stale `iconLayer` reference (aircraft respawn) clears the whole pool — the old marks
  already died along with the old `iconLayer`, so there's nothing to `Destroy` individually, just the
  dictionary to forget.
- **Squad ending**: `SquadTargetsStore.OnSquadEnded()` (called from `Squad.ResetToNone`/
  `HandleLeaderChanged`, alongside `TdStore.OnSquadEnded()`/`RouteStore.OnSquadEnded()`) clears the
  data; the marker pool then naturally empties on the very next frame since nothing queries true
  anymore — no separate HUD-side cleanup needed.
- **Roster shrink**: `SquadTargetsStore.RemoveMember` is called from `Squad.cs`'s `Kick()`/
  `HandleLeave()`, right alongside the existing `TdStore.RenumberAfterMemberRemoved` call — a
  departed member's last-known lock set stops counting toward "any other member" immediately, not
  just after their next (nonexistent) update.

## Non-goals for this pass

- Scaling the marks' offset/size with the target marker's own distance-based icon size — fixed pixel
  offsets for now, `ponytail`-tagged in the source with the same upgrade path `HudFocusMark.cs`
  already named for its own "+".
- A count for "how many other members" — the ticket is explicit this is a single "someone else has
  it" flag, not a tally.
- Drawing the marks as vector geometry instead of text glyphs — revisit only if `*`/`⌃` don't read
  cleanly in-game at HUD scale, same conditional `HudFocusMark.cs` already carries for its own `+`.

## Verification

`dotnet build` (0 errors), `dotnet test` (212/212, including `SquadTargetsStoreTests`' aggregation/
exclusion coverage). Full `*.test.js` suite green (this feature has no browser-side surface — the
marks are native HUD geometry, not rendered by any MFD page). Not yet tested in-game — the reflected
`CombatHUD.markerLookup` field and the on-screen result are first-pass guesses, expect to tune both
after a live look (see "Status").

## Related documents

- [HUD focus mark](hud-focus-mark.md) — the pre-existing amber "+" this shares its `CombatHUD.
  markerLookup` reflection approach with (top-left, not top-right; local-lock-focus, not squad).
- [Target Designator](target-designator.md) — issue #47, the other half of "what the squad is
  targeting" this feature completes (one-directional leader→member designation vs. this ticket's
  every-member-sees-everyone's-live-targeting).
- [Squadron transport](squadron-transport.md) — the star-topology transport and change-driven update
  philosophy this reuses outright, no new mechanism.
