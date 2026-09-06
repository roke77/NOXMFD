using System;
using System.Collections.Generic;
using System.Text;

namespace NOXMFD
{
    // Common shape HudPresetStore and TgtPresetStore both save into a fixed numbered slot: a name
    // and whether it's ever been saved. Each store's own preset type adds its game-specific filter
    // fields on top of this.
    internal interface IPresetSlot
    {
        string Name { get; set; }
        bool HasData { get; set; }
    }

    // BCL-only plumbing shared by every fixed-slot preset store (HudPresetStore, TgtPresetStore):
    // slot creation, the {current,presets:[{index,name,hasData}]} summary JSON, name validation,
    // and disk persistence. Each store still owns its own live game-state capture/apply and its own
    // disk JSON shape (the filter fields differ) — only the plumbing around those is shared here.
    internal static class PresetSlots
    {
        internal static T[] Empty<T>(int count) where T : new()
        {
            var slots = new T[count];
            for (int i = 0; i < count; i++) slots[i] = new T();
            return slots;
        }

        internal static bool[] ParseBoolArray(Dictionary<string, object?> d, string key)
        {
            if (!(d.TryGetValue(key, out object? v) && v is List<object?> list)) return Array.Empty<bool>();
            var arr = new bool[list.Count];
            for (int i = 0; i < list.Count; i++) arr[i] = list[i] is bool b && b;
            return arr;
        }

        internal static string BoolArrayJson(bool[] arr)
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

        internal static string[] ParseStringArray(Dictionary<string, object?> d, string key)
        {
            if (!(d.TryGetValue(key, out object? v) && v is List<object?> list)) return Array.Empty<string>();
            var arr = new string[list.Count];
            for (int i = 0; i < list.Count; i++) arr[i] = list[i] as string ?? string.Empty;
            return arr;
        }

        internal static string StringArrayJson(string[] arr)
        {
            var sb = new StringBuilder(arr.Length * 12);
            sb.Append('[');
            for (int i = 0; i < arr.Length; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append('"').Append(JsonLite.EscapeJson(arr[i])).Append('"');
            }
            sb.Append(']');
            return sb.ToString();
        }

        // Reads the name/hasData half of a slot's disk JSON — the half every store's ParseFrom needs
        // identically; each store still reads its own extra fields (faction, categories, ...) itself.
        internal static void ReadNameAndHasData(Dictionary<string, object?> d, IPresetSlot slot)
        {
            slot.Name = d.TryGetValue("name", out object? nm) ? (nm as string ?? string.Empty) : string.Empty;
            slot.HasData = d.TryGetValue("hasData", out object? hd) && hd is bool hb && hb;
        }

        // Trims and rejects a null/whitespace-only name. IsNullOrEmpty alone lets a spaces-only name
        // from a direct command (bypassing any browser-side trim) through, so callers must check the
        // TRIMMED result's length, not the raw string's.
        internal static string CleanName(string? name) => name?.Trim() ?? string.Empty;

        // {"current":N,"presets":[{"index":i,"name":"...","hasData":bool},...]} — every store's
        // summary JSON is exactly this shape; only the raw filter fields differ, and those never
        // appear here (this is the LOAD picker's data, not gameplay state — see each store's own
        // SelfCheck for the "never leaks filters" assertion).
        internal static string SummaryJson<T>(int current, T[] slots) where T : IPresetSlot
        {
            var sb = new StringBuilder(256);
            sb.Append("{\"current\":").Append(current).Append(",\"presets\":[");
            for (int i = 0; i < slots.Length; i++)
            {
                if (i > 0) sb.Append(',');
                T s = slots[i];
                sb.Append("{\"index\":").Append(i + 1)
                  .Append(",\"name\":\"").Append(JsonLite.EscapeJson(s.Name))
                  .Append("\",\"hasData\":").Append(s.HasData ? "true" : "false")
                  .Append('}');
            }
            sb.Append("]}");
            return sb.ToString();
        }

        internal static bool Rename<T>(T[] slots, int index, string? name, Action persist) where T : IPresetSlot
        {
            if (index < 1 || index > slots.Length) return false;
            string clean = CleanName(name);
            if (clean.Length == 0) return false;
            slots[index - 1].Name = clean;
            persist();
            return true;
        }

        internal static bool Delete<T>(T[] slots, int index, Action persist) where T : new()
        {
            if (index < 1 || index > slots.Length) return false;
            slots[index - 1] = new T();
            persist();
            return true;
        }

        // Backs up whatever's on disk, then writes fresh JSON — same shape every store persisting to
        // BepInEx's config dir already used inline (RouteStore, LayoutStore, and both preset stores).
        internal static void WriteToDisk(string filePath, string json, string logTag)
        {
            try { ConfigBackup.BackupIfExists(filePath); System.IO.File.WriteAllText(filePath, json); }
            catch (Exception ex) { Plugin.Log?.LogWarning($"[NOXMFD] failed to persist {logTag}: {ex.Message}"); }
        }
    }
}
