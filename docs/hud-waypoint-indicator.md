# In-game HUD waypoint indicator

## Status

Design A is implemented on branch `hud-waypoint-indicator`, no ticket filed.
Design B (a cloned `ObjectiveOverlay`) is not built. The build is clean and the
web self-checks pass; the cue's absolute placement on the tape is **not yet
verified in-game** — see the zero-offset question at the bottom, which is the
one thing only flying can settle.

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

### Option 1 — POST the active waypoint back over the command channel (shipped)

Two facts make this cheaper than the section below assumes. Waypoints are stored
as `{ name, x, z }` in raw game world coordinates (`wpt-route.js`), the same
floating-origin-corrected frame `TelemetryReader` publishes as `world.x`/
`world.z` — so the payload is two floats and a name with no coordinate
conversion at either end. And `soi.panes` is already a browser → plugin *state*
POST carrying a `cid`, so the direction is established rather than new.

`waypoints-store.js`'s `save()` is the single publishing choke point: every
mutation (edit, W+/W−, R+/R−, import, clear, auto-advance) already routes
through it, so no caller has to remember to publish. The store is loaded by both
shells, not just the MAP and WPT pages, so the publisher is alive whichever page
is on screen.

`HudWaypointState` is static rather than mission-scoped, so a mission restart
doesn't silently drop the cue — the pilot's route hasn't changed just because
the mission reloaded. A game restart does clear it, and the SSE `hello` (which
the shells already handle as `soi-cid`) is the browser's cue to republish.

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

## What is built

| File | Role |
|---|---|
| `src/plugin/HudWaypointState.cs` | Static holder for the browser's active waypoint |
| `src/plugin/HudWaypointCue.cs` | The renderer — chevron on the tape + two-line readout |
| `src/plugin/CommandDispatcher.cs` | `wpt.active` handler; `wx`/`wz` added to the envelope |
| `src/web/pages/wpt/wpt-route.js` | `activeWaypointArgs(collection)` — the pure payload derivation |
| `src/web/pages/wpt/waypoints-store.js` | `publishActive()` off `save()`, `republishActive()` for reconnects |

The cue ships no art. Every element is an untextured `Image` (an `Image` with no
sprite draws a flat tinted quad), so there is no asset to fail to load; the
chevron is two thin bars meeting at their container's origin, and rotating that
container is what aims it. The only borrowed resource is the font, taken off
whichever `UnityEngine.UI.Text` the game already has on screen so the readout
matches the native readouts rather than introducing a second typeface.

Visibility has its own **WPT** toggle in the HUD page's declutter strip. It is
the one flag there that hides something the mod draws rather than something the
game draws, and it keeps the `Hide*` polarity anyway so every toggle in that
strip reads the same way round.

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

Two of the original questions are settled by the build. Every `Image` and `Text`
the cue creates sets `raycastTarget = false`, so the cue can't eat a click meant
for target selection. And visibility is its own **WPT** declutter toggle rather
than riding `HideTopBoxes`, which is about the game's boxed readouts.

- The compass texture's zero offset. The `+135f` implies the window's centre
  sits at `hdg + 180` in texture space, so the texture is laid out with a half
  turn of offset (or reversed). The 90°-window figure is what the bug math
  needs and is solid; the exact zero point should be confirmed empirically
  before trusting a bug's absolute placement.
- Whether design B's cloned overlay should also be amber, or keep the game's
  objective colouring. Design A is settled (above); B sits among the game's own
  objective markers, so the tradeoff is different there. Only matters if B gets
  built.
- Multi-display arbitration. Two displays each own their own route list, and the
  last one to publish defines the cue. The envelope's `cid` identifies the
  reporter but not which reporter is authoritative, and there is no obvious
  right answer — Option 2 dissolves the question rather than answering it.

## Out of scope

- Design B, the cloned `ObjectiveOverlay` world-space pointer.
- Moving waypoint storage into the plugin — see Option 2; its own ticket.
- Any change to the WPT page or the MFD compass.
- Route *editing* from the HUD. Read-only cue only.

## In-game check

Fly a route with the cue visible and confirm:

- The bug sits on the tape tick the readout's `brg` names — this is the
  zero-offset question above, and the one thing the build can't self-verify.
- The bug tracks the tape smoothly through a turn rather than lagging it. It is
  driven off `cockpit.rb.transform.eulerAngles.y`, the same heading `FlightHud`
  scrolls the tape with, so any lag means that assumption is wrong.
- Crossing ±45° swaps the chevron to a sideways arrow pinned at the edge, and
  crossing back restores it.
- Reaching a waypoint advances the cue to the next one, and finishing the route
  clears it.
- The **WPT** declutter toggle hides and restores it.
- It survives an aircraft respawn (the HUD is rebuilt, taking the cue's objects
  with it) and a mission restart (the published waypoint is held statically).
