using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace NOXMFD
{
    internal sealed class HudPreset
    {
        public string Name = string.Empty;
        public bool HasData;
        public bool[] Categories = Array.Empty<bool>();
        public bool[] Vehicles = Array.Empty<bool>();
        public bool[] Buildings = Array.Empty<bool>();
    }

    // Up to 5 named HUD-filter presets, server-side so any browser can save/load one. Fixed numbered
    // slots (1-5), NOT an arbitrary create/delete list like LayoutStore — "PRESET N" always exists;
    // only its name/data start empty and can be cleared back to empty.
    //
    // Captures/restores the live HUDOptions state directly (categories/vehicles/buildings) — the
    // same three arrays HudCombatModeFilters already snapshots for its own idle baseline. No opaque
    // client-supplied blob (contrast LayoutStore): this is server-owned game state the plugin can
    // read/write itself, not browser state only the browser knows.
    //
    // The raw filter arrays never leave the server: PresetsJson (what /hud-options and /hud-presets
    // both expose) carries only {index,name,hasData} per slot — a browser picks a preset by index,
    // and preset.load applies the arrays straight into HUDOptions here, so there's no reason to ship
    // 7+10+7 booleans down to a page that has nothing to do with them.
    //
    // Static, plugin-lifetime (NOT mission-scoped) — presets must survive a mission restart AND a
    // full game restart, and must be saved/loaded at the main menu (same reasoning as LayoutStore).
    internal static class HudPresetStore
    {
        public const int SlotCount = 5;

        private static readonly HudPreset[] _slots = BuildEmptySlots();
        // Which slot SAVE targets and the bottom label names — plain in-memory, not persisted: it's
        // a UI selection, not saved data, so it resets to 1 on a fresh session.
        private static int _current = 1;

        // Server-thread-readable cache, same threading contract as LayoutStore.LayoutsJson: every
        // mutator below runs on the Unity main thread only (CommandDispatcher.Drain / the Keybinds
        // poll), and rebuilds this string synchronously as its last step.
        internal static volatile string PresetsJson = "{\"current\":1,\"presets\":[]}";

        private static string FilePath =>
            Path.Combine(BepInEx.Paths.ConfigPath, "com.roque.NOXMFD.hud-presets.json");

        private static HudPreset[] BuildEmptySlots()
        {
            var slots = new HudPreset[SlotCount];
            for (int i = 0; i < SlotCount; i++) slots[i] = new HudPreset();
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
                    Plugin.Log?.LogWarning($"[NOXMFD] hud-presets file unreadable, starting empty: {ex.Message}");
                }
            }
            RefreshSummary();
        }

        // Parameterized (not closed over _slots) so SelfCheck below can round-trip a throwaway array
        // without touching the real one the plugin is actually using.
        private static void ParseFrom(Dictionary<string, object?> root, HudPreset[] slots)
        {
            if (!(root.TryGetValue("presets", out object? pv) && pv is List<object?> list)) return;
            for (int i = 0; i < slots.Length && i < list.Count; i++)
            {
                if (list[i] is not Dictionary<string, object?> d) continue;
                HudPreset s = slots[i];
                s.Name = d.TryGetValue("name", out object? nm) ? (nm as string ?? string.Empty) : string.Empty;
                s.HasData = d.TryGetValue("hasData", out object? hd) && hd is bool hb && hb;
                s.Categories = ParseBoolArray(d, "categories");
                s.Vehicles = ParseBoolArray(d, "vehicles");
                s.Buildings = ParseBoolArray(d, "buildings");
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
        private static string BuildSummaryJson(int current, HudPreset[] slots)
        {
            var sb = new StringBuilder(256);
            sb.Append("{\"current\":").Append(current).Append(",\"presets\":[");
            for (int i = 0; i < slots.Length; i++)
            {
                if (i > 0) sb.Append(',');
                HudPreset s = slots[i];
                sb.Append("{\"index\":").Append(i + 1)
                  .Append(",\"name\":\"").Append(TelemetryServer.EscapeJson(s.Name))
                  .Append("\",\"hasData\":").Append(s.HasData ? "true" : "false")
                  .Append('}');
            }
            sb.Append("]}");
            return sb.ToString();
        }

        // Same reasoning: parameterized, not closed over _slots.
        private static string BuildDiskJson(HudPreset[] slots)
        {
            var sb = new StringBuilder(512);
            sb.Append("{\"presets\":[");
            for (int i = 0; i < slots.Length; i++)
            {
                if (i > 0) sb.Append(',');
                HudPreset s = slots[i];
                sb.Append("{\"name\":\"").Append(TelemetryServer.EscapeJson(s.Name))
                  .Append("\",\"hasData\":").Append(s.HasData ? "true" : "false")
                  .Append(",\"categories\":").Append(BoolArrayJson(s.Categories))
                  .Append(",\"vehicles\":").Append(BoolArrayJson(s.Vehicles))
                  .Append(",\"buildings\":").Append(BoolArrayJson(s.Buildings))
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

        // Current preset's index/name, folded into TelemetryServer.RefreshHudOptions' own payload —
        // the bottom label rides the HUD page's existing 1.2s poll rather than a second endpoint.
        public static int CurrentIndex => _current;
        public static string CurrentName => _slots[_current - 1].Name;

        // ── mutators (CommandDispatcher: preset.save / .rename / .delete / .load) ───────────

        // Captures the LIVE HUDOptions state into whichever slot is current, under the given name —
        // always targets `_current`, never an index the client picks (the client only ever supplies
        // a name). Rejects an empty name/unavailable HUDOptions rather than silently saving a
        // blank/stale slot.
        public static bool Save(string? name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            HUDOptions opt = SceneSingleton<HUDOptions>.i;
            if (opt == null) return false;

            HudPreset slot = _slots[_current - 1];
            slot.Name = name!.Trim();
            slot.Categories = SnapshotCategories(opt);
            slot.Vehicles = SnapshotVehicles(opt);
            slot.Buildings = SnapshotBuildings(opt);
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
            _slots[index - 1] = new HudPreset();
            Persist();
            return true;
        }

        // Applies a preset's saved filters onto the live HUD and makes it the current slot (so the
        // bottom label follows it and the next SAVE overwrites it) — the direct-recall behaviour the
        // 5 KEY-page keybinds and the LOAD picker's onPick both drive through this one entry point.
        // An empty slot (never saved) still becomes current — nothing to apply, but selectable, so a
        // player can press "preset 3" then SAVE into it without ever having loaded data there first.
        public static bool LoadPreset(int index)
        {
            if (index < 1 || index > SlotCount) return false;
            _current = index;
            HUDOptions opt = SceneSingleton<HUDOptions>.i;
            HudPreset slot = _slots[index - 1];
            if (opt != null && slot.HasData)
            {
                Apply(opt, slot);
                opt.ApplyHUDSettings();
                // A preset load is a player-driven HUD edit like any hud.set/hud.mode click — while
                // idle, it becomes part of what gets restored the next time A/A or A/G exits back to
                // idle (HudCombatModeFilters), so loading a preset at idle isn't silently discarded
                // by that unrelated feature's own baseline restore later.
                HudCombatModeFilters.CaptureIfIdle();
            }
            RefreshSummary();
            return true;
        }

        private static void Persist()
        {
            RefreshSummary();
            // Back up whatever was on disk BEFORE overwriting it — see RouteStore.Save's comment.
            try { ConfigBackup.BackupIfExists(FilePath); File.WriteAllText(FilePath, BuildDiskJson(_slots)); }
            catch (Exception ex) { Plugin.Log?.LogWarning($"[NOXMFD] failed to persist hud presets: {ex.Message}"); }
        }

        private static bool[] SnapshotCategories(HUDOptions opt)
        {
            var arr = new bool[opt.listCategories.Count];
            for (int i = 0; i < arr.Length; i++) arr[i] = opt.listCategories[i].maximized;
            return arr;
        }

        private static bool[] SnapshotVehicles(HUDOptions opt)
        {
            var arr = new bool[opt.listVehicleTypes.Count];
            for (int i = 0; i < arr.Length; i++) arr[i] = opt.listVehicleTypes[i].status;
            return arr;
        }

        private static bool[] SnapshotBuildings(HUDOptions opt)
        {
            var arr = new bool[opt.listBuildingTypes.Count];
            for (int i = 0; i < arr.Length; i++) arr[i] = opt.listBuildingTypes[i].status;
            return arr;
        }

        private static void Apply(HUDOptions opt, HudPreset slot)
        {
            int n = Math.Min(slot.Categories.Length, opt.listCategories.Count);
            for (int i = 0; i < n; i++) opt.listCategories[i].Set(slot.Categories[i]);

            n = Math.Min(slot.Vehicles.Length, opt.listVehicleTypes.Count);
            for (int i = 0; i < n; i++) opt.listVehicleTypes[i].Set(slot.Vehicles[i]);

            n = Math.Min(slot.Buildings.Length, opt.listBuildingTypes.Count);
            for (int i = 0; i < n; i++) opt.listBuildingTypes[i].Set(slot.Buildings[i]);
        }

        // Verifies this store's own JSON round-trip. Save/LoadPreset/Apply all touch the live
        // HUDOptions singleton and can only be verified in-game; this is the pure data-plumbing
        // slice (write -> parse -> read) where a silent field-name typo or off-by-one would
        // otherwise corrupt saved presets without ever throwing. Runs entirely against a throwaway
        // slot array — never touches the real _slots/_current the plugin is actually using.
        public static void SelfCheck()
        {
            void Check(bool cond, string what)
            {
                if (!cond) throw new Exception($"HudPresetStore.SelfCheck failed: {what}");
            }

            HudPreset[] slots = BuildEmptySlots();
            slots[0].Name = "Dogfight";
            slots[0].HasData = true;
            slots[0].Categories = new[] { true, false, true };   // a false in the middle, not all-same
            slots[0].Vehicles = new[] { false };
            slots[0].Buildings = Array.Empty<bool>();

            string disk = BuildDiskJson(slots);
            Check(JsonLite.Parse(disk) is Dictionary<string, object?>, "disk JSON parses back to an object");
            HudPreset[] roundTripped = BuildEmptySlots();
            ParseFrom((Dictionary<string, object?>)JsonLite.Parse(disk)!, roundTripped);

            Check(roundTripped[0].Name == "Dogfight", "name round-trips through disk JSON");
            Check(roundTripped[0].HasData, "hasData round-trips true");
            Check(!roundTripped[1].HasData, "an untouched slot stays hasData=false");
            Check(roundTripped[0].Categories.Length == 3 && roundTripped[0].Categories[0] &&
                  !roundTripped[0].Categories[1] && roundTripped[0].Categories[2],
                  "bool array round-trips including a false in the middle, not just all-true/all-false");
            Check(roundTripped[0].Vehicles.Length == 1 && !roundTripped[0].Vehicles[0], "single-element array round-trips");
            Check(roundTripped[0].Buildings.Length == 0, "empty array round-trips as empty, not null/missing");

            string summary = BuildSummaryJson(3, slots);
            Check(summary.Contains("\"current\":3"), "summary carries whatever current index it's given");
            Check(summary.Contains("\"index\":1") && summary.Contains("Dogfight"), "summary names the saved slot");
            Check(!summary.Contains("categories") && !summary.Contains("vehicles") && !summary.Contains("buildings"),
                "summary never leaks the raw filter arrays to the client — only index/name/hasData");
        }
    }
}
