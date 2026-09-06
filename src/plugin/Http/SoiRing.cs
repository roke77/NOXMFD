using System;
using System.Collections.Generic;

namespace NOXMFD
{
    // Pure SOI ring-selection rules, no SseHub/TgpManualControl/game dependency — so tools/tests can
    // exercise the ACTUAL production logic directly (linked into NOXMFD.Tests.csproj) instead of only
    // a hand-written JS mirror model (tools/soi-focus.test.js, kept for the browser-facing
    // checkbox/command wiring it alone covers). SoiFocus.cs supplies the live inputs and owns all the
    // locking/state; every method here only computes over plain data.
    internal static class SoiRing
    {
        // Every instance's every surface, instance-major/surface-minor, in the given (already
        // oldest-connection-first) order, deduped by cid, skipping any (cid, pane) in `excluded`
        // (issue #58). `extra`, when given, is appended last (the manual TGP camera's synthetic
        // entry) so an existing pane layout's cycle order never shifts under a pilot who never uses it.
        internal static List<(string cid, int pane)> Build(
            IReadOnlyList<(string cid, int paneCount)> instancesOldestFirst,
            HashSet<(string cid, int pane)> excluded,
            (string cid, int pane)? extra = null)
        {
            var ring = new List<(string, int)>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var inst in instancesOldestFirst)
            {
                if (!seen.Add(inst.cid)) continue;
                for (int p = 0; p < inst.paneCount; p++)
                    if (!excluded.Contains((inst.cid, p))) ring.Add((inst.cid, p));
            }
            if (extra.HasValue) ring.Add(extra.Value);
            return ring;
        }

        // One cycle step. From no focus (curCid/curPane not found in the ring), NEXT takes the first
        // surface and PREV the last, so either key lights something up on the first press.
        internal static (string cid, int pane) Step(
            IReadOnlyList<(string cid, int pane)> ring, string curCid, int curPane, int dir)
        {
            if (ring.Count == 0) return (string.Empty, -1);
            int i = -1;
            for (int k = 0; k < ring.Count; k++)
                if (string.Equals(ring[k].cid, curCid, StringComparison.Ordinal) && ring[k].pane == curPane) { i = k; break; }
            int next = i < 0
                ? (dir >= 0 ? 0 : ring.Count - 1)
                : ((i + dir) % ring.Count + ring.Count) % ring.Count;
            return ring[next];
        }

        // A shrinking SetPaneCount's clamp, SAME-DISPLAY half only (cheap — no ring build needed):
        // the nearest still-included pane at or below the new count, walking down from n-1, so a
        // merge never re-focuses a surface the pilot excluded (issue #58's review-flagged bug). Null
        // when every surviving pane on this display is excluded — the caller falls through to the
        // full ring's first member (FirstOrNone below) in that case.
        internal static (string cid, int pane)? TryClampToIncludedPane(
            string cid, int n, HashSet<(string cid, int pane)> excluded)
        {
            for (int p = n - 1; p >= 0; p--)
                if (!excluded.Contains((cid, p))) return (cid, p);
            return null;
        }

        // The ring's first member, or "nothing focused" for an empty ring — the shared fallback
        // every caller needs once its own preferred candidate (a same-display pane, a lone survivor
        // after a disconnect, etc.) doesn't pan out.
        internal static (string cid, int pane) FirstOrNone(IReadOnlyList<(string cid, int pane)> ring) =>
            ring.Count == 0 ? (string.Empty, -1) : ring[0];
    }
}
