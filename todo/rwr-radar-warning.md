# RWR (Radar Warning Receiver) — planning

## Status

Planning only. No code yet. This documents the game-side data available
for a Radar Warning Receiver display and the work needed to surface it
through the existing telemetry pipeline and a new MFD page (or map
overlay). Findings come from the full decompile in `_scratch/full/`.

## Goal

Give the player a classic RWR scope: a polar (centered-aircraft) display
showing every enemy radar currently painting them, drawn at the correct
bearing, colored by threat state (searching → tracking → locked), with
an emitter symbol/label per contact. This reproduces — on the MFD — the
threat information the game already conveys as the grey/yellow/red lines
on its in-game map.

## The key finding: one event already carries everything

The grey/yellow/red lines on the in-game map are **radar pings** drawn by
`DynamicMap.ShowRadarPing(...)`, fed by a single event on the player's
aircraft:

```csharp
public event Action<OnRadarWarning> onRadarWarning;   // Aircraft.cs:692
```

When any enemy radar sweep paints the player, the server fires a
`ClientRpc` (`Aircraft.RpcGetRadarWarning`, `Aircraft.cs:736`) that raises
this event locally. We read the **same canonical source** the stock HUD's
`RadarWarning` widget and `DynamicMap` consume — not a proxy.

### The payload — `Aircraft.OnRadarWarning` (`Aircraft.cs:33`)

| Field      | Type    | RWR use                                                        |
|------------|---------|----------------------------------------------------------------|
| `emitter`  | `Unit`  | **The threat source.** Position, `unitName`, `definition`, faction. |
| `radar`    | `Radar` | Emitting radar component — range (`RadarParameters.maxRange`), `IsJammed()`. |
| `power`    | `float` | Estimated return signal strength → relative proximity/strength ring. |
| `detected` | `bool`  | The radar has a track on us (skin-paint / search lock).        |
| `isTarget` | `bool`  | **We are this radar's locked target** (highest threat tier).   |

The event is populated in `Aircraft.UserCode_RpcGetRadarWarning` (`Aircraft.cs:2500`):
`detected` comes from `EstimateDetection(radar, out returnSignal)`, `power`
is that `returnSignal`, and `isTarget` from `radarSource.CheckIsTarget(this)`.

## Threat ladder — colors & lifetimes (from `DynamicMap.ShowRadarPing`, `DynamicMap.cs:508`)

The in-game line is drawn from the emitter toward the player icon and
colored by the same three-state ladder we should reproduce:

| State                                   | Color (game)        | Alpha | Line lifetime |
|-----------------------------------------|---------------------|-------|---------------|
| Painted, **not** detected (search sweep)| White (reads grey)  | 0.125 | 1.0 s         |
| `detected` (radar has a track)          | Yellow              | 0.25  | 2.0 s         |
| `isTarget` (locked on us)               | Red                 | 0.5   | 4.0 s         |

The line fades to zero alpha over its lifetime (`RadarMapVis.Refresh`,
`DynamicMap.cs:38`). So: **grey = "being searched," yellow = "tracked,"
red = "locked."** Bearing is simply `emitter.position − player.position`.

These lifetimes (1 / 2 / 4 s) are also our **decay timers** — see below.

## Emitter identity & classification

The `emitter` Unit is fully identifiable, reusing plumbing this mod
already has:

- `emitter.definition.unitName` — already keys the `/icon` endpoint, so an
  emitter symbol reuses existing icon serving.
- `emitter.definition.bogeyName` — the generic label fallback.
- `emitter.definition.typeIdentity` — `{surface, air, missile, radar,
  strategic}` floats (`TypeIdentity.cs`). **High `surface`/`radar` ⇒ a
  SAM/ground radar; high `air` ⇒ an airborne intercept radar.** Lets us
  pick distinct RWR symbols.
- `emitter.radar.RadarParameters.maxRange` — radar's max range (advisory
  ring only; **not** current detection range).
- `Radar.IsJammed()` — jam state, if we want to annotate it.

## Bonus signal — jamming (optional, same widget)

The stock `RadarWarning` widget also renders jamming via a second event:
`Aircraft.onJam` → `Unit.JamEventArgs.jammingUnit` (`RadarWarning.cs:175`).
We could add a jamming indicator to the RWR later; out of scope for v1.

## Integration with this mod

Fits the existing pipeline cleanly:

- The reader already grabs the local aircraft each tick via
  `GameManager.GetLocalAircraft(out Aircraft ac)`
  ([TelemetryReader.cs:166](../src/TelemetryReader.cs)) and already exports
  tracked units (`UnitInfo[]` in
  [TelemetrySnapshot.cs](../src/TelemetrySnapshot.cs)). RWR emitters are
  the same kind of object in the same world space.

### Work item 1 — Reader: subscribe and aggregate

The game gives **discrete ping events**, not a standing "active emitters"
list. The mod must aggregate + decay them itself (exactly as the game's
own HUD does internally).

- On local-aircraft change, `ac.onRadarWarning += handler` (and unsubscribe
  on swap/teardown, mirroring how other per-aircraft hooks are managed).
- The handler runs on Unity's main thread; **buffer** each ping (this mod
  already marshals telemetry to a worker thread, so don't touch shared
  state directly — enqueue and drain on the snapshot tick).
- Maintain a dictionary keyed by `emitter`:
  `{ bearingWorld, power, detected, isTarget, lastSeen }`. Each new ping
  refreshes the entry; a later ping can upgrade/downgrade its tier.
- Each snapshot tick, **expire** entries older than their tier lifetime
  (1 / 2 / 4 s), then serialize survivors.

### Work item 2 — Snapshot + serializer: new `RwrContact[]`

Add to `TelemetrySnapshot` (sketch):

```csharp
internal struct RwrContact
{
    public string Type;       // emitter unitName — keys /icon
    public float  X, Z;       // emitter world position (same space as UnitInfo)
    public byte   Tier;       // 0 search (grey), 1 track (yellow), 2 lock (red)
    public float  Power;      // relative signal strength (ring sizing; not dB)
    public byte   Kind;       // derived from typeIdentity: 0 unknown,1 SAM/ground,2 air
}
```

Bearing is computed client-side from contact position + player position +
`Heading` (already exported), so the server stays geometry-light, matching
the existing floating-origin-resolved contract. Serialize into the SSE
`/stream` frame next to `Units`.

### Work item 3 — Frontend: the RWR display

**Decided: a dedicated RWR scope** (not a map overlay) — it's the feature
users mean by "RWR," and reads as a proper instrument. The map overlay is
demoted to a possible follow-on (see Open questions).

The scope: centered aircraft, polar grid, each contact at its bearing;
color by tier (grey/yellow/red ladder above), ring size by `Power`, symbol
by `Kind`, label from `Type`. The "most-locked" contact (tier 2) gets the
launch-warning emphasis. Bearing is nose-up (rotate by `Heading`) unless we
decide otherwise.

Slots into the MFD page system like AVN/TGP/WPN, including split-view.

## Caveats / decisions to make

1. **Events, not a snapshot.** All aggregation + decay is ours to own. The
   game keeps no public "current emitters" list to poll.
2. **Threading.** `onRadarWarning` fires on the main thread; buffer pings
   and drain them in the snapshot build, like other game-touching reads.
3. **`power` is uncalibrated.** It's `RadarParams.GetSignalStrength(...)` —
   good for *relative* ring sizing, **not** a real dB value or a range.
   Don't label it as distance.
4. **`maxRange` ≠ detection range.** Treat any range ring as advisory.
5. **Bearing frame.** Decide nose-up (rotate by `Heading`) vs. north-up.
   Both are trivial from exported data.
6. **No persistent "known emitter" UI in v1.** Contacts fade on their game
   lifetimes; a "last bearing held" sticky mode is a possible later option.

## Open questions

- A map-overlay RWR (draw the game's emitter→player lines on the MAP page,
  reusing its world→screen transform) as a *later* addition alongside the
  dedicated scope — worth it, or redundant once the scope exists?
- Symbol set for `Kind` — reuse `/icon` art, or draw RWR-style glyphs
  (▽ for SAM, etc.)?
- Audio: the game plays distinct new/existing radar-warning tones
  (`RadarWarning.cs:152`). Do we want any web-side audio cue, or stay
  silent (the player hears the game)?
