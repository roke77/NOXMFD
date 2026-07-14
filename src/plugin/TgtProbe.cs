using System.Collections.Generic;
using System.Text;
using BepInEx.Configuration;
using UnityEngine.UI;

namespace NOXMFD
{
    // ── Throwaway diagnostic: does the game's TargetListSelector singleton exist? ──────────
    // Answers the one open question blocking the TGT MFD page (Option A — drive the game's own
    // filter panel; see docs/tgt-page.md). We need to know whether SceneSingleton<TargetListSelector>.i
    // is non-null WITHOUT the player opening the in-cockpit TGT MFD page, and what its toggle
    // arrays look like (count + labels + ordering) so the future command handlers can index them.
    //
    // Gated behind Diagnostics > TgtProbe (default OFF). Logs only when the observed state CHANGES
    // (presence flip or structure change) so it never spams. DELETE this file (and its Bind/Tick
    // calls) once the question is answered — it reaches into game internals purely to learn them.
    // ponytail: diagnostic-only, remove after the singleton question is settled.
    internal static class TgtProbe
    {
        public static bool Enabled;
        private static string _lastSig = "";

        public static void Bind(ConfigFile config)
        {
            var probe = config.Bind("Diagnostics", "TgtProbe", false,
                "Throwaway probe: logs whether the game's TargetListSelector (TGT filter panel) singleton exists and dumps its toggle labels/ordering. For planning the TGT MFD page — leave OFF for normal play.");
            Enabled = probe.Value;
            probe.SettingChanged += (_, __) => Enabled = probe.Value;
        }

        // Called from the reader's slow tick. Cheap: one singleton read + a signature compare;
        // only formats + logs when the state actually changes.
        public static void Tick()
        {
            if (!Enabled) return;
            string sig = Describe(SceneSingleton<TargetListSelector>.i);
            if (sig == _lastSig) return;
            _lastSig = sig;
            Plugin.Log?.LogInfo("[NOXMFD][TgtProbe] " + sig);
        }

        private static string Describe(TargetListSelector sel)
        {
            if (sel == null) return "TargetListSelector.i = NULL (in-cockpit TGT MFD not opened yet?)";
            var sb = new StringBuilder();
            sb.Append("TargetListSelector.i PRESENT");
            sb.Append(" | screen=").Append(sel.screen != null ? "set" : "null");
            AppendToggles(sb, "faction", sel.toggleFactionItems);
            AppendToggles(sb, "unitType", sel.toggleUnitTypesItems);
            AppendToggles(sb, "vehType", sel.toggleVehicleTypesItems);
            sb.Append(" | laser=").Append(sel.toggleLaser != null ? "set" : "null");
            sb.Append(" | hud=").Append(sel.toggleFollowHUD != null ? "set" : "null");
            return sb.ToString();
        }

        // "faction[2]: FRIENDLY, ENEMY" — count then each toggle's label (newlines from the game's
        // "_"→"\n" wrap reversed back to "_") so we learn both size and array ordering.
        private static void AppendToggles(StringBuilder sb, string name, List<TargetListSelector_ToggleButton> list)
        {
            sb.Append(" | ").Append(name).Append('[');
            if (list == null) { sb.Append("null]"); return; }
            sb.Append(list.Count).Append("]:");
            for (int i = 0; i < list.Count; i++)
            {
                TargetListSelector_ToggleButton b = list[i];
                Text? lbl = b != null ? b.label : null;
                sb.Append(i == 0 ? " " : ", ").Append(lbl != null ? lbl.text.Replace("\n", "_") : "?");
            }
        }
    }
}
