# In-game HUD waypoint indicator

## Status

Design A is implemented and merged to `main`, with route storage fully owned by the plugin
(Option 2, below — not the
Option 1 mirror this doc originally shipped with). Design B (a cloned
`ObjectiveOverlay`) is not built. The build is clean and the web self-checks
pass; the cue's absolute placement on the tape is **not yet verified
in-game** — see the in-game check at the bottom, which is the one thing only
flying can settle.

Two real bugs in the original Option 1 design drove the move to Option 2: (1)
proximity-advance only ran from the WPT page's own `tick()` loop, so flying
past a waypoint while looking at the HUD (or anything else) did nothing; (2)
each browser's `localStorage` was independent, so a route made on one device
was invisible on another, and the HUD cue (mirroring whichever browser last
published) went blank when a second, routeless browser overwrote it. Both
traced to the same root cause: no single process owned route state or ticked
it forward — see "The actual problem" below for the shipped fix.

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

## The actual problem: who owns the waypoint, and who advances it

Originally: waypoints lived entirely in the **browser's** `localStorage`
(`src/web/pages/wpt/waypoints-store.js`). The plugin had no waypoint state at
all — `Keybinds.cs` only pushed *actions* outward
(`TelemetryServer.MapAction("waypoint-next")`) and the browser owned the data
and did the bearing math client-side.

Every other data flow in this mod is plugin → browser. Getting a waypoint onto
the HUD needs browser → plugin state, which didn't exist. Two ways out were
considered — an Option 1 that pushes just the active waypoint (browser stays
the owner), and Option 2, storage moved into the plugin entirely. **Option 2
shipped**, after Option 1 shipped first and broke in exactly the two ways its
own risk section predicted (see Status above).

### Option 2 — the plugin owns the whole route library (shipped)

`RouteStore.cs` is the single source of truth: it holds every route (not just
the active one), persists them to
`BepInEx/config/com.roque.NOXMFD.routes.json` after every edit, and ticks
`AdvanceIfNear` itself once a second from `TelemetryReader`'s existing slow
block — regardless of what page any browser has open, which is what actually
fixes bug (1) above. A `GET /wpt-options` endpoint (mission-independent, like
`/hud-options`) serves the current library to every connected browser; every
browser polls it every 1.2s, the same cadence `hud.js` already uses. Editing
is 14 new `wpt.*` commands (`CommandDispatcher.cs`) — one per action the WPT
page performs, reusing existing `CommandEnvelope` scalar fields wherever the
type fits (`bind` for a route id, `wname` for a display name, `index` for a
waypoint index or a ±1 direction) plus one genuinely new field, `text`, for a
pasted import blob.

No JSON library exists anywhere in this codebase, and Unity's `JsonUtility` is
documented elsewhere as unreliable for nested objects in this Mono runtime —
so reading the persisted file (and a pasted import) uses a new,
narrowly-scoped reader, `JsonLite.cs` (`ponytail:` comment inside naming its
ceiling — two known shapes only, not a general parser).

This resolves both original costs directly: proximity-advance no longer
depends on any page being open, and there is exactly one route library, so
"the active waypoint" is never ambiguous between displays. No migration of
pre-existing browser-local routes was done (explicit decision) — the storage
model changed, and anything only ever saved to the old `localStorage` key is
simply not carried forward.

**Perf fix, 2026-08-18: one poller per device, not one per document.** Shipping
Option 2 as originally built had every document that loaded
`waypoints-store.js` — the shell, plus each open MAP/WPT pane or portal —
running its own independent 1.2s poll loop against `/wpt-options`. A
profiler-plus-request-count investigation (temporary `[NOXMFD-PERF]` logging,
since removed) measured roughly 3.2x the request rate one poller alone should
produce, matching a reported MAP-page stutter. `RouteStore.Save` itself was
never the cause (0ms on every call, disk write included).

Fixed by gating: only the top window (`window === window.top`) runs the
recurring poll now. An embedded MAP/WPT page instead posts a
`wpt-routes-request` message to its parent once on load and just listens —
the shell replies immediately (a freshly loaded iframe needs to explicitly
catch up, since the shell only pushes on a real change) and broadcasts every
subsequent change via `postMessage`, mirroring the pattern every other
telemetry slice already uses in both shells:

- **Classic shell** (`mfd.js`): three explicit forward targets —
  `forwardWptRoutesToPanes`/`ToFrame` (split panes, `#page-frame`) plus
  `forwardWptRoutesToMap` for `mapFrame`, the always-loaded MAP tap, which
  is separate from both and easy to forget (caught in testing — the map's own
  route overlay silently went stale without it).
- **F-35 shell** (`f35.js`): reuses the existing `slices`/`onSlice`/
  `PAGE_FEEDS` relay wholesale — `'wpt-routes'` was simply added to `map`'s and
  `wpt`'s feed lists, so a freshly loaded portal catches up through the same
  `forwardToPage()` mechanism every other feed already relies on, no bespoke
  catch-up path needed.

Each mutator's own `.then(poll)` is untouched — `poll()` itself still runs a
real fetch, called directly by whichever document made the edit, so that
document's own change still shows up in well under a second. Only the
*recurring background* loop is gated; a sibling pane on the *same* device
picks up an edit via the shell's push (near-instant once the shell's own next
change-triggered broadcast fires), while a genuinely different *device* still
learns about it on the shell's own 1.2s poll cycle, same as before.

### Option 1 — POST the active waypoint back over the command channel (shipped first, then superseded)

The browser stayed the owner; only the single active waypoint was pushed
whenever `waypoints-store.js`'s `save()` ran. Cheap to build — `wx`/`wz` are
the same floating-origin-corrected world frame `TelemetryReader` already
publishes, and `soi.panes` was already a browser → plugin *state* POST, so
the direction wasn't new. It shipped, and its own documented costs then
happened for real: the HUD cue depended on some browser having loaded WPT at
least once, and two displays' independent route lists made "the active
waypoint" ambiguous whenever they disagreed. `HudWaypointState.cs`, the
`wpt.active` command, and `waypoints-store.js`'s `publishActive`/
`republishActive` implemented this and are now deleted — Option 2 doesn't
need a browser to push anything, so there was nothing to keep them for.

## What is built

| File | Role |
|---|---|
| `src/plugin/Stores/RouteStore.cs` | The navigation library: route and steer-point storage, every mutation, disk persistence, route-only `AdvanceIfNear`, and both squad-sharing lifecycles (see `docs/steer-points.md`) |
| `src/plugin/JsonLite.cs` | Minimal JSON reader — the persisted file and pasted imports; `SelfCheck()` is its runnable check, called once from `Plugin.Awake` |
| `src/plugin/Hud/HudWaypointCue.cs` | The renderer — chevron on the tape + two-line readout, reads the effective route waypoint or steer point from `RouteStore` in-process |
| `src/plugin/CommandDispatcher.cs` | The `wpt.*` route/waypoint/steer-point mutation and squad-share command family |
| `src/plugin/Http/TelemetryServer.cs`, `src/plugin/Http/TelemetryHttpRouter.cs` | `GET /wpt-options` — the navigation library, mission-independent |
| `src/web/pages/wpt/waypoints-store.js` | Fetch/poll (`/wpt-options`, top window only) + `POST /command` client |
| `src/web/pages/wpt/wpt-route.js` | Trimmed to display-derivation only — mutation logic moved to `RouteStore.cs` |
| `src/web/shell/classic/mfd.js`, `src/web/shell/f35/f35.js` | Relay `/wpt-options` data to embedded MAP/WPT pages — see the perf fix above |

The cue ships no art. Every element is an untextured `Image` (an `Image` with no
sprite draws a flat tinted quad), so there is no asset to fail to load; the
chevron is two thin bars meeting at their container's origin, and rotating that
container is what aims it. The only borrowed resource is the font, taken off
whichever `UnityEngine.UI.Text` the game already has on screen so the readout
matches the native readouts rather than introducing a second typeface.

Visibility follows the route and nothing else: the cue draws while a route is
active and has a waypoint left to fly, and disappears when there is no active
route or the route is complete. No toggle of its own — the browser already sends
an explicit off payload for both of those states.

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

Colour: **amber** (`#FFAA00`), distinct from HUD green. It separates the cue
from the tick labels immediately behind it, which the green variant competes
with. The cost is that it ignores the player's `hudColorR/G/B` setting, unlike
every native HUD element — accepted, since legibility against the tape matters
more here than matching a colour the player chose for a different purpose.
Same value as the web frontend's `--no-amber` token (`theme.css`) — WPT's own
compass needle already used it, and MAP's active-waypoint marker was changed
to match, so the cue and both web pages read as one colour scheme rather than
three unrelated yellows.

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

Every `Image` and `Text` the cue creates sets `raycastTarget = false`, so it
can't eat a click meant for target selection — one of the original questions,
settled by the build.

- The compass texture's zero offset. The `+135f` implies the window's centre
  sits at `hdg + 180` in texture space, so the texture is laid out with a half
  turn of offset (or reversed). The 90°-window figure is what the bug math
  needs and is solid; the exact zero point should be confirmed empirically
  before trusting a bug's absolute placement.
- Whether design B's cloned overlay should also be amber, or keep the game's
  objective colouring. Design A is settled (above); B sits among the game's own
  objective markers, so the tradeoff is different there. Only matters if B gets
  built.
## Out of scope

- Design B, the cloned `ObjectiveOverlay` world-space pointer.
- Migrating routes that only ever existed in a browser's old `localStorage`
  from before Option 2 — explicit decision, see "The actual problem" above.
- Any change to the WPT page's layout or the MFD compass.
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
- Reaching a waypoint advances the cue to the next one — with **no WPT or MAP
  page open on any browser** — and finishing the route clears it. This is the
  bug (1) fix: the plugin ticks it, not any page.
- It survives an aircraft respawn (the HUD is rebuilt, taking the cue's objects
  with it) and a mission restart (`RouteStore` isn't mission-scoped).

Route-storage checks (Option 2):

- Create/rename/delete a route and add waypoints on one browser; confirm it
  appears on a second, genuinely different device's browser within ~1.2s with
  no reload (this is the bug (2) fix) — and on a second PANE/PORTAL on the
  *same* device, confirm it updates via the shell's push rather than that
  pane running its own poll (see the perf fix above).
- Full **game restart** (not just mission restart) — confirm routes on disk
  (`BepInEx/config/com.roque.NOXMFD.routes.json`) survive and reload.
- Toggle the active route from one browser mid-flight; confirm the HUD cue
  switches with no action needed from any other browser.
- Import/export round-trip a route between two browsers; paste garbage into
  the import panel and confirm an inline error shows without a round trip to
  the plugin.
- Corrupt or delete the routes JSON file and restart — confirm a logged
  warning and an empty route list rather than a failed plugin `Awake()`.
