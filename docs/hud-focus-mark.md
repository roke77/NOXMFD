# Native HUD: focus mark on the locked-target symbol

[Issue #68](https://github.com/roke77/NOXMFD/issues/68) follow-up requirement.

## Status

Implemented, not yet tested in-game — expect the same kind of size/offset/color tuning pass
`docs/hud-tti-estimate.md`'s own cue needed after its first live look.

## Goal

An amber "+" over the top-left of the native green diamond/square lock symbol for whichever locked
target is currently *focused* (`TargetFocus.Id`, [issue #62](https://github.com/roke77/NOXMFD/issues/62)) —
the same shared focus [TGT](../man/tgt.md)/[FCR/HSD](../man/rdr.md#when-a-target-is-locked) already
show, and the one [Single Target Weapon Release](single-target-weapon-release.md) fires at. With
several targets locked, this lets a pilot tell which one is focused by looking at the real world
through the canopy, without glancing away to a display.

## What the game already gives us

Every locked target already gets its own on-screen marker: `CombatHUD.CreateMarker` instantiates a
`HUDUnitMarker` (`_scratch/full/HUDUnitMarker.cs`) per unit, wrapping a plain `Image` parented under
`CombatHUD.iconLayer` (public — `HudTgpCue.cs` already anchors to the same layer). Locking a target
calls `HUDUnitMarker.SelectMarker()`, which sets `selected = true`, turns the icon green, and scales
it up (`Vector3.one * 20f`) — the diamond/square this feature attaches to. Every frame,
`HUDUnitMarker.UpdatePosition` re-projects the unit's world position to screen space and either
places the icon there, or — once the target leaves view — hides the icon and hands off to
`CombatHUD.SetTargetArrow`'s off-screen edge arrow instead.

`CombatHUD` keeps a `Dictionary<Unit, HUDUnitMarker>` (`markerLookup`) for exactly this kind of
lookup — resolving a `Unit` to its own on-screen marker — but it's private, so reaching it needs the
same reflected-field treatment `Altitude.radarAlt` already gets in `HudTtiCue.cs`. `iconLayer`
itself is already public and already used by `HudTgpCue.cs`, so no reflection is needed for that
part.

## Design

**`src/plugin/Hud/HudFocusMark.cs`** — a small `MonoBehaviour`, added alongside
`HudWaypointCue`/`HudTgpCue`/`HudTtiCue` in `MissionLifecycle.StartReader`:

- **Refresh**: no focused target (`TargetFocus.Id == 0`) → hide. Otherwise reflect into
  `CombatHUD.markerLookup`, resolve the focused id to a live `Unit` (`TargetUnitLookup.TryResolve` —
  shared with `TargetTtiEstimator`/`WeaponSelectors.FireSingleAtFocused`, all three grew the same
  `UnitRegistry.TryGetUnit`/null/disabled check independently before it was pulled out), and look up
  its `HUDUnitMarker`. Only proceeds if that marker is actually `selected` (a real lock, not just any
  visible unit) **and** its `Image` is currently `enabled` — a locked target that's left view has
  `selected == true` but `image.enabled == false` (`HUDUnitMarker.UpdatePosition`'s off-screen
  edge-arrow branch), and this mark has nothing sensible to sit next to in that case, so it hides too
  rather than pinning to the arrow.
- **Position**: rather than reprojecting the target's world position independently, the mark sits as
  a **sibling** of the marker's own `Image`, under the same `iconLayer`, copying
  `marker.image.rectTransform.position` plus a fixed screen-pixel offset toward the top-left every
  frame. This inherits the marker's own screen tracking, distance-based scaling, and off-screen
  handling for free, instead of re-deriving any of it — the same reasoning `HudTtiCue.cs` used to
  reuse `radarAlt`'s own transform/material rather than computing its own placement from scratch.
- **Rendering**: a `TextMeshProUGUI` showing `"+"` (`TMP_Settings.defaultFontAsset`, no reflection
  needed — unlike `radarAlt`, nothing here needs to match another native element's exact font), same
  amber as every other mod-added HUD cue (`#FFAA00`).
- **Rebuild**: same "HUD tears down and comes back" shape every other cue here uses — a stale
  `iconLayer` reference (aircraft respawn) triggers a clean rebuild.

## Non-goals for this pass

- Scaling the mark's offset/size with the target marker's own distance-based icon size
  (`HUDUnitMarker.customScale`/`distanceScale`) — a fixed pixel offset for now (`ponytail`-tagged in
  the source, with its upgrade path named).
- Any behavior while the focused lock is off-screen (edge-arrow-pinned) — the mark simply hides
  rather than trying to attach to the arrow too.
- Drawing the mark as vector geometry instead of a text glyph — revisit only if the "+" character
  doesn't read cleanly in-game at HUD scale.

## Verification

`dotnet build` (0 errors) — confirms `CombatHUD.iconLayer`/`HUDUnitMarker.image`/`.selected` and the
reflected `markerLookup` field all still match the live game build, the same way every other
reflection-based HUD cue in this codebase self-verifies at compile/first-run time. No new pure logic
to unit-test (this is live `CombatHUD`/`HUDUnitMarker` orchestration, the same shape
`HudTgpCue`/`HudTtiCue` already have with no direct unit tests). Not yet tested in-game — placement
offset and mark size are first-pass guesses, expect to tune both after an in-game look (see "Status").

## Related documents

- [Single Target Weapon Release](single-target-weapon-release.md) — the ticket this was added to as
  a follow-up requirement.
- [TGT cycle focus](tgt-cycle-focus.md) — the shared `TargetFocus.Id` this feature reads.
- [HUD TTI estimate](hud-tti-estimate.md) — the other native HUD cue reading the same shared focus,
  and the precedent for reflecting into a private native HUD field.
