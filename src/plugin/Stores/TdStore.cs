using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace NOXMFD
{
    // Target Designator (issue #47, docs/target-designator.md) — a squad leader assigns targets
    // from their own live TGT list to squad slots on an assignment matrix (one tap per target/slot
    // pair, a whole row or a whole column at once), then DESIGNATEs (pushes) each slot's targets
    // to that member over the squad transport (Squad.SendDataTo). The member answers on their own
    // TGT page: ADD, REPLACE or DISMISS. No persistence (like Squad.cs/RouteStore's shared state),
    // everything here resets on plugin restart.
    //
    // The target ROWS themselves (name/grid/range/faction/datalink) are never computed here — they
    // are entirely client-side, decoded from the telemetry frame the same way TGT's own list is
    // (src/web/services/telemetry-source.js). This class only owns what must survive a page reload:
    // the leader's assignment matrix and what each slot was last sent, and the member's pending
    // designation plus which targets came from an accepted one.
    //
    // Deliberately 100% BCL, no Squad/Unit/CommandDispatcher touchpoint — same testability seam
    // RouteStore.cs keeps (tools/tests/NOXMFD.Tests.csproj compiles this file standalone). Callers
    // (CommandDispatcher.cs) own both the leader-only gating (Squad.IsLeader) and the actual
    // in-game unit selection (TdAccept, next to ClearDatalinkTargets/ClearStaleTargets).
    internal static class TdStore
    {
        internal sealed class Row
        {
            internal Row(uint id, string n, string g, double r, int f, bool dl)
            { Id = id; N = n; G = g; R = r; F = f; Dl = dl; }
            internal uint   Id { get; }
            internal string N  { get; }
            internal string G  { get; }
            internal double R  { get; }
            internal int    F  { get; }
            internal bool   Dl { get; }
        }

        // Leader-only matrix: which squad slots each target id has been assigned to (slot 1 =
        // leader/self — a tag-only marker, DESIGNATE never sends to it). Keyed by the same
        // persistentID the browser's tgt-targets rows carry.
        private static readonly Dictionary<uint, HashSet<int>> _assignments = new Dictionary<uint, HashSet<int>>();

        // Leader-only: the id set each member slot was last DESIGNATEd, so the matrix can tell a
        // slot whose list is SENT from one that CHANGED since (td-matrix.js's slotStatus).
        private static readonly Dictionary<int, SortedSet<uint>> _sent = new Dictionary<int, SortedSet<uint>>();

        // Member-only: the leader's last DESIGNATE, still waiting for ADD/REPLACE/DISMISS on TGT.
        // Replaced wholesale on every receipt (a repeat DESIGNATE replaces, never merges — see
        // ReceiveDesignation).
        private static List<Row> _designated = new List<Row>();

        // Member-only: ids that arrived through an accepted designation, so TGT can tag those rows.
        private static readonly HashSet<uint> _accepted = new HashSet<uint>();

        // Server-thread-readable cache, same threading contract as Squad.StateJson/RouteStore.RoutesJson:
        // every mutator below runs on the Unity main thread only, and rebuilds this string
        // synchronously as its last step.
        internal static volatile string StateJson = BuildStateJson();

        // Injected by Plugin.cs (RouteStore.LogWarning's own seam) so this file stays BepInEx-free.
        // Logged on receipt so a member's own log confirms a DESIGNATE actually arrived, independent
        // of whether the browser page happened to be open/refreshed to show it.
        internal static Action<string>? LogInfo;

        // ── Leader actions ──────────────────────────────────────────────────────

        // A matrix cell: one target to one slot, or back off it.
        internal static bool ToggleCell(uint id, int slot)
        {
            if (id == 0 || slot <= 0) return false;
            Set(id, slot, !Has(id, slot));
            RebuildState();
            return true;
        }

        // A matrix row (the target's name): that target to every slot, leader included. All-or-
        // nothing rather than a per-slot flip, so a partly-assigned target gains the missing slots
        // instead of losing the ones it had; a target already on every slot is taken off them all.
        internal static bool ToggleRow(uint id, IReadOnlyList<int> slots)
        {
            if (id == 0 || slots.Count == 0) return false;
            bool full = slots.All(s => Has(id, s));
            foreach (int slot in slots)
                if (slot > 0) Set(id, slot, !full);
            RebuildState();
            return true;
        }

        // A matrix column head: every target on the leader's table to that slot, same all-or-
        // nothing rule as ToggleRow. `ids` is the table the leader is looking at — the page sends
        // it because the target list lives in the browser, not here (see the class header).
        internal static bool ToggleColumn(int slot, IReadOnlyList<uint> ids)
        {
            if (slot <= 0 || ids.Count == 0) return false;
            bool full = ids.All(id => Has(id, slot));
            foreach (uint id in ids)
                if (id != 0) Set(id, slot, !full);
            RebuildState();
            return true;
        }

        // Records what DESIGNATE just sent this slot (CommandDispatcher.TdDesignate, only after a
        // successful send), replacing whatever it was sent before.
        internal static void MarkSent(int slot, IEnumerable<uint> ids)
        {
            if (slot <= 0) return;
            _sent[slot] = new SortedSet<uint>(ids);
            RebuildState();
        }

        private static bool Has(uint id, int slot) =>
            _assignments.TryGetValue(id, out HashSet<int>? slots) && slots.Contains(slot);

        // An empty slot set removes the target entirely, so the served state never carries "id: []".
        private static void Set(uint id, int slot, bool on)
        {
            if (!_assignments.TryGetValue(id, out HashSet<int>? slots))
            {
                if (!on) return;
                _assignments[id] = slots = new HashSet<int>();
            }
            if (on) slots.Add(slot); else slots.Remove(slot);
            if (slots.Count == 0) _assignments.Remove(id);
        }

        // Slot numbers are each member's own Squad Member.Slot, kept when others leave — so a kick,
        // leave or dropout only drops assignments to the departed member's slot, which stays empty
        // until someone joins into it. Without this, an assignment made before the departure would
        // land on whoever takes that slot next. Called from Squad.cs's CleanupRemovedMember.
        internal static void ClearSlot(int slot)
        {
            if (slot <= 0) return;
            bool changed = _sent.Remove(slot);
            var emptyIds = new List<uint>();
            foreach (var kv in _assignments)
            {
                if (kv.Value.Remove(slot)) changed = true;
                if (kv.Value.Count == 0) emptyIds.Add(kv.Key);
            }
            // Same "an empty slot set removes the target entirely" convention Assign() already uses.
            foreach (uint id in emptyIds) _assignments.Remove(id);
            if (changed) RebuildState();
        }

        // A squad reorder (Squad.MoveMember) swaps two slots' numbers (or moves a member into an
        // empty one), so every assignment to either slot moves with its member.
        internal static void SwapSlots(int a, int b)
        {
            bool changed = false;
            foreach (HashSet<int> slots in _assignments.Values)
            {
                bool hasA = slots.Remove(a), hasB = slots.Remove(b);
                if (hasA) slots.Add(b);
                if (hasB) slots.Add(a);
                changed |= hasA != hasB;
            }
            // What each slot was last sent moves with its member too, or the matrix would call a
            // moved member's untouched list CHANGED.
            bool sentA = _sent.TryGetValue(a, out SortedSet<uint>? toB), sentB = _sent.TryGetValue(b, out SortedSet<uint>? toA);
            _sent.Remove(a); _sent.Remove(b);
            if (sentA) _sent[b] = toB!;
            if (sentB) _sent[a] = toA!;
            if (changed || sentA || sentB) RebuildState();
        }

        // Leader's CLEAR — discards the matrix. What was already sent stays as it was: members keep
        // their lists, and the matrix shows those slots as CHANGED until the next DESIGNATE.
        internal static bool ClearOwn()
        {
            if (_assignments.Count == 0) return false;
            _assignments.Clear();
            RebuildState();
            return true;
        }

        // ── Member actions ──────────────────────────────────────────────────────

        // A new DESIGNATE always replaces the member's pending designation (per issue #47's scope),
        // never merges — the leader's DESIGNATE already sends the complete set it wants this member
        // to have. An empty list withdraws a pending one, so TGT's dock closes.
        internal static bool ReceiveDesignation(string? json)
        {
            if (JsonLite.Parse(json ?? string.Empty) is not List<object?> list) return false;
            var rows = new List<Row>();
            foreach (object? item in list)
            {
                if (item is not Dictionary<string, object?> d) continue;
                if (!(d.TryGetValue("id", out object? idv) && idv is double idd)) continue;
                string n = d.TryGetValue("n", out object? nv) && nv is string ns ? ns : string.Empty;
                string g = d.TryGetValue("g", out object? gv) && gv is string gs ? gs : string.Empty;
                double r = d.TryGetValue("r", out object? rv) && rv is double rd ? rd : 0.0;
                int f = d.TryGetValue("f", out object? fv) && fv is double fd ? (int)fd : -1;
                bool dl = d.TryGetValue("dl", out object? dlv) && dlv is bool dlb && dlb;
                rows.Add(new Row(unchecked((uint)idd), n, g, r, f, dl));
            }
            _designated = rows;
            RebuildState();
            LogInfo?.Invoke($"[NOXMFD] td.receive-designation: {rows.Count} target(s) received.");
            return true;
        }

        // DISMISS on the member's TGT dock: the pending designation goes away, nothing is selected.
        internal static bool ClearDesignated()
        {
            if (_designated.Count == 0) return false;
            _designated = new List<Row>();
            RebuildState();
            return true;
        }

        // ADD/REPLACE on the member's TGT dock, after CommandDispatcher.TdAccept has selected the
        // targets in-game: the pending designation closes, and its ids are remembered so TGT can
        // tag those rows as the leader's.
        internal static bool AcceptDesignated()
        {
            if (_designated.Count == 0) return false;
            foreach (Row row in _designated) _accepted.Add(row.Id);
            _designated = new List<Row>();
            RebuildState();
            return true;
        }

        // ADD/REPLACE read this to select the pending targets in-game — the unit lookup/selection
        // lives in CommandDispatcher.TdAccept (next to ClearDatalinkTargets/ClearStaleTargets),
        // since that needs Unit/UnitRegistry, not this file.
        internal static IReadOnlyList<Row> Designated => _designated;

        // ── Squad lifecycle ──────────────────────────────────────────────────────

        // Called from Squad.cs whenever this pilot's squad membership ends or changes leader
        // (ResetToNone / HandleLeaderChanged) — same reasoning RouteStore.OnSquadEnded gives:
        // leader-side assignment work and member-side designations only mean something within the
        // squad session that produced them.
        internal static void OnSquadEnded()
        {
            bool changed = _assignments.Count > 0 || _sent.Count > 0 || _designated.Count > 0 || _accepted.Count > 0;
            _assignments.Clear();
            _sent.Clear();
            _designated = new List<Row>();
            _accepted.Clear();
            if (changed) RebuildState();
        }

        // ── Served state ──────────────────────────────────────────────────────────

        private static void RebuildState() { StateJson = BuildStateJson(); }

        private static string BuildStateJson()
        {
            var sb = new StringBuilder();
            sb.Append("{\"assignments\":{");
            bool first = true;
            foreach (var kv in _assignments)
            {
                if (!first) sb.Append(',');
                first = false;
                sb.Append('"').Append(kv.Key.ToString(CultureInfo.InvariantCulture)).Append("\":[").Append(Csv(kv.Value)).Append(']');
            }
            sb.Append("},\"sent\":{");
            first = true;
            foreach (var kv in _sent)
            {
                if (!first) sb.Append(',');
                first = false;
                sb.Append('"').Append(kv.Key.ToString(CultureInfo.InvariantCulture)).Append("\":[").Append(Csv(kv.Value)).Append(']');
            }
            sb.Append("},\"accepted\":[").Append(Csv(_accepted)).Append(']');
            sb.Append(",\"designated\":[");
            for (int i = 0; i < _designated.Count; i++)
            {
                if (i > 0) sb.Append(',');
                Row row = _designated[i];
                sb.Append("{\"id\":").Append(row.Id.ToString(CultureInfo.InvariantCulture))
                  .Append(",\"n\":\"").Append(JsonLite.EscapeJson(row.N))
                  .Append("\",\"g\":\"").Append(JsonLite.EscapeJson(row.G))
                  .Append("\",\"r\":").Append(row.R.ToString(CultureInfo.InvariantCulture))
                  .Append(",\"f\":").Append(row.F.ToString(CultureInfo.InvariantCulture))
                  .Append(",\"dl\":").Append(row.Dl ? "true" : "false")
                  .Append('}');
            }
            sb.Append(']').Append('}');
            return sb.ToString();
        }

        private static string Csv<T>(IEnumerable<T> values) where T : IFormattable =>
            string.Join(",", values.Select(v => v.ToString(null, CultureInfo.InvariantCulture)));

        // Test-only: static fields are plugin-lifetime by design (same reasoning RouteStore.cs's
        // own ResetForTests gives) — a standalone test project resets them between test methods.
        internal static void ResetForTests()
        {
            _assignments.Clear();
            _sent.Clear();
            _designated = new List<Row>();
            _accepted.Clear();
            StateJson = BuildStateJson();
        }
    }
}
