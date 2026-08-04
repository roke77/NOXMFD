# Radar & Master Arms — [issue #32](https://github.com/roke77/NOXMFD/issues/32)

**Branch:** `radar-master-arms`. **Status:** planning.

## Goal

Four related cold-start/immersion controls, all opt-in — nothing here changes default behavior
unless the pilot turns it on:

1. **Radar start state** — a persistent setting, **default ON** (today's behavior, untouched). A
   pilot who wants more immersion switches it to OFF, and from then on a freshly-spawned aircraft's
   radar starts off.
2. **Engine start state** — same shape: a persistent setting, **default ON** (today's behavior).
   Switched OFF, a freshly-spawned aircraft's engine(s) start off, and the pilot lights them up
   manually.
3. **Master Arms start state** — same shape again: a persistent setting, **default ON**
   (unrestricted, today's behavior). Switched OFF, a new mod-only "master arms" flag starts off on
   every spawn; a dedicated keybind lets the pilot arm/disarm it in flight. OFF blocks the mod's own
   weapon and countermeasure keybinds.
4. **Combat mode (A/A / A/G)** — a runtime tri-state, **always starts at ALL** (unrestricted — no
   setting needed, this one has no legacy behavior to preserve). While set to A/A, Cycle Missile
   only reaches air-to-air missiles; while A/G, only everything else. Guns are unaffected either way;
   Cycle Bomb no-ops while in A/A.

Eight new keybinds (KEY page), plus three new persistent on/off settings (KEY page, not bound to a
key — same shape as the existing **Input When Game Unfocused** toggle):

| Bind | Tap | Hold |
|---|---|---|
| **Master Arms ON** | arm (`MasterArms.On = true`) | disarm (`= false`) |
| **Master Arms OFF** | disarm (dedicated; no hold behavior) | — |
| **Radar ON** | radar on (if currently off) | radar off |
| **Radar OFF** | radar off (dedicated; no hold behavior) | — |
| **Engine ON** | engine on (if currently off) | engine off |
| **Engine OFF** | engine off (dedicated; no hold behavior) | — |
| **A/A** | combat mode → A/A | combat mode → ALL |
| **A/G** | combat mode → A/G | combat mode → ALL |

The dedicated OFF binds exist so disarming/killing the radar or engine never *requires* a hold — a
pilot who finds holding awkward mid-fight has a direct one-press option too. A/A and A/G don't need
a third "ALL" bind for the same reason: holding either one already gets there.

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

### Radar — the game already has the toggle; only the start-state push is new

- `Aircraft.CmdToggleRadar()` is the real, network-safe toggle — server RPC pair
  (`CmdToggleRadar` → `RpcToggleRadar`) flipping `Radar.activated`, the same path the stock `Radar`
  keybind/radial menu use. `Unit.HasRadarEmission()` (`return radar is Radar r && r.activated;`) is
  what `TelemetryReader.cs` already reads into `TelemetrySnapshot.RadarOn` — so the mod already has
  both the read and the exact call needed to write it.
- **"New aircraft" detection already has a house pattern** — `TelemetryReader.cs` caches a per-
  airframe reference and resets state via a `ReferenceEquals(ac, _cached)` guard run every
  `PushSnapshot()` tick: see `EnsureRwrSubscription` and `EnsureAfterburnerCache`. A new
  `EnsureSpawnDefaults(Aircraft ac)` on that same shape is where all three start-state resets
  (radar, master arms, combat mode) belong — one hook, not three.
- The **Radar ON / Radar OFF** keybinds are direct, not a blind toggle like the stock bind — they
  check current state and only call `CmdToggleRadar()` when it actually needs to change, so pressing
  "ON" while already on (or "OFF" while already off) is a clean no-op.
- **Confirmed: the game always spawns radar on** — `Radar.Awake()`/`AttachToUnit()` unconditionally
  set `activated = true`, no code path spawns it off. So "toggle off if currently on" is the correct,
  safe polarity for a reactive post-spawn push — it can never wrongly turn radar on.
- **Confirmed: reactive-toggle timing is safe** — by the time `GameManager.GetLocalAircraft` returns
  the new aircraft, Mirage network authority is already established (ownership is conveyed with the
  spawn message itself, before `OnStartClient`/`OnStartAuthority` fire). `Keybinds.cs`'s existing
  `SetGear` call already relies on exactly this — gated only by `GetLocalAircraft` returning
  non-null, no extra readiness check — so `EnsureSpawnDefaults` needs none either.
- **No artifact on the radar side** — `Radar.cs` has no sound/visual effect tied to the on→off
  transition, only continuous steady-state scan behavior. A reactive toggle here is clean.

### Engine — same shape as radar, one flag drives every engine

- `Aircraft.CmdToggleIgnition()` is the network-safe toggle (Cmd only, no companion Rpc — the
  `NetworkIgnition` SyncVar replicates itself), flipping `Aircraft.Ignition`. Every engine component
  (`TurbineEngine`/`Turbojet`/`Turbofan`) reads that same one flag in its own `Update()`, so a
  multi-engine aircraft needs no per-engine handling — one call covers all of them, same as the
  stock `"Toggle Engine"` bind (`PilotPlayerState.cs`) and the radial menu already do.
- **Already read** — `TelemetryReader.cs` already sets `TelemetrySnapshot.Ignition =
  aircraft.Ignition`, so no plugin/snapshot change is needed for the read side, only the write
  (`CmdToggleIgnition()`) and the same direct-not-blind ON/OFF check the Radar binds use.
- **Ignition ≠ engine health** — `operable`/`hasFuel` are separate per-engine concerns (damage,
  fuel starvation); `Ignition` is only the pilot's switch, and toggling it can't revive a destroyed
  engine. Turning it back on after being off for a while re-spools over the engine's own
  `spoolUpTime`/`startupTime` — normal stock behavior, nothing the mod needs to simulate or guard
  against itself.
- **Confirmed: the game always spawns ignition on** (`Aircraft.OnStartServer()` explicitly sets
  `NetworkIgnition = true`), so — same as radar — "toggle off if currently on" is the correct
  polarity.
- **Known limitation, accepted: a brief startup sound plays regardless.**
  `TurbineEngine.Update()` (and `Turbojet`/`Turbofan`) starts the engine startup audio on the
  aircraft's very first client-side `Update()` frame once `Ignition` is true — which it already is
  at spawn, well before the mod's next ~100ms telemetry tick can react and call
  `CmdToggleIgnition()`. A reactive, no-Harmony toggle can cut the engine back off almost
  immediately, but **cannot prevent that initial startup sound from playing once** on every spawn,
  even with "Engine ON on start" set to OFF. Fixing this at the source would need a Harmony patch
  before `Aircraft.OnStartServer`/`Radar.Awake` sync the SyncVar — explicitly out of scope for this
  pass (no Harmony), same call as the Master Arms stock-trigger gap. Documented here as a known,
  accepted limitation, not a blocker.

### Master Arms — no game concept to hook; this is a mod-only flag, no Harmony

- Decompiled source has **no** master-arms/safety-switch concept on `Aircraft`/`WeaponManager`.
  `WeaponStation.SafetyIsOn()` exists but is a *ground/gear* safety, unrelated.
- **This mod has no Harmony dependency anywhere** and this pass isn't adding one. That means Master
  Arms enforcement covers **the mod's own fire/countermeasure keybinds only**:
  - `WeaponSelectors.Fire()` — the single funnel `FireGun`/`FireRelease`/`FireJammerPod` all go
    through before `wm.Fire()`. One `if (!MasterArms.On) return;` at the top gates all three.
  - `Keybinds.Drive(ac, CountermeasureManager, category)` — same guard, since countermeasures don't
    route through `WeaponSelectors.Fire()`.
  - The **stock trigger/mouse/joystick fire path is explicitly out of scope for this pass** —
    `WeaponManager.Fire()` is called directly by the game's own input code, not through anything this
    mod owns, and reaching it would need a Harmony prefix patch. Left as a known gap, not a blocker.
- **State is plain in-memory, not a `ConfigEntry`** — `MasterArms.On` and `CombatMode` shouldn't
  survive a restart; only the two *start-state settings* (below) are persistent.

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

- **File:** `src/plugin/Keybinds.cs`. Tap-vs-hold branching within one bind already has a working
  precedent — `Jammer`'s `Drive(...)` distinguishes a tap from a hold for its own purposes — so the
  ON/A-A/A-G binds' "tap does X, hold does Y" behavior isn't new plumbing, just a new pair of
  branches. The dedicated OFF binds are plain `edge: true` binds, the same shape as `gear-up`/
  `gear-down`.
- **The three start-state settings are not binds** — no key/joystick capture, just an on/off value.
  Model them after `HudDeclutterConfig.cs` (`ConfigEntry<bool>`, `Browsable = false` so they don't
  duplicate onto the F1 menu) for persistence, surfaced on the KEY page the same bespoke way
  **Input When Game Unfocused** already is (`Keybinds.cs` + a small block in
  `src/web/pages/keybinds/keybinds.js`) — that's the one existing precedent for a KEY-page control
  that isn't a bind.
- **The eight binds need zero web changes** — normal `Def`/`DefFree` rows render automatically from
  `/keybinds-config`, same as every existing bind.

## The plan (proposed)

1. **Three persistent settings** (`Keybinds.cs`, modeled on `HudDeclutterConfig`) —
   `RadarOnOnStart`, `EngineOnOnStart`, and `MasterArmsOnOnStart` (all default `true`). Surfaced on
   the KEY page as toggle rows, same treatment as **Input When Game Unfocused**.
2. **Runtime state** — `MasterArms.On` (bool) and `CombatMode` (enum: `All` / `AirToAir` /
   `AirToGround`), both plain in-memory, owned wherever `Keybinds.cs` keeps similar mod state.
3. **`EnsureSpawnDefaults(Aircraft ac)`** (`TelemetryReader.cs`, `ReferenceEquals`-guarded like
   `EnsureRwrSubscription`) — on every new aircraft: set radar to match `RadarOnOnStart` (via
   `CmdToggleRadar()` only if it needs to change), engine to match `EngineOnOnStart` (via
   `CmdToggleIgnition()` only if it needs to change), `MasterArms.On = MasterArmsOnOnStart`,
   `CombatMode = All`.
4. **Eight keybinds** (`Keybinds.cs`) — `master-arms-on` / `master-arms-off`, `radar-on` /
   `radar-off`, `engine-on` / `engine-off`, `combat-mode-aa` / `combat-mode-ag`, each `edge: true`,
   tap/hold branching per the table above.
5. **Master Arms enforcement** — guard clause in `WeaponSelectors.Fire()` and in
   `Keybinds.Drive(..., CountermeasureManager, ...)`. Mod's own keybinds only; stock trigger path
   explicitly deferred (see above).
6. **Combat-mode enforcement** (`WeaponSelectors.cs`) — `CycleMissile` filters its candidate list by
   `CombatMode` (`All`: unchanged; `AirToAir`: only the five-name list; `AirToGround`: everything
   `IsMissile` flags minus that list). `CycleBomb`/bomb release no-ops while `CombatMode ==
   AirToAir`. Guns untouched.
7. **Verify the A/A name list** in an actual session (`TryLogWeaponInfo` output) before relying on
   it — adjust spelling if the live `WeaponInfo` name differs from the Encyclopedia name.
8. **WPN ARM/SAFE** — `MasterArmsOn` added to the `'wpn'` payload
   (`TelemetrySnapshot.cs`/`TelemetryServer.cs`); a `master-arms.set {on}` command
   (`CommandDispatcher.cs`, `env.on` idiom); `placeWpnNavLabels()`/`renderSplitLabels()` (`mfd.js`)
   and `f35-wpn-paging.js` place ARM/SAFE unconditionally, alongside MAIN/PREV/NEXT; a
   `markMasterArms()` toggles `.on`/`.nav-item.on` on whichever matches live state, mirroring
   `markFollowLabels`/`markFollow`; F-35's `canDo()`/`dispatch()` gain a case for the new action.
9. **WPN SAFE overlay** — `wpn.js`/`wpn.css` draw a full-screen X with a centered **SAFE** label
   underneath whenever `masterArmsOn` is false, independent of the ARM/SAFE nav controls above.
10. **KEY page — "Immersion options" section** — all eight binds and three settings from steps 1
    and 4 render under one new `SectionTitle`, appended after every existing section, behind a
    separator.
11. **docs/keybinds-page.md** — document the new section once built.

## Open questions

- **A/A name-string verification** (see above) — needs an in-game session log before step 6/7 can be
  trusted; treat the five strings as provisional until confirmed.
- **Does combat mode reset to ALL on every new-aircraft spawn**, same as the two start-state
  settings, or persist across spawns within a session? Plan above assumes reset-to-ALL (consistent
  with "nothing here should be inherited"); flag if you want it sticky instead.
- **Master Arms / stock-trigger gap** — confirmed out of scope for this pass (no Harmony). Revisit
  only if full coverage becomes a real ask later.
- **Engine startup sound on spawn** — confirmed unavoidable without Harmony (see Engine section
  above). Accepted as a known limitation for this pass: engine ends up off almost immediately, but a
  brief startup sound plays once on every spawn even with "Engine ON on start" set to OFF.
- **Exact ARM/SAFE key/cell placement** — CLASSIC has `right[1]`/`right[2]` free in full-view WPN
  (weapon rows occupy `left[1..5]`); F-35 needs explicit `cell` hints in `f35-wpn-paging.js`'s `nav`
  array, same as NEXT already gets. Split-pane WPN placement (`renderSplitLabels`'s list branch) also
  needs a slot decision — not yet picked, but mechanically identical to how MAIN/PREV/NEXT already
  place there.
