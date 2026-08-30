# In-game HUD TGP line-of-sight tracker

## Status

Implemented on `main`. The work originated on `tgp-hud-tracker`, was consolidated with the stacked
TGP work, and is now part of the shipped implementation. Core cockpit behavior has been
live-tested, including the
in-view brackets, off-screen caret, reduced visual scale, centre precision dot, and PAD Cursor
Select handoff into the normal game lock. The extended free-look/TrackIR, respawn, resolution/UI
scale, and dense-overlap matrix below remains useful release validation rather than an
implementation blocker.

Current implementation state:

- `HudTgpCue` is attached mission-side and draws on `CombatHUD.iconLayer` only in cockpit mode.
- `TgpManualControl.TryGetAimDirection` exposes the controller's final normalized direction without
  exposing mutation.
- In-view four-corner brackets, 2x2 precision dot, and the independent off-screen edge caret are
  built from untextured Unity UI primitives.
- `HudDirectionCueMath` owns screen-edge placement, behind-camera inversion, and exact-rear
  stabilization; its standalone tests pass.
- The plugin compiles against the installed game assemblies without new warnings.
- The first live cockpit pass reduced the brackets and caret to half their original linear size;
  the revised size and centre dot are confirmed working in game.
- Full TrackIR, respawn, unusual resolution/UI-scale, and dense-overlap coverage is still pending.

This implementation covers [issue #59, “(in) HUD: TGP tracker”](https://github.com/roke77/NOXMFD/issues/59).
Its description was backfilled from the agreed requirements after separate user authorization.

## Goal

While manual TGP camera control is active, add a symbol to the in-game pilot HUD showing the TGP's
current line of sight.

The symbol must not be limited to the aircraft-fixed HUD combiner area. It must use the same
full-screen, head-relative display space as the game's native centre-view diamond and selectable
unit markers, so it remains useful while the pilot looks left, right, up, down, or almost directly
behind the aircraft.

The shipped symbol is four separated amber corner brackets with a small centre precision dot and a
readable `TGP` label. Its dimensions were reduced after the initial in-game legibility pass.

## Required behavior

- Show the cue only while `TgpManualControl.ManualMode` is active.
- Cover both free Area Track and stabilized Point Track.
- Project the TGP line of sight through the pilot's current main/head camera.
- Allow the cue to appear anywhere in the currently visible head-look viewport, not only over the
  aircraft-forward HUD symbology.
- When the line of sight is outside the current viewport, show a dedicated inset edge cue pointing
  toward it.
- Keep the bracket interior clear apart from the small precision dot so the native view-centre
  diamond, unit marker, and terrain remain readable when they overlap.
- Hide immediately when manual mode exits, including a successful unit-lock handoff from either
  Area Track or Point Track,
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
renderer reads it through the shipped narrow read-only accessor:

```csharp
internal static bool TryGetAimDirection(out Vector3 direction)
```

It returns `false` unless manual mode is active and the stored direction is valid, and does not
expose mutation of the controller's state.

### Waypoint HUD precedent

`HudWaypointCue` proved that NOXMFD can add untextured `Image` objects to the game's HUD, keep them
non-interactive, detect Unity fake-null after a respawn, and rebuild against the new HUD.

Only that lifecycle and construction pattern should be reused. The waypoint cue itself is parented
to the aircraft heading tape and uses aircraft-relative bearing arithmetic, which is the wrong
coordinate space for a head-relative TGP line-of-sight marker.

### Update ordering

`TelemetryReader.Update` calls `TgpManualControl.Tick(dt)`. `HudTgpCue.LateUpdate` therefore sees
the manual controller's final aim direction for the current frame, after normal
`Update` work and before presentation completes.

## Line-of-sight semantics

The cue represents the TGP's angular look direction and does not require a terrain or unit hit.

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

When the direction is outside the current viewport, the implementation:

1. Determines its direction from screen centre in camera-relative space.
2. Intersects that direction with a rectangle inset from the physical screen edges.
3. Places a reduced two-arm caret at the intersection.
4. Rotates it to point toward the off-screen TGP line of sight.
5. Keeps the `TGP` label upright.

The game exposes `HUDFunctions.PinToScreenEdge`, but the new cue should not call
`CombatHUD.SetTargetArrow`. The native targeting system owns that shared arrow and enables/disables
it as the target list changes, which would cause ownership conflicts and flicker.

The shipped NOXMFD-owned clamping helper reserves a safe inset, defines exact-rear behavior, and is
covered by unit tests without the game runtime.

### Exact-rear degeneracy

A direction exactly 180° behind the current camera has no unique left/right/up/down screen edge.
Near that point, tiny floating-point changes can otherwise make the edge cue jump between sides.

Shipped behavior:

- retain the last stable non-degenerate edge direction while manual mode remains active;
- if no prior direction exists, use a documented bottom-edge fallback;
- clear that cached direction when manual mode exits.

## Agreed visual design

### On-screen marker

Baseline from the approved mockup:

- four separated L-shaped corner brackets;
- approximately 31x31 reference pixels at the baseline UI scale;
- approximately 7.5-pixel corner arms;
- approximately 1-pixel strokes;
- a small 2x2-pixel centre dot for precise line-of-sight placement;
- otherwise empty centre with a generous gap;
- amber/yellow colour, distinct from the native green centre diamond;
- no filled background;
- small uppercase `TGP` label centred below the brackets.

These are reference dimensions, halved after the first live cockpit pass showed the original
62x62 treatment was too prominent. The implementation keeps them as a compact group of named
constants for further tuning at 1080p, higher resolutions, ultrawide, and the game's UI scaling
options. The off-screen caret is halved by the same factor; the `TGP` label remains readable rather
than scaling all the way down with the geometry.

The amber treatment matches NOXMFD's waypoint cue family and keeps the TGP indicator distinguishable
from green native flight/selection symbology. Like the waypoint cue, this means it intentionally does
not follow `hudColorR/G/B`. If live testing shows poor accessibility against a particular scene or
custom HUD palette, colour configurability can be considered separately.

### Alignment with the native diamond

When the pilot looks directly along the TGP line of sight, the native centre-view diamond and the
tiny TGP precision dot coincide in the open middle of the four brackets. The dot is small enough to
read as a pinpoint within the native symbol rather than a replacement for it:

```text
    ┌             ┐
          ◈
    └             ┘
          TGP
```

The open interior is essential. The 2x2 precision dot marks the exact sensor line of sight without
the obstruction a filled diamond or crosshair would cause.

Every generated `Image` and the label set `raycastTarget = false`.

### Drawing order

The root is placed last under `iconLayer` when built so it remains visible above ordinary unit
icons, without altering the sibling order or enabled state of native objects afterward.

## Implemented design

### 1. Read-only aim accessor

`TgpManualControl.cs` exposes the current normalized direction only while manual mode is active.
`_panDir` remains private and the state machine remains its sole writer.

### 2. Pure edge-clamping helper

`HudDirectionCueMath.cs` is the Unity-independent geometry helper. It accepts:

- projected X/Y coordinates relative to screen centre;
- whether the direction is behind the camera;
- screen width and height;
- edge inset;
- optional previous stable direction.

It returns:

- whether the marker is on screen;
- final screen X/Y;
- edge-arrow angle;
- the new stable edge direction.

World-to-screen projection stays in the Unity-facing renderer; only the screen rectangle math is
pure.

### 3. `HudTgpCue`

`src/plugin/Hud/HudTgpCue.cs` is a mission-scoped `MonoBehaviour`.

Its `LateUpdate`:

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

The renderer creates one root `RectTransform` under `iconLayer`, eight bracket-arm `Image` objects,
one centre-dot `Image`, two caret-arm `Image` objects, and one small label. No sprite or external
art is required.

The symbol is built once per HUD lifetime. Visibility changes toggle the existing root rather than
create and destroy GameObjects repeatedly.

### 5. Register mission lifecycle

`MissionLifecycle.StartReader` adds `HudTgpCue` beside `TelemetryReader`, `HudDeclutter`, and
`HudWaypointCue`.

On mission stop, destroying `NOXMFD_Runner` invokes the cue's `OnDestroy`, which cleans up any
surviving generated UI objects. If the aircraft HUD was already destroyed, Unity fake-null makes
that cleanup a safe no-op.

### 6. Document the shipped behavior

`src/plugin/README.md`, `docs/tgp-manual-control.md`, and this implementation record describe the
shipped component and lifecycle. Issue #59 was updated separately after explicit permission.

## Performance characteristics

The active per-frame work is deliberately small:

- one read of an already-computed normalized direction;
- one camera projection;
- a few scalar clamp/angle operations;
- one UI position update and occasional state toggles.

The renderer performs no raycast, target scan, network work, reflection, string formatting, or
per-frame allocation. When manual mode is off, it reduces to a visibility check and early return.

This is significantly cheaper than the existing manual TGP image/readback pipeline and should not
need independent rate limiting.

## Automated tests

The shipped geometry tests cover:

- screen centre;
- all four off-screen edges;
- an off-screen corner and safe-edge intersection;
- a direction behind the camera;
- exact rear fallback and previous-direction stabilization;
- edge inset enforcement;
- zero/invalid screen dimensions returning a safe hidden result;
- invalid insets and non-finite projected coordinates.

Additional aspect-ratio and near-rear cases can be added if the remaining live matrix exposes a
geometry problem; the pure helper is already isolated for that purpose.

The plugin build verifies access to the public game APIs and compilation of the Unity-facing
component. No Harmony patch or new reflection site was needed.

## In-game validation matrix

### Visibility and lifecycle

- Manual mode off: no TGP cue.
- Engage manual mode: cue appears immediately.
- Toggle between Area Track and Point Track: cue remains continuous.
- Successful PAD Cursor Select unit-lock handoff from Area Track: manual cue disappears and the
  normal game target state takes over without a one-frame stale marker.
- Successful PAD Cursor Select unit-lock handoff from Point Track: same transition and 50 m/large-
  unit acquisition rule.
- Failed unit-lock handoff in either mode: manual mode and cue remain unchanged.
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
- PAD Cursor Select can promote a nearby selectable unit from either Area Track or Point Track.
- Respawn and mission transitions create no orphaned or duplicated UI objects.
- The implementation creates no recurring garbage allocation and performs no per-frame raycast.
- Full plugin build and geometry tests pass.
- The remaining head-look, TrackIR, overlap, and lifecycle matrix is recommended before a release.

## Files involved

- `src/plugin/Tgp/TgpManualControl.cs` — read-only aim-direction accessor.
- `src/plugin/Hud/HudTgpCue.cs` — new Unity-facing renderer and HUD lifecycle.
- `src/plugin/Hud/HudDirectionCueMath.cs` — pure edge-clamping geometry.
- `src/plugin/MissionLifecycle.cs` — attaches the mission-scoped cue.
- `tools/tests/HudDirectionCueMathTests.cs` — projection/clamping boundary tests.
- `tools/tests/NOXMFD.Tests.csproj` — includes the pure helper and tests explicitly.
- `src/plugin/README.md` — component inventory.
- `docs/tgp-manual-control.md` — manual-mode HUD behavior.
- `src/plugin/Input/Keybinds.cs` — documents Area/Point Track selection in the existing PAD Cursor Select
  bind description.

No changes should be required in telemetry snapshots, telemetry JSON, HTTP endpoints, the browser
TGP page, `TgpFeed`, or Harmony patches.

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

## Remaining validation and tuning questions

1. Confirm whether the `TGP` text remains legible enough at low resolutions or should hide below a
   scale threshold.
2. Confirm the fixed amber colour against user-custom HUD colours and night scenes.
3. Confirm the cockpit-only camera gate; do not extend to external modes without deliberate testing.
4. Exercise TrackIR, respawn, unusual aspect ratios, and the exact-rear transition in the extended
   live matrix.

## Delivery record

1. Implemented and tested the pure screen-edge geometry.
2. Exposed the manual aim direction without changing control behavior.
3. Built the full-screen `HudTgpCue` using the approved bracket/caret design.
4. Completed the initial cockpit pass and tuned the visual constants to half scale.
5. Added the centre precision dot and extended PAD Cursor Select to both Area and Point Track.
6. Ran the full build/test suite and deployed each code change to the game folder.
7. Updated the internal implementation documentation.
8. Backfilled issue #59 after separate explicit authorization.
