using System;
using System.Collections.Generic;

namespace NOXMFD
{
    // ── Inbound command channel ──────────────────────────────────────────────────
    // The web client POSTs JSON commands to /command; TelemetryServer parses + queues them on a
    // server thread, and this dispatcher drains the queue on the Unity main thread (called from
    // TelemetryReader.Update) and runs the matching handler.
    //
    // Handlers MUST only run on the main thread (they touch game state), keep themselves
    // idempotent, validate against live state, and prefer the game's own high-level input methods
    // (e.g. CombatHUD.SelectUnit) over low-level setters so the in-cockpit side effects — marker
    // colour, audio, map sync — come along for free.

    // Wire envelope: { "cmd": "target.select", "id": 1234 }. Deliberately FLAT — every field is a
    // top-level primitive. Unity's JsonUtility reliably populates top-level fields of a
    // [Serializable] class but is flaky deserializing nested [Serializable] objects in the game's
    // Mono runtime (it silently left a nested args.id at 0). So all command params live here as a
    // flat union; each handler reads the fields it cares about and absent ones default to 0.
    [Serializable]
    internal class CommandEnvelope
    {
        public string cmd;
        public long   id;      // target unit persistentID (target.select / target.deselect)
        public string wname;   // weapon type name (weapon.select) — matches LoadoutEntry.Name
                                // wpt.active : the active waypoint's display name ("" if unnamed)
        public string group;   // tgt.set / tgt.only : "faction" | "category" | "vehicle"
                                // combat-mode.set : "all" | "aa" | "ag"
                                // avn.toggle : "gear" | "radar" | "guns" | "eng" | "assist" | "nvg" |
                                //              "lights" | "turret"
        public int    index;   // tgt.set / tgt.only : toggle index within the group
                                // wpt.active : the waypoint's 0-based position in its route
        public bool   on;      // tgt.set / tgt.laser / tgt.hud : desired toggle state
                                // wpt.active : false = no active waypoint, clear the HUD cue
        public string bind;    // keybind.* : BindDef id ("flares", "gear-up", ...)
        public string key;     // keybind.set-key : Unity KeyCode name ("" or "None" clears)
        public string cid;     // soi.panes : which instance is reporting (a POST isn't tied to its /stream)
        public int    n;       // soi.panes : how many focusable surfaces that instance now shows
        public float  hz;      // rates.set : desired rate in Hz (group picks which — "fast" | "tgp")
        public float  wx;      // wpt.active : active waypoint's world X (floating-origin corrected)
        public float  wz;      // wpt.active : active waypoint's world Z
    }

    internal static class CommandDispatcher
    {
        private static readonly Dictionary<string, Action<CommandEnvelope>> _handlers =
            new Dictionary<string, Action<CommandEnvelope>>(StringComparer.Ordinal)
            {
                { "target.select",   TargetSelect },
                { "target.deselect", TargetDeselect },
                { "weapon.select",   WeaponSelect },
                { "tgt.set",         TgtSet },
                { "tgt.only",        TgtOnly },
                { "tgt.reset",       TgtReset },
                { "tgt.clear",       TgtClear },
                { "tgt.clear-datalink", TgtClearDatalink },
                { "tgt.clear-stale",    TgtClearStale },
                { "tgt.laser",       TgtLaser },
                { "tgt.hud",         TgtHud },
                { "hud.set",         HudSet },
                { "hud.mode",        HudMode },
                { "declutter.set",   DeclutterSet },
                { "avn.toggle",      AvnToggle },
                // cfg-rates experiment (issue #39) — RTS page's two sliders. group picks which rate;
                // "tgp" is the camera feed, anything else (default "fast") is the main 10 Hz tick.
                { "rates.set",       e => { if (e.group == "tgp") RatesConfig.SetTgpHz(e.hz); else RatesConfig.SetFastHz(e.hz); } },
                { "master-arms.set", e => ImmersionState.MasterArmsOn = e.on },
                // Routes through Keybinds.SetCombatMode (not a bare assignment) so the WPN page's own
                // A/A · A/G controls (bezel and F-35) get the same weapon auto-switch as the physical
                // keybind (docs/radar-master-arms.md, issue #32) — one behavior, one source, not a
                // second copy that could drift from it.
                { "combat-mode.set", e => Keybinds.SetCombatMode(e.group switch
                    {
                        "aa" => CombatMode.AirToAir,
                        "ag" => CombatMode.AirToGround,
                        _    => CombatMode.All,
                    }) },
                { "keybind.set-key",    e => Log("set-key",    e.bind, Keybinds.SetKeyBind(e.bind, e.key)) },
                { "keybind.arm-joy",    e => Log("arm-joy",    e.bind, Keybinds.ArmJoyCapture(e.bind)) },
                { "keybind.cancel-joy", e => Keybinds.CancelJoyCapture() },
                { "keybind.clear-joy",  e => Log("clear-joy",  e.bind, Keybinds.ClearJoyBind(e.bind)) },
                // Analog axis capture/clear/invert (docs/map-cursor.md) — the MAP cursor's Horizontal/
                // Vertical rows only; same arm/cancel/clear shape as the joystick-button commands above.
                { "keybind.arm-axis",       e => Log("arm-axis",       e.bind, Keybinds.ArmAxisCapture(e.bind)) },
                { "keybind.cancel-axis",    e => Keybinds.CancelAxisCapture() },
                { "keybind.clear-axis",     e => Log("clear-axis",     e.bind, Keybinds.ClearAxisBind(e.bind)) },
                { "keybind.set-axis-invert", e => Log("set-axis-invert", e.bind, Keybinds.SetAxisInvert(e.bind, e.on)) },
                // Input-while-unfocused toggle — the /keybinds page's first entry, not a bind (no
                // key/joy/axis source of its own).
                { "keybind.set-bg-input", e => Keybinds.SetBackgroundInput(e.on) },
                // Immersion start-state toggles (docs/radar-master-arms.md) — the KEY page's other
                // non-bind rows, same shape as keybind.set-bg-input.
                { "keybind.set-radar-on-start",       e => ImmersionConfig.SetRadarOnOnStart(e.on) },
                { "keybind.set-engine-on-start",      e => ImmersionConfig.SetEngineOnOnStart(e.on) },
                { "keybind.set-master-arms-on-start", e => ImmersionConfig.SetMasterArmsOnOnStart(e.on) },
                // SOI focus. These will get HOTAS binds of their own; as commands they are how focus
                // is driven (and tested) from a browser, with no controller and no aircraft.
                { "soi.next",           e => TelemetryServer.SoiCycle(1) },
                { "soi.prev",           e => TelemetryServer.SoiCycle(-1) },
                // A client reports its current surface count so SOI can cycle surfaces, not documents
                // (docs/keybinds-page.md, "surface-level focus"). Carries its own cid — a POST isn't
                // tied to the /stream connection the count belongs to.
                { "soi.panes",          e => TelemetryServer.SetPaneCount(e.cid ?? string.Empty, e.n) },
                // The browser's active waypoint, for the in-game HUD cue (docs/hud-waypoint-indicator.md).
                // The only browser -> plugin STATE command; see HudWaypointState for why it isn't
                // mission-scoped and how a second display's route list is (not) arbitrated.
                { "wpt.active",         e => HudWaypointState.Set(e.on, e.wx, e.wz, e.wname, e.index) },
            };

        // Keybind writes just delegate to the Keybinds registry; log rejections (unknown id / bad key).
        private static void Log(string op, string bind, bool ok)
        {
            if (!ok) Plugin.Log?.LogInfo($"[NOXMFD] keybind.{op} '{bind}': rejected.");
        }

        // True for a cmd we have a handler for — lets the server reject unknown commands at the
        // boundary (422) instead of silently queueing them.
        public static bool IsKnown(string cmd) => cmd != null && _handlers.ContainsKey(cmd);

        // Drained once per frame on the main thread.
        public static void Drain()
        {
            while (TelemetryServer.TryDequeueCommand(out CommandEnvelope env))
            {
                if (env == null) continue;
                if (_handlers.TryGetValue(env.cmd ?? string.Empty, out Action<CommandEnvelope> handler))
                {
                    try { handler(env); }
                    catch (Exception ex) { Plugin.Log?.LogWarning($"[NOXMFD] command '{env.cmd}' threw: {ex.Message}"); }
                }
                else
                {
                    Plugin.Log?.LogInfo($"[NOXMFD] unknown command '{env.cmd}' — dropped.");
                }
            }
        }

        // ── Handlers ─────────────────────────────────────────────────────────────

        // Add a unit to the player's weapon target list (map tap-to-target). Select-only: never
        // deselects, and no-ops if the unit is already targeted (AddTargetList has no de-dup).
        // Routes through CombatHUD.SelectUnit so the cockpit marker recolours (faction → green),
        // the select beep plays, and the DynamicMap icon syncs; falls back to the bare
        // weaponManager op for a contact the HUD isn't tracking.
        private static void TargetSelect(CommandEnvelope env)
        {
            uint id = unchecked((uint)env.id);
            if (id == 0) { Plugin.Log?.LogInfo("[NOXMFD] target.select: id=0 (missing/unparsed) — ignored."); return; }

            if (!UnitRegistry.TryGetUnit(new PersistentID { Id = id }, out Unit unit) || unit == null || unit.disabled)
            {
                Plugin.Log?.LogInfo($"[NOXMFD] target.select id={id}: no live unit (stale) — ignored.");
                return;
            }

            GameManager.GetLocalAircraft(out Aircraft ac);
            if (ac == null || ac.weaponManager == null) return;
            if (ReferenceEquals(unit, ac)) return;   // can't target yourself

            string name = unit.definition?.unitName ?? "?";

            // Neutral (no-faction) units are never selectable by default. The game's own TGT filter
            // panel has no toggle for them at all — TargetListSelector_ToggleButton.CheckFactions
            // only gates Friendly/Enemy, so a no-faction contact always passes it — which meant a
            // MAP tap or RDR lock could weapon-lock something the pilot's own filters were never
            // built to offer. Issue: a player targeted grey/white contacts via MAP that then never
            // appeared in the in-game TGT list at all.
            if (DynamicMap.GetFactionMode(unit.NetworkHQ) == FactionMode.NoFaction)
            {
                Plugin.Log?.LogInfo($"[NOXMFD] target.select '{name}' (id={id}): no-faction unit — ignored.");
                return;
            }

            // Respect the TGT filter panel's current faction/category/vehicle/laser toggles — a MAP
            // tap or RDR lock shouldn't be able to select anything the pilot has filtered out there.
            // Reuses the game's own exclusion check (docs/tgt-page.md's "Option A"); if the singleton
            // isn't up yet, fail open rather than block every selection.
            TargetListSelector tgtSel = SceneSingleton<TargetListSelector>.i;
            if (tgtSel != null && tgtSel.CheckExclusions(unit))
            {
                Plugin.Log?.LogInfo($"[NOXMFD] target.select '{name}' (id={id}): excluded by TGT filters — ignored.");
                return;
            }

            WeaponManager wm = ac.weaponManager;
            if (wm.CheckIsTarget(unit))
            {
                Plugin.Log?.LogInfo($"[NOXMFD] target.select '{name}' (id={id}): already targeted — no-op.");
                return;
            }

            CombatHUD hud = SceneSingleton<CombatHUD>.i;
            bool viaHud = hud != null && ReferenceEquals(hud.aircraft, ac) && hud.MarkerExists(unit);
            if (viaHud) hud.SelectUnit(unit);
            else        wm.AddTargetList(unit);
            Plugin.Log?.LogInfo($"[NOXMFD] target.select → '{name}' (id={id}, viaHud={viaHud}).");
        }

        // Drop a unit from the player's weapon target list (TGT page's list checkbox). Mirrors the
        // in-cockpit deselect via CombatHUD.DeSelectUnit, which reverts the marker colour, plays
        // the deselect beep, and syncs the DynamicMap icon; falls back to the bare weaponManager
        // op when the HUD isn't tracking the contact. No-ops if it isn't currently a target.
        private static void TargetDeselect(CommandEnvelope env)
        {
            uint id = unchecked((uint)env.id);
            if (id == 0) { Plugin.Log?.LogInfo("[NOXMFD] target.deselect: id=0 (missing/unparsed) — ignored."); return; }

            if (!UnitRegistry.TryGetUnit(new PersistentID { Id = id }, out Unit unit) || unit == null)
            {
                Plugin.Log?.LogInfo($"[NOXMFD] target.deselect id={id}: no such unit — ignored.");
                return;
            }

            GameManager.GetLocalAircraft(out Aircraft ac);
            if (ac == null || ac.weaponManager == null) return;

            WeaponManager wm = ac.weaponManager;
            string name = unit.definition?.unitName ?? "?";
            if (!wm.CheckIsTarget(unit))
            {
                Plugin.Log?.LogInfo($"[NOXMFD] target.deselect '{name}' (id={id}): not targeted — no-op.");
                return;
            }

            CombatHUD hud = SceneSingleton<CombatHUD>.i;
            bool viaHud = hud != null && ReferenceEquals(hud.aircraft, ac) && hud.MarkerExists(unit);
            if (viaHud) hud.DeSelectUnit(unit);
            else        wm.RemoveTargetList(unit);
            Plugin.Log?.LogInfo($"[NOXMFD] target.deselect ← '{name}' (id={id}, viaHud={viaHud}).");
        }

        // Make the aircraft's active weapon the first station of the requested type (WPN page bezel
        // key → the weapon aligned with it). The loadout aggregates stations by name, so we match on
        // the same weaponName/shortName BuildLoadout uses and pick the first visible station of that
        // type; the game cycles any duplicate stations of the same type with its own next/prev.
        // Replays the game's own NextWeaponStation() sequence — point the manager at the station,
        // activate it (networked-aware), sync the cockpit HUD — so the marker + select beep come
        // along. No-ops if that weapon is already selected.
        private static void WeaponSelect(CommandEnvelope env)
        {
            string wname = env.wname;
            if (string.IsNullOrEmpty(wname)) { Plugin.Log?.LogInfo("[NOXMFD] weapon.select: empty name — ignored."); return; }

            GameManager.GetLocalAircraft(out Aircraft ac);
            if (ac == null || ac.weaponManager == null || ac.weaponStations == null) return;
            WeaponManager wm = ac.weaponManager;

            WeaponStation target = WeaponSelectors.FindStationByName(ac, wname);
            if (target == null) { Plugin.Log?.LogInfo($"[NOXMFD] weapon.select '{wname}': no matching station — ignored."); return; }

            if (ReferenceEquals(wm.currentWeaponStation, target))
            {
                Plugin.Log?.LogInfo($"[NOXMFD] weapon.select '{wname}': already selected — no-op.");
                return;
            }

            WeaponSelectors.SelectStation(ac, target);
            Plugin.Log?.LogInfo($"[NOXMFD] weapon.select → '{wname}' (station {target.Number}).");
        }

        // ── TGT filter panel (docs/tgt-page.md) ──────────────────────────────────
        // Option A: we don't reimplement the filter — we drive the game's own
        // TargetListSelector singleton, so its prune of the current selection, its live gate on
        // future selections, and the map-icon recolour all come along for free. The singleton is a
        // SceneSingleton present for the whole mission (confirmed by TgtProbe), but every handler
        // still null-guards so it no-ops rather than throws if the game hasn't built it.

        private static List<TargetListSelector_ToggleButton> TgtGroup(TargetListSelector sel, string group)
        {
            switch (group)
            {
                case "faction":  return sel.toggleFactionItems;
                case "category": return sel.toggleUnitTypesItems;
                case "vehicle":  return sel.toggleVehicleTypesItems;
                default:         return null;
            }
        }

        // Resolve the live singleton + a validated toggle for {group, index}; logs and returns null
        // on any miss (absent singleton, unknown group, out-of-range/null toggle). sel is always set.
        private static TargetListSelector_ToggleButton TgtResolve(CommandEnvelope env, string op, out TargetListSelector sel)
        {
            sel = SceneSingleton<TargetListSelector>.i;
            if (sel == null) { Plugin.Log?.LogInfo($"[NOXMFD] {op}: TargetListSelector absent — ignored."); return null; }

            List<TargetListSelector_ToggleButton> list = TgtGroup(sel, env.group);
            if (list == null) { Plugin.Log?.LogInfo($"[NOXMFD] {op}: unknown group '{env.group}' — ignored."); return null; }
            if (env.index < 0 || env.index >= list.Count)
            {
                Plugin.Log?.LogInfo($"[NOXMFD] {op}: index {env.index} out of range for '{env.group}' [{list.Count}] — ignored.");
                return null;
            }
            TargetListSelector_ToggleButton btn = list[env.index];
            if (btn == null) { Plugin.Log?.LogInfo($"[NOXMFD] {op}: null toggle at {env.group}[{env.index}] — ignored."); return null; }
            return btn;
        }

        // Set one filter toggle to an explicit state (not a blind flip — the page mirrors state, so an
        // explicit target is idempotent and survives a dropped click). Set() fires the game's own
        // NeedUpdateIcons → prune + recolour, and the toggle then gates future selections.
        private static void TgtSet(CommandEnvelope env)
        {
            TargetListSelector_ToggleButton btn = TgtResolve(env, "tgt.set", out _);
            if (btn == null) return;
            if (btn.status == env.on) return;   // already there — no-op (avoids a needless prune pass)
            btn.Set(env.on);
            Plugin.Log?.LogInfo($"[NOXMFD] tgt.set {env.group}[{env.index}] = {env.on}.");
        }

        // Right-click "only this": turn every other toggle in the group off, this one on.
        private static void TgtOnly(CommandEnvelope env)
        {
            TargetListSelector_ToggleButton btn = TgtResolve(env, "tgt.only", out TargetListSelector sel);
            if (btn == null) return;
            sel.SetOnlyItem(btn);
            Plugin.Log?.LogInfo($"[NOXMFD] tgt.only {env.group}[{env.index}].");
        }

        // RESET FILTER — all toggles back on. Does NOT re-select anything already cleared.
        private static void TgtReset(CommandEnvelope env)
        {
            TargetListSelector sel = SceneSingleton<TargetListSelector>.i;
            if (sel == null) { Plugin.Log?.LogInfo("[NOXMFD] tgt.reset: TargetListSelector absent — ignored."); return; }
            sel.ResetFilters();
            Plugin.Log?.LogInfo("[NOXMFD] tgt.reset — all filters on.");
        }

        // CLEAR TARGETS — deselect the whole current target list.
        private static void TgtClear(CommandEnvelope env)
        {
            TargetListSelector sel = SceneSingleton<TargetListSelector>.i;
            if (sel == null) { Plugin.Log?.LogInfo("[NOXMFD] tgt.clear: TargetListSelector absent — ignored."); return; }
            sel.DeselectAll();
            Plugin.Log?.LogInfo("[NOXMFD] tgt.clear — deselected all targets.");
        }

        // TGT page's DATALINK button (docs/tgt-datalink-cancel.md): tap deselects datalink-only
        // targets. Same staleness check as TelemetryReader.BuildUnits' Datalink field, duplicated
        // here since it's a one-line read with no shared game-query helper in this codebase yet.
        private static bool IsDatalinkOnly(FactionHQ playerHQ, Unit unit)
        {
            if (unit == null || unit.NetworkHQ == playerHQ) return false;   // only enemy/other-faction targets can be datalink-only
            return !(playerHQ.GetTrackingData(unit.persistentID)?.Observed() ?? false);
        }

        // TGT page's STALE button (docs/tgt-stale-lock.md): tap deselects locked targets whose
        // relayed position the game itself no longer trusts — the same FactionHQ check that swaps a
        // locked target's TGP box for the "?" (outdated) sprite (TargetScreenUI.outdatedSprite), same
        // 20m threshold. Duplicated from TelemetryReader.BuildUnits' Stale field for the reason above.
        private static bool IsStale(FactionHQ playerHQ, Unit unit)
        {
            if (unit == null || unit.NetworkHQ == playerHQ) return false;
            return !playerHQ.IsTargetPositionAccurate(unit, 20f);
        }

        // Shared by TgtClearDatalink/TgtClearStale: bulk-deselect whichever currently-locked targets
        // match the given predicate.
        private static void TgtClearBy(string op, Func<FactionHQ, Unit, bool> predicate)
        {
            TargetListSelector sel = SceneSingleton<TargetListSelector>.i;
            if (sel == null) { Plugin.Log?.LogInfo($"[NOXMFD] {op}: TargetListSelector absent — ignored."); return; }

            GameManager.GetLocalAircraft(out Aircraft ac);
            if (ac == null || ac.weaponManager == null || ac.NetworkHQ == null) return;
            FactionHQ playerHQ = ac.NetworkHQ;

            List<Unit> targets = ac.weaponManager.GetTargetList();
            if (targets == null || targets.Count == 0) return;

            int cleared = 0;
            foreach (Unit unit in new List<Unit>(targets))   // copy: ForceDeselect mutates the live list
            {
                if (!predicate(playerHQ, unit)) continue;
                sel.ForceDeselect(unit);
                cleared++;
            }
            Plugin.Log?.LogInfo($"[NOXMFD] {op} — deselected {cleared} target(s).");
        }

        private static void TgtClearDatalink(CommandEnvelope env) => TgtClearBy("tgt.clear-datalink", IsDatalinkOnly);
        private static void TgtClearStale(CommandEnvelope env)    => TgtClearBy("tgt.clear-stale", IsStale);

        // LASER toggle — keep only lased targets when on.
        private static void TgtLaser(CommandEnvelope env)
        {
            TargetListSelector sel = SceneSingleton<TargetListSelector>.i;
            if (sel == null || sel.toggleLaser == null) { Plugin.Log?.LogInfo("[NOXMFD] tgt.laser: unavailable — ignored."); return; }
            if (sel.toggleLaser.status == env.on) return;
            sel.toggleLaser.Set(env.on);
            Plugin.Log?.LogInfo($"[NOXMFD] tgt.laser = {env.on}.");
        }

        // HUD-follow toggle — mirror the filter to the HUD priority options. Set() fires the game's
        // OnToggleFollowHUD, which applies (on) or resets (off) the whole filter set.
        private static void TgtHud(CommandEnvelope env)
        {
            TargetListSelector sel = SceneSingleton<TargetListSelector>.i;
            if (sel == null || sel.toggleFollowHUD == null) { Plugin.Log?.LogInfo("[NOXMFD] tgt.hud: unavailable — ignored."); return; }
            if (sel.toggleFollowHUD.status == env.on) return;
            sel.toggleFollowHUD.Set(env.on);
            Plugin.Log?.LogInfo($"[NOXMFD] tgt.hud = {env.on}.");
        }

        // HUD OPTIONS — the in-game MFD "HUD OPTIONS" page (SceneSingleton<HUDOptions>). It gates
        // each unit's HUD icon: CheckMaximizeIcon() returns 0 when a unit's category or type is off,
        // so its marker shrinks to a minimized dot (enemy) or vanishes (friendly). Toggling any of
        // these live, then ApplyHUDSettings() to fire OnApplyOptions so the HUD re-renders now
        // rather than after the ~1s idle refresh. Proven in game (issue #20).
        //
        // hud.set flips one toggle within a group; the page reads the current state and names from
        // /hud-options (TelemetryServer.RefreshHudOptions), so indices here are always paired with
        // a list the page fetched from the same source — no hardcoded ordering.
        //   group "category" → listCategories[i]   (FRIENDLY/ENEMY/AIRCRAFT/MISSILES/VEHICLES/…)
        //   group "vehicle"  → listVehicleTypes[i] (TRUCK/UGV/LCV/…)
        //   group "building" → listBuildingTypes[i](CIV/FAC/RDR/…)
        private static void HudSet(CommandEnvelope env)
        {
            HUDOptions opt = SceneSingleton<HUDOptions>.i;
            if (opt == null) { Plugin.Log?.LogInfo("[NOXMFD] hud.set: HUDOptions unavailable — ignored."); return; }

            switch (env.group)
            {
                case "category":
                    if (!InList(opt.listCategories, env.index, "hud.set category")) return;
                    HUDOptions_Category cat = opt.listCategories[env.index];
                    if (cat == null || cat.maximized == env.on) return;   // null / already there
                    cat.Set(env.on);
                    break;
                case "vehicle":
                    if (!InList(opt.listVehicleTypes, env.index, "hud.set vehicle")) return;
                    HUDOptions_ToggleButton veh = opt.listVehicleTypes[env.index];
                    if (veh == null || veh.status == env.on) return;
                    veh.Set(env.on);
                    break;
                case "building":
                    if (!InList(opt.listBuildingTypes, env.index, "hud.set building")) return;
                    HUDOptions_ToggleButton bld = opt.listBuildingTypes[env.index];
                    if (bld == null || bld.status == env.on) return;
                    bld.Set(env.on);
                    break;
                default:
                    Plugin.Log?.LogInfo($"[NOXMFD] hud.set: unknown group '{env.group}' — ignored.");
                    return;
            }
            opt.ApplyHUDSettings();
            Plugin.Log?.LogInfo($"[NOXMFD] hud.set {env.group}[{env.index}] = {env.on}.");
        }

        // Mode tabs (NAV/GUN/A2A/A2G/EW/LOG) — radio buttons, each carrying a saved priority preset.
        // ToggleButtons() does the radio flip and, via the button's own Set(), applies that preset's
        // category/vehicle/building priorities; ApplyHUDSettings() then re-renders the HUD at once.
        private static void HudMode(CommandEnvelope env)
        {
            HUDOptions opt = SceneSingleton<HUDOptions>.i;
            if (opt == null) { Plugin.Log?.LogInfo("[NOXMFD] hud.mode: HUDOptions unavailable — ignored."); return; }
            if (!InList(opt.listModes, env.index, "hud.mode")) return;
            HUDOptions_ToggleButton btn = opt.listModes[env.index];
            if (btn == null) { Plugin.Log?.LogInfo($"[NOXMFD] hud.mode[{env.index}] is null — ignored."); return; }
            opt.ToggleButtons(btn);
            opt.ApplyHUDSettings();
            Plugin.Log?.LogInfo($"[NOXMFD] hud.mode = {env.index}.");
        }

        // Native-HUD declutter — the mod's OWN HudDeclutter flags (HudDeclutterConfig), not the game's
        // HUDOptions. These hide native HUD widgets: env.group is "weapon" (top-right weapon/ammo/CM
        // cluster), "minimap" (bottom-left corner map), "boxes" (boxed heading/speed/alt readouts) or
        // "feed" (native kill-feed ticker, issue #34).
        // Writing the ConfigEntry persists it and fires SettingChanged, so the in-game F1 checkbox
        // follows; HudDeclutter reads the flag each tick and hides/restores within ~0.5s (the minimap
        // within a frame), so there's nothing to apply here. env.on is the desired HIDE state.
        private static void DeclutterSet(CommandEnvelope env)
        {
            switch (env.group)
            {
                case "weapon":  HudDeclutterConfig.SetHideWeaponAmmo(env.on); break;
                case "minimap": HudDeclutterConfig.SetHideMinimap(env.on);    break;
                case "boxes":   HudDeclutterConfig.SetHideTopBoxes(env.on);   break;
                case "feed":    HudDeclutterConfig.SetHideKillFeed(env.on);   break;
                case "wpt":     HudDeclutterConfig.SetHideWaypointCue(env.on); break;
                default:
                    Plugin.Log?.LogInfo($"[NOXMFD] declutter.set: unknown group '{env.group}' — ignored.");
                    return;
            }
            Plugin.Log?.LogInfo($"[NOXMFD] declutter.set {env.group} hide={env.on}.");
        }

        // F-35 master-strip status icons (issue #35) — toggles the same
        // systems the AVN annunciators already read (TelemetryReader.BuildAvn). gear/radar/eng go
        // through the game's own networked Cmd calls, the same path the immersion keybinds use;
        // guns/assist/lights/turret are local, client-only toggles; nvg is a player-camera setting,
        // not per-aircraft, but still gated on a live aircraft so the icon only works in flight,
        // matching the row it lives in. Each game-side call already self-guards on the airframe's
        // own capability (no turret stations, no flight-assist-capable controls filter, etc.), so
        // this just fires it — no capability check duplicated here.
        private static void AvnToggle(CommandEnvelope env)
        {
            GameManager.GetLocalAircraft(out Aircraft ac);
            if (ac == null || ac.disabled) return;

            switch (env.group)
            {
                case "gear":   ac.SetGear(!ac.gearDeployed); break;
                case "radar":  ac.CmdToggleRadar(); break;
                case "guns":   ac.weaponManager?.ToggleGunsLinked(); break;
                case "eng":    ac.CmdToggleIgnition(); break;
                case "assist": ac.TogglePitchLimiter(); break;
                case "nvg":    NightVision.Toggle(); break;
                case "lights": ac.ToggleNavLights(); break;
                case "turret": SceneSingleton<CombatHUD>.i?.ToggleAutoControl(); break;
                default:
                    Plugin.Log?.LogInfo($"[NOXMFD] avn.toggle: unknown group '{env.group}' — ignored.");
                    return;
            }
            Plugin.Log?.LogInfo($"[NOXMFD] avn.toggle {env.group}.");
        }

        // Bounds guard shared by the HUD handlers: true when index addresses a live element.
        private static bool InList<T>(System.Collections.Generic.List<T> list, int index, string who)
        {
            if (list == null || index < 0 || index >= list.Count)
            {
                Plugin.Log?.LogInfo($"[NOXMFD] {who}: index {index} out of range — ignored.");
                return false;
            }
            return true;
        }
    }
}
