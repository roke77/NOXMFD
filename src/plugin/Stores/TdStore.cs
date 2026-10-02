using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace NOXMFD
{
    // Target Designator (issue #47, docs/target-designator.md) — a squad leader assigns targets
    // from their own live TGT list to squad members on an assignment matrix (one tap per
    // target/member pair, a whole row or a whole column at once), then DESIGNATEs (pushes) each
    // member's targets to that member over the squad transport (Squad.SendDataTo). Members are keyed
    // by SteamID (issue #107): squads no longer number their members. The member answers on their own
    // TGT page: ADD, REPLACE or DISMISS. No persistence (like Squad.cs/RouteStore's shared state),
    // everything here resets on plugin restart.
    //
    // The target ROWS themselves (name/grid/range/faction/datalink) are never computed here — they
    // are entirely client-side, decoded from the telemetry frame the same way TGT's own list is
    // (src/web/services/telemetry-source.js). This class only owns what must survive a page reload:
    // the leader's assignment matrix and what each member was last sent, and the member's pending
    // designation.
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

        // Leader-only matrix: which squad members (SteamIDs, the leader's own included - a tag-only
        // marker, DESIGNATE never sends to it) each target id has been assigned to. Keyed by the same
        // persistentID the browser's tgt-targets rows carry.
        private static readonly Dictionary<uint, HashSet<ulong>> _assignments = new Dictionary<uint, HashSet<ulong>>();

        // Leader-only: the id set each member was last DESIGNATEd, so the matrix can tell a member
        // whose list is SENT from one that CHANGED since (td-matrix.js's memberStatus).
        private static readonly Dictionary<ulong, SortedSet<uint>> _sent = new Dictionary<ulong, SortedSet<uint>>();

        // Member-only: the leader's last DESIGNATE, still waiting for ADD/REPLACE/DISMISS on TGT.
        // Replaced wholesale on every receipt (a repeat DESIGNATE replaces, never merges — see
        // ReceiveDesignation).
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

        // A matrix cell: one target to one member, or back off it.
        internal static bool ToggleCell(uint id, ulong member)
        {
            if (id == 0 || member == 0) return false;
            Set(id, member, !Has(id, member));
            RebuildState();
            return true;
        }

        // A matrix row (the target's name): that target to every member, leader included. All-or-
        // nothing rather than a per-member flip, so a partly-assigned target gains the missing members
        // instead of losing the ones it had; a target already on every member is taken off them all.
        internal static bool ToggleRow(uint id, IReadOnlyList<ulong> members)
        {
            if (id == 0 || members.Count == 0) return false;
            bool full = members.All(m => Has(id, m));
            foreach (ulong member in members)
                if (member != 0) Set(id, member, !full);
            RebuildState();
            return true;
        }

        // A matrix column head: every target on the leader's table to that member, same all-or-
        // nothing rule as ToggleRow. `ids` is the table the leader is looking at — the page sends
        // it because the target list lives in the browser, not here (see the class header).
        internal static bool ToggleColumn(ulong member, IReadOnlyList<uint> ids)
        {
            if (member == 0 || ids.Count == 0) return false;
            bool full = ids.All(id => Has(id, member));
            foreach (uint id in ids)
                if (id != 0) Set(id, member, !full);
            RebuildState();
            return true;
        }

        // Records what DESIGNATE just sent this member (CommandDispatcher.TdDesignate, only after a
        // successful send), replacing whatever they were sent before.
        internal static void MarkSent(ulong member, IEnumerable<uint> ids)
        {
            if (member == 0) return;
            _sent[member] = new SortedSet<uint>(ids);
            RebuildState();
        }

        private static bool Has(uint id, ulong member) =>
            _assignments.TryGetValue(id, out HashSet<ulong>? members) && members.Contains(member);

        // An empty member set removes the target entirely, so the served state never carries "id: []".
        private static void Set(uint id, ulong member, bool on)
        {
            if (!_assignments.TryGetValue(id, out HashSet<ulong>? members))
            {
                if (!on) return;
                _assignments[id] = members = new HashSet<ulong>();
            }
            if (on) members.Add(member); else members.Remove(member);
            if (members.Count == 0) _assignments.Remove(id);
        }

        // A kick, leave or dropout drops the departed member's assignments and what they were last
        // sent, so nothing lingers if the same pilot is invited back. Called from Squad.cs's
        // CleanupRemovedMember.
        internal static void ClearMember(ulong member)
        {
            if (member == 0) return;
            bool changed = _sent.Remove(member);
            var emptyIds = new List<uint>();
            foreach (var kv in _assignments)
            {
                if (kv.Value.Remove(member)) changed = true;
                if (kv.Value.Count == 0) emptyIds.Add(kv.Key);
            }
            // Same "an empty member set removes the target entirely" convention Set() already uses.
            foreach (uint id in emptyIds) _assignments.Remove(id);
            if (changed) RebuildState();
        }

        // Leader's CLEAR — discards the matrix. What was already sent stays as it was: members keep
        // their lists, and the matrix shows those members as CHANGED until the next DESIGNATE.
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
        // targets in-game: the pending designation closes.
        internal static bool AcceptDesignated()
        {
            if (_designated.Count == 0) return false;
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
            bool changed = _assignments.Count > 0 || _sent.Count > 0 || _designated.Count > 0;
            _assignments.Clear();
            _sent.Clear();
            _designated = new List<Row>();
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
                sb.Append('"').Append(kv.Key.ToString(CultureInfo.InvariantCulture)).Append("\":[").Append(QuotedCsv(kv.Value)).Append(']');
            }
            sb.Append("},\"sent\":{");
            first = true;
            foreach (var kv in _sent)
            {
                if (!first) sb.Append(',');
                first = false;
                sb.Append('"').Append(kv.Key.ToString(CultureInfo.InvariantCulture)).Append("\":[").Append(Csv(kv.Value)).Append(']');
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

        // SteamIDs go out as strings: a 17-digit id overflows what a JavaScript number holds exactly.
        private static string QuotedCsv(IEnumerable<ulong> values) =>
            string.Join(",", values.Select(v => "\"" + v.ToString(CultureInfo.InvariantCulture) + "\""));

        private static string Csv<T>(IEnumerable<T> values) where T : IFormattable =>
            string.Join(",", values.Select(v => v.ToString(null, CultureInfo.InvariantCulture)));

        // Test-only: static fields are plugin-lifetime by design (same reasoning RouteStore.cs's
        // own ResetForTests gives) — a standalone test project resets them between test methods.
        internal static void ResetForTests()
        {
            _assignments.Clear();
            _sent.Clear();
            _designated = new List<Row>();
            StateJson = BuildStateJson();
        }
    }
}
