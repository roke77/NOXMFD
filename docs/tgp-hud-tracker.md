# In-game HUD TGP line-of-sight tracker

## Status

Planning only. Nothing in this document is implemented yet.

This plan covers [issue #59, “(in) HUD: TGP tracker”](https://github.com/roke77/NOXMFD/issues/59).
The issue currently has no description. This document does not backfill or otherwise modify the
ticket; ticket wording remains a separate, explicitly-authorized follow-up.

## Goal

While manual TGP camera control is active, add a symbol to the in-game pilot HUD showing the TGP's
current line of sight.

The symbol must not be limited to the aircraft-fixed HUD combiner area. It must use the same
full-screen, head-relative display space as the game's native centre-view diamond and selectable
unit markers, so it remains useful while the pilot looks left, right, up, down, or almost directly
behind the aircraft.

The agreed initial symbol is four separated corner brackets with an empty centre. The standard
mockup variant is the baseline: medium-sized amber brackets with a small `TGP` label. Exact pixel
dimensions remain subject to an in-game legibility pass.

## Required behavior

- Show the cue only while `TgpManualControl.ManualMode` is active.
- Cover both free Area Track and stabilized Point Track.
- Project the TGP line of sight through the pilot's current main/head camera.
- Allow the cue to appear anywhere in the currently visible head-look viewport, not only over the
  aircraft-forward HUD symbology.
- When the line of sight is outside the current viewport, show a dedicated inset edge cue pointing
  toward it.
- Keep the centre of the bracket symbol empty so the native view-centre diamond, unit marker, and
  terrain remain readable when they overlap.
- Hide immediately when manual mode exits, including a successful Point Track-to-unit-lock handoff,
  aircraft loss, landing-camera takeover, or mission exit.
- Rebuild correctly after an aircraft respawn rebuilds the game HUD.
- Never intercept clicks or native target-selection input.

## What the game already provides

### A full-screen marker layer

`CombatHUD.iconLayer` is public and is the game's normal injection point for screen-space combat
markers. Native unit markers, hit markers, radar warnings, notch indicators, and objective overlays
are parented there.

This is materially different from `FlightHud.GetHUDCenter()`. `HUDCenter` is projected from a point
4 km ahead of the aircraft and carries aircraft-fixed flight and weapon symbology. It moves away
from the centre of the screen when the pilot turns their head. Parenting the TGP cue there would
incorrectly confine it to the forward HUD area.

The TGP cue must therefore be parented to `CombatHUD.iconLayer`, not `HUDCenter`, `HMDCenter`, the
heading tape, or the waypoint cue.

### The native centre-view diamond and unit selection

`CombatHUD.targetDesignator` is the diamond at the centre of the pilot's current view. Native unit
markers are projected with `CameraStateManager.mainCamera.WorldToScreenPoint` and drawn beneath
`iconLayer`. When the player presses the game's Select action, `CombatHUD.TargetSelect` considers
enabled unit markers within 100 screen pixels of the centre designator and selects the highest
priority candidate.

This confirms that `iconLayer` spans the pilot's full current viewport and that assigning projected
screen positions to its children is the supported native pattern.

The new TGP cue is display-only. It does not participate in that 100-pixel selection search and it
does not replace or move `targetDesignator`.

### Head-look camera behavior

`CameraCockpitState` rotates `CameraStateManager.mainCamera` from the player's Pan View and Tilt
View inputs. The same transform also receives TrackIR offsets and the game's padlock view.

The normal cockpit limits observed in the game code are approximately:

- horizontal: -165° to +165°;
- vertical: -65° to +65° normally, with the padlock branch limiting upward view to +45°;
- pilot-view FOV: 20° to 120°.

Because the TGP cue will be reprojected through `mainCamera` every frame, all of these view modes
come along automatically. The cue enters the viewport when the pilot looks toward the TGP line of
sight and moves out again when the pilot looks away.

The final 30° directly behind the aircraft is outside the normal horizontal head-look range. An
edge cue remains appropriate there because the pilot cannot centre that direction without changing
the aircraft's heading.

## Existing NOXMFD foundations

### Authoritative TGP aim direction

`TgpManualControl` already maintains `_panDir`, a normalized world-space direction applied directly
to the TargetCam mount every frame:

- Area Track derives it from the aircraft's current transform and the aircraft-local pan offset.
- Point Track recomputes it toward the tracked `GlobalPosition`, then applies any active nudge.

No telemetry, HTTP request, browser state, target list, or additional world query is required. The
only missing seam is a narrow read-only accessor for the HUD renderer.

Recommended API shape:

```csharp
internal static bool TryGetAimDirection(out Vector3 direction)
```

It should return `false` unless manual mode is active and the stored direction is valid. It must not
expose mutation of the controller's state.

### Waypoint HUD precedent

`HudWaypointCue` proved that NOXMFD can add untextured `Image` objects to the game's HUD, keep them
non-interactive, detect Unity fake-null after a respawn, and rebuild against the new HUD.

Only that lifecycle and construction pattern should be reused. The waypoint cue itself is parented
to the aircraft heading tape and uses aircraft-relative bearing arithmetic, which is the wrong
coordinate space for a head-relative TGP line-of-sight marker.

### Update ordering

`TelemetryReader.Update` calls `TgpManualControl.Tick(dt)`. A new `HudTgpCue.LateUpdate` will
therefore see the manual controller's final aim direction for the current frame, after normal
`Update` work and before presentation completes.

## Line-of-sight semantics

The cue should represent the TGP's angular look direction, not require a terrain or unit hit.

Recommended projection point:

```csharp
Vector3 worldPoint = mainCamera.transform.position + aimDirection * ProjectionDistance;
Vector3 screenPoint = mainCamera.WorldToScreenPoint(worldPoint);
```

Starting from the pilot camera position deliberately removes the small parallax between the TGP
mount and the pilot's eye. The marker answers “which direction is the sensor looking?” in the same
angular frame the pilot sees.

The actual positive projection distance is not a range measurement. It only creates a sufficiently
distant world point along the direction so Unity can project it. A fixed value such as 10 km is
adequate; the selected value should be named to make this intent clear.

This approach has several advantages:

- it works when the TGP points into clear sky;
- it performs no per-frame physics raycast;
- it has no floating-origin state to maintain;
- Area Track and Point Track share exactly the same drawing path;
- it remains stable when the pilot moves their head position through TrackIR.

Point Track still visually stays on its tracked point in the normal case because `_panDir` is
continuously recomputed toward that point. At extremely short range, the angular-direction cue may
differ slightly from projecting the exact surface point from the pilot's displaced eye; that is an
intentional consequence of showing sensor line of sight rather than pilot-eye parallax.

## Projection and edge behavior

### In-view state

Convert the direction to a point in front of or behind the current main camera, then use
`WorldToScreenPoint`. Treat it as in view only when:

- the camera-space depth is positive;
- its X coordinate is within the current screen width;
- its Y coordinate is within the current screen height.

Place the four-corner symbol at that screen position using the same `Transform.position` convention
as native `HUDUnitMarker` objects.

### Off-screen state

When the direction is outside the current viewport:

1. Determine its direction from screen centre in camera-relative space.
2. Intersect that direction with a rectangle inset from the physical screen edges.
3. Place a reduced two-corner/chevron form at the intersection.
4. Rotate it to point toward the off-screen TGP line of sight.
5. Keep the `TGP` label upright.

The game exposes `HUDFunctions.PinToScreenEdge`, but the new cue should not call
`CombatHUD.SetTargetArrow`. The native targeting system owns that shared arrow and enables/disables
it as the target list changes, which would cause ownership conflicts and flicker.

A small NOXMFD-owned clamping helper is preferable to calling the native pinning helper directly:
it can reserve a safe inset, define exact-rear behavior, and be covered by unit tests without the
game runtime.

### Exact-rear degeneracy

A direction exactly 180° behind the current camera has no unique left/right/up/down screen edge.
Near that point, tiny floating-point changes can otherwise make the edge cue jump between sides.

Recommended behavior:

- retain the last stable non-degenerate edge direction while manual mode remains active;
- if no prior direction exists, use a documented bottom-edge fallback;
- clear that cached direction when manual mode exits.

## Agreed visual design

### On-screen marker

Baseline from the approved mockup:

- four separated L-shaped corner brackets;
- approximately 62x62 reference pixels at the baseline UI scale;
- approximately 15-pixel corner arms;
- approximately 2-pixel strokes;
- empty centre with a generous gap;
- amber/yellow colour, distinct from the native green centre diamond;
- subtle glow only, without a filled background;
- small uppercase `TGP` label centred below the brackets.

These are reference dimensions, not constants to ship blindly. The implementation should express
them as a compact group of named constants and tune them at 1080p, higher resolutions, ultrawide,
and the game's UI scaling options.

The amber treatment matches NOXMFD's waypoint cue family and keeps the TGP indicator distinguishable
from green native flight/selection symbology. Like the waypoint cue, this means it intentionally does
not follow `hudColorR/G/B`. If live testing shows poor accessibility against a particular scene or
custom HUD palette, colour configurability can be considered separately.

### Alignment with the native diamond

When the pilot looks directly along the TGP line of sight, the native centre-view diamond sits in
the open middle of the four brackets. Neither symbol needs to be hidden:

```text
    ┌             ┐
          ◇
    └             ┘
          TGP
```

The empty centre is essential. A filled diamond, dot, or crosshair would obscure the native selector
and make it harder to see a unit marker under the same point.

Every generated `Image` and any label must set `raycastTarget = false`.

### Drawing order

The root should sit late enough under `iconLayer` to remain visible above ordinary unit icons, but
it must not alter the sibling order or enabled state of native objects. Reasserting “last sibling”
every frame is unnecessary and could fight native UI creation; set the initial sibling once when
building and verify the result in game.

## Proposed implementation

### 1. Add a read-only aim accessor

Modify `TgpManualControl.cs` to expose the current normalized direction only while manual mode is
active. Keep `_panDir` private and preserve the state machine as the sole writer.

### 2. Add a pure edge-clamping helper

Add a small Unity-independent geometry helper, tentatively `HudDirectionCueMath.cs`, that accepts:

- projected X/Y coordinates relative to screen centre;
- whether the direction is behind the camera;
- screen width and height;
- edge inset;
- optional previous stable direction.

Return:

- whether the marker is on screen;
- final screen X/Y;
- edge-arrow angle;
- the new stable edge direction.

Keep world-to-screen projection in the Unity-facing renderer; only the screen rectangle math needs
to be pure.

### 3. Add `HudTgpCue`

Create `src/plugin/Hud/HudTgpCue.cs` as a mission-scoped `MonoBehaviour`.

Its `LateUpdate` should:

1. Hide and return unless manual mode is active.
2. Hide and return unless the current camera mode is the local player's cockpit view.
3. Resolve `CombatHUD`, `CombatHUD.iconLayer`, and `CameraStateManager.mainCamera`.
4. Rebuild if the cached layer or root is Unity fake-null after a respawn.
5. Read the current manual aim direction.
6. Project it through the current pilot camera.
7. Apply the in-view bracket or off-screen edge state.
8. Avoid allocations, scene scans, reflection, and physics queries.

The explicit cockpit-view gate prevents the pilot-HUD cue from unexpectedly appearing over orbit,
free, encyclopedia, or other external camera modes. If live testing shows native full-screen markers
remain useful and expected in an external mode, expanding that gate can be a later deliberate
choice.

### 4. Build the symbol from primitives

Create one root `RectTransform` under `iconLayer`, four child `Image` arms/corners, and one small
label. No sprite or external art is required.

Build once per HUD lifetime. Visibility changes should toggle the existing root rather than create
and destroy GameObjects repeatedly.

### 5. Register mission lifecycle

Add `HudTgpCue` beside `TelemetryReader`, `HudDeclutter`, and `HudWaypointCue` in
`MissionLifecycle.StartReader`.

On mission stop, destroying `NOXMFD_Runner` invokes the cue's `OnDestroy`, which must clean up any
surviving generated UI objects. If the aircraft HUD was already destroyed, Unity fake-null makes
that cleanup a safe no-op.

### 6. Document the shipped behavior

After implementation and live validation, update:

- `src/plugin/README.md` with the new component and responsibility;
- `docs/tgp-manual-control.md` with the HUD cue behavior and lifecycle;
- this document's status and live-test results.

Updating issue #59's description remains a separate action requiring explicit permission.

## Performance expectations

The active per-frame work is expected to be negligible:

- one read of an already-computed normalized direction;
- one camera projection;
- a few scalar clamp/angle operations;
- one UI position update and occasional state toggles.

The renderer should perform no raycast, target scan, network work, reflection, string formatting,
or per-frame allocation. When manual mode is off, it should reduce to a visibility check and early
return.

This is significantly cheaper than the existing manual TGP image/readback pipeline and should not
need independent rate limiting.

## Automated tests

The pure geometry helper should cover at least:

- screen centre;
- visible left, right, above, and below positions;
- all four off-screen edges;
- all four off-screen corners;
- a direction behind the camera;
- exact and near-exact rear directions;
- previous-direction stabilization across the rear discontinuity;
- edge inset enforcement;
- 16:9, ultrawide, and narrower aspect ratios;
- zero/invalid screen dimensions returning a safe hidden result;
- no NaN or infinity for vertical or almost-zero direction components.

The plugin build verifies access to the public game APIs and compilation of the Unity-facing
component. No Harmony patch or new reflection site should be needed.

## In-game validation matrix

### Visibility and lifecycle

- Manual mode off: no TGP cue.
- Engage manual mode: cue appears immediately.
- Toggle between Area Track and Point Track: cue remains continuous.
- Successful PAD Cursor Select unit-lock handoff: manual cue disappears and the normal game target
  state takes over without a one-frame stale marker.
- Failed unit-lock handoff: manual mode and cue remain unchanged.
- Landing camera takeover: cue disappears with manual mode.
- Aircraft death/respawn: no orphaned marker; cue rebuilds on the new HUD.
- Mission exit/re-entry: no surviving old marker.

### Head-look coverage

- Mouse free-look at the horizontal and vertical limits.
- Joystick/HOTAS Pan View and Tilt View.
- TrackIR rotation and positional movement.
- Native padlock view.
- View-centre reset.
- Pilot FOV at 20°, normal default, and 120°.
- TGP ahead, left, right, overhead, below, and behind.
- Turn the pilot's head until the cue moves from an edge into the viewport and surrounds the native
  centre diamond.

### Visual coexistence

- TGP brackets aligned exactly around `targetDesignator`.
- TGP brackets over a selectable native unit marker.
- TGP brackets near a selected unit's off-screen target arrow.
- TGP brackets near the waypoint heading cue and readout.
- Dense combat scene with multiple unit markers, warnings, and weapon symbology.
- Bright sky, dark terrain, smoke, explosions, and night/IR-adjacent lighting conditions.
- 1080p, higher resolution, ultrawide, and non-default UI scale.

### Camera-mode boundary

- Cockpit mode: cue visible as specified.
- Orbit/free/external modes: cue hidden under the initial cockpit-only policy.
- Return to cockpit while manual mode remains valid: cue restores at the correct projected position.

## Acceptance criteria

- The cue is visible anywhere within the current pilot head-look viewport, not just the
  aircraft-fixed HUD region.
- The cue tracks the TGP line of sight correctly as either the pilot's head or the TGP moves.
- The cue works without a terrain hit, including when looking into sky.
- Point Track remains visually stable over its direction while the aircraft maneuvers.
- Off-screen direction is clear and does not flicker between edges near the rear discontinuity.
- The standard four-corner marker remains readable without obscuring the native centre diamond.
- Native unit selection behaves exactly as before.
- The cue disappears immediately whenever manual mode ends.
- Respawn and mission transitions create no orphaned or duplicated UI objects.
- The implementation creates no recurring garbage allocation and performs no per-frame raycast.
- Full plugin build and geometry tests pass.
- The head-look, TrackIR, overlap, and lifecycle matrix is verified in game before shipping.

## Likely files involved

- `src/plugin/TgpManualControl.cs` — read-only aim-direction accessor.
- `src/plugin/Hud/HudTgpCue.cs` — new Unity-facing renderer and HUD lifecycle.
- `src/plugin/Hud/HudDirectionCueMath.cs` — proposed pure edge-clamping geometry.
- `src/plugin/MissionLifecycle.cs` — attach the mission-scoped cue.
- `tools/tests/HudDirectionCueMathTests.cs` — projection/clamping boundary tests.
- `tools/tests/NOXMFD.Tests.csproj` — include the pure helper and tests explicitly.
- `src/plugin/README.md` — component inventory after implementation.
- `docs/tgp-manual-control.md` — manual-mode HUD behavior after implementation.

No changes should be required in telemetry snapshots, telemetry JSON, HTTP endpoints, the browser
TGP page, keybind definitions, `TgpFeed`, or Harmony patches.

## Alternatives rejected

### Parent the cue to `FlightHud.GetHUDCenter()`

Rejected because that anchor follows aircraft boresight. The cue would leave the pilot's usable
head-look area whenever the pilot looked away from the forward HUD—the exact failure the expanded
requirement rules out.

### Put the cue on the heading tape like the waypoint bug

Rejected because the tape carries horizontal aircraft-relative bearing only. It cannot represent
vertical aim, head-relative position, or a TGP direction behind the current aircraft-forward view.

### Reuse `CombatHUD.targetDesignator`

Rejected because it is the player's native view-centre selector and is shared by weapon HUD states
and unit selection. Moving or restyling it would change game behavior and make the TGP direction
indistinguishable from pilot gaze.

### Reuse `CombatHUD.SetTargetArrow`

Rejected because the native targeting system owns and resets that arrow based on target-list state.
The TGP cue needs independent lifecycle and visibility.

### Clone `ObjectiveOverlay`

Technically feasible but unnecessarily heavy. It brings reflected prefab access, objective text,
distance formatting, and size-indicator behavior that the line-of-sight cue does not need. Four
primitive corner brackets are simpler and visually distinct.

### Project the terrain hit point every frame

Rejected as the primary behavior because Area Track can point into empty sky, a HUD-rate raycast is
unnecessary, and surface-point parallax is not the requested line-of-sight meaning. Existing TGP
overlay data may continue raycasting at its own lower cadence independently.

## Open implementation decisions

1. Tune bracket size, arm length, line width, label spacing, glow, and screen-edge inset in game.
2. Confirm whether the `TGP` text remains legible enough at low resolutions or should hide below a
   scale threshold.
3. Confirm the fixed amber colour against user-custom HUD colours and night scenes.
4. Choose the exact reduced off-screen chevron geometry while preserving the approved four-corner
   in-view symbol.
5. Confirm the cockpit-only camera gate; do not extend to external modes without deliberate testing.
6. Decide the precise exact-rear fallback after observing native head-look motion near ±165°.

## Proposed delivery sequence

1. Implement and test the pure screen-edge geometry.
2. Expose the manual aim direction without changing control behavior.
3. Build the full-screen `HudTgpCue` using the standard bracket design.
4. Validate forward view, free-look, TrackIR, off-screen behavior, and unit-marker overlap.
5. Tune visual constants and rear-edge stabilization from live footage.
6. Run the full build/test suite and repeat lifecycle checks across respawn and mission changes.
7. Update implementation documentation and evidence.
8. Backfill issue #59 only after separate explicit authorization.

