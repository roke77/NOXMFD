using System;
using System.Collections.Generic;

namespace NOXMFD
{
    // TGT's column sort (NAME / SRC / RNG), owned here rather than per page so every TGT view in
    // every browser shows the same order AND Next/Previous (TargetFocus.Cycle) steps through the
    // table in that visible order — a page-local sort would disagree with the one shared focus.
    // Key "" = no sort: the game's own weaponManager.GetTargetList() order, the default.
    //
    // Main-thread only: Set runs from CommandDispatcher.Drain, Sort from the contact scan, Follow
    // from Next/Previous — all inside TelemetryReader.Update.
    internal static class TargetSort
    {
        internal struct Row
        {
            public string Name;   // UnitInfo.Type — what TGT's NAME column shows
            public string Src;    // "DATALINK" | "SENSOR" | "STALE" — TGT's SRC column text
            public float  Range;  // metres; NaN when the lock has no disclosed contact
        }

        internal static string Key { get; private set; } = "";
        internal static int Dir { get; private set; } = 1;

        // The last sorted lock order (Sort's output) — Follow reorders a live lock list by it.
        private static uint[] _order = Array.Empty<uint>();

        internal static bool Set(string key, int dir)
        {
            key ??= "";
            if (key != "" && key != "n" && key != "src" && key != "r") return false;
            Key = key;
            Dir = dir < 0 ? -1 : 1;
            return true;
        }

        internal static string SrcLabel(bool datalink, bool stale) => stale ? "STALE" : datalink ? "DATALINK" : "SENSOR";

        // Stable (ties keep lock order, so equal rows never swap between scans); a lock with no
        // row, or an unknown range under RNG, always sorts last in either direction.
        internal static uint[] Sort(uint[] lockedIds, Func<uint, Row?> row)
        {
            uint[] sorted = (uint[])lockedIds.Clone();
            if (Key != "")
            {
                var rows = new Row?[sorted.Length];
                var idx = new int[sorted.Length];
                for (int i = 0; i < sorted.Length; i++) { rows[i] = row(sorted[i]); idx[i] = i; }
                Array.Sort(idx, (a, b) =>
                {
                    int c = Compare(rows[a], rows[b]);
                    return c != 0 ? c : a.CompareTo(b);
                });
                for (int i = 0; i < idx.Length; i++) sorted[i] = lockedIds[idx[i]];
            }
            _order = sorted;
            return sorted;
        }

        private static int Compare(Row? a, Row? b)
        {
            bool ka = a.HasValue && (Key != "r" || !float.IsNaN(a.Value.Range));
            bool kb = b.HasValue && (Key != "r" || !float.IsNaN(b.Value.Range));
            if (ka != kb) return ka ? -1 : 1;
            if (!ka) return 0;
            Row x = a.GetValueOrDefault(), y = b.GetValueOrDefault();
            int c = Key == "n"   ? string.Compare(x.Name, y.Name, StringComparison.OrdinalIgnoreCase)
                  : Key == "src" ? string.CompareOrdinal(x.Src, y.Src)
                  :                x.Range.CompareTo(y.Range);
            return c * Dir;
        }

        // A live lock list (a Next/Previous press reads weaponManager directly) put into the last
        // sorted order; ids the last scan hadn't seen yet go last, in their own lock order. Unsorted,
        // the live list already IS the order.
        internal static IReadOnlyList<uint> Follow(IReadOnlyList<uint> live)
        {
            if (Key == "") return live;
            var result = new List<uint>(live.Count);
            foreach (uint id in _order) if (Contains(live, id)) result.Add(id);
            foreach (uint id in live) if (!result.Contains(id)) result.Add(id);
            return result;
        }

        private static bool Contains(IReadOnlyList<uint> ids, uint id)
        {
            for (int i = 0; i < ids.Count; i++) if (ids[i] == id) return true;
            return false;
        }
    }
}
