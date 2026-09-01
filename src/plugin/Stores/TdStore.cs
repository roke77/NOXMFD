using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace NOXMFD
{
    // Target Designator (issue #47, docs/target-designator.md) — a squad leader hand-assigns
    // targets from their own live TGT list to named squad slots, then DESIGNATEs (pushes) each
    // slot's targets to that member over the squad transport (Squad.SendDataTo). No persistence
    // (like Squad.cs/RouteStore's shared state), everything here resets on plugin restart.
    //
    // The target ROWS themselves (name/grid/range/faction/datalink) are never computed here — they
    // are entirely client-side, decoded from the telemetry frame the same way TGT's own list is
    // (src/web/services/telemetry-source.js). This class only owns the two things that must
    // survive a page reload: the leader's in-progress selection/assignment overlay, and each
    // member's last-received designated-target snapshot.
    //
    // Deliberately 100% BCL, no Squad/Unit/CommandDispatcher touchpoint — same testability seam
    // RouteStore.cs keeps (tools/tests/NOXMFD.Tests.csproj compiles this file standalone). Callers
    // (CommandDispatcher.cs) own both the leader-only gating (Squad.IsLeader) and the actual
    // in-game unit selection (TdAcquireAll, next to ClearDatalinkTargets/ClearStaleTargets).
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

        // Leader-only overlay: which rows are currently highlighted, and which squad slots each
        // target id has been assigned to (slot 1 = leader/self — a tag-only marker, DESIGNATE never
        // sends to it). Keyed by the same persistentID the browser's tgt-targets rows carry.
        private static readonly HashSet<uint> _selected = new HashSet<uint>();
        private static readonly Dictionary<uint, HashSet<int>> _assignments = new Dictionary<uint, HashSet<int>>();

        // Member-only: the leader's last DESIGNATE, replaced wholesale on every receipt (a repeat
        // DESIGNATE replaces, never merges — see ReceiveDesignation).
        private static List<Row> _designated = new List<Row>();

        // Server-thread-readable cache, same threading contract as Squad.StateJson/RouteStore.RoutesJson:
        // every mutator below runs on the Unity main thread only, and rebuilds this string
        // synchronously as its last step.
        internal static volatile string StateJson = BuildStateJson();

        // Injected by Plugin.cs (RouteStore.LogWarning's own seam) so this file stays BepInEx-free.
        // Logged on receipt so a member's own log confirms a DESIGNATE actually arrived, independent
        // of whether the browser page happened to be open/refreshed to show it.
        internal static Action<string>? LogInfo;

        // ── Leader actions ──────────────────────────────────────────────────────

        internal static bool ToggleSelect(uint id)
        {
            if (id == 0) return false;
            if (!_selected.Add(id)) _selected.Remove(id);
            RebuildState();
            return true;
        }

        // Toggles slot membership for every currently-selected target, then clears the selection —
        // same outcome whether it came from a squad-button click or one of the 9 keybinds. `retain`
        // (issue #47 follow-up: td.js's own tap-vs-long-press gesture on the squad button, mirroring
        // TGT's tap/long-press cells) skips the clear, so a leader can long-press to designate the
        // same selection to several slots in a row without re-selecting between each one. Defaults
        // false so every pre-existing call site (including the test suite) is unchanged.
        internal static bool Assign(int slot, bool retain = false)
        {
            if (slot <= 0 || _selected.Count == 0) return false;
            foreach (uint id in _selected)
            {
                if (!_assignments.TryGetValue(id, out HashSet<int>? slots))
                    _assignments[id] = slots = new HashSet<int>();
                if (!slots.Add(slot)) slots.Remove(slot);
                if (slots.Count == 0) _assignments.Remove(id);
            }
            if (!retain) _selected.Clear();
            RebuildState();
            return true;
        }

        // Slot numbers are POSITIONAL (slot = a member's index in Squad._members + 2, the same
        // number td.js's own squadSlots() computes) — a kick or voluntary leave shrinks that list,
        // so everyone after the departed member shifts down one slot on the very next read (a poll,
        // a DESIGNATE). Without this, an assignment made before the departure would silently land on
        // whoever now sits in that old slot instead. Called from Squad.cs's Kick()/HandleLeave() with
        // the departed member's own slot, before anything else reads _assignments against the
        // already-shrunk roster.
        internal static void RenumberAfterMemberRemoved(int removedSlot)
        {
            if (removedSlot <= 0 || _assignments.Count == 0) return;
            bool changed = false;
            var emptyIds = new List<uint>();
            foreach (var kv in _assignments)
            {
                HashSet<int> slots = kv.Value;
                if (slots.Remove(removedSlot)) changed = true;
                var toShift = new List<int>();
                foreach (int s in slots) if (s > removedSlot) toShift.Add(s);
                foreach (int s in toShift) { slots.Remove(s); slots.Add(s - 1); changed = true; }
                if (slots.Count == 0) emptyIds.Add(kv.Key);
            }
            // Same "an empty slot set removes the target entirely" convention Assign() already uses.
            foreach (uint id in emptyIds) _assignments.Remove(id);
            if (changed) RebuildState();
        }

        // Leader's CLEAR — discards in-progress (unsent) selection/assignment work.
        internal static bool ClearOwn()
        {
            if (_selected.Count == 0 && _assignments.Count == 0) return false;
            _selected.Clear();
            _assignments.Clear();
            RebuildState();
            return true;
        }

        // ── Member actions ──────────────────────────────────────────────────────

        // A new DESIGNATE always replaces the member's whole table (per issue #47's scope), never
        // merges — the leader's own DESIGNATE click already sends the complete set it wants this
        // member to have.
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

        internal static bool ClearDesignated()
        {
            if (_designated.Count == 0) return false;
            _designated = new List<Row>();
            RebuildState();
            return true;
        }

        // AQUIRE reads this to select every currently-designated target in-game, all at once —
        // the actual unit lookup/selection lives in CommandDispatcher.TdAcquireAll (next to
        // ClearDatalinkTargets/ClearStaleTargets), since that needs Unit/UnitRegistry, not this file.
        internal static IReadOnlyList<Row> Designated => _designated;

        // ── Squad lifecycle ──────────────────────────────────────────────────────

        // Called from Squad.cs whenever this pilot's squad membership ends or changes leader
        // (ResetToNone / HandleLeaderChanged) — same reasoning RouteStore.OnSquadEnded gives:
        // leader-side assignment work and member-side designations only mean something within the
        // squad session that produced them.
        internal static void OnSquadEnded()
        {
            bool changed = _selected.Count > 0 || _assignments.Count > 0 || _designated.Count > 0;
            _selected.Clear();
            _assignments.Clear();
            _designated = new List<Row>();
            if (changed) RebuildState();
        }

        // ── Served state ──────────────────────────────────────────────────────────

        private static void RebuildState() { StateJson = BuildStateJson(); }

        private static string BuildStateJson()
        {
            var sb = new StringBuilder();
            sb.Append("{\"selected\":[").Append(string.Join(",", _selected.Select(id => id.ToString(CultureInfo.InvariantCulture)))).Append(']');
            sb.Append(",\"assignments\":{");
            bool first = true;
            foreach (var kv in _assignments)
            {
                if (!first) sb.Append(',');
                first = false;
                sb.Append('"').Append(kv.Key.ToString(CultureInfo.InvariantCulture)).Append("\":[")
                  .Append(string.Join(",", kv.Value.Select(s => s.ToString(CultureInfo.InvariantCulture)))).Append(']');
            }
            sb.Append("},\"designated\":[");
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

        // Test-only: static fields are plugin-lifetime by design (same reasoning RouteStore.cs's
        // own ResetForTests gives) — a standalone test project resets them between test methods.
        internal static void ResetForTests()
        {
            _selected.Clear();
            _assignments.Clear();
            _designated = new List<Row>();
            StateJson = BuildStateJson();
        }
    }
}
