# Radar & Master Arms — [issue #32](https://github.com/roke77/NOXMFD/issues/32)

**Branch:** `radar-master-arms`. **Status:** planning.

## Goal

Four related cold-start/immersion controls, all opt-in — nothing here changes default behavior
unless the pilot turns it on:

1. **Radar start state** — a persistent setting, **default ON** (today's behavior, untouched). A
   pilot who wants more immersion switches it to OFF, and from then on a freshly-spawned aircraft's
   radar starts off — silently, with no flicker (see **Harmony**, below).
2. **Engine start state** — same shape: a persistent setting, **default ON** (today's behavior).
   Switched OFF, a freshly-spawned aircraft's engine(s) start off — also silently, no startup sound.
3. **Master Arms start state** — same shape again: a persistent setting, **default ON**
   (unrestricted, today's behavior). Switched OFF, a new mod-only "master arms" flag starts off on
   every spawn; a dedicated keybind lets the pilot arm/disarm it in flight. OFF blocks **all**
   weapon/countermeasure fire — the mod's own keybinds *and* the game's own stock
   trigger/mouse/joystick fire path.
4. **Combat mode (A/A / A/G)** — a runtime tri-state, **always starts at ALL** (unrestricted — no
   setting needed, this one has no legacy behavior to preserve). While set to A/A, Cycle Missile
   only reaches air-to-air missiles; while A/G, only everything else. Guns are unaffected either way;
   Cycle Bomb no-ops while in A/A.

### Harmony

This pass adds a Harmony (HarmonyX) dependency — a reversal of the earlier no-Harmony decision, made
once its actual cost was understood: **BepInEx 5 already bundles Harmony as part of its own core
runtime**, so every user who can run this mod at all already has it — no new install step, no new
file, no README change. It unlocks two things the mod's existing read-reflection/call-public-method
approach structurally can't:

- **Silent spawn defaults** — a prefix patch on `Aircraft.OnStartServer()` (ignition) and
  `Radar.Awake()`/`AttachToUnit()` (radar) sets the *initial* SyncVar value directly, before it ever
  syncs to a client, instead of reactively toggling it back off a tick later. This removes the
  engine-startup-sound artifact entirely (see the old "known limitation" below, now resolved) rather
  than just cutting it short.
- **Full Master Arms coverage** — a patch on the weapon-fire and countermeasure-dispense paths blocks
  firing at the source, so OFF blocks *every* way to fire, not just the mod's own keybinds.

The trade-off is real but bounded: Harmony patches are more brittle across game updates than this
mod's usual reflection reads (`apicheck` catches a renamed/retyped member; it does **not** catch a
patch target whose method body changed shape), and there's a small chance of colliding with another
mod patching the same method. Both are accepted for this pass.

Eight new keybinds (KEY page), plus three new persistent on/off settings (KEY page, not bound to a
key — same shape as the existing **Input When Game Unfocused** toggle):

| Bind | Tap | Hold |
|---|---|---|
| **Master Arms ON** | arm (`MasterArms.On = true`) | — (dedicated; no hold behavior) |
| **Master Arms OFF** | disarm (`= false`) | — (dedicated; no hold behavior) |
| **Radar ON** | radar on | — (dedicated; no hold behavior) |
| **Radar OFF** | radar off | — (dedicated; no hold behavior) |
| **Engine ON** | engine on | — (dedicated; no hold behavior) |
| **Engine OFF** | engine off | — (dedicated; no hold behavior) |
| **A/A** | combat mode → A/A | combat mode → ALL |
| **A/G** | combat mode → A/G | combat mode → ALL |

Master Arms/Radar/Engine ended up as **plain dedicated ON+OFF pairs, no tap/hold** — the game
already has its own single-toggle bind for each (Radar, Toggle Engine), so anyone who wants
one-key-does-both already has that; a tap/hold trick on top would only add complexity for no new
capability. **A/A and A/G keep the tap/hold pair** because there's no stock "reset combat mode"
control to fall back on — without the hold, there'd be no keybind way back to ALL at all.

### WPN page — ARM / SAFE controls, always present

The WPN page always gains two new selectable controls, **ARM** and **SAFE** — unconditional,
regardless of the `MasterArmsOnOnStart` setting (even a pilot who leaves the default ON can arm/
disarm mid-flight straight from the WPN page, not just via keybind). Whichever matches the live
Master Arms state renders active (amber box, same as every other engaged control); clicking,
tapping, or SOI-navigating to it and pressing Select sets that state.

- **When `MasterArms.On` is false (SAFE)**, the WPN page additionally draws a full-screen **X**
  over its own content, with a **SAFE** label centered just below the X's middle — a content-level
  visual, not a bezel/nav element, drawn by `wpn.js`/`wpn.css` regardless of layout.

### Extended keybinds page — new content is appended, not interleaved

All eight new binds and three new settings land in one new section, **"Immersion options"**, appended
after everything the KEY page already has, below a separator — existing sections are untouched.

## What already exists to reuse (read before building)

### Radar — patched at the source, not reactively toggled

- `Radar.Awake()`/`AttachToUnit()` unconditionally set `activated = true` — confirmed, no code path
  spawns radar off. A Harmony prefix (or postfix forcing the field) on whichever of the two actually
  fires for a player aircraft's radar sets `activated = RadarOnOnStart` at that point instead — the
  client never observes an on-transition at all when the setting is OFF, so there's nothing to
  detect or react to.
- **The runtime keybinds still use the existing toggle, unchanged** —
  `Aircraft.CmdToggleRadar()` (→ `RpcToggleRadar`) is still the right call for the **Radar ON /
  Radar OFF** keybinds during flight; only the *spawn* default moves to a patch. The keybinds check
  current state and only call `CmdToggleRadar()` when it actually needs to change, same as before.
- No "new aircraft" detection hook is needed for radar's spawn default at all now — the patch fires
  exactly once, at the moment the game itself would have turned it on, so there's no reactive
  `TelemetryReader.cs` polling involved for this part.

### Engine — same patch shape as radar, and it's what actually fixes the startup sound

- `Aircraft.OnStartServer()` explicitly sets `NetworkIgnition = true` — confirmed, no code path
  spawns ignition off. A Harmony prefix here sets it to `EngineOnOnStart` instead, **before** the
  SyncVar ever syncs to any client. This is what actually solves the case discussed earlier:
  `TurbineEngine.Update()` (and `Turbojet`/`Turbofan`) only plays the startup sound on an
  `Ignition` on-transition, and if the client never sees `Ignition` become `true` in the first place,
  that transition never happens — no reactive cleanup needed, no brief sound, fully silent. This is
  strictly better than the reactive `CmdToggleIgnition()`-after-detection approach considered
  earlier, which could only cut the sound short, not prevent it.
- **The runtime keybinds still use the existing toggle, unchanged** — `Aircraft.CmdToggleIgnition()`
  remains the right call for the **Engine ON / Engine OFF** keybinds during flight; only the spawn
  default moves to the patch.
- **Ignition ≠ engine health**, unaffected by this change — `operable`/`hasFuel` are separate
  per-engine concerns; the patch only changes the *initial* value the pilot's switch starts at.

### Master Arms — mod-only flag; now fully enforced via Harmony, including the stock trigger

- Decompiled source has **no** master-arms/safety-switch concept on `Aircraft`/`WeaponManager`.
  `WeaponStation.SafetyIsOn()` exists but is a *ground/gear* safety, unrelated — `MasterArms.On`
  remains a mod-only flag with no game-side equivalent to alias.
- **Full coverage, not just the mod's own keybinds**:
  - `WeaponManager.Fire()` — the game's own single funnel for gun/missile/bomb fire, called both by
    the mod's `WeaponSelectors.Fire()` *and* directly by the game's own stock trigger/mouse/joystick
    input code. A Harmony prefix here, short-circuiting (returning `false`/skipping the original)
    when `MasterArms.On` is false, blocks **every** firing path in one patch — mod keybinds and stock
    input alike. This replaces the narrower `WeaponSelectors.Fire()`-only guard considered earlier;
    that in-mod guard can be dropped once the patch covers the same call from underneath it.
  - `CountermeasureManager.DeployCountermeasure()` — countermeasures don't route through
    `WeaponManager.Fire()`, so this needs its own prefix, same shape, to cover both the mod's
    `Keybinds.Drive(...)` path and the game's own stock countermeasure keybind.
- **State is plain in-memory, not a `ConfigEntry`** — `MasterArms.On` and `CombatMode` shouldn't
  survive a restart; only the *start-state settings* are persistent.

### A/A / A/G — guns and bombs need no new classification; missiles get a hardcoded list

- `WeaponSelectors.cs` already isolates guns and bombs by flag: `IsGun` (`i.gun`), `IsBomb` (`i.bomb
  || i.glideBomb`). Nothing new needed there — guns cycle/fire in any mode; `CycleBomb`/bomb release
  just needs one more guard, no-op while `CombatMode == AA`.
- `IsMissile` is today's catch-all (not gun/bomb/jammer/cargo/troops/sling) — no AA/AG tag exists
  anywhere in `WeaponInfo` or the decompiled source, so this needs a maintained list, not a data
  read. Per the confirmed classification: **air-to-air, exhaustively** —
  `AAM-29 Scythe`, `AAM-36 Scimitar`, `IRM-S2`, `MMR-S3`, `IRM-S1` — and every other weapon
  `WeaponSelectors.IsMissile` already counts as a missile is A/G. New A/A-capable weapons added by
  future game updates land as A/G by default until this list is updated — acceptable per the earlier
  discussion (A/G additions are the common case; the tight A/A list is the one to actively maintain).
- **Naming caveat, must be checked before the list can be trusted**: those five names come from
  `_scratch/units.json` (the Encyclopedia/`UnitDefinition` roster), not from a captured
  `WeaponInfo.weaponName`/`shortName` — the two naming schemes aren't confirmed identical anywhere in
  this repo. Before wiring the match, log real `WeaponInfo` names in an actual session (the existing
  `AssetCapture.TryLogWeaponInfo` diagnostic already does this) and confirm the five strings match
  exactly, adjusting spelling in source if the WPN-page name differs from the Encyclopedia name.

### ARM / SAFE wiring — reuses WPN's existing shell-placed-label + amber-toggle machinery

- **Precedent: WPN's own MAIN/PREV/NEXT.** `nav-model.js` deliberately leaves `NAV.wpn` empty
  because WPN's navigation is *shell-owned pagination*, placed directly by
  `placeWpnNavLabels()`/`renderSplitLabels()` (`mfd.js`) and `f35-wpn-paging.js` (F-35) rather than
  the generic per-page NAV table. ARM/SAFE are the same kind of thing — page-specific labels the
  shell places directly — so they extend this existing mechanism rather than inventing a new one.
  Unlike NEXT (conditional on page count), ARM/SAFE are unconditional — always placed whenever WPN
  is shown, simpler than the earlier conditional-on-setting version of this plan.
- **SOI/select reachability is already generic on CLASSIC** — `soiKeys()` (`mfd.js`) collects every
  bezel key that currently has `dataset.action` set; it isn't a fixed list. Placing ARM/SAFE via the
  same `placeOverlayLabel()` used for MAIN/PREV/NEXT (e.g. on the otherwise-unused `right[1]`/
  `right[2]` slots in full-view WPN) makes them SOI-selectable with **no extra plumbing**.
- **F-35 needs one small extension.** Its `canDo()`/`dispatch()` (`f35.js`) only recognize page
  names, the WPN pager actions, `MAP_ACTIONS`, and `GLASS_ACTIONS` — a new `master-arms.set` action
  needs a case added there (mirroring `MAP_ACTIONS`'s shape) or `canDo()` will treat the new
  `nav-item` as disabled. Once added, F-35's `navItems()` (every non-disabled `.nav-item` in the
  focused portal) reaches it the same generically-collected way CLASSIC's `soiKeys()` does.
- **Active-state styling reuses FLW's pattern, not LYT's.** `.overlay-item.on` (CLASSIC) /
  `.nav-item.on` (F-35) is the existing amber-engaged class. FLW is the closer model than LYT because
  it's a *live toggle reflecting mod/game state*, re-applied every relevant re-render
  (`markFollowLabels`/`markFollow`) rather than a static "current choice" — ARM/SAFE want a
  `markMasterArms()` following that exact shape, run every WPN data tick.
- **Transport — one new field on the existing `'wpn'` message, a new command mirroring
  `declutter.set`.** `TelemetrySnapshot`/`TelemetryServer` gain `MasterArmsOn` (live state) riding
  the same WPN payload `selWeapon`/`softGun` already ride — no new channel, no setting needed here
  since ARM/SAFE are unconditional. Clicking ARM/SAFE calls `sendCommand('master-arms.set', { on })`,
  a new `CommandDispatcher.cs` handler following the `env.on` idiom `declutter.set`/`tgt.laser`
  already use (no-op if already in that state).
- **The full-screen SAFE X is separate from ARM/SAFE themselves** — it's WPN *content*
  (`wpn.js`/`wpn.css`, drawn from the same `masterArmsOn` field once it reaches the iframe), not a
  bezel/nav-item concern, and needs no shell changes beyond the state already riding along for
  ARM/SAFE.

### Keybind registration — eight ordinary binds, three ordinary settings

- **File:** `src/plugin/Keybinds.cs`. **Built, and turned out to need genuinely new plumbing** — the
  plan originally assumed `Jammer`'s `Drive(...)` was a working tap-vs-hold precedent to reuse, but on
  closer inspection it isn't one: Jammer just re-fires the *same* action every frame held, and only
  looks tap/hold-shaped because the underlying game method self-limits to ~0.1s per call. TGT's
  PAD-cursor tap/long-press doesn't transfer either — that's decided client-side in JS off a raw held
  flag, which only works because a page is in the loop; these are pure keybind-to-plugin actions with
  no page involved. What got built instead: a small `PollTapHold(BindDef, onTap, onHold)` helper,
  tracking press-start time on two new `BindDef` scratch fields (`PressStartTime`, `HoldFired`) —
  fires `onTap` the instant the bind is pressed, `onHold` once if still held past 0.35s. Registered as
  no-op held binds (`edge: false`), same shape as the existing cursor-direction binds, with `Poll()`
  driving them directly instead of through the generic per-frame dispatch. **Used for A/A and A/G
  only** — Master Arms/Radar/Engine turned out not to need it (see Goal): they ended up as plain
  dedicated `edge: true` ON+OFF pairs, the same shape as `gear-up`/`gear-down`, since the game's own
  single-toggle bind already covers the "one key does both" case for those three.
- **The three start-state settings are not binds** — no key/joystick capture, just an on/off value.
  Model them after `HudDeclutterConfig.cs` (`ConfigEntry<bool>`, `Browsable = false` so they don't
  duplicate onto the F1 menu) for persistence, surfaced on the KEY page the same bespoke way
  **Input When Game Unfocused** already is (`Keybinds.cs` + a small block in
  `src/web/pages/keybinds/keybinds.js`) — that's the one existing precedent for a KEY-page control
  that isn't a bind.
- **The eight binds need zero web changes** — normal `Def`/`DefFree` rows render automatically from
  `/keybinds-config`, same as every existing bind.

## The plan (proposed)

1. **Add the Harmony reference** (`NOXMFD.csproj`) — a `<Reference Include="0Harmony">` pointing at
   `$(GameDir)\BepInEx\core\0Harmony.dll` (already present in any BepInEx 5 install, same pattern as
   the existing `Assembly-CSharp`/`Mirage`/`Rewired_Core` references). One `Harmony` instance created
   and `PatchAll()`-ed (or targeted `Patch()` calls) once in `Plugin.Awake()`.
2. **Three persistent settings** (`Keybinds.cs`, modeled on `HudDeclutterConfig`) —
   `RadarOnOnStart`, `EngineOnOnStart`, and `MasterArmsOnOnStart` (all default `true`). Surfaced on
   the KEY page as toggle rows, same treatment as **Input When Game Unfocused**.
3. **Runtime state** — `MasterArms.On` (bool) and `CombatMode` (enum: `All` / `AirToAir` /
   `AirToGround`), both plain in-memory, owned wherever `Keybinds.cs` keeps similar mod state.
   `CombatMode` always starts `All`; no patch needed for it, it has no game-side default to fight.
4. **Spawn-default patches** — a prefix on `Radar.Awake()`/`AttachToUnit()` setting `activated =
   RadarOnOnStart`, and a prefix on `Aircraft.OnStartServer()` setting `NetworkIgnition =
   EngineOnOnStart`, both reading the settings from step 2. Replaces the earlier reactive
   `TelemetryReader.cs`-hook idea entirely for these two — no polling, no timing window.
5. **Eight keybinds** (`Keybinds.cs`) — `master-arms-on` / `master-arms-off`, `radar-on` /
   `radar-off`, `engine-on` / `engine-off`, `combat-mode-aa` / `combat-mode-ag`, each `edge: true`,
   tap/hold branching per the table above. Radar/Engine keybinds still call the existing
   `CmdToggleRadar()`/`CmdToggleIgnition()` — only the spawn default changed, not the in-flight
   controls.
6. **Master Arms enforcement patches** — a prefix on `WeaponManager.Fire()` and a prefix on
   `CountermeasureManager.DeployCountermeasure()`, both short-circuiting when `MasterArms.On` is
   false. Covers the mod's own keybinds and the game's stock trigger/mouse/joystick input in one
   patch each, since both call the same two methods underneath.
7. **Combat-mode enforcement** (`WeaponSelectors.cs`, no Harmony needed — this is the mod's own
   cycling logic, not a game method) — `CycleMissile` filters its candidate list by `CombatMode`
   (`All`: unchanged; `AirToAir`: only the five-name list; `AirToGround`: everything `IsMissile`
   flags minus that list). `CycleBomb`/bomb release no-ops while `CombatMode == AirToAir`. Guns
   untouched.
8. **Verify the A/A name list** in an actual session (`TryLogWeaponInfo` output) before relying on
   it — adjust spelling if the live `WeaponInfo` name differs from the Encyclopedia name.
9. **WPN ARM/SAFE** — `MasterArmsOn` added to the `'wpn'` payload
   (`TelemetrySnapshot.cs`/`TelemetryServer.cs`); a `master-arms.set {on}` command
   (`CommandDispatcher.cs`, `env.on` idiom); `placeWpnNavLabels()`/`renderSplitLabels()` (`mfd.js`)
   and `f35-wpn-paging.js` place ARM/SAFE unconditionally, alongside MAIN/PREV/NEXT; a
   `markMasterArms()` toggles `.on`/`.nav-item.on` on whichever matches live state, mirroring
   `markFollowLabels`/`markFollow`; F-35's `canDo()`/`dispatch()` gain a case for the new action.
10. **WPN SAFE overlay** — `wpn.js`/`wpn.css` draw a full-screen X with a centered **SAFE** label
    underneath whenever `masterArmsOn` is false, independent of the ARM/SAFE nav controls above.
11. **KEY page — "Immersion options" section** — all eight binds and three settings from steps 2
    and 5 render under one new `SectionTitle`, appended after every existing section, behind a
    separator.
12. **docs/keybinds-page.md** — document the new section once built.

## Decisions confirmed

- **Combat mode resets to ALL on every new-aircraft spawn** — same as Radar/Engine/Master Arms,
  nothing here carries over between spawns.
- **A/A name-string verification happens during build** — the five names are provisional until then;
  confirm against real `WeaponInfo`/WPN-page names (`TryLogWeaponInfo` output) before step 7/8 relies
  on them, adjusting spelling in source if they differ from the Encyclopedia names.

## Open questions

- **Exact ARM/SAFE key/cell placement** — CLASSIC has `right[1]`/`right[2]` free in full-view WPN
  (weapon rows occupy `left[1..5]`); F-35 needs explicit `cell` hints in `f35-wpn-paging.js`'s `nav`
  array, same as NEXT already gets. Split-pane WPN placement (`renderSplitLabels`'s list branch) also
  needs a slot decision — not yet picked, but mechanically identical to how MAIN/PREV/NEXT already
  place there.
- **`Radar.Awake()`'s separate attach path is unpatched, unconfirmed** — implemented so far
  (`HarmonyPatches.cs`) only patches `Radar.AttachToUnit()`, gated to the local player's own aircraft
  via `GameManager.GetLocalAircraft`. `Awake()`'s path (`attachedUnit` pre-wired before `Awake` runs)
  looks built for scene-placed AI units with a serialized radar reference, not a dynamically-spawned
  player aircraft, but this is unconfirmed — if a player aircraft's radar turns out to attach that way
  instead, `RadarOnOnStart` would silently have no effect. Worth an in-game check the first time this
  ships.
- **Dedicated-server topology gap, confirmed and accepted** — `Aircraft.OnStartServer()` only
  executes on the network SERVER. Host mode (single-player or a player-hosted lobby) is fine, since
  the host process is both client and server. Connecting as a plain client to someone else's
  dedicated `NuclearOptionServer.exe` means the Engine patch never fires for your own aircraft at all
  (that method runs on their machine, not yours, and a mod normally isn't installed server-side) — the
  `EngineOnOnStart` setting silently has no effect in that topology. Accepted: this mod's primary use
  case is single-player/host play; revisit only if dedicated-server support becomes a real ask.
- **Patch fragility across game updates** — accepted cost of adding Harmony (see Goal). Not covered
  by `apicheck`; a future game update that reshapes `WeaponManager.Fire()`/`OnStartServer()`/
  `Radar.AttachToUnit()` could make a patch silently misbehave rather than throw. Worth a manual
  smoke-test of these four patches specifically after every game update, alongside the usual
  `apicheck` run.
