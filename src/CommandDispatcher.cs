using System;
using System.Collections.Generic;

namespace NOXMFD
{
    // ── Inbound command channel ──────────────────────────────────────────────────
    // The web client POSTs JSON commands to /command; TelemetryServer parses + queues them on a
    // server thread, and this dispatcher drains the queue on the Unity main thread (called from
    // TelemetryReader.Update) and runs the matching handler. See todo/write-command-channel.md.
    //
    // Handlers MUST only run on the main thread (they touch game state), keep themselves
    // idempotent, validate against live state, and prefer the game's own high-level input methods
    // (e.g. CombatHUD.SelectUnit) over low-level setters so the in-cockpit side effects — marker
    // colour, audio, map sync — come along for free.

    // Wire envelope: { "cmd": "target.select", "args": { "id": 1234 } }. JsonUtility-friendly
    // ([Serializable] classes, lowercase fields). args is a flat union of every command's params —
    // each handler reads the fields it cares about; absent ones default to 0.
    [Serializable]
    internal class CommandEnvelope
    {
        public string      cmd;
        public CommandArgs args;
    }

    [Serializable]
    internal class CommandArgs
    {
        public long id;   // target unit persistentID (target.select)
    }

    internal static class CommandDispatcher
    {
        private static readonly Dictionary<string, Action<CommandArgs>> _handlers =
            new Dictionary<string, Action<CommandArgs>>(StringComparer.Ordinal)
            {
                { "target.select", TargetSelect },
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
                if (_handlers.TryGetValue(env.cmd ?? string.Empty, out Action<CommandArgs> handler))
                {
                    try { handler(env.args ?? new CommandArgs()); }
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
        private static void TargetSelect(CommandArgs args)
        {
            uint id = unchecked((uint)args.id);
            if (id == 0) return;

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
    }
}
