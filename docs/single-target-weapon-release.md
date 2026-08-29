# Single Target Weapon Release

[Issue #68](https://github.com/roke77/NOXMFD/issues/68).

## Goal

A new keybind, **Single Target Weapon Release**, placed right after **Weapon Release** in the
Weapon Keybinds group. When pressed (held, same as Weapon Release), it releases one missile/bomb
at only the currently *focused* locked target ([issue #62](https://github.com/roke77/NOXMFD/issues/62)'s
shared `TargetFocus.Id`) — even when other targets are also locked.

## Why

The stock Weapon Release trigger (`WeaponManager.Fire()`, `_scratch/full/WeaponManager.cs`)
already handles a single lock correctly — it fires one round at `targetList[0]`. With **two or
more** locked targets, though, it instead fires a staggered *salvo*: one round at *every* locked
target in sequence (`SalvoFire`, `fireInterval * 1.1f` apart). There was no way to release a single
weapon at just one chosen target while keeping the others locked — e.g. engaging the most
threatening contact first, or saving the rest of a limited loadout for follow-up shots.

Issue #62 already gives every locked target a consistent "focused" concept, steppable with
Next/Previous Target and shown in TGT/FCR/HSD (and, per-row, in TGT's own target list —
`docs/hud-tti-estimate.md`'s later extension). That's the natural target for a single-target
release: whichever lock is focused right now, no new UI needed.

## Design

**`src/plugin/Input/WeaponSelectors.cs`** — `FireReleaseSingle(Aircraft ac)`, alongside the
existing `FireGun`/`FireRelease`/`FireJammerPod`. All four share one private `Fire(ac, name, ref
lastFrame, ref switchHold, commit)` helper for the two-stage switch-then-fire arbitration (a press
while the release weapon isn't already selected only switches to it; the next press/hold actually
fires) — `commit` is what stage 2 does, previously hardcoded to `wm.Fire()` for all three existing
binds, now a parameter so `FireReleaseSingle` can commit to something else without duplicating the
arbitration logic. `FireReleaseSingle` reuses `EffectiveRelease`'s selection (so both binds agree on
"which weapon" — pressing one then the other never surprise-switches), but keeps its own
`_relSingleFrame`/`_relSingleSwitchHold` pair rather than sharing `FireRelease`'s: it's a distinct
keybind a pilot can press without having pressed Weapon Release first, so it needs its own
independent press/hold tracking.

`FireSingleAtFocused(ac, wm)` is the new commit action — a single-target counterpart to
`WeaponManager.Fire()` itself. It mirrors that method's guard chain (`currentWeaponStation` null/
`SafetyIsOn`/no weapon stations, `remoteSim`/`Ready()`/`SalvoInProgress`) and its single-target
branches (`WeaponStation.Fire()` for a zero-fire-interval or sling weapon, `LaunchMount()`
otherwise) exactly, but:

- resolves the target to `TargetFocus.Id` instead of `targetList[0]`;
- always takes the single-target path — never `Fire()`'s own `targetList.Count > 1` branch, which
  is precisely the staggered-salvo behavior this ticket exists to bypass;
- no-ops if nothing is focused (`Reconcile`, `TargetFocus.cs`, only ever leaves that `0` when
  nothing is locked at all — genuinely nothing to release at, not an unpicked default) or if the
  focused unit isn't in `wm.GetTargetList()` (focus stale relative to this station's own locks).

Guns are never reached here — `FireReleaseSingle` only ever runs through `EffectiveRelease`, whose
bucket (missile/bomb) excludes guns entirely — so there's no need to replicate `Fire()`'s own
gun/guns-linked special case.

**`src/plugin/Input/Keybinds.cs`** — a new `Def(...)` bind (`"weapon-release-single"`, `Weapon
Keybinds` group, `edge: false` to match Weapon Release's own HOLD-to-keep-releasing behavior),
placed right after `weapon-release`. Unlike `gun-trigger`/`weapon-release`/`jammer-pod`, this one is
**not** added to `IsCombinedFireBind`'s exclusion list — those three are special-cased out of the
ordinary per-frame `Drive` loop so they can be OR'd with a remote/PWA fire state
(`TelemetryServer.GetRemoteFireState`) instead; this bind has no remote counterpart for this pass,
so it drives normally through the same loop every other keybind does.

## Non-goals for this pass

- Remote/PWA keybind support — native keybind only, per the ticket's own "add a new keybind" scope.
- Any TGT/HUD UI changes — the focused-target concept and its display already exist from issues
  #62/#67.
- Firing at a target that isn't locked, or picking a target automatically when nothing is
  focused — both are deliberate no-ops (see "Design" above).

## Verification

`dotnet build` (0 errors). No new pure logic to unit-test: `FireSingleAtFocused`/`Fire`'s
arbitration are orchestration over live `WeaponManager`/`WeaponStation`/`Aircraft` objects, the
same shape the pre-existing `FireGun`/`FireRelease`/`FireJammerPod` already have with no direct
unit tests (only `WeaponSelectorLogic`'s pure cycling/effective-selection math is covered,
`tools/tests/WeaponSelectorLogicTests.cs`, which this change doesn't touch). Full
`tools/ci-check.ps1` green. Not yet tested in-game.

## Related documents

- [TGT cycle focus](tgt-cycle-focus.md) — the shared `TargetFocus.Id` this feature releases at.
- [HUD TTI estimate](hud-tti-estimate.md) — the other feature reading `TargetFocus.Id` per
  locked target, not just the single focused one.
