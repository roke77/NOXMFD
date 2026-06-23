# RWR (Radar Warning Receiver) — planning

## Status

**Frontend + backend reader done (compile-verified); live test pending.**
End-to-end path is built:

- Frontend (View 1): full-view page in `MfdPage`, split bare page `RwrPage`,
  the `rwr` broadcast in `ClientPage`, synthetic emitters in
  `tools/preview-mock.js`. Verified in preview.
- Backend reader: `TelemetryReader` subscribes to `Aircraft.onRadarWarning`,
  aggregates emitters with per-tier decay (search 1 s / track 2 s / lock 4 s,
  matching the game's map pings), and serializes the `rwr` array
  (`TelemetrySnapshot.RwrContact` → `TelemetryServer.RwrArray`). Compiles
  against the game assemblies; **not yet runtime-tested** (needs a live game
  while under threat).

`pw` (closeness) is currently a heuristic: `clamp01(1 - dist / radarMaxRange)`
— tune once the live capture shows real values. **Remaining:** live in-game
test + capture (with the "wait-for-rwr" tweak), then View 2 (MAP bearing
lines). Rest of this doc is the original research + plan.

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

**Wire shape (APPROVED + implemented on the frontend).** Each frame carries
an `rwr` array of terse entries, matching the existing frame style
(`contacts` use `t,f,x,z,…`; `targets` use `n,g,r,f`):

```jsonc
rwr: [ { "x": 8476, "z": 5508, "tr": 2, "pw": 0.66, "fr": 0.9, "n": "SA-10", "k": 1 } ]
```

| key | meaning |
|-----|---------|
| `x`,`z` | emitter world position (same space as `contacts`) |
| `tr` | tier: `0` search / `1` track / `2` lock |
| `pw` | signal power `0..1` (→ closeness; higher = nearer centre) |
| `fr` | ping freshness `0..1` (`1` = just pinged, fades to `0` over the tier TTL) → diamond opacity |
| `n` | display name / label (frontend compresses to ≤7-char code for display) |
| `k` | kind: `0` unknown / `1` ground-SAM / `2` air |

The map client (`ClientPage`) converts each entry to a nose-up plot
`{ az, d, tr, n, k }` — `az` = bearing minus heading (deg clockwise from
nose), `d` = `1 - pw` clamped (radius from centre) — and broadcasts
`{type:'rwr', items}` to the shell, exactly like `targets`/`avn`. The
backend just needs to emit the `rwr` array; **all the geometry + rendering
already exist.**

### Work item 3 — Frontend: two views of the same feed

The same `RwrContact[]` feed drives **two** presentations:

1. **Dedicated RWR scope** (Option C, below) — a polar, nose-up instrument.
2. **MAP-page bearing lines** — replicate the in-game look (the grey /
   yellow / red spokes from ownship to each emitter).

Both read the identical data; no extra reader work for view 2.

#### View 1 — dedicated RWR scope

**Mask: Option C — minimal scope** (smoked white on black, nose-up).
Chosen for v1 for its restraint, matching the project's clean MFD
aesthetic. The mask graticule is drawn in **smoked white** — a soft,
slightly translucent off-white (e.g. `rgba(255,255,255,0.55)`), not pure
white — so the static reticle recedes and the colored threat blips (the
grey/yellow/red ladder) read as the foreground. Elements:

- A solid **outer ring** (the scope boundary).
- Two **dashed concentric range rings** inside it (≈0.66R and ≈0.33R) — soft
  proximity references, dashed so they stay subordinate to the solid rim and
  the blips.
- Short **cardinal tick marks** at N/E/S/W on the rim + a small center
  cross — orientation cues.
- An **ownship caret** at the center (small up-pointing aircraft wedge).
- A **heading triangle** at 12 o'clock just outside the ring (top = current
  heading; the display is nose-up, rotated by `Heading`).

Each contact is a **diamond** plotted at its bearing + closeness (no spokes —
they were tried, then dropped as too busy):

- **Position**: **angle** = bearing (nose-up); **radius** = closeness, from
  `pw` (`d = 1 - pw`, clamped), judged against the dashed range rings.
- **Color** = the grey/yellow/red tier ladder (`tr`).
- **Opacity = ping freshness** (`fr`): the diamond is 100% bright on a fresh
  sweep and fades to 0 over the tier lifetime, so it "pings" with the radar.
  A continuously-painting radar stays bright; a single sweep fades and
  expires. (Driven by `fr` from the backend, re-rendered each frame.)
- **Label**: `n`, compressed to a ≤7-char code (`rwrShort`) so it doesn't
  crowd the scope.
- The most-locked contact (tier 2 / red) gets **launch brackets** around the
  diamond.

Slots into the MFD page system like AVN/TGP/WPN, including split-view.

#### View 2 — in-game bearing lines on the MAP page

Reproduce the stock map look (the spokes in the reference screenshot). For
each active contact, draw a line **anchored at the emitter's map position,
pointing back to the ownship icon, length = distance between them** —
exactly `DynamicMap`'s `RadarMapVis` (`DynamicMap.cs:38`). Specifics to
mirror:

- **Color** = the tier ladder: grey (search) / yellow (track) / red (lock).
- **Fade**: alpha decays toward 0 over the contact's lifetime (the game
  lerps `a → 0`), so older pings dim out rather than vanishing abruptly.
- Endpoints come from data we already have: emitter `X,Z` (from
  `OnRadarWarning.emitter.GlobalPosition()`, even for fog-of-war SAMs the
  player can't otherwise see) and ownship `WorldX,WorldZ`. The MAP page
  already owns the world→screen transform, so this is purely a new draw
  pass over `RwrContact[]` — **no new telemetry**.

This is the lowest-effort half of the feature and gives the authentic
in-game presentation; the dedicated scope (View 1) is the value-add on top.

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

- Build order: ship the MAP bearing lines (View 2) first as the quick win,
  then the dedicated scope (View 1)? Or build the scope first as the
  headline feature? (Lean: View 2 first — it's nearly free once the reader
  exists, and validates the data end-to-end before the scope work.)
- Symbol set for `Kind` — reuse `/icon` art, or draw RWR-style glyphs
  (▽ for SAM, etc.)?
- Audio: the game plays distinct new/existing radar-warning tones
  (`RadarWarning.cs:152`). Do we want any web-side audio cue, or stay
  silent (the player hears the game)?
