# Jamming and tactical-contact telemetry hardening

**Status:** F2, F3, F5 fixed; F4 decided (kept as-is, no code change); F1/F6 still open  
**Investigation date:** 2026-08-29 (static); 2026-08-30 (F2/F3/F4/F5 — see each finding for detail)  
**Repository baseline:** `main` at `39df2e6` (`0.34.0`)

## Problem statement

A player reports that NO XMFD continues to show useful enemy information while the player's
aircraft is being jammed by a Medusa or Alkyon. The reported symptoms are:

- enemy positions remain clean and continue updating on the MAP page while the native map is
  disrupted;
- enemy bearing information remains available without a current datalink track; and
- the external display may reveal tactical information that the in-game displays no longer make
  usable.

This document records the first code and game-assembly investigation and proposes a conservative
first fix. No implementation is included here.

## Security boundary

The browser is not a security boundary. A client can inspect the raw `/stream` SSE payload and can
POST commands directly to `/command`; hiding or distorting an icon only in `map.js` does not prevent
the underlying coordinates or target id from being used.

The game client also has live `Unit` objects for units that its UI does not currently expose. Any
direct read from an enemy `Unit` (`GlobalPosition`, `transform`, `speed`, and similar state) must be
treated as privileged data. The mod must apply the game's visibility and jamming rules before that
data is serialized or accepted by a command handler.

## Evidence baseline

The game-side comparison uses the installed assembly linked by this checkout's `GameDir.props`:

- assembly: `NuclearOption_Data/Managed/Assembly-CSharp.dll`
- timestamp: 2026-08-14 22:14:15 +02:00
- size: 3,025,408 bytes
- SHA-256: `EB3B93BDAEC37DD7B3BAB72F801A2C84E5BE2AE3C559F39251E2320AE6B11CCC`

The relevant types were inspected with `ilspycmd`: `FactionHQ`, `TrackingInfo`, `DynamicMap`,
`UnitMapIcon`, `CombatHUD`, `Radar`, `JammingPod`, `Unit`, and `JammedMarker`.

## Current NO XMFD data flow

1. `TelemetryReader.ScanWorld()` discovers every live `Unit` once per second with
   `FindObjectsByType<Unit>()`. This is an unfiltered world list.
2. `BuildUnits()` creates MAP/TGT contacts. It requires
   `player.NetworkHQ.TryGetKnownPosition(unit, out position)`, which keeps completely unknown enemy
   units out of the ordinary contact array.
3. `BuildRdr()` combines the player's own `Radar.detectedTargets` with faction-known datalink
   contacts.
4. `BuildHsd()` includes enemy aircraft known by datalink or detected by the player's radar.
5. `BuildRwr()` independently records radar-warning events and reads each emitter's live world
   position. RWR contacts do not require a datalink track.
6. `TelemetryJson` serializes all of these values into `/stream`.
7. `TelemetrySource` parses each frame and gives the raw frame to MAP. `map.js` replaces its previous
   frame with the new one; it does not extrapolate or retain removed contacts.

The contact refresh defaults to 4 Hz. RWR/MW and the main telemetry frame default to 10 Hz.

## Native game behavior

### Faction tracking

`FactionHQ.GetKnownPosition()` returns a friendly unit's live position. For an enemy it only returns
the associated `TrackingInfo.GetPosition()` from the faction tracking database. A never-discovered
enemy therefore has no ordinary native map icon and does not pass NO XMFD's `BuildUnits()` gate.

`TrackingInfo.GetPosition()` has an important nuance:

- for four seconds after `lastSpottedTime`, it refreshes `lastKnownPosition` from the enemy's live
  `Unit.GlobalPosition()`;
- after four seconds, it returns the frozen `lastKnownPosition`; and
- `TrackingInfo.Observed()` uses the same four-second freshness window.

The native `DynamicMap` creates icons for the local faction's tracking database and friendly units.
Its `UnitMapIcon` uses the same `TrackingInfo.GetPosition()` result, so the ordinary coordinate gate
in NO XMFD largely matches the native map.

### Jamming presentation

Every `Unit.Jam` event reaches the local aircraft's `CombatHUD`. `CombatHUD` accumulates
`jamAmount * 0.5` in its public `jamAccumulation` field and decays it over time. While the value is
above zero:

- `DynamicMap.UpdateIcons()` calls `UnitMapIcon.JammingDistortion()` for map contacts;
- each affected icon is displaced randomly; and
- its alpha is reduced according to the jamming strength.

This is separate from `Radar.IsJammed()`. The radar has its own accumulation, tolerance threshold,
and decay. A jam event can therefore disrupt the native map before or without NO XMFD's current
`PlayerJammed` flag becoming true.

### Stale orientation

The native map updates an oriented contact's rotation only while its `TrackingInfo` is absent or
`Observed()` is true. Once an enemy track is stale, its last position and last displayed orientation
both freeze.

### Radar-warning bearing

`DynamicMap.ShowRadarPing()` creates a `RadarMapVis` directly from an
`Aircraft.OnRadarWarning` event. The visualization follows the emitter's live position for one
second after a search ping, two seconds after a detection/track ping, or four seconds after a target
lock ping. It does not require the emitter to be present in the faction tracking database.

This means an RWR bearing without a datalink contact is native behavior, not automatically a data
leak. NO XMFD still has to match the native lifetime and avoid adding extra information to it.

## Findings

### F1 — clean enemy picture bypasses the native jamming effect — high

NO XMFD computes `PlayerJammed` through `GetJamState(player)`, which only tests
`player.radar is Radar && radar.IsJammed()`. The MAP client then adds a jam glyph and an optional
line to a visible jammer. It never consumes `CombatHUD.jamAccumulation` and never disrupts or
suppresses the contact picture.

As a result, enemy coordinates that the native map intentionally makes faint and unstable remain
clean, readable, and selectable on the external MAP. The clean coordinates also remain in
`/stream`, so a browser-only distortion would not close the leak.

This is the most likely explanation for the main reported symptom. The original jamming change
(`612f512`, "Show radar-jamming lines on the MAP page") replicated `JammedMarker`, but did not
replicate the separate `CombatHUD`/`DynamicMap` distortion path.

Confirmed directly in the decompiled assembly: `Radar.cs` holds its own private
`jamAccumulation`/`jamTolerance` pair (`IsJammed() => jamAccumulation > jamTolerance`), entirely
separate from `CombatHUD.jamAccumulation`. Both are driven by the same `Unit.Jam` event but never
share state. `DynamicMap.UpdateIcons()` reads only the `CombatHUD` value; `GetJamState()` in
`TelemetryReader.cs` reads only the `Radar` value. There is no code path in either game or mod that
reconciles them.

Also confirmed: `CombatHUD.jamAccumulation` is clamped to `[0, 1]`, and
`UnitMapIcon.JammingDistortion()` computes alpha as `1 - jammingStrength * 0.7f`, so native jamming
never fully hides a map icon — it bottoms out around 30% opacity plus positional jitter. The
"conservative containment" fix below (fully omitting jammed contacts) is intentionally stricter than
this native floor, which is already noted in the proposal; it is not an inconsistency to resolve.

Affected surfaces:

- MAP ordinary contacts;
- HSD datalink contacts;
- FCR datalink-only contacts;
- the TGT rows derived from MAP contacts; and
- command-driven target selection from these surfaces.

### F2 — stale enemies retain a live heading — high — fixed 2026-08-30

`BuildUnits`, the datalink pass in `BuildRdr`, and `BuildHsd` use the faction-known position but read
heading directly from `unit.transform.eulerAngles.y` on every contact refresh.

The position freezes when `TrackingInfo.GetPosition()` becomes stale, but the icon can continue to
rotate as the real enemy turns. This differs from `UnitMapIcon.UpdateIcon()`, which freezes rotation
when `TrackingInfo.Observed()` becomes false. It can reveal maneuvers after the faction loses the
track and may look like a position or bearing update in a recording.

**Fixed.** Added `TelemetryReader._lastHeading` (an unpruned per-unit cache, same tradeoff as the
existing `_jammedBy` field) and a `GetDisplayHeading(Unit, bool fresh)` helper: while fresh it
records and returns the live heading, otherwise it returns the last one recorded. All three
builders now call it with `fresh: !stale`, reusing the exact same `Stale` boundary each already
serializes (`datalinkKnown && !IsTargetPositionAccurate(u, 20f)`) rather than a second, narrower
`Observed()`-based one — closing the inconsistency flagged during independent verification below.
`BuildRdr`'s own-radar pass (always actively painted) uses `fresh: true` unconditionally; its
datalink-only pass needed the same staleness check added since it never had a `Stale` concept of
its own. Build succeeds, all 166 tests pass. No live jamming needed to verify: get a track on any
enemy, then break contact (radar off, turn away, terrain mask) for 4+ seconds and confirm the MAP/
HSD/RDR icon's heading stops changing even if the real unit keeps turning.

**MAP page follow-up, 2026-08-30**: `UnitInfo.Stale` (`st`) was already serialized but `map.js` never
read it. Added a visual treatment — a stale contact's icon now draws at reduced opacity
(`STALE_ALPHA`). Pure frontend change in `src/web/pages/map/map.js`; no backend field needed since
`st` already existed. Requires a DLL rebuild to deploy (`src/web/*` is an embedded resource, not
served from disk).

Verified live via `tools/serve_web.py` against a real captured stale contact (a T/A-30 Compass) by
sampling canvas pixels directly rather than eyeballing a screenshot. First pass used
`STALE_ALPHA = 0.2` plus a white ring (`drawStaleRing`) — pixels confirmed both rendered
(`rgb(255,255,255)` ring, icon colour ~20% of full red), but read as too subtle in a screenshot.
Tried `STALE_ALPHA = 0.9` with the ring removed — verified (icon colour ~90% of full red, zero
ring pixels), but in practice barely distinguishable from a fresh contact at a glance. Settled back
on `STALE_ALPHA = 0.2`, no ring: noticeably faded is the point, not a subtle hint.

### F3 — `target.select` does not enforce contact visibility — high

The public command accepts a persistent id, resolves it with `UnitRegistry.TryGetUnit`, and passes
the resulting live unit to `TrySelectTarget`. The handler checks that the unit is live, not the
player, not neutral, not excluded by the TGT filters, and not already selected. It does not require
the unit to be faction-known or detected by the player's radar.

The shipped web UI only sends ids from rendered contacts, but a caller can POST an id directly. A
guessed, reused, logged, or previously observed id can therefore select a currently hidden enemy.
Removing an icon during jamming is insufficient unless the command path applies the same disclosure
policy.

The manual TGP calls `TrySelectTarget` internally after its own camera/line-of-sight selection logic.
The fix must preserve that sensor-specific path rather than adding a blanket check that breaks point
track.

This is a complete-enumeration exploit, not a probabilistic one. `UnitRegistry.cs` assigns every
`persistentID.Id` from a single `nextIndex++` counter — a small, sequential, mission-scoped integer,
not a token or GUID. A client already knows its own id and every friendly id it can see, so it knows
the live id range without guessing anything. It can `target.select` every integer in that range and
acquire every enemy on the map in one pass, jammed or not, regardless of any visibility rule this
document proposes elsewhere. F3 should be treated as equal priority to F1, not a secondary hardening
step — closing F1 alone still leaves the entire enemy roster selectable by id enumeration.

**Confirmed live, 2026-08-30**, single-player, "Free Flight - Heartland": with `playerId=135`,
POSTing `target.select` for every id from 1 to 150 (`curl` loop against `/command`, 100ms apart, no
mission editor or jamming involved) added over 20 units the player's own `/stream` never disclosed
straight onto the weapon target list — ids `70, 71, 72, 84, 90–93, 99–109, 120–129, 134`, spanning
hangars, helipads, munitions bunkers, fuel trucks, a Shard Class Corvette, and a **Hyperion Class
Carrier (id=122)** that appeared in no telemetry field at all, including the faction ship-count
tables, and had `viaHud=False` — it wasn't even in the player's own faction tracking database. The
only rejections were the no-faction check (ids `73, 81–83`), confirming that check is the sole
working gate today; faction-known/detected is not checked at all. No jamming was active during this
run, confirming F3 is exploitable independently of the jamming scenario that motivated this
investigation.

**Confirmed live, 2026-08-30**, single-player, "08. Infiltration" (`playerId=141`, 171 total units):
the same sweep (ids 1–200) against a mission with real combat units, not just static scenery. Only 8
ids were on the player's own MAP page at the time; the sweep additionally selected 33 hidden units —
`63, 128–138, 146–158, 164–171` — including three **Linebreaker SAM** launchers (ids 151, 152, 156)
that never appeared in any telemetry field, plus IFVs, APCs, recon trucks, a radar truck, fuel and
munitions trucks, an aircraft revetment, vehicle depots, and helipads. This mission's entire premise
is flying undetected to avoid air defenses; a player able to designate live SAM sites they have never
detected defeats that mechanic directly, independent of any jamming.

**Fixed and verified live, 2026-08-30.** `TargetSelectionPolicy.IsSelectable` (new,
`src/plugin/TargetSelectionPolicy.cs`) requires `factionKnown || ownRadarDetected` — the same two
gates `BuildUnits`/`BuildRdr` already use for what MAP/FCR disclose. It's applied only in
`CommandDispatcher.TargetSelect`, the external `/command` entry point, not in the shared
`TrySelectTarget` — the manual TGP's internal call to `TrySelectTarget` after its own
line-of-sight acquisition is intentionally left untouched, per the design note above. A 4-case
xunit regression test (`tools/tests/TargetSelectionPolicyTests.cs`) locks in the truth table.

Re-running the exact "08. Infiltration" sweep after restarting with the fix deployed: only the 6
units already on the player's MAP page at mission start were selectable; all three Linebreaker SAM
sites (`151, 152, 156`) and every other previously-hidden id were rejected with
`not visible to player`. No regressions on the units that should remain selectable.

### F4 — RWR bearing is native, but NO XMFD retains it 50% longer — medium

`BuildRwr()` reads the emitter's live `GlobalPosition()` without consulting the faction tracking
database. That matches `DynamicMap.RadarMapVis` and explains how a bearing line can exist without an
ordinary enemy contact.

NO XMFD uses lifetimes of 1.5/3/6 seconds for search/track/lock. The game uses 1/2/4 seconds. Commit
`b221cab` deliberately lengthened them. During the extra 0.5/1/2 seconds, NO XMFD continues to
publish and update the emitter's exact bearing after the native indication has expired.

`b221cab`'s own message ("RWR: fade contacts 50% slower") shows this was a deliberate gameplay-feel
change, not an oversight — a straight revert to native timings undoes a decision that was made on
purpose.

**Decided, 2026-08-30: keep the 1.5/3/6s lifetimes.** The gameplay-feel change stands; the extra
0.5/1/2s of RWR retention is accepted as a known, minor, low-severity parity gap. No code change.

### F5 — raw telemetry exposes unfiltered world counts — medium — fixed 2026-08-30

`ScanWorld()` counted every discovered `Unit` and `Aircraft` before faction visibility filtering, and
`TelemetryJson` published those totals as top-level `units` and `aircraft` values. No web consumer
ever read them (confirmed by search), but an SSE client could use them to infer hidden spawns,
losses, or force changes.

Removed entirely: `TelemetrySnapshot.TotalUnits`/`TotalAircraft`, their computation in
`TelemetryReader.ScanWorld()`, and the two `units`/`aircraft` fields from `TelemetryJson`'s wire
format (with the following placeholder indices renumbered). Build succeeds, all 166 xunit tests
pass; no test or web page referenced either field. No live jamming or mission setup needed to
verify — the fields simply no longer appear in `/stream`.

### F6 — a hidden jammer's persistent id is serialized — low

`PlayerJammedBy` records the `Unit` supplied by the jam event and serializes its persistent id even
when the jammer is not a visible contact. The MAP only draws the line when it can resolve that id to
a visible contact, but the raw payload still exposes the identifier. It gives no position by itself,
yet it is unnecessary hidden-unit metadata and can be correlated with later frames.

## Negative findings and interpretation limits

- An enemy that has never entered the player's faction tracking database does not pass
  `BuildUnits()` and cannot become an ordinary MAP contact through the normal telemetry path.
- `map.js` does not synthesize motion. Each parsed frame replaces `lastData`, and a removed contact
  disappears from the next draw.
- A moving radar-warning spoke with no red contact icon is expected native RWR behavior. Its extended
  NO XMFD lifetime remains a parity issue.
- If an enemy contact's `x/z` continues updating after its track is stale, another friendly sensor
  may still be refreshing the faction database. If `x/z` freezes but the icon turns, F2 is the
  direct cause.
- The investigation is static. A controlled live reproduction is still required to associate the
  reporter's exact visual element with `contacts`, `rwr`, or another payload block.

## Recommended first approach: conservative server-side containment

The first fix should favor preventing clean data disclosure. Native-style browser jitter alone is
not sufficient because the exact coordinates would remain readable in `/stream` and selectable by
id.

### 1. Read the native picture-jamming state on the main thread

During `PushSnapshot`, read `SceneSingleton<CombatHUD>.i.jamAccumulation` for the current local
aircraft. Treat `> 0` as active, matching `DynamicMap.UpdateIcons()`.

Keep this distinct from `Radar.IsJammed()`:

- `pictureJam` controls tactical-picture disclosure;
- `Radar.IsJammed()` continues to describe the radar subsystem and FCR detector behavior; and
- the existing jammer marker can remain a separate presentation feature.

A scalar or boolean may be sent to the browser so MAP/HSD/TGT can show a `JAMMED` state, but clean
enemy coordinates must already be removed before serialization.

### 2. Suppress enemy tactical coordinates while picture jamming is active

For the initial containment version:

- omit enemy entries from `TelemetrySnapshot.Units`;
- omit enemy entries from `TelemetrySnapshot.Hsd`;
- omit datalink-only entries from `TelemetrySnapshot.Rdr`;
- preserve the player's own position and friendly contacts;
- preserve RWR and missile-warning cues because the native game continues to provide those warning
  channels; and
- let FCR keep only contacts that are present in the player's current `Radar.detectedTargets`, since
  that list already applies the radar's jamming and detection model.

This is intentionally stricter than reproducing native map jitter. It closes the raw telemetry leak
immediately and provides a safe baseline. A later fidelity pass can explore server-generated,
non-reversible uncertainty if hiding all enemy map contacts proves too severe.

The browser must clear the corresponding icon and hit-test state as soon as the suppressed arrays
arrive. It should display an explicit `JAMMED` indication rather than silently looking empty or
disconnected.

### 3. Apply the same policy to command-driven target selection

`target.select` must authorize the id against current game state before calling `TrySelectTarget`:

- outside picture jamming, an enemy is eligible only if the player's HQ has a known position or the
  player's own radar currently detects it;
- during picture jamming, an enemy is eligible only if the own radar currently detects it; and
- friendlies continue to follow the existing faction and TGT-filter rules.

Keep this check in the external `target.select` handler or pass an explicit eligibility result into
the shared selector. Do not apply it blindly to manual TGP point-track, whose caller already performs
its own visual/line-of-sight acquisition.

### 4. Freeze stale heading at the disclosure boundary

Maintain a small per-unit last-known-heading cache:

- update it while the enemy `TrackingInfo.Observed()` is true or the player's own radar detects the
  unit;
- publish the cached value after the track becomes stale;
- never read a fresh enemy transform solely to update a stale MAP/HSD/datalink contact; and
- remove or overwrite the entry when the unit disappears, changes faction, or becomes observed
  again.

This mirrors the native `UnitMapIcon` behavior without removing useful last-known orientation.

Use the same staleness boundary the client already sees, not raw `Observed()`. NO XMFD's existing
`UnitInfo.Stale` field is `datalink && !playerHQ.IsTargetPositionAccurate(u, 20f)`, and
`FactionHQ.IsTargetPositionAccurate` returns true either while `Observed()` (the same 4-second
window) or — even after that window — while the enemy's live position is still within the given
threshold of its last known position. It is strictly more lenient than `Observed()` alone. Freezing
heading on raw `Observed()` going false would create a window where the client sees `Stale: false`
(a contact it already treats as "good") while its heading has silently stopped updating — two fields
describing the same contact would disagree. Trigger the heading freeze on the same condition that
already sets `Stale`, not a second, narrower one.

### 5. Remove or narrow secondary disclosures

- ~~Decide on RWR lifetimes~~ — decided 2026-08-30: keep 1.5/3/6s, accept the parity gap (F4).
- ~~Remove the unused top-level world `units` and `aircraft` counts~~ — done 2026-08-30 (F5).
- Serialize `PlayerJammedBy` only when the jammer id is also present in the already-disclosed contact
  set; otherwise send zero. (F6, still open.)

### 6. Centralize the pure policy, not the Unity reads

MAP, HSD, FCR, and `target.select` need identical decisions. A small pure policy seam is justified
here because it has multiple consumers and needs unit coverage. It should accept plain facts such as:

- friendly/enemy;
- picture jam active;
- faction-known;
- tracking info observed;
- own-radar detected; and
- requested operation (publish MAP/HSD, publish FCR, or external select).

Unity lookups remain in `TelemetryReader` and `CommandDispatcher`; the policy only returns disclosure
and selection decisions. This keeps it compilable in `tools/tests` without adding game assemblies to
the test project.

## Proposed implementation sequence

1. Add the pure disclosure policy and table-driven C# tests. *(Still open for F1/F2; F3's own
   narrower policy is done — see below.)*
2. Read `CombatHUD.jamAccumulation` once per snapshot and apply the policy to MAP/HSD/FCR builders.
3. ~~Gate external `target.select` with the same facts~~ — **done, 2026-08-30.** Closed the
   id-enumeration exploit (F3): `TargetSelectionPolicy.IsSelectable`, verified live against two
   missions (a hidden carrier, three hidden SAM sites), zero regressions.
4. ~~Add the last-known-heading cache~~ — **done, 2026-08-30** (F2): `GetDisplayHeading`, reusing
   the existing `Stale` boundary, not raw `Observed()`. Live verification (get a track, break
   contact, confirm heading freezes) still pending — no Unity-testable pure function to unit-test.
5. ~~Decide on RWR lifetimes~~ / ~~remove unused world totals~~ — **done, 2026-08-30** (F4 kept as-is,
   F5 removed). Hidden jammer ids (F6) still open.
6. Add the browser `JAMMED` state and preview mocks after the payload contract is settled.
7. Run `tools\ci-check.ps1`, then perform the live-game matrix below.

Keep the implementation in focused commits so the containment policy, selection hardening, heading
fix, and UI treatment can be reviewed independently.

## Automated verification

Add tests that prove:

- a never-known enemy is not published or externally selectable;
- a faction-known enemy is published normally when picture jamming is inactive;
- an enemy known only through datalink is removed from MAP/HSD/FCR when picture jamming is active;
- an own-radar detection remains eligible for FCR and external selection during picture jamming;
- stale contacts retain their last observed heading rather than reading a new live heading;
- RWR contacts expire at 1/2/4 seconds;
- serialized frames contain no hidden enemy coordinates, global world counts, or hidden jammer ids;
- a jammed frame replaces the previous MAP frame and leaves no stale hit target that can be clicked;
  and
- clearing jamming restores only contacts that still satisfy current visibility rules.

## Live-game verification matrix

Static and preview tests cannot exercise Unity tracking or jamming. Verify against a rebuilt plugin
in at least these scenarios:

| Scenario | Native reference | Expected NO XMFD result |
|---|---|---|
| No jammer, never-detected enemy | no enemy map icon | no MAP/HSD contact and direct `target.select` is rejected |
| No jammer, fresh faction track | normal native icon | normal MAP/HSD contact and selection works |
| No jammer, track becomes stale | position and orientation freeze | position and heading both freeze |
| Medusa jams player | native map icons distort/fade | enemy MAP/HSD/datalink coordinates are absent and page reads `JAMMED` |
| Alkyon jams player | same native jam path | same containment behavior |
| Jam active, own radar still detects a target | native radar list contains target | FCR may show/select the own-radar contact; datalink-only contacts remain suppressed |
| Jam active, enemy radar paints player without datalink | native radar-warning spoke | RWR/MAP warning bearing appears for the native lifetime only |
| Jam ends | native picture recovers as accumulation decays to zero | eligible contacts return; hidden/stale contacts do not flash from an old client frame |
| Multiplayer non-host client | local DynamicMap/CombatHUD behavior | same disclosure rules as host/single-player |

For the reproduction, inspect the live `/stream` alongside the two displays. Record, per relevant
contact, `id`, `x`, `z`, `h`, `dl`, `st`, the RWR block, radar-detected membership, and the native
`jamAccumulation`/`Radar.IsJammed()` states. This distinguishes a contact update from an RWR spoke or
an orientation-only leak.

## Exact single-player local test plan

A small custom mission with no friendly sensors provides the cleanest reproduction. With no friendly
radars, aircraft, ships, or AWACS present, an updating enemy track cannot be explained by a friendly
datalink source.

### Mission setup

Create a mission containing:

- one radar-equipped player aircraft;
- one hostile EW-25 Medusa for the first jamming run;
- one hostile AB-4 Alkyon for a separate jamming run;
- one hostile maneuvering aircraft to act as the tracked contact; and
- no friendly units other than the player aircraft.

Start all aircraft airborne, at a useful radar range, and with clear line of sight. Give the jammer an
aggressive task and place it close enough to detect and track the player. The hostile side must first
track the player before its jammer can affect the player's aircraft.

Run the following configurations without otherwise changing the mission:

| Run | Configuration | Purpose |
|---|---|---|
| Baseline | no active jammer and no friendly sensors | establish native-map/NO XMFD parity |
| Medusa | hostile Medusa active and no friendly sensors | reproduce picture behavior under Medusa jamming |
| Alkyon | hostile Alkyon active and no friendly sensors | reproduce picture behavior under Alkyon jamming |
| Datalink control | one friendly radar asset added | show how a legitimate faction track differs from the isolated runs |

### Test 1: global picture-jamming discrepancy

Display the native tactical map and the NO XMFD MAP page at the same time. Before jamming, confirm
that the hostile contact and its heading agree on both displays. Once jamming begins:

1. Confirm that native tactical-map unit symbols jitter or fade. This is the primary indication that
   `CombatHUD.jamAccumulation` is active.
2. Observe whether NO XMFD continues to show precise, stable contact symbols and coordinates.
3. Do not use the NO XMFD jammer glyph as the only activation check. Its current `pjm` value reflects
   radar-specific jamming and can remain false while the native tactical picture is visibly disrupted.

The finding is reproduced when the native map deliberately obscures a hostile contact while NO XMFD
continues to publish and display its clean, changing coordinates.

### Test 2: stale-contact heading disclosure

Acquire the hostile maneuvering aircraft, then stop observing it by turning off the player radar,
turning away, increasing separation, or placing terrain between the aircraft. Keep the hostile
aircraft turning and wait at least five seconds.

The native contact's position and orientation should both freeze. If the NO XMFD contact position
freezes but its icon continues rotating, NO XMFD is reading the unit's live heading after the track
became stale. With no friendly sensors in the mission, datalink cannot account for the update.

### Test 3: RWR bearing lifetime

Let the hostile radar illuminate the player briefly, then make the emitter turn away, disable its
radar, or destroy it. Time the remaining bearing indication on both displays.

A bearing line without an ordinary hostile contact can be legitimate RWR information. The native
map retains search/track/lock indications for 1/2/4 seconds. The current NO XMFD behavior retains
them for approximately 1.5/3/6 seconds. The test passes after hardening when the NO XMFD indications
expire at the native durations.

### Test 4: raw telemetry capture

Capture the SSE stream during each run from a PowerShell terminal:

```powershell
curl.exe -N --max-time 30 http://127.0.0.1:5005/stream > "$env:TEMP\noxmfd-jam-stream.txt"
```

Record the native map and NO XMFD together for the same interval. Inspect these payload fields:

- `contacts[].id`, `contacts[].x`, and `contacts[].z` for contact identity and position;
- `contacts[].h` for heading;
- `contacts[].dl` for datalink classification;
- `contacts[].st` for stale state;
- `pjm` for NO XMFD's radar-specific jamming indication; and
- `rwr` for radar-warning bearings.

Interpret the capture as follows:

- changing `x/z` values during native picture distortion confirm that clean coordinates remain
  available in raw telemetry;
- `st = 1` with unchanged `x/z` and a changing `h` confirms stale-heading disclosure;
- an `rwr` entry without an ordinary contact is an RWR indication rather than proof of a contact
  leak; and
- continued movement in the datalink-control run may be legitimate if the added friendly sensor is
  still refreshing the faction track.

### Test order and evidence to retain

Run the baseline first, then Medusa, then Alkyon, and finally the friendly-datalink control. Retain:

- the custom mission and its unit/task configuration;
- one synchronized recording of the native tactical map and NO XMFD for each jammer;
- the corresponding 30-second `/stream` captures; and
- the approximate times when native distortion starts, the contact becomes stale, and each RWR
  indication disappears.

Repeat the Medusa and Alkyon runs after each containment implementation. The post-fix expectation is
that suppressed enemy coordinates are absent from `/stream`, stale headings remain frozen, and RWR
indications use the native lifetimes.

## Acceptance criteria

- Raw `/stream` contains no clean enemy MAP/HSD/datalink coordinate that the chosen jamming policy
  suppresses.
- External `target.select` cannot acquire a unit that the current policy would not disclose.
- A stale enemy's heading does not change after its native tracking record stops being observed.
- RWR warning duration matches the native 1/2/4-second behavior.
- Completely unknown enemies never enter normal MAP/HSD/TGT telemetry.
- MAP, HSD, FCR, TGT, and the command path make consistent visibility decisions.
- The behavior is confirmed with both Medusa and Alkyon jamming in a live mission, including a
  non-host multiplayer client when practical.

## Follow-up decision after containment

Once the exploit is closed, decide whether to keep strict suppression or reproduce a degraded native
picture. Any fidelity design must operate before serialization and resist recovering an accurate
position by averaging successive frames. Purely cosmetic client-side jitter is not an acceptable
security fix.
