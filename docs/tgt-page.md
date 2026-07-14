# TGT — target-selection MFD page

## Status

**Built on `feat/tgt-page`, verified in the `serve_web` harness; in-game
pass pending.** This is an **experiment** requested by an alpha tester:
replicate the game's in-cockpit **TGT ("TARGET SELECTION")** page as a
new, fully-clickable MFD page in the web frontend. Approach: **Option A —
drive the game's own `TargetListSelector`** (see "The fork" below). The
one runtime question that gated it — does the singleton exist unprompted?
— is **answered YES** (see "Probe result"). See "Plan" for what shipped.

## What the in-game TGT page is

The game panel is `TargetListSelector` (decompile in
`_scratch/full/TargetListSelector.cs`). It is **not** a contact/scope
display — it is a stateful **filter + gate over the player's selected
weapon target list** (the same `weaponManager.GetTargetList()` our
**TGL** page already renders). It governs which targets you have
committed to. That is why it is called TGT.

Controls (from `TargetListSelector.cs` + `TargetListSelector_ToggleButton.cs`):

| Control | Game call | Effect |
|---|---|---|
| **RESET FILTER** | `ResetFilters()` | Turn every toggle back on (all pass). Does **not** re-select cleared targets. |
| **CLEAR TARGETS** | `DeselectAll()` | Deselect every current target. |
| **FRIENDLY / ENEMY** | `toggleFactionItems[]` | Faction filter. |
| **AIR / MSL / GND / BLD / SHP** | `toggleUnitTypesItems[]` | Unit-category filter (by `UnitDefinition` subtype). |
| **TRUCK…RDR** (10) | `toggleVehicleTypesItems[]` | Vehicle-subtype filter, built at runtime from `Encyclopedia.vehicleTypes`. |
| **LASER** | `toggleLaser` | Keep only lased targets. |
| **HUD** | `toggleFollowHUD` | Mirror the filter to the HUD priority options. |

Interaction: left-click toggles a filter; **right-click = "only this"**
(`SetOnlyItem` — turn everything else in that group off).

## How the filter really behaves (sticky, not one-shot)

Two things happen when a toggle goes **off**, and the second is the one
that makes it a real filter rather than a bulk button:

1. **Prunes the current selection.** The toggle press calls
   `NeedUpdateIcons()`; ~1 s later `Update()` runs
   `CheckAllExclusions()`, which `ForceDeselect`s every selected unit
   matching an OFF filter.
2. **Gates future selection.** Every selection attempt consults the
   filter and is refused if it matches an OFF toggle:

   ```csharp
   // DynamicMap.cs:624 — the map select path
   if (... && !SceneSingleton<TargetListSelector>.i.CheckExclusions(unit))
       this.onUnitSelected?.Invoke(unit);
   // DynamicMap.cs:874 — the click-raycast path consults CheckExclusions too
   ```

The filter **only ever subtracts** — `ResetFilters()` re-enables the
toggles but never re-adds cleared targets. The re-prune (#1) only fires
on a toggle press, but the gate (#2) is live continuously.

**In play:** in a furball you tap ENEMY-only. Your mis-tapped friendlies
drop immediately, *and* for the rest of the fight the map refuses every
friendly you fat-finger — the selection stays clean as the battle
evolves. That live gate is the value of the page.

## The fork: why Option A

The selection gate (#2) lives **inside** `DynamicMap` / `CombatHUD`, and
those paths only ever consult the game's own `TargetListSelector.i` —
nothing the mod can inject into. So there are only two honest ways to
build the page:

- **Option A — drive the game's `TargetListSelector` singleton.** Our
  clickable buttons send commands that call `.toggleX.Set()`,
  `.ResetFilters()`, `.DeselectAll()` on the live instance. The game
  then does everything faithfully — prunes, gates, recolors icons, syncs
  the cockpit — for free. The web page just mirrors the toggle states
  for display. **This is both the faithful and the lazier option**
  (we flip the game's switches instead of reimplementing them).
- **Option B — mod-side one-shot bulk-deselect.** Buttons send "deselect
  all current targets matching category X." Self-contained, no singleton
  dependency, but **not a filter**: the gate is absent, so re-selecting a
  pruned unit re-adds it and the button state drifts out of sync
  instantly. Fallback only.

**Decision: Option A.** Option B is the fallback iff the probe shows the
singleton is unusable.

## The gating question → the probe

Option A depends on `SceneSingleton<TargetListSelector>.i` being
non-null. It is a `SceneSingleton` tied to `public MFDScreen screen`, so
it is very likely instantiated **only once the in-cockpit MFD has shown
its TGT page**. If it is null when the player never opens that page, the
commands no-op and we need a different trigger (or Option B).

**Probe (landed):** `src/plugin/TgtProbe.cs`, gated behind
**Diagnostics > TgtProbe** (default OFF). On the reader's slow tick it
reads the singleton and logs — only when the state changes, so no spam —
whether it is present, plus the count + labels + ordering of the three
toggle arrays and the laser/hud toggles. That ordering is exactly what
the Option-A command handlers will index into.

**How to run it:**
1. F1 config menu → Diagnostics → enable **TgtProbe**.
2. Start a mission. **Do not** open the in-cockpit TGT MFD. Watch
   `BepInEx/LogOutput.log` for `[NOXMFD][TgtProbe]`.
   - Logs `PRESENT …` before you ever touch the MFD → Option A works
     unconditionally. Best case.
   - Logs `NULL …` until you open the in-cockpit TGT page → the
     singleton is lazy; we either force its creation or accept that TGT
     requires the player to have opened it once.
3. Note the dumped toggle labels/ordering for the handler design.

**Delete `TgtProbe.cs` (and its `Bind`/`Tick` calls in `Plugin.cs` /
`TelemetryReader.cs`) once the question is answered** — it reaches into
game internals purely to learn them.

### Probe result (resolved)

Run in a live mission, **without** opening the in-cockpit TGT MFD and
with **no targets selected**:

```
TargetListSelector.i PRESENT | screen=set
  faction[2]:  FRIENDLY, ENEMY
  unitType[5]: AIR, MSL, GND, BLD, SHP
  vehType[10]: TRUCK, UGV, LCV, AFV, MBT, ART, AAA, IR_SAM, R_SAM, RDR
  laser=set | hud=set
```

Conclusions:

- **Option A is viable with no preconditions** — the singleton exists in
  a mission with the cockpit TGT page never opened and no selection made.
  The probe logs only on state change and only ever logged `PRESENT`, so
  it was never null. No lazy-init trigger needed.
- Every control group is wired (both factions, all 5 categories, all 10
  vehicle types, laser, hud) and the **array ordering matches the
  on-screen panel exactly** — handlers can index by position, with the
  labels kept in the wire format as a sanity guard.
- The 10 `vehType` names double as the keys for the icon capture (their
  sprites are `Encyclopedia.i.vehicleTypes[i].typeSprite`).

## Plan

Built on the `feat/tgt-page` branch; verified in the `serve_web` harness.
Only the in-game pass is outstanding (needs a game restart to load the DLL).

1. **[done]** Land the probe; answer the singleton question in-game.
   Result: singleton present unprompted (see "Probe result").
2. **[done] Commands.** `CommandDispatcher` gained flat handlers that drive
   the singleton on the main thread (null-guarded, validated): `tgt.set
   {group, index, on}`, `tgt.only {group, index}`, `tgt.reset`,
   `tgt.clear`, `tgt.laser {on}`, `tgt.hud {on}`. `group` is
   `faction | category | vehicle`, indexed as the probe reported.
3. **[done] Telemetry.** A `tgt` block in the SSE frame mirrors the live
   toggle states — the two standalone toggles plus the three groups, each
   `{n, on}` in game order; `{present:false}` when the singleton is absent.
4. **[done] Frontend page.** `src/web/pages/tgt/` — fully clickable, no
   bezel keys but MAIN. Echoes the in-game panel (faction / category rows,
   vehicle-type grid with captured icons, LASER / HUD, RESET / CLEAR).
   Tap → `tgt.set`; **long-press → `tgt.only`** (chosen over a per-button
   "ONLY" affordance for the touch tablet). State from the `tgt` block.
   Vehicle icons captured from `Encyclopedia.i.vehicleTypes` and served at
   `/tgt-icon?type=`.
5. **[done] Wire nav.** TGT registered in `FRAME_PAGES` + `PAGES`; a TGT
   entry added to MAIN (right column, since the left column was full).
   Full-view only — not wired into split panes for v1.
6. **[done in harness] Verify.** `serve_web` + `preview-mock` gained a mock
   `tgt` block and `/tgt-icon`; confirmed the page renders the states and
   that tap / long-press / RESET / CLEAR / LASER / HUD fire the right
   commands, plus the `present:false` UNAVAILABLE path. **In-game pass
   still pending** (restart to load the new DLL, then MAIN → TGT).

## Open questions

- ~~Does the singleton exist before the in-cockpit TGT page is opened?~~
  **Resolved: yes, unprompted (see "Probe result").**
- ~~If it is lazy: force-instantiate or require the page opened once?~~
  Moot — not lazy.
- Is the toggle-array ordering stable across missions / aircraft? Matched
  the panel on this run; wire format keeps labels as a guard in case a
  future build reorders them.
- ~~Right-click on the physical tablet — long-press or a per-button
  affordance?~~ **Resolved: long-press** (500 ms) fires `tgt.only`; tap
  toggles. Needs a real-tablet feel check during the in-game pass.
- ~~Vehicle-type icon capture: which serving endpoint?~~ **Resolved:**
  `/tgt-icon?type=<name>`, keyed by the `vehType` names, captured from
  `Encyclopedia.i.vehicleTypes[i].typeSprite`.
