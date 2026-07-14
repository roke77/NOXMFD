# TGT — target-selection MFD page (planning)

## Status

Planning + probe only. No page code yet. This is an **experiment**
requested by an alpha tester: replicate the game's in-cockpit **TGT
("TARGET SELECTION")** page as a new, fully-clickable MFD page in the
web frontend. Approach chosen: **Option A — drive the game's own
`TargetListSelector`** (see "The fork" below). One runtime question
gates the whole thing; a throwaway probe (already landed) answers it.

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

## Plan (pending probe result)

1. **[done]** Land the probe; answer the singleton question in-game.
2. **Commands.** Extend `CommandDispatcher` with flat handlers that drive
   the singleton on the main thread, mirroring the existing
   `target.select` pattern (validate against live state, route through
   the game's own methods): e.g. `tgt.toggle {group, index, on}`,
   `tgt.only {group, index}`, `tgt.reset`, `tgt.clear`,
   `tgt.laser {on}`, `tgt.hud {on}`. Exact param shape follows the
   probe's array ordering.
3. **Telemetry.** Add a small `tgt` block to the snapshot mirroring the
   live toggle states (+ labels for the dynamic vehicle-type row) so the
   page renders the real filter state, not a local guess. Guard for the
   singleton being absent.
4. **Frontend page.** New `src/web/pages/tgt/` (sibling of `tgl/`),
   hosted in `#page-frame` like the other full-view pages. Fully
   clickable — **no bezel keys except MAIN/back** — laid out to echo the
   in-game panel (faction row, category row, vehicle-type grid, LASER /
   HUD, RESET FILTER, CLEAR TARGETS). Left-click → `tgt.toggle`;
   right-click → `tgt.only`. Reflects state from the `tgt` snapshot block.
5. **Wire nav.** Add TGT to the page/softkey registry so MAIN can reach
   it and it can return to MAIN.
6. **Verify** in the `serve_web` harness (mock the `tgt` block), then a
   `dotnet build -c Release --no-incremental` and in-game test.

## Open questions

- Does the singleton exist before the in-cockpit TGT page is opened?
  (**the probe answers this — everything else is downstream.**)
- If it is lazy: is force-instantiating it safe, or do we require the
  player to have opened the page once per session?
- Is the toggle-array ordering stable across missions / aircraft, or must
  we key off labels? (probe dumps labels so we can decide.)
- Right-click on the physical tablet — long-press, or a small "ONLY"
  affordance per button? (design during step 4.)
