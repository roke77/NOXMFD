# Power Toggle

[Issue #69](https://github.com/roke77/NOXMFD/issues/69).

## Goal

A new Immersion Options entry, **Power**, alongside Radar/Engine/Master Arm:

- **Power ON / Power OFF** dedicated binds. Default ON.
- **Enable Power on start** toggle (default ON, matching today's behaviour), same shape as
  Enable Radar/Engine/Master Arm on start.

While Power is OFF, the entire in-cockpit HUD is hidden — every symbology element, not a specific
subset — simulating total avionics power loss. Turning Power back ON restores it immediately (once
the aircraft is in cockpit view).

## Why

Radar/Engine/Master Arm already let a pilot simulate partial system loss or a deliberate cold
start. Power goes one step further: no HUD at all, as if the jet has no electrical power to drive
any display or symbology. It's a purely mod-side concept — the game has no such switch — kept
consistent with how [Master Arm](../man/keybinds.md#immersion-options) already works (no native
field to patch, tracked in `ImmersionState` instead of `HarmonyPatches`' spawn-time field writes).

## What the game already gives us

`FlightHud` (`_scratch/full/FlightHud.cs`) owns a single `Canvas` for the entire in-cockpit HUD.
Its public static `EnableCanvas(bool)` just flips `canvas.gameObject.SetActive(...)` — and
`CombatHUD` (`_scratch/full/CombatHUD.cs`) instantiates every weapon/boresight/missile UI as a
child of `FlightHud.i.GetHUDCenter()`, the same canvas tree, so a single `SetActive(false)` already
hides the boxed readouts, compass, weapon panel, kill-feed-in-HUD, unit markers, and this mod's own
added cues (`HudTtiCue`, `HudFocusMark`, `HudTgpCue`, `HudWaypointCue`) in one shot — no per-element
enumeration needed, unlike `HudDeclutter`'s granular per-widget hiding.

The game itself already calls `FlightHud.EnableCanvas` in several places (chase/orbit/free camera
states, pause menu, map screen) — but only as an event-driven one-shot on specific transitions, not
polled every frame. That means our own "hide" has to be reasserted every tick to win over any
native re-enable while Power is off, and our own "restore" has to fire once ourselves when Power
comes back on, rather than waiting for a native transition that might never happen.

## Design

**`ImmersionConfig.cs`**: add `PowerOnOnStart` (default `true`), same persisted/hidden
`ConfigEntry<bool>` shape as the other three on-start settings.

**`ImmersionState.cs`**: add `PowerOn` (default `true`, no native field — same reasoning as
`MasterArmsOn`), reset from `ImmersionConfig.PowerOnOnStart` in `EnsureSpawnDefaults`.

**`Keybinds.cs`**: `power-on` / `power-off` dedicated binds in the Immersion Keybinds section,
right after Master Arm ON/OFF — `DefFree` setting `ImmersionState.PowerOn`, same shape as
`master-arms-on`/`master-arms-off`.

**`src/plugin/Hud/HudPower.cs`** (new) — a small `MonoBehaviour`, added alongside `HudDeclutter` in
`MissionLifecycle.StartReader`:

- While `!ImmersionState.PowerOn`: call `FlightHud.EnableCanvas(false)` every tick (idempotent,
  same "reassert every frame" idiom `HudDeclutter.UpdateMinimap` uses for `DynamicMap.EnableCanvas`).
- Once `PowerOn` flips back true, restore with a single `FlightHud.EnableCanvas(true)` — but only
  once the aircraft is actually in cockpit camera view (`CameraStateManager.i.currentState ==
  cockpitState`), so flipping Power on mid chase-cam doesn't fight the native camera-driven hide.
  Until then, the native camera-state transition into cockpit view will show the HUD itself, same
  as it would for any other pilot.

**Web (KEY page)**: `keybind.set-power-on-start` command, `powerOnOnStart` field on
`/keybinds-config` (`ConfigEndpoint.cs`), and a 4th `ENABLE POWER ON START` row alongside the
existing three in `keybinds.html`/`keybinds.js` — identical shape via the existing
`makeSettingToggle` factory, no new JS abstraction needed.

## Non-goals for this pass

- Any gradual/partial power-loss simulation (dimming, flicker) — a plain instant on/off, matching
  Radar/Engine/Master Arm's own binary behaviour.
- Hiding anything on the mod's own web pages (TGT/RDR/WPN/etc.) — Power only affects the in-cockpit
  HUD; the tablet overlay keeps working so a pilot can still fly off instruments if desired.

## Verification

`dotnet build` (0 errors) confirms `FlightHud.EnableCanvas`/`CameraStateManager.currentState`/
`cockpitState` still match the live game build. No new pure logic to unit-test — this is live
`FlightHud`/`CameraStateManager` orchestration, same shape as `HudDeclutter`'s minimap handling,
which also has no direct unit tests. In-game tested: Power OFF hides the entire HUD (including
this mod's own overlays), Power ON restores it immediately.
