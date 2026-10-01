using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace NOXMFD
{
    // Faction-wide identity broadcast, pure part (issue #106, docs/faction-broadcast.md). Each NOXMFD
    // instance describes ONLY itself — squad callsign/flight/slot, its leader and its fuel — in the
    // `presence` beat; every receiver keeps a SteamID → identity table and merges it with its own
    // squad's roster, so a pilot sees every squad's designations, not just their own. BCL-only so
    // tools/tests links it directly; Presence.cs is the live glue.
    //
    // A received record is untrusted input from another player's machine: bounded, range-checked,
    // and it can only ever describe its own sender (Steam authenticates `from`).
    internal static class FactionIdentity
    {
        internal const int SchemaVersion = 1;
        internal const int MaxPayloadChars = 256;
        internal const int MaxCallsignChars = 20;   // Squad.CreateSquad's limit
        internal const int MaxSlot = 99;

        // A designation outlives a missing beat by this long, for as long as the pilot stays in the
        // faction: a pilot still loading a mission sends nothing, and bouncing them to their Steam
        // name and back would flap every label. Matches Presence.AbsentLostSeconds.
        internal const float DesignationTtlSeconds = 120f;
        // Fuel changes while flying, so it goes stale much faster than a squad membership does.
        internal const float FuelTtlSeconds = 45f;

        // What one pilot says about themselves. Callsign is empty outside a squad; LeaderId is then 0.
        internal readonly struct Identity
        {
            internal readonly string Callsign;
            internal readonly int Flight;
            internal readonly int Slot;
            internal readonly ulong LeaderId;
            internal readonly float? Fuel;

            internal Identity(string callsign, int flight, int slot, ulong leaderId, float? fuel)
            {
                Callsign = callsign; Flight = flight; Slot = slot; LeaderId = leaderId; Fuel = fuel;
            }

            internal bool InSquad => Callsign.Length > 0;
            internal string Designation => SquadDesignations.Format(Callsign, Flight, Slot);
        }

        internal static string Serialize(Identity id)
        {
            var sb = new StringBuilder("{\"v\":").Append(SchemaVersion);
            if (id.InSquad)
            {
                sb.Append(",\"c\":\"").Append(JsonLite.EscapeJson(id.Callsign)).Append('"')
                  .Append(",\"f\":").Append(id.Flight.ToString(CultureInfo.InvariantCulture))
                  .Append(",\"s\":").Append(id.Slot.ToString(CultureInfo.InvariantCulture))
                  .Append(",\"l\":\"").Append(id.LeaderId.ToString(CultureInfo.InvariantCulture)).Append('"');
            }
            if (id.Fuel.HasValue)
            {
                float f = Math.Max(0f, Math.Min(1f, id.Fuel.Value));
                sb.Append(",\"fu\":").Append(f.ToString("0.000", CultureInfo.InvariantCulture));
            }
            return sb.Append('}').ToString();
        }

        // false for anything that isn't a valid v1 record — an older client's empty beat, a newer
        // schema, garbage. The caller still counts the beat as presence; it just learns nothing else.
        internal static bool TryParse(string? payload, ulong from, out Identity id)
        {
            id = default;
            if (string.IsNullOrEmpty(payload) || payload!.Length > MaxPayloadChars) return false;
            if (!(JsonLite.Parse(payload) is Dictionary<string, object?> o)) return false;
            if (!(o.TryGetValue("v", out object? v) && v is double dv && dv == SchemaVersion)) return false;

            float? fuel = null;
            if (o.TryGetValue("fu", out object? fu) && fu is double dfu && !double.IsNaN(dfu) && !double.IsInfinity(dfu))
                fuel = (float)Math.Max(0.0, Math.Min(1.0, dfu));

            if (!o.TryGetValue("c", out object? c))
            {
                id = new Identity(string.Empty, 0, 0, 0, fuel);   // not in a squad: also the explicit clear
                return true;
            }

            if (!(c is string callsign)) return false;
            callsign = callsign.Trim();
            if (callsign.Length == 0 || callsign.Length > MaxCallsignChars || HasControlChar(callsign)) return false;
            if (!TryInt(o, "f", 1, 9, out int flight)) return false;
            if (!TryInt(o, "s", 1, MaxSlot, out int slot)) return false;
            if (!(o.TryGetValue("l", out object? l) && l is string ls
                  && ulong.TryParse(ls, NumberStyles.None, CultureInfo.InvariantCulture, out ulong leader) && leader != 0)) return false;
            // Slot 1 is the leader, so the leader id and the sender agree exactly when the slot is 1.
            if ((slot == 1) != (leader == from)) return false;

            id = new Identity(callsign, flight, slot, leader, fuel);
            return true;
        }

        // SteamID → identity as last heard, with when. Time is passed in so tests drive it.
        internal sealed class Table
        {
            private struct Entry { public Identity Id; public float At; }
            private readonly Dictionary<ulong, Entry> _entries = new Dictionary<ulong, Entry>();

            internal void Note(ulong from, Identity id, float now) => _entries[from] = new Entry { Id = id, At = now };

            // SteamID → designation for everyone heard in a squad and still in the faction within the
            // TTL. A record without a callsign (the explicit clear) simply isn't in the result.
            internal Dictionary<ulong, string> Designations(float now, Func<ulong, bool> inFaction)
            {
                var result = new Dictionary<ulong, string>();
                foreach (var kv in _entries)
                    if (kv.Value.Id.InSquad && Live(kv.Key, kv.Value, now, inFaction))
                        result[kv.Key] = kv.Value.Id.Designation;
                return result;
            }

            internal float? FuelFor(ulong steamId, float now) =>
                _entries.TryGetValue(steamId, out Entry e) && e.Id.Fuel.HasValue && now - e.At < FuelTtlSeconds
                    ? e.Id.Fuel : null;

            // Every squad heard of, except the viewer's own (excludeLeader), ordered by callsign then
            // flight. Callsign and flight come from the leader's own record when we have it: members
            // of a squad being re-numbered briefly disagree with it.
            internal List<FactionSquad> Squads(float now, Func<ulong, bool> inFaction, ulong excludeLeader)
            {
                var byLeader = new Dictionary<ulong, FactionSquad>();
                foreach (var kv in _entries)
                {
                    Identity id = kv.Value.Id;
                    if (!id.InSquad || id.LeaderId == excludeLeader || !Live(kv.Key, kv.Value, now, inFaction)) continue;
                    if (!byLeader.TryGetValue(id.LeaderId, out FactionSquad? sq))
                        byLeader[id.LeaderId] = sq = new FactionSquad(id.LeaderId, id.Callsign, id.Flight);
                    if (kv.Key == id.LeaderId) { sq.Callsign = id.Callsign; sq.Flight = id.Flight; }
                    sq.Members.Add((kv.Key, id.Slot));
                }
                var list = new List<FactionSquad>(byLeader.Values);
                foreach (FactionSquad sq in list) sq.Members.Sort((a, b) => a.Slot.CompareTo(b.Slot));
                list.Sort((a, b) =>
                {
                    int c = string.CompareOrdinal(a.Callsign, b.Callsign);
                    return c != 0 ? c : a.Flight != b.Flight ? a.Flight.CompareTo(b.Flight) : a.LeaderId.CompareTo(b.LeaderId);
                });
                return list;
            }

            private static bool Live(ulong id, Entry e, float now, Func<ulong, bool> inFaction) =>
                now - e.At < DesignationTtlSeconds && inFaction(id);
        }

        internal sealed class FactionSquad
        {
            internal readonly ulong LeaderId;
            internal string Callsign;
            internal int Flight;
            internal readonly List<(ulong Id, int Slot)> Members = new List<(ulong Id, int Slot)>();
            internal FactionSquad(ulong leaderId, string callsign, int flight) { LeaderId = leaderId; Callsign = callsign; Flight = flight; }
        }

        // The viewer's own squad wins for squadmates (its roster is authoritative); everyone else
        // comes from the faction table.
        internal static Dictionary<ulong, string> Merge(IReadOnlyDictionary<ulong, string> ownSquad, IReadOnlyDictionary<ulong, string> faction)
        {
            var result = new Dictionary<ulong, string>();
            foreach (var kv in faction) result[kv.Key] = kv.Value;
            foreach (var kv in ownSquad) result[kv.Key] = kv.Value;
            return result;
        }

        // The (callsign, flight) pairs flown by more than one squad, for SQD's SAME DESIGNATION
        // warnings. Pass every squad including the viewer's own.
        internal static HashSet<(string Callsign, int Flight)> Duplicates(IEnumerable<(string Callsign, int Flight, ulong LeaderId)> squads)
        {
            var leaders = new Dictionary<(string, int), HashSet<ulong>>();
            foreach (var s in squads)
            {
                var key = (s.Callsign.ToUpperInvariant(), s.Flight);
                if (!leaders.TryGetValue(key, out HashSet<ulong>? set)) leaders[key] = set = new HashSet<ulong>();
                set.Add(s.LeaderId);
            }
            var result = new HashSet<(string Callsign, int Flight)>();
            foreach (var kv in leaders) if (kv.Value.Count > 1) result.Add(kv.Key);
            return result;
        }

        // What SQD shows of the faction beyond the viewer's own squad (state.faction in /squad):
        // every other squad with its members, the (callsign, flight) pairs in use anywhere in the
        // faction for the picker's marks, and whether the viewer's own pair clashes. Steam name and
        // aircraft come from the local faction scan, not from the broadcast.
        internal static string FactionJson(IReadOnlyList<FactionSquad> others, (string Callsign, int Flight, ulong LeaderId)? own,
                                           Func<ulong, string> nameFor, Func<ulong, string> aircraftFor)
        {
            var all = new List<(string Callsign, int Flight, ulong LeaderId)>();
            foreach (FactionSquad sq in others) all.Add((sq.Callsign, sq.Flight, sq.LeaderId));
            if (own.HasValue) all.Add(own.Value);
            HashSet<(string Callsign, int Flight)> dup = Duplicates(all);

            var sb = new StringBuilder("{\"squads\":[");
            for (int i = 0; i < others.Count; i++)
            {
                FactionSquad sq = others[i];
                if (i > 0) sb.Append(',');
                sb.Append("{\"leader\":\"").Append(sq.LeaderId.ToString(CultureInfo.InvariantCulture))
                  .Append("\",\"callsign\":\"").Append(JsonLite.EscapeJson(sq.Callsign))
                  .Append("\",\"flight\":").Append(sq.Flight.ToString(CultureInfo.InvariantCulture))
                  .Append(",\"dup\":").Append(dup.Contains((sq.Callsign.ToUpperInvariant(), sq.Flight)) ? "true" : "false")
                  .Append(",\"members\":[");
                for (int j = 0; j < sq.Members.Count; j++)
                {
                    var m = sq.Members[j];
                    if (j > 0) sb.Append(',');
                    sb.Append("{\"id\":\"").Append(m.Id.ToString(CultureInfo.InvariantCulture))
                      .Append("\",\"slot\":").Append(m.Slot.ToString(CultureInfo.InvariantCulture))
                      .Append(",\"name\":\"").Append(JsonLite.EscapeJson(nameFor(m.Id)))
                      .Append("\",\"aircraft\":\"").Append(JsonLite.EscapeJson(aircraftFor(m.Id))).Append("\"}");
                }
                sb.Append("]}");
            }
            sb.Append("],\"used\":[");
            var used = new SortedSet<(string Callsign, int Flight)>();
            foreach (var s in all) used.Add((s.Callsign.ToUpperInvariant(), s.Flight));
            bool first = true;
            foreach (var u in used)
            {
                if (!first) sb.Append(',');
                first = false;
                sb.Append("[\"").Append(JsonLite.EscapeJson(u.Callsign)).Append("\",").Append(u.Flight.ToString(CultureInfo.InvariantCulture)).Append(']');
            }
            bool selfDup = own.HasValue && dup.Contains((own.Value.Callsign.ToUpperInvariant(), own.Value.Flight));
            return sb.Append("],\"selfDup\":").Append(selfDup ? "true" : "false").Append('}').ToString();
        }

        private static bool TryInt(Dictionary<string, object?> o, string key, int min, int max, out int value)
        {
            value = 0;
            if (!(o.TryGetValue(key, out object? v) && v is double d) || d != Math.Floor(d) || d < min || d > max) return false;
            value = (int)d;
            return true;
        }

        private static bool HasControlChar(string s)
        {
            foreach (char ch in s) if (ch < 0x20 || ch == 0x7f) return true;
            return false;
        }

    }
}
