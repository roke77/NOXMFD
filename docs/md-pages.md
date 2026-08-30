# MIS and OBJ — mission-info and objective-list MD pages

## Status

**Merged to `main` and in-game verified** (original branch `mission-objective`, ticket
[#37](https://github.com/roke77/NOXMFD/issues/37)). Backend telemetry, JSON
wire format, both bare pages, and the four-way MD switch (including its
vertical-label treatment in split mode and F-35) are implemented, wired, and
confirmed working in a live mission — including OBJ's collapsible
position sub-rows, added after the ticket's original scope.

AKF (advanced kill feed, branch `advanced-kill-feed`, ticket
[#34](https://github.com/roke77/NOXMFD/issues/34)) has joined the MD switch
as its default landing page and is fully implemented and in-game verified —
see [docs/akf-page.md](akf-page.md) for its own data model and design notes,
kept separate from this doc rather than folded in here.

## What these replicate

Both pages mirror the game's own in-cockpit `ObjectiveInfoList` component
(`_scratch/full/ObjectiveInfoList.cs` — the decompiled dump in `decompiled/`
predates this and doesn't have it), which toggles between two views:

- **MIS** — `ShowMissionInfo()`: mission name, `Time <clock> -- Duration
  <elapsed>`, `Score <escalation> -- <level> level`, and the mission's full
  description text (mission authors write campaign name, synopsis, and
  "Play Order" directly into this one field — nothing to parse out).
- **OBJ** — `ShowObjectiveList()`: the player faction's currently active
  objectives, one row each — name, status, completion percent, and
  collapsible position sub-rows (grid label + live range) where applicable.

MD (Mission Data) is now a five-way switch: **AKF / MIS / OBJ / BDF /
PAL**, all reachable from each other (`nav-model.js` `NAV.akf`/`NAV.mis`/
`NAV.obj`/`NAV.bdf`/`NAV.pal`, each the same six items with `mark` on
whichever is live). MAIN still reaches the family via `BEZEL_EXTRAS.main`'s
`MD` label, action `'akf'` — AKF is the group's default landing page
(issue #34 follow-up; was `'bdf'`).

## Data model

**MIS** (`TelemetryReader.BuildMis`, `TelemetrySnapshot.Mis*`, `TelemetryServer.MisBlock`):

| Field | Source |
|---|---|
| `present` | `GameManager.gameState == SinglePlayer && MissionManager.CurrentMission != null`. Multiplayer shows the Steam lobby name instead in-game (no description exists for that case) — not plumbed through, so MIS reads unavailable there. |
| `name` | `MissionManager.CurrentMission.Name` (reuses the existing `MissionName` field) |
| `description` | `MissionManager.CurrentMission.missionSettings.description` |
| `tod` | `LevelInfo.timeOfDay` (0..24) — "Time" |
| `duration` | `NetworkSceneSingleton<MissionManager>.i.MissionTime` (seconds) — "Duration" |
| `score` | `NetworkSceneSingleton<MissionManager>.i.currentEscalation` |
| `level` | 0/1/2 (Conventional/Tactical/Strategic), reproducing the same threshold comparison `ObjectiveInfoList.UpdateMissionInfo` does inline against `.tacticalThreshold`/`.strategicThreshold` |

**OBJ** (`TelemetryReader.BuildObj`, `TelemetrySnapshot.Obj`, `TelemetryServer.ObjBlock`):

| Field | Source |
|---|---|
| `present` | `SceneSingleton<DynamicMap>.i.HQ != null` and `MissionPosition.TryGetActiveObjectives` succeeds |
| `items[].n` | `Objective.SavedObjective.DisplayName` |
| `items[].s` | `Objective.Status` (`NuclearOption.SavedMission.ObjectiveStatus`: 0 NotStarted, 1 Running, 2 Complete — no Failed state exists) |
| `items[].p` | `Objective.CompletePercent` (0..1) |
| `items[].pos[]` | Position sub-rows (`ObjectiveInfoList_Item` — "DestroyUnits / Lb105 / 18km"), see below |

Objectives are dropped when `SavedObjective.Hidden` (e.g. the always-present
hidden mission-start bookkeeping objective) **or** when the objective isn't
`IObjectiveWithPosition` — matching `ObjectiveInfoList.AddObjectiveEntry`/
`InitializeObjectiveList` exactly. This isn't just a map-pin gate: it's the
game's own list-membership filter, so position-less objective types
(WaitSeconds, DialogueBox, CompleteOtherObjective, SuccessfulSortie, ...)
never appear in the in-game OBJ list at all, not just on the map — an earlier
version of this page got that backwards and only filtered `Hidden`, which
would have shown objectives the real panel never does.

**Position sub-rows** (`ObjectiveInfoList_Item`, the collapsible "DestroyUnits
/ Lb105 / 18km" rows under an objective): one `MissionPosition.GetAllPositionsResults(map.HQ, ...)`
call per `BuildObj` gathers every position for every active objective at
once (mirrors `ObjectiveInfoList.UpdateObjectiveInfo`'s own single call),
grouped by objective. Only `Name`/`X`/`Z` (true world coords, same space as
`WorldX`/`WorldZ`) travel from the plugin — `Name` is
`SavedObjective.ObjectiveTypeEnum.ToString()` (e.g. "DestroyUnits"), **not**
`TypeName`, which no longer exists on the current game build (caught by a
build failure against the *real* installed DLL — the `_scratch/full`
reference dump this doc's data model was traced against predates this
rename). The grid label and live range shown on the page are derived
client-side in `telemetry-source.js`, the same way the map's target list
already derives both (`gridLabel()` + `Math.hypot(dx,dz)/1000`) — this keeps
range live at the base frame's own rate instead of the plugin's 1 Hz refresh,
and avoids re-deriving `DynamicMap.gridLabels`' exact offset math twice.

Both MIS and OBJ (including its position sub-rows) refresh at 1 Hz
(`TelemetryReader.ScanWorld`), matching the game's own
`ObjectiveInfoList.Update()` cadence (`refreshDelay = 1f`) — except OBJ's
displayed *range*, which updates at the base frame's rate since it's derived
client-side from the objective's static position and the player's live one.

## Out of scope this pass

- The FORCES-dropdown-style extra display modes BDF/PAL already skip; MIS/OBJ
  have no equivalent in the source panel anyway.
