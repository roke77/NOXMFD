using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace NOXMFD
{
    internal sealed class TgtPreset
    {
        public string Name = string.Empty;
        public bool HasData;
        public bool[] Faction = Array.Empty<bool>();
        public bool[] Category = Array.Empty<bool>();
        public bool[] Vehicle = Array.Empty<bool>();
        public bool Laser;
        public bool Hud;
    }

    // Up to 5 named TGT-filter presets, server-side so any browser can save/load one. Fixed numbered
    // slots (1-5), NOT an arbitrary create/delete list like LayoutStore — "PRESET N" always exists;
    // only its name/data start empty and can be cleared back to empty. Same shape as HudPresetStore
    // (docs/hud-presets.md), applied to TargetListSelector's filters instead of HUDOptions' — see
    // docs/tgt-presets.md.
    //
    // Captures/restores the live TargetListSelector state directly (faction/category/vehicle toggle
    // arrays, plus the standalone laser/HUD-follow toggles) — the same fields CommandDispatcher's
    // tgt.set/tgt.laser/tgt.hud already read/write. No opaque client-supplied blob: this is
    // server-owned game state the plugin can read/write itself, not browser state only the browser
    // knows.
    //
    // The raw filter arrays never leave the server: PresetsJson (what the 'tgt' telemetry block and
    // /tgt-presets both expose) carries only {index,name,hasData} per slot — a browser picks a
    // preset by index, and preset.load applies the arrays straight into TargetListSelector here.
    //
    // Static, plugin-lifetime (NOT mission-scoped) — presets must survive a mission restart AND a
    // full game restart, same reasoning as HudPresetStore/RouteStore/LayoutStore.
    internal static class TgtPresetStore
    {
        public const int SlotCount = 5;

        private static readonly TgtPreset[] _slots = BuildEmptySlots();
        // Which slot SAVE targets and the bottom label names — plain in-memory, not persisted: it's
        // a UI selection, not saved data, so it resets to 1 on a fresh session.
        private static int _current = 1;

        // Server-thread-readable cache, same threading contract as HudPresetStore.PresetsJson: every
        // mutator below runs on the Unity main thread only (CommandDispatcher.Drain / the Keybinds
        // poll), and rebuilds this string synchronously as its last step.
        internal static volatile string PresetsJson = "{\"current\":1,\"presets\":[]}";

        private static string FilePath =>
            Path.Combine(BepInEx.Paths.ConfigPath, "com.roque.NOXMFD.tgt-presets.json");

        private static TgtPreset[] BuildEmptySlots()
        {
            var slots = new TgtPreset[SlotCount];
            for (int i = 0; i < SlotCount; i++) slots[i] = new TgtPreset();
            return slots;
        }

        // ── lifecycle ────────────────────────────────────────────────────────────────────────

        public static void Load()
        {
            if (File.Exists(FilePath))
            {
                try
                {
                    string text = File.ReadAllText(FilePath);
                    if (JsonLite.Parse(text) is Dictionary<string, object?> root) ParseFrom(root, _slots);
                }
                catch (Exception ex)
                {
                    Plugin.Log?.LogWarning($"[NOXMFD] tgt-presets file unreadable, starting empty: {ex.Message}");
                }
            }
            RefreshSummary();
        }

        // Parameterized (not closed over _slots) so SelfCheck below can round-trip a throwaway array
        // without touching the real one the plugin is actually using.
        private static void ParseFrom(Dictionary<string, object?> root, TgtPreset[] slots)
        {
            if (!(root.TryGetValue("presets", out object? pv) && pv is List<object?> list)) return;
            for (int i = 0; i < slots.Length && i < list.Count; i++)
            {
                if (list[i] is not Dictionary<string, object?> d) continue;
                TgtPreset s = slots[i];
                s.Name = d.TryGetValue("name", out object? nm) ? (nm as string ?? string.Empty) : string.Empty;
                s.HasData = d.TryGetValue("hasData", out object? hd) && hd is bool hb && hb;
                s.Faction = ParseBoolArray(d, "faction");
                s.Category = ParseBoolArray(d, "category");
                s.Vehicle = ParseBoolArray(d, "vehicle");
                s.Laser = d.TryGetValue("laser", out object? lz) && lz is bool lb && lb;
                s.Hud = d.TryGetValue("hud", out object? hu) && hu is bool ub && ub;
            }
        }

        private static bool[] ParseBoolArray(Dictionary<string, object?> d, string key)
        {
            if (!(d.TryGetValue(key, out object? v) && v is List<object?> list)) return Array.Empty<bool>();
            var arr = new bool[list.Count];
            for (int i = 0; i < list.Count; i++) arr[i] = list[i] is bool b && b;
            return arr;
        }

        // ── reads (the bottom label + the LOAD picker) ──────────────────────────────────────

        private static void RefreshSummary() { PresetsJson = BuildSummaryJson(_current, _slots); }

        // Parameterized (not closed over _current/_slots) so SelfCheck below can exercise it against
        // a throwaway array without touching the real state the plugin is actually using.
        private static string BuildSummaryJson(int current, TgtPreset[] slots)
        {
            var sb = new StringBuilder(256);
            sb.Append("{\"current\":").Append(current).Append(",\"presets\":[");
            for (int i = 0; i < slots.Length; i++)
            {
                if (i > 0) sb.Append(',');
                TgtPreset s = slots[i];
                sb.Append("{\"index\":").Append(i + 1)
                  .Append(",\"name\":\"").Append(JsonLite.EscapeJson(s.Name))
                  .Append("\",\"hasData\":").Append(s.HasData ? "true" : "false")
                  .Append('}');
            }
            sb.Append("]}");
            return sb.ToString();
        }

        // Same reasoning: parameterized, not closed over _slots.
        private static string BuildDiskJson(TgtPreset[] slots)
        {
            var sb = new StringBuilder(512);
            sb.Append("{\"presets\":[");
            for (int i = 0; i < slots.Length; i++)
            {
                if (i > 0) sb.Append(',');
                TgtPreset s = slots[i];
                sb.Append("{\"name\":\"").Append(JsonLite.EscapeJson(s.Name))
                  .Append("\",\"hasData\":").Append(s.HasData ? "true" : "false")
                  .Append(",\"faction\":").Append(BoolArrayJson(s.Faction))
                  .Append(",\"category\":").Append(BoolArrayJson(s.Category))
                  .Append(",\"vehicle\":").Append(BoolArrayJson(s.Vehicle))
                  .Append(",\"laser\":").Append(s.Laser ? "true" : "false")
                  .Append(",\"hud\":").Append(s.Hud ? "true" : "false")
                  .Append('}');
            }
            sb.Append("]}");
            return sb.ToString();
        }

        private static string BoolArrayJson(bool[] arr)
        {
            var sb = new StringBuilder(arr.Length * 6);
            sb.Append('[');
            for (int i = 0; i < arr.Length; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append(arr[i] ? "true" : "false");
            }
            sb.Append(']');
            return sb.ToString();
        }

        // Current preset's index/name, folded into the 'tgt' telemetry block's own payload — the
        // bottom label rides the TGT page's existing telemetry stream rather than a second endpoint.
        public static int CurrentIndex => _current;
        public static string CurrentName => _slots[_current - 1].Name;

        // ── mutators (CommandDispatcher: tgt-preset.save / .rename / .delete / .load) ───────

        // Captures the LIVE TargetListSelector state into whichever slot is current, under the given
        // name — always targets `_current`, never an index the client picks (the client only ever
        // supplies a name). Rejects an empty name/unavailable TargetListSelector rather than silently
        // saving a blank/stale slot.
        public static bool Save(string? name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            TargetListSelector sel = SceneSingleton<TargetListSelector>.i;
            if (sel == null) return false;

            TgtPreset slot = _slots[_current - 1];
            slot.Name = name!.Trim();
            slot.Faction = SnapshotToggles(sel.toggleFactionItems);
            slot.Category = SnapshotToggles(sel.toggleUnitTypesItems);
            slot.Vehicle = SnapshotToggles(sel.toggleVehicleTypesItems);
            slot.Laser = sel.toggleLaser != null && sel.toggleLaser.status;
            slot.Hud = sel.toggleFollowHUD != null && sel.toggleFollowHUD.status;
            slot.HasData = true;
            Persist();
            return true;
        }

        public static bool Rename(int index, string? name)
        {
            if (index < 1 || index > SlotCount || string.IsNullOrEmpty(name)) return false;
            _slots[index - 1].Name = name!.Trim();
            Persist();
            return true;
        }

        // Clears the slot back to empty (name + data) — the slot itself always exists (1-5 are fixed),
        // so "delete" can't remove it, only blank it.
        public static bool Delete(int index)
        {
            if (index < 1 || index > SlotCount) return false;
            _slots[index - 1] = new TgtPreset();
            Persist();
            return true;
        }

        // Applies a preset's saved filters onto the live TGT panel and makes it the current slot (so
        // the bottom label follows it and the next SAVE overwrites it) — the direct-recall behaviour
        // the 5 KEY-page keybinds and the LOAD picker's onPick both drive through this one entry
        // point. An empty slot (never saved) still becomes current — nothing to apply, but selectable,
        // so a player can press "preset 3" then SAVE into it without ever having loaded data there
        // first (mirrors HudPresetStore.LoadPreset).
        public static bool LoadPreset(int index)
        {
            if (index < 1 || index > SlotCount) return false;
            _current = index;
            TargetListSelector sel = SceneSingleton<TargetListSelector>.i;
            TgtPreset slot = _slots[index - 1];
            if (sel != null && slot.HasData) Apply(sel, slot);
            RefreshSummary();
            return true;
        }

        private static void Persist()
        {
            RefreshSummary();
            // Back up whatever was on disk BEFORE overwriting it — see RouteStore.Save's comment.
            try { ConfigBackup.BackupIfExists(FilePath); File.WriteAllText(FilePath, BuildDiskJson(_slots)); }
            catch (Exception ex) { Plugin.Log?.LogWarning($"[NOXMFD] failed to persist tgt presets: {ex.Message}"); }
        }

        private static bool[] SnapshotToggles(List<TargetListSelector_ToggleButton> list)
        {
            if (list == null) return Array.Empty<bool>();
            var arr = new bool[list.Count];
            for (int i = 0; i < arr.Length; i++) arr[i] = list[i] != null && list[i].status;
            return arr;
        }

        private static void Apply(TargetListSelector sel, TgtPreset slot)
        {
            ApplyToggles(sel.toggleFactionItems, slot.Faction);
            ApplyToggles(sel.toggleUnitTypesItems, slot.Category);
            ApplyToggles(sel.toggleVehicleTypesItems, slot.Vehicle);
            // Set() fires the game's own NeedUpdateIcons -> prune + recolour (CommandDispatcher.TgtSet's
            // own comment) — same early-return-if-unchanged guard, so restoring an already-matching
            // toggle doesn't pay for a needless prune pass.
            if (sel.toggleLaser != null && sel.toggleLaser.status != slot.Laser) sel.toggleLaser.Set(slot.Laser);
            if (sel.toggleFollowHUD != null && sel.toggleFollowHUD.status != slot.Hud) sel.toggleFollowHUD.Set(slot.Hud);
        }

        private static void ApplyToggles(List<TargetListSelector_ToggleButton> list, bool[] values)
        {
            if (list == null) return;
            int n = Math.Min(list.Count, values.Length);
            for (int i = 0; i < n; i++)
                if (list[i] != null && list[i].status != values[i]) list[i].Set(values[i]);
        }

        // Verifies this store's own JSON round-trip. Save/LoadPreset/Apply all touch the live
        // TargetListSelector singleton and can only be verified in-game; this is the pure
        // data-plumbing slice (write -> parse -> read) where a silent field-name typo or off-by-one
        // would otherwise corrupt saved presets without ever throwing — same reasoning as
        // HudPresetStore.SelfCheck. Runs entirely against a throwaway slot array — never touches the
        // real _slots/_current the plugin is actually using.
        public static void SelfCheck()
        {
            void Check(bool cond, string what)
            {
                if (!cond) throw new Exception($"TgtPresetStore.SelfCheck failed: {what}");
            }

            TgtPreset[] slots = BuildEmptySlots();
            slots[0].Name = "BVR";
            slots[0].HasData = true;
            slots[0].Faction = new[] { true, false };       // a false in the middle, not all-same
            slots[0].Category = new[] { true, true, false };
            slots[0].Vehicle = Array.Empty<bool>();
            slots[0].Laser = true;
            slots[0].Hud = false;

            string disk = BuildDiskJson(slots);
            Check(JsonLite.Parse(disk) is Dictionary<string, object?>, "disk JSON parses back to an object");
            TgtPreset[] roundTripped = BuildEmptySlots();
            ParseFrom((Dictionary<string, object?>)JsonLite.Parse(disk)!, roundTripped);

            Check(roundTripped[0].Name == "BVR", "name round-trips through disk JSON");
            Check(roundTripped[0].HasData, "hasData round-trips true");
            Check(!roundTripped[1].HasData, "an untouched slot stays hasData=false");
            Check(roundTripped[0].Faction.Length == 2 && roundTripped[0].Faction[0] && !roundTripped[0].Faction[1],
                  "bool array round-trips including a false, not just all-true/all-false");
            Check(roundTripped[0].Category.Length == 3 && roundTripped[0].Category[2] == false,
                  "a second bool array round-trips independently of the first");
            Check(roundTripped[0].Vehicle.Length == 0, "empty array round-trips as empty, not null/missing");
            Check(roundTripped[0].Laser, "laser round-trips true");
            Check(!roundTripped[0].Hud, "hud round-trips false");

            string summary = BuildSummaryJson(3, slots);
            Check(summary.Contains("\"current\":3"), "summary carries whatever current index it's given");
            Check(summary.Contains("\"index\":1") && summary.Contains("BVR"), "summary names the saved slot");
            Check(!summary.Contains("faction") && !summary.Contains("category") && !summary.Contains("vehicle") &&
                  !summary.Contains("laser") && !summary.Contains("hud"),
                "summary never leaks the raw filter state to the client — only index/name/hasData");
        }
    }
}
