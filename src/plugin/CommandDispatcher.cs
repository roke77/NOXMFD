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
        public long   id;   // target unit persistentID (target.select)
    }

    internal static class CommandDispatcher
    {
        private static readonly Dictionary<string, Action<CommandEnvelope>> _handlers =
            new Dictionary<string, Action<CommandEnvelope>>(StringComparer.Ordinal)
            {
                { "target.select",   TargetSelect },
                { "target.deselect", TargetDeselect },
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

        // Drop a unit from the player's weapon target list (TGL page bezel button). Mirrors the
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
    }
}
