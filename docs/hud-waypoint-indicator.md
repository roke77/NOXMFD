# In-game HUD waypoint indicator (planning)

## Status

Planning only. No code yet. Branch `hud-waypoint-indicator`, no ticket filed
yet. Every game symbol cited below was read out of `_scratch/full` (the fuller
decompile) during this pass; nothing here has been run in-game.

This is the mod's first **additive** HUD change. Everything `HudDeclutter`
does today is subtractive — find an existing component, disable it, restore
it. Drawing something new into the game's HUD is a different problem, and the
notes below exist because the answer turned out to hinge on data plumbing
rather than on rendering.

## Goal

Put the WPT page's next-waypoint bearing cue onto the **in-game** HUD, so a
pilot flying head-up gets the same steer the MFD gives them without looking
away. The MFD's own compass stays exactly as it is; this is an addition, not
a move.

## What the game already gives us

Three findings, in rough order of how much work each one saves.

### `CombatHUD.iconLayer` is the sanctioned injection point

```csharp
public Transform iconLayer;
```

Public — no reflection needed, unlike `topRightPanel` / `killFeedText`. The
game instantiates its own HUD markers into it (`unitMarker`, `hitMarker`,
`notchIndicatorPrefab`), so parenting our own object there is the same thing
the game does, not a trick.

`CombatHUD.SetTargetArrow(bool enabled, Vector3 position, Vector3 angles)` is
also public and already drives an off-screen direction arrow. We should not
call it — it belongs to the targeting system and gets reset whenever the
target list empties — but it confirms the pattern.

### `ObjectiveOverlay` is a complete, reusable world-space pointer

`ObjectiveOverlay` already does the whole job for objectives: project a world
position with `WorldToScreenPoint`, clamp to the screen edge when the point is
behind the camera or outside the viewport, rotate the pointer to face it, and
render a distance label. Its whole driving surface is public:

```csharp
public void Initialize(Transform iconLayer)
public void UpdateOverlay(MissionPosition.PositionResult result)
public void HideOverlay()
public void SetColor(Color color)
```

The prefab it clones lives on `ObjectiveOverlayManager.overlayPrefab`
(`[SerializeField] private`), so cloning it costs one reflected field read.

The result struct is constructible from outside:

```csharp
public readonly struct ObjectivePosition(GlobalPosition position, float? range)
public PositionResult(Objective obj, ObjectivePosition objPos, float dist, GlobalPosition from)
```

`ObjectivePosition` is a two-field readonly struct we can new up directly, and
`PositionResult`'s public constructor takes it. Two details make a
waypoint-shaped call degrade cleanly rather than break:

- `Objective` may be **null** — `ObjectiveOverlay` already falls back to the
  literal label `"Waypoint"` in that case.
- `Range` may be **null** — the size indicator then scales to 0 and its colour
  multiplies to fully transparent, so the ring simply doesn't draw.

So a first cut is roughly: clone the prefab, `Initialize(CombatHUD.i.iconLayer)`,
and each frame feed it a `PositionResult` built from the active waypoint's
position. Distance formatting (`UnitConverter.DistanceReading`) and text sizing
(`PlayerSettings.overlayTextSize`) come along for free and match the game's
own overlays.

### The top compass is a UV-scrolled tape with simple arithmetic

`FlightHud.compass` is a `[SerializeField] private RawImage` scrolled in
`Update()`:

```csharp
compass.uvRect = new Rect((hdg + 135f) / 360f, 0f, 0.25f, 1f);
```

A `0.25` window over a texture that wraps 360° means the tape shows **90° of
arc**. That makes a heading bug pure arithmetic against the tape's own
`RectTransform`:

- pixels per degree = `tapeWidth / 90`
- bug offset from centre = `relativeBearing * pixelsPerDegree`
- hide the bug when `|relativeBearing| > 45`

`FlightHud` also exposes `statusAnchor` and `HMDCenter` as public `Transform`s
and `GetHUDCenter()` as a public method, so there are anchor points near the
tape that don't need reflection even though `compass` itself does.

## The actual problem: the plugin doesn't know where the waypoint is

Waypoints live entirely in the **browser's** `localStorage`
(`src/web/pages/wpt/waypoints-store.js`). The plugin has no waypoint state at
all — `Keybinds.cs` only pushes *actions* outward
(`TelemetryServer.MapAction("waypoint-next")`) and the browser owns the data
and does the bearing math client-side.

Every data flow in this mod is plugin → browser. This feature needs
browser → plugin, and that direction doesn't exist yet for state (only for
one-shot commands). Two ways out:

### Option 1 — POST the active waypoint back over the command channel

Smallest diff. `CommandDispatcher`'s `CommandEnvelope` is a deliberately flat
union of scalars (`cmd`/`id`/`group`/`index`/`on`/`bind`/`key`/`cid`/`n`/`hz`)
because nested `[Serializable]` objects deserialize unreliably in the game's
Mono runtime — so this would add two `float`s (world x/z, or lat/lon) plus a
name, and a `wpt.active` handler that parks them on a small plugin-side holder.

Costs, both real:

- The in-game HUD silently depends on a browser tab being open and on the WPT
  page having been loaded at least once.
- With two displays open, each has its own route list, so "the active
  waypoint" is ambiguous. The `cid` field already identifies which instance is
  reporting, but *choosing* between two disagreeing instances is a new policy
  decision with no obvious right answer.

### Option 2 — move waypoint storage into the plugin

Bigger change: routes become plugin state, served in the telemetry snapshot,
edited through commands. The WPT page becomes a view over server state rather
than the owner of it.

This makes the HUD indicator standalone (works with no browser open) and
resolves the multi-display divergence as a side effect rather than as a policy
patch. It also breaks the current "routes survive because they're in *your*
browser" property — routes would live and die with wherever the plugin decides
to persist them.

**Recommendation:** Option 2 is the right end state, but it is a much larger
change than the HUD work it unblocks, and it changes an existing shipped
feature's storage model. Prototype against Option 1 (or even a hardcoded
bearing) to prove the rendering first, and treat the storage move as its own
ticket rather than smuggling it in under this one.

## Rendering: two designs

| # | Design | Look | Effort | Notes |
|---|---|---|---|---|
| A | Heading bug on the compass tape | HSI-style caret sliding along the existing tape | Custom UI object + the UV arithmetic above | Matches the original ask most literally; only meaningful within ±45° of the nose |
| B | Cloned `ObjectiveOverlay` | Identical to the game's own objective markers | Near-zero UI work | World-space pointer with edge clamping; works at any bearing including behind |

These are not exclusive — B is a pointer *at* the waypoint, A is a bug *on the
tape*. A real aircraft would have both.

**Sequencing:** build B first. It has almost no UI risk (the prefab is already
styled, positioned, and clamped by the game), so the first increment tests the
data plumbing rather than testing two unknowns at once. Add A afterwards, once
there's a known-good waypoint position arriving on the plugin side.

## Design A's UI, settled

Reviewed as an interactive mockup and approved. The tape, its ticks and
labels, and the green centre caret stay vanilla; the additions are a coloured
bug on the tape and a two-line waypoint readout at the tape's top right
(`WPT 3 · RIDGE` over `12.4 km · brg 045`).

The 90° window is the constraint that shapes the rest. A bug is only
meaningful within ±45° of the nose, which is narrow enough that clamping it
silently at the tape's end would be actively misleading during a turn — the
cue would sit still while the aircraft swings. So the bug has two states:

- **On tape** (`|relBearing| <= 45`): a downward caret at
  `relativeBearing * (tapeWidth / 90)` from centre, with a short stem.
- **Off tape** (`|relBearing| > 45`): the caret becomes a sideways arrow
  pinned at the tape edge it left, pointing the way to turn. A waypoint 130°
  right reads as "turn right, a lot" rather than as a stalled marker.

Colour: **amber**, distinct from HUD green. It separates the cue from the tick
labels immediately behind it, which the green variant competes with. The cost
is that it ignores the player's `hudColorR/G/B` setting, unlike every native
HUD element — accepted, since legibility against the tape matters more here
than matching a colour the player chose for a different purpose.

Two things the mockup does not settle: tick interval and glyph style are baked
into the compass texture and were reconstructed rather than read, and the
whole layout rests on the zero-offset assumption below. Both want an in-game
check before the bug's absolute placement is trusted.

## Lifecycle

The HUD is rebuilt per aircraft spawn — `HUDAppManager` unsubscribes and
`Destroy`s itself on `aircraft.onDisableUnit`. Anything we parent into
`iconLayer` dies with it.

`HudDeclutter` already solves exactly this: a slow re-apply interval plus
Unity fake-null detection on tracked objects to notice a rebuild
(`AnyHiddenDestroyed`). This feature should reuse that pattern, and probably
that component, rather than adding a second MonoBehaviour with its own timer.

## What we deliberately don't do

- **Don't call `CombatHUD.SetTargetArrow`.** It's public and tempting, but the
  targeting system owns it and clears it whenever `targetList` empties, so
  sharing it would make the waypoint cue flicker with target state.
- **Don't modify the game's own `ObjectiveOverlay` instances.** Clone the
  prefab and drive our own; the manager pools and reuses its instances against
  live objective positions every frame.
- **Don't move or restyle the existing compass.** The indicator is additive;
  the tape keeps vanilla behaviour so `HideTopBoxes` and the HMD paths stay
  unaffected.

## Open questions

- The compass texture's zero offset. The `+135f` implies the window's centre
  sits at `hdg + 180` in texture space, so the texture is laid out with a half
  turn of offset (or reversed). The 90°-window figure is what the bug math
  needs and is solid; the exact zero point should be confirmed empirically
  before trusting a bug's absolute placement.
- Whether the cloned overlay needs `SetRaycastTarget(false)`. The objective
  overlays are click targets in some contexts; a waypoint cue that eats clicks
  meant for target selection would be a regression.
- Whether design B's cloned overlay should also be amber, or keep the game's
  objective colouring. Design A is settled (above); B sits among the game's own
  objective markers, so the tradeoff is different there.
- Whether the cue should respect `HideTopBoxes` / the declutter strip, or get
  its own toggle. It's an addition, so a new toggle is likely, but it lives in
  the same visual real estate the declutter flags are about.
- Multi-display arbitration under Option 1 (above), if Option 1 ships.

## Out of scope

- Building any of this. This document is plan-only.
- Moving waypoint storage into the plugin — see Option 2; its own ticket.
- Any change to the WPT page or the MFD compass.
- Route *editing* from the HUD. Read-only cue only.

## Pre-flight before implementing

- Read `src/plugin/HudDeclutter.cs` — the reflect-once/re-apply/restore idiom
  and the respawn detection this feature has to carry over.
- Read `_scratch/full/ObjectiveOverlay.cs` and
  `_scratch/full/ObjectiveOverlayManager.cs` — the clamping math and the
  prefab/pooling shape design B copies.
- Read `_scratch/full/FlightHud.cs` — the compass UV scroll design A depends on.
- Decide Option 1 vs Option 2 before writing any plugin code; the rendering
  work is the same either way, but the plugin's waypoint-state surface is not.
