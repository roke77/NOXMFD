# Native HUD: time-to-impact estimate

[Issue #67](https://github.com/roke77/NOXMFD/issues/67).

## Status

In-game testing found and fixed two real bugs the design didn't anticipate:

1. **`TargetFocus.Reconcile` could get stuck unfocused.** Locking two or more targets before ever
   pressing Next/Previous Target left focus at `0` indefinitely — the reconcile logic only knew how
   to auto-pick a focus for exactly one lock, not "nothing focused yet, several already exist."
   Fixed in `TargetFocus.cs`; see `docs/tgt-cycle-focus.md`.
2. **`Altitude.radarAlt` is `TMP_Text`, not `UnityEngine.UI.Text`.** The decompile snapshot
   (`_scratch/full/Altitude.cs`) predates the game's own Text→TextMeshPro migration that
   `TgpNativeOverlay.cs` already had to work around for `TargetScreenUI`'s fields — the same
   migration bit this readout too. `HudTtiCue.cs` now reflects/clones a `TMP_Text` instead.

Placement (the offset below `radarAlt`) is still unconfirmed — that needs the label actually
rendering on screen to check, which the TMP fix above should now allow.

## Goal

Show a time-to-impact (TTI) estimate at the bottom right of the native in-game HUD, directly below
the existing radar altitude readout. TTI describes the player's own in-flight guided weapon(s)
currently tracking the locked, focused target ([issue #62](https://github.com/roke77/NOXMFD/issues/62)'s
shared `TargetFocus`) — not a pre-release "if I fired now" estimate.

## What the game already gives us

**Radar altitude's native home**: `Altitude : HUDApp` (`_scratch/full/Altitude.cs`) holds two
private fields, `radarAlt` (`"R[" + altitude + "]"`) and `absAlt` — the decompile shows them typed
`Text`, but the live game build has them as `TMP_Text` (see "Status" above). Neither is public, and
`Altitude` isn't a scene singleton — the same access shape `TgpManualTargetCamAccess.cs` already
solves for `TargetCam`'s internals: cache one reflected `FieldInfo`, find the live instance
(`FindFirstObjectByType`), rebuild if it goes Unity fake-null (the HUD is rebuilt per aircraft spawn,
same as `CombatHUD.iconLayer` in `HudTgpCue.cs`).

**No existing "outgoing weapon TTI" concept — but there is a mirror-image one already shipped**: the
AI's own missile-evasion logic estimates how long an *incoming* missile has left
(`AIPilotCombatModes.EvadeModeRadar`, `_scratch/full/AIPilotCombatModes.cs`):

```csharp
Vector3 vector = missileAlerts[0].transform.position - aircraft.transform.position;
float magnitude = vector.magnitude;
float num = Mathf.Max(Vector3.Dot(-vector.normalized, missileAlerts[0].rb.velocity - aircraft.rb.velocity), 1f);
missileImpactTime = magnitude / num;
```

Straight-line range over closing speed, floored at 1 so a non-closing or barely-closing geometry
can't produce a huge or negative time. The game already accepts this as good enough for a
maneuvering, guided missile — it's not a real intercept simulation — which is exactly the bar this
feature needs to clear, just applied with the roles reversed: our weapon, their target.

**Every guided weapon already tracks its target with a public field**: `Missile.targetID`
(`PersistentID`, `_scratch/full/Missile.cs`). Guided bombs are `Missile` instances too
(`OpticalSeekerBomb` is one of several `MissileSeeker` components a `Missile` can carry), so this
one field covers both weapon families. `Missile.ownerID` identifies who fired it.
`TelemetryReader.BuildPitbull` already runs almost this exact query (`m.ownerID.Id == playerId`),
narrowed further there to `seekerMode == activeLock && GetSeekerType() == "ARH"` for RDR's own
Pitbull display — dropping that narrowing and matching `m.targetID.Id` against `TargetFocus.Id`
instead answers "is this weapon of mine tracking my focused target," for any guided weapon type.

**Enumerating live weapons needs no reflection**: `UnitRegistry.allUnits` (public) and
`UnitRegistry.TryGetUnit(PersistentID?, out Unit)` (already used by `CommandDispatcher`) are both
public API. Only `Altitude`'s label fields need the reflection treatment.

**Unguided weapons (dumb bombs, guns) have no `targetID`** — nothing to compute an intercept from
without a real ballistics simulation. Out of scope (see "Non-goals").

## Design

**`src/plugin/Hud/HudTtiMath.cs`** — pure math, no Unity/game types, same treatment
`TgpManualAimMath`/`HudDirectionCueMath`/`HudWaypointCueMath` already get so `tools/tests` can pin
it without a live game install:

- `TimeToImpact(fromX/Y/Z, toX/Y/Z, relVelX/Y/Z)` — the range/closing-speed formula above, taking
  plain floats (position and *weapon velocity minus target velocity*) rather than `Vector3`. Returns
  `-1` when the two points coincide (nothing to divide by).
- `FormatTti(seconds)` — renders `"M:SS"`.

**`src/plugin/Hud/HudTtiCue.cs`** — the native MonoBehaviour, following `HudTgpCue.cs`'s shape
(build once, refresh every `LateUpdate`, rebuild on a stale reference). Added alongside
`HudWaypointCue`/`HudTgpCue` in `MissionLifecycle.StartReader`, so its lifetime matches theirs
(spawned when a mission starts, torn down when it ends).

- **Build**: find the live `Altitude`, reflect its `radarAlt` label (`TMP_Text`, confirmed in-game
  — see "Status"), and instantiate a new sibling `TextMeshProUGUI` cloned from it (font/size/
  alignment/overflow copied, not reinvented) anchored directly below via a fixed
  `anchoredPosition` offset (`radarAlt`'s own height plus a small gap). Color is
  the one exception — amber (`HudTgpCue`'s own shade), not `radarAlt`'s native green, so the
  readout reads as mod-added rather than blending into the stock HUD.
- **Refresh**: no local aircraft, no focused target (`TargetFocus.Id == 0`), or the focused unit
  gone/disabled → hide. Otherwise scan `UnitRegistry.allUnits` for the player's own live `Missile`s
  whose `targetID` matches; none found → hide (no pre-release estimate — see "Non-goals"). Among any
  found, show the smallest TTI — the one closest to hitting, per the ticket's own "first or closest
  weapon release" wording (see "Open questions" below on that reading).
- Text reads `"TTI " + HudTtiMath.FormatTti(tti)`.

No telemetry/web changes — this lives entirely in the native HUD, unlike issue #62's work.

## Non-goals for this pass

- A pre-release "if I fired now" estimate for the currently selected weapon.
- Unguided bombs or guns (no `targetID` to track).
- True ballistic/intercept simulation — the same range/closing-speed approximation the game's own
  AI already relies on for the mirror-image (incoming-missile) case.

## Open questions

- **"First or closest weapon release"** is read here as: among the player's own in-flight guided
  weapons already tracking the focused target, show whichever is closest to impact (smallest TTI) —
  not literally "whichever was released first" (which could be a slower weapon that would arrive
  later than one fired afterward on a shorter path). Confirm this reading once tested.
- **Placement offset** below `radarAlt` — sign and magnitude are a first guess
  (`radarAlt`'s own height plus 4px), not yet confirmed against a real flight.
- **Display format** — `"TTI M:SS"` chosen for this pass; `Altitude` itself uses a `R[...]` bracket
  convention (`"R[500ft]"`) that a `"TTI[0:07]"` form might match more closely once seen in-game.

## Verification

`dotnet build` (0 errors). `tools/tests/HudTtiMathTests.cs` covers `TimeToImpact`'s closing-speed
projection (head-on, stationary-target, sideways-motion-ignored, non-closing-floors-at-minimum,
coincident-points cases) and `FormatTti`'s rounding. Full `tools/ci-check.ps1` green. Not yet tested
in-game — the HUD placement and the TTI numbers themselves both need a real flight to confirm.

## Related documents

- [TGT cycle focus](tgt-cycle-focus.md) — the shared `TargetFocus` this feature reads.
- [HUD waypoint indicator](hud-waypoint-indicator.md) — the first additive native HUD change, and
  the precedent for "not yet verified in-game" placement caveats.
- [TGP high-quality mode](tgp-high-quality-mode.md) — `HudTgpCue.cs`'s own build/refresh shape,
  which this follows.
