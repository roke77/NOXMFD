# Targeting Telemetry — Highlight the Player's Targeted Unit(s)

## Context

The web HUD draws every contact in its faction color. There is no way to see which
unit the player has **targeted** in the game. Goal: when a unit is one of the
player's current targets, make its map icon stand out so the locked contact is
obvious.

**Not purely client-side.** Whether a unit is targeted is game state that lives only
in the mod process, so it must be detected in `src/TelemetryReader.cs`, added to the
payload, and rendered by the client. Chain: read targets (server) → flag the unit
(snapshot) → serialize → highlight (client).

> Refreshed after Phase 0 was actually carried out on the build box (this machine),
> using the decompiled source. The biggest unknown is now resolved, and a colour
> assumption in the original plan turned out to be wrong — see Phase 3.

## Phase 0 — RESOLVED: the targeting API (no reflection needed)

`WeaponManager` exposes the player's target list as **public API** (confirmed in
`decompiled/WeaponManager.decompiled.cs`):

```csharp
public class WeaponManager : MonoBehaviour {
    private List<Unit> targetList = new List<Unit>();      // never null
    public List<Unit> GetTargetList() => targetList;       // returns the LIVE list
    public bool CheckIsTarget(Unit candidate) =>           // true if candidate is a target
        targetList.Count > 0 && targetList.Contains(candidate);
    public void AddTargetList(Unit t) { targetList.Insert(0, t); ... }   // index 0 = primary
}
```

- The mod already holds the player's `WeaponManager` in `PushSnapshot`
  (`TelemetryReader.cs:261`, `aircraft.weaponManager`), and `BuildUnits` already
  receives the player `Aircraft`, so `player.weaponManager.GetTargetList()` is
  reachable with no new plumbing.
- `targetList[0]` is the most-recently-added entry (`AddTargetList` inserts at 0) and
  is what the weapon code fires at (`WeaponManager` `Fire`/`LaunchMount` use
  `targetList[0]`). So this list **is** the HUD-designated target(s) — exactly what
  the user means. Multi-lock weapons populate more than one entry, so designing for a
  *set* falls out for free.
- **Identity matching:** `_units` (`TelemetryReader.cs:30`, `Unit[]` from
  `FindObjectsByType<Unit>`) holds the same `Unit` references the target list does, so
  `List<Unit>.Contains(u)` (reference equality for the `Unit` class) is exact. No
  position matching, no fog-of-war edge cases.
- **No reflection, and more robust than the original plan assumed.** These are direct
  compile-time calls against the referenced `Assembly-CSharp`. If a future game update
  removed them the mod would fail to *compile* (caught at build), rather than needing a
  runtime `try/catch` fallback. (Contrast `GetSelectedCmCategory` at
  `TelemetryReader.cs:129`, which reflects only because that member is private.)

## Phase 1 — Server: flag the targeted unit(s)

1. **Snapshot model** (`src/TelemetrySnapshot.cs`): add `public bool Targeted;` to the
   `UnitInfo` struct (`:60-68`). Defaults to `false`.

2. **Flag in `BuildUnits`** (`src/TelemetryReader.cs:345-376`) — no signature change
   needed, since it already takes the player `Aircraft`. Resolve the target list once
   at the top of the method:
   ```csharp
   var targets = player.weaponManager != null ? player.weaponManager.GetTargetList() : null;
   bool hasTargets = targets != null && targets.Count > 0;
   ```
   Then on each `UnitInfo`: `Targeted = hasTargets && targets.Contains(u)`. (`Contains`
   over a ≤32-entry list per unit is negligible; a `HashSet<Unit>` is an option but not
   worth it.) `GetTargetList()` returns the live internal list — read-only here, do not
   mutate; zero allocation.

3. **Cadence note (unchanged risk):** `_units` is rebuilt at 1 Hz (`ScanWorld`) but the
   target list is read at 10 Hz here. A target acquired on a unit not yet in `_units`
   won't highlight for up to ~1 s — acceptable; once it appears the reference match is
   correct. The targeted enemy is by definition tracked, so it already passes
   `TryGetKnownPosition` — no extra FOW handling.

## Phase 2 — Serialize

In `src/TelemetryServer.cs`, `UnitsArray(...)` (~`:307-322`), add a `tg` field to each
contact object alongside `t/x/z/h/f/o/s`. Keep the compact style — emit `"tg":1`/`0`
(or only when true). Update the `AppendFormat` template + args.

## Phase 3 — Client: faction colour + target ring (chosen approach)

**Colour caveat discovered in Phase 0.** The original plan assumed faction colours were
green (friendly) / red (enemy) and proposed "friendly → light blue, enemy → orange".
But the colours come from the game's own HUD (`ReadFactionColors` →
`GameAssets.HUDFriendly/Hostile/Neutral`), and a real capture shows the actual palette
is **friendly = blue (~`#008fff`), enemy = red (~`#ff140a`), neutral = grey**, with the
**player = green `#39ff14`**. So "friendly target = light blue" would collide with the
normal friendly blue. The highlight must contrast with blue, red, grey **and** green.

Keep each targeted icon its faction colour and draw a bright **target ring** around it
— the same visual language as the new crosshair cursor and the game's own target box.
This preserves faction info while clearly signalling "locked".

In `src/ClientPage.cs`:

1. Add a target-highlight constant near the faction colours:
   `const TARGET_COLOR = '#ff8000';   // orange ring — distinct from blue/red/grey/green`
   Orange reads clearly against the dark map and doesn't collide with the faction
   palette. It's only drawn on the map, so it won't be confused with the amber
   `#ffaa00` side-panel highlights. Tune in the preview (below).

2. In `drawOverlay`'s contacts loop, draw the icon in its faction colour exactly as
   today and keep the returned half-extent `r`; then, when `u.tg` is set, draw the ring
   on top — a stroked circle (≈ `r + 4` radius) in `TARGET_COLOR` with a matching
   `shadowBlur` glow, optionally with four short gap ticks for a reticle look. Add a
   small helper `drawTargetRing(cx, cy, radius)` so `drawIcon` stays unchanged.

3. Draw the ring right after its icon so it sits on top of that icon; the player icon
   (drawn last) stays untouched and above everything.

Because the icon keeps its faction `hex`, the hover label is unaffected — a targeted
unit's label still shows its faction colour, with the ring conveying "locked".

## Decisions settled

- **Highlight style:** faction colour + target ring (approach B). Chosen.
- **Ring colour:** a single orange `#ff8000` for any target — faction is still encoded
  by the icon underneath, so no friend/foe colour split is needed. Tunable in preview.
- **Neutral targets:** same ring as any other target (the ring colour is
  faction-independent).
- **Primary vs secondary** (multi-lock): all targeted units get the same ring; add a
  per-target index later only if distinguishing the primary becomes useful.

## What's still challenging

1. **Colour clarity against the real palette** (the live issue) — see Phase 3; easy to
   iterate now that it's previewable.
2. **Scan-vs-push cadence** — brief highlight lag on a freshly acquired target;
   acceptable.
3. **One vs many targets** — the per-unit boolean keeps the wire format simple; primary
   vs secondary isn't distinguished unless we later add an index.
4. The big risk from the original plan — *finding the API* — is **gone**.

## Files to modify

- `src/TelemetrySnapshot.cs` — add `Targeted` to `UnitInfo`.
- `src/TelemetryReader.cs` — resolve target list + set `UnitInfo.Targeted` in `BuildUnits`.
- `src/TelemetryServer.cs` — serialize `tg` in `UnitsArray`.
- `src/ClientPage.cs` — highlight targeted icons in `drawOverlay` (recolour or ring).

## Verification

The client rendering is now **previewable without the game** (this is new — we have the
preview tooling): after `tools/capture_assets.py`, hand-edit a contact in
`preview/assets/manifest.json` to add `"tg":1`, rebuild with `tools/build_preview.py`,
and check the highlight colour/ring against the real captured map and icons. Iterate on
colour there.

End-to-end still needs the game (server side):
- Build (auto-deploys to `BepInEx/plugins`), launch a mission, open `localhost:5005`.
- Lock an **enemy** → its icon highlights; clear the lock → back to red.
- Lock a **friendly** → highlights; clear → back to blue.
- Switch targets → highlight follows the new unit, old one reverts.
- Multi-lock weapon → all designated units highlight.
- Player icon and non-targeted contacts unchanged; with no target, the push loop runs
  normally (no crash — it's a plain method call).
