using System.Collections.Generic;
using System.Threading;

namespace NOXMFD
{
    // Which locked target, among however many the player's weaponManager currently holds, is "the
    // one" TGT/FCR/HSD describe/highlight — issue #62, docs/tgt-cycle-focus.md. Split out next to
    // SoiFocus.cs but a different concept from it: SoiFocus tracks which SURFACE is focused, this
    // tracks which TARGET is focused. Orthogonal, and deliberately not folded together — a pilot can
    // cycle target focus without ever touching SOI, and it has to reach every open TGT/FCR/HSD page
    // in every browser, not just whichever surface currently holds SOI.
    //
    // 0 means "nothing focused" — a real Unit's persistentID is never 0 (TelemetrySnapshot's other
    // persistentID fields, e.g. PlayerJammedBy, use the same convention).
    internal static class TargetFocus
    {
        private static uint _id;
        private static readonly object _lock = new object();

        internal static uint Id => Volatile.Read(ref _id);

        // No version counter, unlike SoiFocus's own SetTargetLocked: that one needs one because SOI
        // state is serialized straight off SoiFocus at request time (TelemetryServer.GetFrameBytes).
        // FocusedTargetId instead rides inside TelemetrySnapshot itself (TelemetryReader.PushSnapshot),
        // so it's already covered by the snapshot's own version — a separate counter here would have
        // no reader.
        private static void SetIdLocked(uint id) => Volatile.Write(ref _id, id);

        // Called once per contact scan (TelemetryReader.RefreshContactSnapshotIfNeeded) with the
        // player's CURRENT lock list, in the game's own weaponManager.GetTargetList() order —
        // reconciles focus against locks changing for reasons other than a Next/Prev press (a lock
        // destroyed, deselected elsewhere, or freshly the only one left):
        //  - 0 remaining clears focus, nothing to focus;
        //  - exactly 1 remaining always focuses it, matching "first locked" being the always-true
        //    case today when there's only one (docs/rdr-fcr-hsd.md);
        //  - nothing focused yet but 2+ are already locked (the pilot locked several before ever
        //    touching Next/Previous) defaults to the first one, matching WeaponManager.Fire()'s own
        //    targetList[0] convention — found live (issue #67's HUD TTI report) after locking two
        //    targets from the MAP left focus stuck at "none" with no Next/Prev press to seed it;
        //  - losing the currently focused one (but others remain) drops back to none rather than
        //    silently jumping the pilot's attention to a target they didn't choose.
        internal static void Reconcile(IReadOnlyList<uint> lockedIds)
        {
            lock (_lock)
            {
                if (lockedIds.Count == 0) { SetIdLocked(0); return; }
                if (lockedIds.Count == 1) { SetIdLocked(lockedIds[0]); return; }
                uint id = Volatile.Read(ref _id);
                if (id == 0) { SetIdLocked(lockedIds[0]); return; }
                if (IndexOf(lockedIds, id) < 0) SetIdLocked(0);
            }
        }

        // Next/Previous Target (docs/tgt-cycle-focus.md) — steps to the next/previous id in the
        // game's own lock order, wrapping at both ends (matches TGT's navHighlight wrap,
        // docs/tgt-keybind-nav.md). With 0 or 1 locks there's nothing to step between, but still
        // resolves onto whatever single target remains rather than leaving a stale id from a lock
        // that's since gone away.
        internal static void Cycle(int dir, IReadOnlyList<uint> lockedIds)
        {
            lock (_lock)
            {
                if (lockedIds.Count == 0) { SetIdLocked(0); return; }
                if (lockedIds.Count == 1) { SetIdLocked(lockedIds[0]); return; }
                int i = IndexOf(lockedIds, Volatile.Read(ref _id));
                int next = i < 0
                    ? (dir >= 0 ? 0 : lockedIds.Count - 1)
                    : ((i + dir) % lockedIds.Count + lockedIds.Count) % lockedIds.Count;
                SetIdLocked(lockedIds[next]);
            }
        }

        private static int IndexOf(IReadOnlyList<uint> ids, uint id)
        {
            for (int i = 0; i < ids.Count; i++) if (ids[i] == id) return i;
            return -1;
        }
    }
}
