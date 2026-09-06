using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace NOXMFD
{
    internal sealed class TgtPreset : IPresetSlot
    {
        public string Name { get; set; } = string.Empty;
        public bool HasData { get; set; }
        public bool[] Faction = Array.Empty<bool>();
        public bool[] Category = Array.Empty<bool>();
        public bool[] Vehicle = Array.Empty<bool>();
        // Parallel toggle-label arrays, captured alongside the positional bool arrays above — the
        // vehicle list in particular is built at runtime from Encyclopedia.i.vehicleTypes, so a game
        // update that inserts or reorders a vehicle type would otherwise apply a saved value to the
        // wrong toggle. Empty on a preset saved before this existed; TgtPresetStore.ApplyToggles falls
        // back to positional application in that case, so old saves keep working exactly as before.
        public string[] FactionNames = Array.Empty<string>();
        public string[] CategoryNames = Array.Empty<string>();
        public string[] VehicleNames = Array.Empty<string>();
        public bool Laser;
        public bool Hud;
    }

    // Up to 5 named TGT-filter presets, server-side so any browser can save/load one. Fixed numbered
    // slots (1-5), NOT an arbitrary create/delete list like LayoutStore — "PRESET N" always exists;
    // only its name/data start empty and can be cleared back to empty. Same shape as HudPresetStore
    // (docs/hud-presets.md), applied to TargetListSelector's filters instead of HUDOptions' — see
    // docs/tgt-presets.md. Shares its fixed-slot/summary-JSON/persistence plumbing with HudPresetStore
    // via PresetSlots — only the live capture/apply logic below is TGT-specific.
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

        private static readonly TgtPreset[] _slots = PresetSlots.Empty<TgtPreset>(SlotCount);
        // Which slot SAVE targets and the preset label names — plain in-memory, not persisted: it's
        // a UI selection, not saved data, so it resets to 1 on a fresh session.
        private static int _current = 1;

        // Server-thread-readable cache, same threading contract as HudPresetStore.PresetsJson: every
        // mutator below runs on the Unity main thread only (CommandDispatcher.Drain / the Keybinds
        // poll), and rebuilds this string synchronously as its last step.
        internal static volatile string PresetsJson = "{\"current\":1,\"presets\":[]}";

        private static string FilePath =>
            Path.Combine(BepInEx.Paths.ConfigPath, "com.roque.NOXMFD.tgt-presets.json");

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
                PresetSlots.ReadNameAndHasData(d, s);
                s.Faction = PresetSlots.ParseBoolArray(d, "faction");
                s.Category = PresetSlots.ParseBoolArray(d, "category");
                s.Vehicle = PresetSlots.ParseBoolArray(d, "vehicle");
                s.FactionNames = PresetSlots.ParseStringArray(d, "factionNames");
                s.CategoryNames = PresetSlots.ParseStringArray(d, "categoryNames");
                s.VehicleNames = PresetSlots.ParseStringArray(d, "vehicleNames");
                s.Laser = d.TryGetValue("laser", out object? lz) && lz is bool lb && lb;
                s.Hud = d.TryGetValue("hud", out object? hu) && hu is bool ub && ub;
            }
        }

        // ── reads (the preset label + the LOAD picker) ──────────────────────────────────────

        private static void RefreshSummary() { PresetsJson = PresetSlots.SummaryJson(_current, _slots); }

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
                  .Append(",\"faction\":").Append(PresetSlots.BoolArrayJson(s.Faction))
                  .Append(",\"category\":").Append(PresetSlots.BoolArrayJson(s.Category))
                  .Append(",\"vehicle\":").Append(PresetSlots.BoolArrayJson(s.Vehicle))
                  .Append(",\"factionNames\":").Append(PresetSlots.StringArrayJson(s.FactionNames))
                  .Append(",\"categoryNames\":").Append(PresetSlots.StringArrayJson(s.CategoryNames))
                  .Append(",\"vehicleNames\":").Append(PresetSlots.StringArrayJson(s.VehicleNames))
                  .Append(",\"laser\":").Append(s.Laser ? "true" : "false")
                  .Append(",\"hud\":").Append(s.Hud ? "true" : "false")
                  .Append('}');
            }
            sb.Append("]}");
            return sb.ToString();
        }

        // Current preset's index/name, folded into the 'tgt' telemetry block's own payload — the
        // preset label rides the TGT page's existing telemetry stream rather than a second endpoint.
        public static int CurrentIndex => _current;
        public static string CurrentName => _slots[_current - 1].Name;

        // ── mutators (CommandDispatcher: tgt-preset.save / .rename / .delete / .load) ───────

        // Captures the LIVE TargetListSelector state into whichever slot is current, under the given
        // name — always targets `_current`, never an index the client picks (the client only ever
        // supplies a name). Rejects an empty name/unavailable TargetListSelector rather than silently
        // saving a blank/stale slot.
        public static bool Save(string? name)
        {
            string cleanName = PresetSlots.CleanName(name);
            if (cleanName.Length == 0) return false;
            TargetListSelector sel = SceneSingleton<TargetListSelector>.i;
            if (sel == null) return false;

            TgtPreset slot = _slots[_current - 1];
            slot.Name = cleanName;
            (slot.Faction, slot.FactionNames) = SnapshotToggles(sel.toggleFactionItems);
            (slot.Category, slot.CategoryNames) = SnapshotToggles(sel.toggleUnitTypesItems);
            (slot.Vehicle, slot.VehicleNames) = SnapshotToggles(sel.toggleVehicleTypesItems);
            slot.Laser = sel.toggleLaser != null && sel.toggleLaser.status;
            slot.Hud = sel.toggleFollowHUD != null && sel.toggleFollowHUD.status;
            slot.HasData = true;
            Persist();
            return true;
        }

        public static bool Rename(int index, string? name) => PresetSlots.Rename(_slots, index, name, Persist);

        // Clears the slot back to empty (name + data) — the slot itself always exists (1-5 are fixed),
        // so "delete" can't remove it, only blank it.
        public static bool Delete(int index) => PresetSlots.Delete(_slots, index, Persist);

        // Applies a preset's saved filters onto the live TGT panel and makes it the current slot (so
        // the preset label follows it and the next SAVE overwrites it) — the direct-recall behaviour
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
            PresetSlots.WriteToDisk(FilePath, BuildDiskJson(_slots), "tgt presets");
        }

        private static void Apply(TargetListSelector sel, TgtPreset slot)
        {
            // toggleFollowHUD.Set() fires the game's own OnToggleFollowHUD, which calls SetFilters()
            // (turning on) or ResetFilters() (turning off) — either would clobber the faction/category/
            // vehicle arrays below if applied afterward, so HUD-follow must be set FIRST and the saved
            // filters re-applied on top of whatever it just did.
            if (sel.toggleFollowHUD != null && sel.toggleFollowHUD.status != slot.Hud) sel.toggleFollowHUD.Set(slot.Hud);
            ApplyToggles(sel.toggleFactionItems, slot.Faction, slot.FactionNames, "faction");
            ApplyToggles(sel.toggleUnitTypesItems, slot.Category, slot.CategoryNames, "category");
            ApplyToggles(sel.toggleVehicleTypesItems, slot.Vehicle, slot.VehicleNames, "vehicle");
            // Set() fires the game's own NeedUpdateIcons -> prune + recolour (CommandDispatcher.TgtSet's
            // own comment) — same early-return-if-unchanged guard, so restoring an already-matching
            // toggle doesn't pay for a needless prune pass.
            if (sel.toggleLaser != null && sel.toggleLaser.status != slot.Laser) sel.toggleLaser.Set(slot.Laser);
        }

        // Same "_"->"\n" wrap reversal TelemetryReader.ReadToggles uses, so a name captured here
        // matches what a fresh capture would produce for the same toggle.
        private static string ToggleLabel(TargetListSelector_ToggleButton b) =>
            (b != null && b.label != null) ? b.label.text.Replace("\n", "_") : string.Empty;

        private static (bool[] values, string[] names) SnapshotToggles(List<TargetListSelector_ToggleButton> list)
        {
            if (list == null) return (Array.Empty<bool>(), Array.Empty<string>());
            var values = new bool[list.Count];
            var names = new string[list.Count];
            for (int i = 0; i < list.Count; i++)
            {
                values[i] = list[i] != null && list[i].status;
                names[i] = ToggleLabel(list[i]);
            }
            return (values, names);
        }

        // Applies saved values by matching each live toggle's OWN current label against the names
        // captured at save time — not by position — so a game update that inserts or reorders a
        // toggle (e.g. Encyclopedia.vehicleTypes growing a new entry) can't silently apply a saved
        // value to the wrong toggle. Falls back to positional application for a preset saved before
        // names were captured (savedNames empty) or if the two saved arrays ever go out of sync.
        // A live toggle whose name isn't in the saved set (added since this preset was saved) is left
        // exactly as it already is — that current state IS the default for a toggle this preset never
        // knew about.
        //
        // The two LogInfo calls below are a TEMPORARY in-game verification diagnostic
        // (docs/tgt-presets.md's persist-by-name section needs a live confirmation that this path
        // actually runs, and that a simulated reorder — see that doc's manual JSON-edit recipe —
        // still resolves every toggle by its own label). Remove both once confirmed.
        private static void ApplyToggles(List<TargetListSelector_ToggleButton> list, bool[] values, string[] savedNames, string groupName)
        {
            if (list == null) return;
            if (savedNames.Length == 0 || savedNames.Length != values.Length)
            {
                Plugin.Log?.LogInfo($"[NOXMFD] TGT preset apply ({groupName}): positional fallback ({savedNames.Length} names vs {values.Length} values)");
                int n = Math.Min(list.Count, values.Length);
                for (int i = 0; i < n; i++)
                    if (list[i] != null && list[i].status != values[i]) list[i].Set(values[i]);
                return;
            }
            var byName = new Dictionary<string, bool>(savedNames.Length, StringComparer.Ordinal);
            for (int i = 0; i < savedNames.Length; i++)
                if (savedNames[i].Length > 0) byName[savedNames[i]] = values[i];   // last write wins on a dup label
            int unmatched = 0;
            for (int i = 0; i < list.Count; i++)
            {
                TargetListSelector_ToggleButton b = list[i];
                if (b == null) continue;
                string name = ToggleLabel(b);
                if (name.Length > 0 && byName.TryGetValue(name, out bool want))
                {
                    if (b.status != want) b.Set(want);
                }
                else unmatched++;
            }
            Plugin.Log?.LogInfo($"[NOXMFD] TGT preset apply ({groupName}): by-name reconciliation, {byName.Count} saved names, {unmatched} live toggle(s) unmatched");
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

            TgtPreset[] slots = PresetSlots.Empty<TgtPreset>(SlotCount);
            slots[0].Name = "BVR";
            slots[0].HasData = true;
            slots[0].Faction = new[] { true, false };       // a false in the middle, not all-same
            slots[0].Category = new[] { true, true, false };
            slots[0].Vehicle = Array.Empty<bool>();
            slots[0].FactionNames = new[] { "FRIENDLY", "ENEMY" };
            slots[0].CategoryNames = new[] { "AIR", "MSL", "GND" };
            slots[0].VehicleNames = Array.Empty<string>();
            slots[0].Laser = true;
            slots[0].Hud = false;

            string disk = BuildDiskJson(slots);
            Check(JsonLite.Parse(disk) is Dictionary<string, object?>, "disk JSON parses back to an object");
            TgtPreset[] roundTripped = PresetSlots.Empty<TgtPreset>(SlotCount);
            ParseFrom((Dictionary<string, object?>)JsonLite.Parse(disk)!, roundTripped);

            Check(roundTripped[0].Name == "BVR", "name round-trips through disk JSON");
            Check(roundTripped[0].HasData, "hasData round-trips true");
            Check(!roundTripped[1].HasData, "an untouched slot stays hasData=false");
            Check(roundTripped[0].Faction.Length == 2 && roundTripped[0].Faction[0] && !roundTripped[0].Faction[1],
                  "bool array round-trips including a false, not just all-true/all-false");
            Check(roundTripped[0].Category.Length == 3 && roundTripped[0].Category[2] == false,
                  "a second bool array round-trips independently of the first");
            Check(roundTripped[0].Vehicle.Length == 0, "empty array round-trips as empty, not null/missing");
            Check(roundTripped[0].FactionNames.Length == 2 && roundTripped[0].FactionNames[1] == "ENEMY",
                  "the parallel name array round-trips alongside its values");
            Check(roundTripped[0].VehicleNames.Length == 0, "an empty name array round-trips as empty, not null/missing");
            Check(roundTripped[1].FactionNames.Length == 0,
                  "a slot with no saved names (legacy preset shape) parses as empty, not null — so ApplyToggles falls back to positional");
            Check(roundTripped[0].Laser, "laser round-trips true");
            Check(!roundTripped[0].Hud, "hud round-trips false");

            // A disk file written before this feature existed simply has no *Names keys at all —
            // confirm that shape still parses cleanly rather than throwing on the missing fields.
            string legacyDisk = "{\"presets\":[{\"name\":\"OLD\",\"hasData\":true,\"faction\":[true,false],\"category\":[true],\"vehicle\":[],\"laser\":false,\"hud\":true}]}";
            TgtPreset[] legacy = PresetSlots.Empty<TgtPreset>(SlotCount);
            ParseFrom((Dictionary<string, object?>)JsonLite.Parse(legacyDisk)!, legacy);
            Check(legacy[0].Name == "OLD" && legacy[0].Faction.Length == 2 && legacy[0].FactionNames.Length == 0,
                  "a pre-existing disk file without name arrays still parses, with names defaulting empty");

            string summary = PresetSlots.SummaryJson(3, slots);
            Check(summary.Contains("\"current\":3"), "summary carries whatever current index it's given");
            Check(summary.Contains("\"index\":1") && summary.Contains("BVR"), "summary names the saved slot");
            Check(!summary.Contains("faction") && !summary.Contains("category") && !summary.Contains("vehicle") &&
                  !summary.Contains("laser") && !summary.Contains("hud"),
                "summary never leaks the raw filter state to the client — only index/name/hasData");
        }
    }
}
