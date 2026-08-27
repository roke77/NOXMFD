# Radar & Master Arms — [issue #32](https://github.com/roke77/NOXMFD/issues/32)

**Status:** merged to `main` and partly in-game tested. The first real play-test caught the Radar
spawn-default bug (see the Radar section below) — fixed. RDR was also found missing from the F-35
layout's MAIN list (a pre-existing gap unrelated to this feature, fixed in passing). Combat-mode
HUD filtering was later tested in-game and one bug was fixed. Four of five A/A missile names are
confirmed against real session logs (see Decisions confirmed); only `IRM-S1` remains provisional.
The remaining live matrix is the Radar fix, Engine behavior, all eight keybinds, Master Arms
enforcement, and weapon-selection combat-mode filtering.

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
   every spawn; a dedicated keybind lets the pilot arm/disarm it in flight. OFF blocks **all** gun/
   missile/bomb fire — the mod's own keybinds *and* the game's own stock trigger/mouse/joystick fire
   path.
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

- **Silent spawn defaults** — a prefix patch on `Aircraft.OnStartServer()` (ignition) and a postfix on
  `Aircraft.OnStartClient()` (radar — see below for why that's the correct target, not `Radar.Awake`/
  `AttachToUnit` directly) sets the *initial* value directly, before it's ever observable, instead of
  reactively toggling it back off a tick later. This removes the engine-startup-sound artifact
  entirely (see the old "known limitation" below, now resolved) rather than just cutting it short.
- **Full Master Arms coverage** — a patch on the weapon-fire path blocks firing at the source, so OFF
  blocks *every* way to fire guns/missiles/bombs, not just the mod's own keybinds.

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

### WPN page — ARM / SAFE and A/A / A/G controls, always present

The WPN page always gains four new selectable controls: **ARM**/**SAFE** (Master Arms) and
**A/A**/**A/G** (combat mode) — all unconditional, regardless of the relevant setting (even a pilot
who leaves Master Arms' default ON can arm/disarm mid-flight straight from the WPN page, not just
via keybind). Whichever matches the live state renders active (amber box, same as every other
engaged control); clicking, tapping, or SOI-navigating to it and pressing Select sets that state.

- **No dedicated ALL control** — holding either A/A or A/G already resets combat mode to ALL (see
  the keybinds table above), so ALL just reads as neither of the two being lit, the same way it does
  for the keybinds themselves. One less control to place, and no inconsistency between the two ways
  to reach ALL.
- **When `MasterArms.On` is false (SAFE)**, the WPN page additionally draws a full-screen **X**
  over its own content, plus a centered info card — the same shape as the shell's MAIN "about" card
  (bordered, glow), red instead of green, but solid black fill (not the translucent `--no-panel-bg`
  other cards use — this card IS the alert, not a status readout layered over live data). A
  hazard-stripe band forms the card's own top and bottom edge (not a full-screen bar — mockup-tested
  against the user before landing on this). Reads **MASTER ARMS** (smaller) over **SAFE** (bigger,
  heavier: the state is what matters at a glance). Content-level, not a bezel/nav element, drawn by
  `wpn.js`/`wpn.css` regardless of layout. Combat mode has no equivalent content-level overlay —
  only the nav controls.

### Extended keybinds page — new content is appended, not interleaved

A true second section, not appended content sharing the existing table: its own EXTENDED-KEYBINDS-
sized title ("IMMERSION OPTIONS"), a short description, the three start-state settings, then its own
table (own header row, own rows) for the eight binds — below a separator, after everything the KEY
page already has. Existing sections/table are untouched.

## What already exists to reuse (read before building)

### Radar — patched at the source, not reactively toggled

- **Bug found and fixed after first ship**: the original patch targeted `Radar.AttachToUnit()`,
  which turned out to be the WRONG method for a normal aircraft's built-in radar — confirmed both by
  an in-game report (radar stayed on with the setting OFF) and by re-reading the decompiled source.
  `AttachToUnit()` only fires for a hardpoint-*mounted* radar pod (`Hardpoint.SpawnMount`, gated on
  `weaponMount.radar`); the built-in radar every normal loadout has attaches via `Radar.Awake()`
  instead, which hardcodes `activated = true` with no way to gate it in place — `Awake()` runs the
  instant the prefab is instantiated, before `Player.SetAircraft(this)` has run, so
  `GameManager.GetLocalAircraft` can't even resolve correctly at that point.
- **Fix: patch `Aircraft.OnStartClient()` instead**, as a postfix. By the time the whole method body
  has run — including `Player.SetAircraft(this)` partway through, and the `InitializeUnit()` call at
  the very end that triggers `Hardpoint.SpawnMount` for a radar pod if the loadout has one —
  `aircraft.radar` is populated correctly regardless of which path attached it, and
  `GameManager.GetLocalAircraft` resolves correctly. One patch now covers both radar shapes. Still
  never observably flickers on, for the same reason as before: `OnStartClient` completes
  synchronously, well before the first `Update()` that would otherwise render/scan with it on.
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

### Master Arms — mod-only flag; enforced via Harmony, including the stock trigger

- Decompiled source has **no** master-arms/safety-switch concept on `Aircraft`/`WeaponManager`.
  `WeaponStation.SafetyIsOn()` exists but is a *ground/gear* safety, unrelated — `MasterArms.On`
  remains a mod-only flag with no game-side equivalent to alias.
- **Full coverage of weapon fire, not just the mod's own keybinds**:
  - `WeaponManager.Fire()` — the game's own single funnel for gun/missile/bomb fire, called both by
    the mod's `WeaponSelectors.Fire()` *and* directly by the game's own stock trigger/mouse/joystick
    input code. A Harmony prefix here, short-circuiting (returning `false`/skipping the original)
    when `MasterArms.On` is false, blocks **every** firing path in one patch — mod keybinds and stock
    input alike. This replaces the narrower `WeaponSelectors.Fire()`-only guard considered earlier;
    that in-mod guard can be dropped once the patch covers the same call from underneath it.
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
- **Auto-switch on entering a mode** (`WeaponSelectors.OnCombatModeChanged`, called from
  `Keybinds.SetCombatMode` — the A/A/A/G tap *and* the WPN page's own A/A · A/G controls, bezel and
  F-35 alike, both route through it via `CommandDispatcher`'s `combat-mode.set` — before the
  mode-restricted class filter above ever narrows Cycle Missile): if the currently selected weapon is
  a bomb or an A/G missile and A/A is
  set, snaps to the first available A/A missile, falling back to the first gun if none has ammo; if
  it's an A/A missile and A/G is set, snaps to the first available A/G missile, falling back to the
  first bomb and then the first gun. Guns are exempt as the *current* selection — no-ops immediately,
  same as `CycleGun`'s own independence from combat mode — but are always the last resort when
  nothing in the new mode can fire. Anything already valid for the new mode (a bomb entering A/G, an
  already-matching missile, a jammer pod) is left alone.

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

- **File:** `src/plugin/Input/Keybinds.cs`. **Built, and turned out to need genuinely new plumbing** — the
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
4. **Spawn-default patches** — a postfix on `Aircraft.OnStartClient()` setting
   `radar.activated = RadarOnOnStart` (fixed from an original, wrong `Radar.AttachToUnit()` target —
   see the Radar section above), and a prefix on `Aircraft.OnStartServer()` setting
   `NetworkIgnition = EngineOnOnStart`, both reading the settings from step 2. Replaces the earlier
   reactive `TelemetryReader.cs`-hook idea entirely for these two — no polling, no timing window.
5. **Eight keybinds** (`Keybinds.cs`) — `master-arms-on` / `master-arms-off`, `radar-on` /
   `radar-off`, `engine-on` / `engine-off`, `combat-mode-aa` / `combat-mode-ag`, each `edge: true`,
   tap/hold branching per the table above. Radar/Engine keybinds still call the existing
   `CmdToggleRadar()`/`CmdToggleIgnition()` — only the spawn default changed, not the in-flight
   controls.
6. **Master Arms enforcement patch** — a prefix on `WeaponManager.Fire()`, short-circuiting when
   `MasterArms.On` is false. Covers the mod's own keybinds and the game's stock trigger/mouse/joystick
   input in one patch, since both call the same method underneath.
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
- **A/A name-string verification — 4 of 5 confirmed against real session logs**
  (`TryLogWeaponInfo` output, two separate in-game sessions): `AAM-29 Scythe`, `AAM-36 Scimitar`,
  `IRM-S2`, and `MMR-S3` all match the Encyclopedia names in `WeaponSelectors.AirToAirMissiles`
  exactly — no spelling changes needed. Only `IRM-S1` remains unconfirmed (not seen in either
  session's loadouts); still provisional until it turns up in a real log.

## Open questions

- **Decorative MASTER/MODE labels — resolved everywhere, including split and F-35.** Purely
  decorative (no click action): a word with a solid triangle above/below, centered in the gap
  between a control pair — "MASTER" between ARM/SAFE, "MODE" between A/A/A-G (`.wpn-decor` in
  `mfd.css`). Design was mockup-tested with the user through several rounds — arrow/icon styles, a
  boxed word, an outer border enclosing the whole pair — before settling on plain white text with
  the arrows and no border. CLASSIC full view: `placeWpnDecorators()` in `mfd.js`, positioned on
  the separator between the pair's two keys via `sepElsRight`. **Split-pane** (added later —
  it had no call site at all originally, so the decorator silently never appeared there):
  `placeWpnPaneDecorator()` finds each pair by id in the pane's `slice.slots` rather than a
  hardcoded position, since a split pane's pairs can land on any of its 4 item slots depending on
  pagination; skips drawing when a pair straddles the left/right column boundary (no sensible
  "between" position exists there — a rare pagination edge case, not the common one). **F-35**:
  `placeWpnDecorator(actionA, actionB, word)` there looks its two keys up by `data-action` rather
  than physical position, so it needed no split-specific handling at all.
- **ARM/SAFE/A-A/A-G key/cell placement — resolved everywhere, including split.** CLASSIC uses
  `right[1..4]` in full-view WPN (weapon rows occupy `left[1..5]`); F-35 uses explicit `cell` hints in
  `f35.js`'s `COMBAT_MODE_NAV`/`MASTER_ARMS_NAV` (rows 2-5 of the right column), same as NEXT already
  gets. **Split-pane** (`mfd.js`'s `buildWpnSplitPages`): the four controls are appended after the
  weapon list into the same shared 4-slot-per-page window weapons already page through — a split pane
  has no spare keys the way full view does. A pair (ARM+SAFE, A/A+A/G) never splits across a page
  boundary: since a pair is always two adjacent entries in the combined list, it only ever splits
  when the first item would land on a page's last slot, so one empty slot inserted right before the
  pair pushes the whole thing to the next page instead, leaving the leftover slot(s) on the previous
  page blank. Weapons and controls can share a page when there's room (e.g. 2 weapons + ARM + SAFE
  fits one page exactly) — only a would-be split is special-cased.
- **`Radar.Awake()`/`AttachToUnit()` targeting — resolved.** Confirmed by an in-game bug report
  (radar stayed on with the setting OFF) that the original patch target was wrong; fixed by moving to
  an `Aircraft.OnStartClient()` postfix instead. See the Radar section above for the full story.
- **Dedicated-server topology gap, confirmed and accepted** — `Aircraft.OnStartServer()` only
  executes on the network SERVER. Host mode (single-player or a player-hosted lobby) is fine, since
  the host process is both client and server. Connecting as a plain client to someone else's
  dedicated `NuclearOptionServer.exe` means the Engine patch never fires for your own aircraft at all
  (that method runs on their machine, not yours, and a mod normally isn't installed server-side) — the
  `EngineOnOnStart` setting silently has no effect in that topology. Accepted: this mod's primary use
  case is single-player/host play; revisit only if dedicated-server support becomes a real ask. (The
  Radar fix above is client-side, so it does NOT have this gap — only Engine does.)
- **Patch fragility across game updates** — accepted cost of adding Harmony (see Goal). Not covered
  by `apicheck`; a future game update that reshapes `WeaponManager.Fire()`/`OnStartServer()`/
  `OnStartClient()` could make a patch silently misbehave rather than throw. Worth a manual
  smoke-test of these four patches specifically after every game update, alongside the usual
  `apicheck` run.

## HUD filter automation on combat mode — [issue #50](https://github.com/roke77/NOXMFD/issues/50)

**Status:** in-game tested (2026-08-22), one bug found and fixed (see Related fixes below). Reusing
the game's own A2A/A2G HUD-mode presets unmodified for now — whether they already produce the
desired effect, or need tuning, is to be confirmed in-game before any preset values themselves
change. Off by default behind its own KEY page toggle (see below) — a pilot has to opt in before
any of this runs at all.

Combat mode now also drives the HUD page's own unit-icon filters
([docs/hud-page.md](hud-page.md)), not just weapon selection:

- **Off by default — opt-in setting.** `ImmersionConfig.HudFiltersOnCombatMode` (KEY page, its own
  toggle in the same "Immersion options" block as the three start-state settings above) gates all
  of this: `HudCombatModeFilters.OnCombatModeChanged` returns immediately when it's off, so a pilot
  who hasn't turned it on gets no HUD change at all from a combat-mode switch — not a silent
  no-op-but-still-tracking, a complete skip. Unlike Radar/Engine/Master Arms' on-start settings,
  this one defaults OFF: it's a new, opinionated behavior to opt into, not a preserved default.
  `HudCombatModeFilters.CaptureIfIdle`/`EnsureBootstrap` keep running regardless of the setting, so
  the baseline is already warm the moment it's turned on rather than restoring a stale/empty one.
- **Entering A/A or A/G** (with the setting on) force-loads that HUD mode tab's own saved preset —
  the same one the player could apply by hand from the HUD page (`HUDOptions.listModes[A2A]`/
  `[A2G]`, applied via the existing `ToggleButtons`/`ApplySettings` mechanism).
- **Re-pressing an already-active A/A or A/G re-forces that preset.** If the player tweaks HUD
  filters by hand while A/A (say) is active and then wants the A/A preset back rather than their
  own tweaks, pressing A/A again (bezel key or keybind) reloads it — this isn't gated on an actual
  mode transition, since `HUDOptions.ToggleButtons`/`HUDOptions_ToggleButton.Set` have no "already
  this value" guard of their own to fight.
- **Returning to idle** (long-press, same as resetting combat mode itself) restores a running
  snapshot of the player's own HUD filter values — not a fixed default. The snapshot is updated on
  every `hud.set`/`hud.mode` command, but **only while combat mode is idle**; edits made while A/A
  or A/G is active are allowed to change the live HUD (the forced preset is a starting point, not a
  lock) but never touch the snapshot, so they can't leak into what gets restored on exit.
- **Bootstrap**: if no snapshot exists yet (fresh plugin session, HUD page never touched), the
  first time `HUDOptions` exists is captured as the initial snapshot, so there's always something
  valid to restore to.
- New file: [`src/plugin/Hud/HudCombatModeFilters.cs`](../src/plugin/Hud/HudCombatModeFilters.cs). Driven
  entirely through the same `Keybinds.SetCombatMode` choke point described above — both the WPN
  page's A/A · A/G controls and the physical keybind (tap **and** hold-to-ALL, which previously
  bypassed `SetCombatMode` with a bare field write and has been fixed to route through it) trigger
  it identically. `ImmersionState.EnsureSpawnDefaults`'s per-respawn `CombatMode` reset was likewise
  changed to route through `SetCombatMode` rather than a bare field write, so dying mid-A/A or A/G
  still restores the HUD baseline instead of leaving that mode's preset visually stuck.
- **Named preset save/load for the HUD page — built, as a separate feature.** What was "explicitly
  out of scope" here shipped as [HUD presets](hud-presets.md): 5 player-named slots, independent of
  this combat-mode automation (which only ever drives the game's own built-in A2A/A2G tabs). The
  two share one touchpoint — loading a preset counts as a player edit for this feature's own idle
  baseline (see hud-presets.md) — otherwise unrelated.

### Related fixes found while building this

- **WPN's on-screen A/A · A/G bezel keys had no hold detection at all.** The physical PC keybind's
  tap/hold pair (`PollTapHold`) worked; a mouse/touch press-and-hold on the on-screen key did
  nothing — every click always sent `combat-mode.set {group:'aa'|'ag'}`, nothing ever sent `'all'`.
  Fixed with a `pointerdown`/`pointerup` timer (500ms, matching TGT's own `LONG_MS`) scoped to just
  those two keys in both `mfd.js` and `f35.js`; a hold suppresses the tap action that would
  otherwise also fire on release. The SOI/physical-Select path (`soiAct` calling `mfdButton()`
  directly, no pointer events to time) still can't distinguish hold from tap — an accepted gap,
  since a literal mouse/touch press is what this covers.
- **HUD page content overlapped the bezel's vertical MAIN/HUD/KEY/LYT/RTS label.** `mfd.js`'s
  `isVmainPage()` deliberately excluded `'hud'`, banking on a hud.css "top clearance" comment that
  was never actually implemented — meanwhile `mfd.css`'s own comment already said the label should
  stand upright for HUD, same as TGT/BDF. Fixed by putting `'hud'` back in `isVmainPage()` (and
  F-35's matching CSS selector list) and giving `.hud-panel` real left/right edge padding — TGT's
  own `clamp()` values undershot the F-35 case by ~9px (measured against a real 4-portal glass:
  the label's fixed ~43px footprint doesn't shrink with a narrow portal), so HUD uses its own,
  larger floor.
- **HUD filters still changed on A/A · A/G with `HudFiltersOnCombatMode` turned OFF.** Reported
  in-game: switching combat mode moved HUD filter values even with the opt-in setting off, though
  turning it on produced the correct A2A/A2G preset. `HudCombatModeFilters.OnCombatModeChanged`
  itself was already gated correctly and returning immediately when off — the leak wasn't coming
  from this class at all. The real cause: `WeaponSelectors.OnCombatModeChanged` (issue #32,
  unconditional, no opt-in gate) auto-switches the selected weapon whenever the new combat mode
  disables it, and that weapon-station change trips the game's own native
  `HUDOptions.AutomaticToggle` — which re-lights whichever HUD mode tab matches the newly-selected
  weapon's role, regardless of any setting of ours. Fixed in `Keybinds.SetCombatMode`: a snapshot
  of the live HUD filter state is taken immediately before the weapon auto-switch
  (`HudCombatModeFilters.CaptureBeforeSwitch`), and — only when the toggle is off —
  `UndoNativeAutoToggleIfOff` restores it right after, undoing `AutomaticToggle`'s side effect.
  When the toggle is on, this is a no-op, since `OnCombatModeChanged`'s own explicit apply already
  produced the intended state.
