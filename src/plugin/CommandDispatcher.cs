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
        public string group;   // tgt.set / tgt.only : "faction" | "category" | "vehicle"
        public int    index;   // tgt.set / tgt.only : toggle index within the group
        public bool   on;      // tgt.set / tgt.laser / tgt.hud : desired toggle state
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
                { "tgt.laser",       TgtLaser },
                { "tgt.hud",         TgtHud },
                { "hud.category",    HudCategory },
            };

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

            WeaponManager wm = ac.weaponManager;
            string name = unit.definition?.unitName ?? "?";
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

            WeaponStation target = null;
            foreach (WeaponStation st in ac.weaponStations)
            {
                if (st == null) continue;
                WeaponInfo info = st.WeaponInfo;
                if (info == null || info.hideInDisplay) continue;
                string name = !string.IsNullOrEmpty(info.weaponName) ? info.weaponName : info.shortName;
                if (string.Equals(name, wname, StringComparison.Ordinal)) { target = st; break; }
            }
            if (target == null) { Plugin.Log?.LogInfo($"[NOXMFD] weapon.select '{wname}': no matching station — ignored."); return; }

            if (ReferenceEquals(wm.currentWeaponStation, target))
            {
                Plugin.Log?.LogInfo($"[NOXMFD] weapon.select '{wname}': already selected — no-op.");
                return;
            }

            wm.currentWeaponStation = target;
            ac.SetActiveStation(target.Number);
            CombatHUD hud = SceneSingleton<CombatHUD>.i;
            if (hud != null && ReferenceEquals(hud.aircraft, ac)) hud.ShowWeaponStation(target);
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

        // HUD OPTIONS — maximize/minimize one of the icon categories (FRIENDLY / ENEMY / AIRCRAFT /
        // MISSILES / VEHICLES / BUILDINGS / SHIPS). This is the in-game MFD "HUD OPTIONS" page:
        // HUDOptions.CheckMaximizeIcon() scales a unit's HUD icon to 0 when its category isn't
        // maximized, so toggling a category off hides those icons live. Category.Set() updates the
        // maximize button; ApplyHUDSettings() fires OnApplyOptions so the change takes effect now
        // rather than after the ~1s idle refresh.
        //
        // ponytail: env.index is a raw index into listCategories, whose order is set in the game's
        // Unity inspector, not by us — fine for the proof-of-concept (index 2 = AIRCRAFT), but the
        // real page must read the categories' names/order at runtime rather than trust a constant.
        // Upgrade path: emit the category list as telemetry and address by name.
        private static void HudCategory(CommandEnvelope env)
        {
            HUDOptions opt = SceneSingleton<HUDOptions>.i;
            if (opt == null || opt.listCategories == null) { Plugin.Log?.LogInfo("[NOXMFD] hud.category: unavailable — ignored."); return; }
            if (env.index < 0 || env.index >= opt.listCategories.Count)
            {
                Plugin.Log?.LogInfo($"[NOXMFD] hud.category: index {env.index} out of range (0..{opt.listCategories.Count - 1}) — ignored.");
                return;
            }
            HUDOptions_Category cat = opt.listCategories[env.index];
            if (cat == null) { Plugin.Log?.LogInfo($"[NOXMFD] hud.category[{env.index}] is null — ignored."); return; }
            if (cat.maximized == env.on) return;   // already there
            cat.Set(env.on);
            opt.ApplyHUDSettings();
            Plugin.Log?.LogInfo($"[NOXMFD] hud.category[{env.index}].maximized = {env.on}.");
        }
    }
}
