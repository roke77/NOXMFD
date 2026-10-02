using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace NOXMFD
{
    // Faction-wide identity broadcast, pure part (issue #106, #107, docs/faction-broadcast.md,
    // docs/self-callsign.md). Each NOXMFD instance describes ONLY itself - its own callsign, its
    // squad's callsign/flight and leader, its fuel and its squad protocol version - in the `presence`
    // beat; every receiver keeps a SteamID -> identity table, which is how a pilot sees every
    // pilot's callsign and every squad, not just their own. BCL-only so tools/tests links it
    // directly; Presence.cs is the live glue.
    //
    // A received record is untrusted input from another player's machine: bounded, range-checked,
    // and it can only ever describe its own sender (Steam authenticates `from`).
    internal static class FactionIdentity
    {
        internal const int SchemaVersion = 2;
        // The squad protocol this build speaks. Squads need every member on it: a peer that sends
        // less (or nothing) is listed as UPDATE NOXMFD and can't be invited or accepted from.
        internal const int SquadProtocolVersion = 2;
        internal const int MaxPayloadChars = 320;

        // A callsign outlives a missing beat by this long, for as long as the pilot stays in the
        // faction: a pilot still loading a mission sends nothing, and bouncing them to their Steam
        // name and back would flap every label. Matches Presence.AbsentLostSeconds.
        internal const float DesignationTtlSeconds = 120f;
        // Fuel changes while flying, so it goes stale much faster than a squad membership does.
        internal const float FuelTtlSeconds = 45f;

        // What one pilot says about themselves. Callsign is the pilot's own ("" = none set);
        // SquadCallsign is the squad's name ("" = not in a squad, LeaderId is then 0).
        internal readonly struct Identity
        {
            internal readonly string Callsign;
            internal readonly int Flight;
            internal readonly int Number;
            internal readonly string SquadCallsign;
            internal readonly int SquadFlight;
            internal readonly ulong LeaderId;
            internal readonly int SquadVersion;
            internal readonly float? Fuel;

            internal Identity(string callsign, int flight, int number, string squadCallsign, int squadFlight,
                              ulong leaderId, int squadVersion, float? fuel)
            {
                Callsign = callsign; Flight = flight; Number = number;
                SquadCallsign = squadCallsign; SquadFlight = squadFlight; LeaderId = leaderId;
                SquadVersion = squadVersion; Fuel = fuel;
            }

            internal bool HasCallsign => Callsign.Length > 0;
            internal bool InSquad => SquadCallsign.Length > 0;
            internal string Designation => HasCallsign ? SquadDesignations.Format(Callsign, Flight, Number) : string.Empty;
        }

        internal static string Serialize(Identity id)
        {
            var sb = new StringBuilder("{\"v\":").Append(SchemaVersion)
                .Append(",\"sv\":").Append(id.SquadVersion.ToString(CultureInfo.InvariantCulture));
            if (id.HasCallsign)
            {
                sb.Append(",\"p\":\"").Append(JsonLite.EscapeJson(id.Callsign)).Append('"')
                  .Append(",\"pf\":").Append(id.Flight.ToString(CultureInfo.InvariantCulture))
                  .Append(",\"pn\":").Append(id.Number.ToString(CultureInfo.InvariantCulture));
            }
            if (id.InSquad)
            {
                sb.Append(",\"c\":\"").Append(JsonLite.EscapeJson(id.SquadCallsign)).Append('"')
                  .Append(",\"f\":").Append(id.SquadFlight.ToString(CultureInfo.InvariantCulture))
                  .Append(",\"l\":\"").Append(id.LeaderId.ToString(CultureInfo.InvariantCulture)).Append('"');
            }
            if (id.Fuel.HasValue)
            {
                float f = Math.Max(0f, Math.Min(1f, id.Fuel.Value));
                sb.Append(",\"fu\":").Append(f.ToString("0.000", CultureInfo.InvariantCulture));
            }
            return sb.Append('}').ToString();
        }

        // false for anything that isn't a valid record - an even older client's empty beat, a newer
        // schema, garbage. The caller still counts the beat as presence; it just learns nothing else.
        // A schema 1 record (the build before self callsigns) yields only its fuel, with squad
        // version 0: that is how an older peer is recognised.
        internal static bool TryParse(string? payload, out Identity id)
        {
            id = default;
            if (string.IsNullOrEmpty(payload) || payload!.Length > MaxPayloadChars) return false;
            if (!(JsonLite.Parse(payload) is Dictionary<string, object?> o)) return false;
            if (!(o.TryGetValue("v", out object? v) && v is double dv)) return false;

            float? fuel = null;
            if (o.TryGetValue("fu", out object? fu) && fu is double dfu && !double.IsNaN(dfu) && !double.IsInfinity(dfu))
                fuel = (float)Math.Max(0.0, Math.Min(1.0, dfu));

            if (dv == 1) { id = new Identity(string.Empty, 0, 0, string.Empty, 0, 0, 0, fuel); return true; }
            if (dv != SchemaVersion) return false;

            int squadVersion = 0;
            if (o.TryGetValue("sv", out object? sv) && sv is double dsv && dsv == Math.Floor(dsv) && dsv >= 0 && dsv <= 99)
                squadVersion = (int)dsv;

            string callsign = string.Empty;
            int flight = 0, number = 0;
            if (o.TryGetValue("p", out object? p))
            {
                string? name = PilotCallsign.CleanName(p as string);
                if (name == null
                    || !TryInt(o, "pf", PilotCallsign.MinNumber, PilotCallsign.MaxNumber, out flight)
                    || !TryInt(o, "pn", PilotCallsign.MinNumber, PilotCallsign.MaxNumber, out number)) return false;
                callsign = name;
            }

            if (!o.TryGetValue("c", out object? c))
            {
                id = new Identity(callsign, flight, number, string.Empty, 0, 0, squadVersion, fuel);   // not in a squad: also the explicit clear
                return true;
            }

            string? squad = PilotCallsign.CleanName(c as string);
            if (squad == null || !TryInt(o, "f", PilotCallsign.MinNumber, PilotCallsign.MaxNumber, out int squadFlight)) return false;
            if (!(o.TryGetValue("l", out object? l) && l is string ls
                  && ulong.TryParse(ls, NumberStyles.None, CultureInfo.InvariantCulture, out ulong leader) && leader != 0)) return false;

            id = new Identity(callsign, flight, number, squad, squadFlight, leader, squadVersion, fuel);
            return true;
        }

        // SteamID -> identity as last heard, with when. Time is passed in so tests drive it.
        internal sealed class Table
        {
            // Seq orders pilots by when this client first heard them in their current squad: the join
            // order of an other squad's roster, which the record does not carry.
            private struct Entry { public Identity Id; public float At; public long Seq; }
            private readonly Dictionary<ulong, Entry> _entries = new Dictionary<ulong, Entry>();
            private long _seq;

            internal void Note(ulong from, Identity id, float now)
            {
                long seq = _entries.TryGetValue(from, out Entry old) && old.Id.InSquad && id.InSquad && old.Id.LeaderId == id.LeaderId
                    ? old.Seq : ++_seq;
                _entries[from] = new Entry { Id = id, At = now, Seq = seq };
            }

            // SteamID -> designation for everyone heard with a callsign and still in the faction
            // within the TTL. A pilot without one simply isn't in the result.
            internal Dictionary<ulong, string> Designations(float now, Func<ulong, bool> inFaction)
            {
                var result = new Dictionary<ulong, string>();
                foreach (var kv in _entries)
                    if (kv.Value.Id.HasCallsign && Live(kv.Key, kv.Value, now, inFaction))
                        result[kv.Key] = kv.Value.Id.Designation;
                return result;
            }

            internal float? FuelFor(ulong steamId, float now) =>
                _entries.TryGetValue(steamId, out Entry e) && e.Id.Fuel.HasValue && now - e.At < FuelTtlSeconds
                    ? e.Id.Fuel : null;

            // The squad protocol a pilot last announced; 0 for one never heard from, or heard only
            // through an older client's record.
            internal int SquadVersion(ulong steamId) =>
                _entries.TryGetValue(steamId, out Entry e) ? e.Id.SquadVersion : 0;

            // Every squad heard of, except the viewer's own (excludeLeader), ordered by callsign then
            // flight. Callsign and flight come from the leader's own record when we have it: members
            // of a squad being renamed briefly disagree with it. Members list leader first, then in
            // the order this client first heard them.
            internal List<FactionSquad> Squads(float now, Func<ulong, bool> inFaction, ulong excludeLeader)
            {
                var byLeader = new Dictionary<ulong, FactionSquad>();
                var order = new Dictionary<ulong, List<(ulong Id, long Seq)>>();
                foreach (var kv in _entries)
                {
                    Identity id = kv.Value.Id;
                    if (!id.InSquad || id.LeaderId == excludeLeader || !Live(kv.Key, kv.Value, now, inFaction)) continue;
                    if (!byLeader.TryGetValue(id.LeaderId, out FactionSquad? sq))
                    {
                        byLeader[id.LeaderId] = sq = new FactionSquad(id.LeaderId, id.SquadCallsign, id.SquadFlight);
                        order[id.LeaderId] = new List<(ulong, long)>();
                    }
                    if (kv.Key == id.LeaderId) { sq.Callsign = id.SquadCallsign; sq.Flight = id.SquadFlight; }
                    order[id.LeaderId].Add((kv.Key, kv.Value.Seq));
                }
                var list = new List<FactionSquad>(byLeader.Values);
                foreach (FactionSquad sq in list)
                {
                    List<(ulong Id, long Seq)> members = order[sq.LeaderId];
                    members.Sort((a, b) =>
                        a.Id == sq.LeaderId ? (b.Id == sq.LeaderId ? 0 : -1) : b.Id == sq.LeaderId ? 1 : a.Seq.CompareTo(b.Seq));
                    foreach (var m in members) sq.Members.Add(m.Id);
                }
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
            internal readonly List<ulong> Members = new List<ulong>();
            internal FactionSquad(ulong leaderId, string callsign, int flight) { LeaderId = leaderId; Callsign = callsign; Flight = flight; }
        }

        // The viewer's own callsign wins over what the table holds for them (a table entry for self
        // only exists if our own beat looped back, which Steam does not do, but the order is the
        // contract); everyone else comes from the faction table.
        internal static Dictionary<ulong, string> Merge(IReadOnlyDictionary<ulong, string> own, IReadOnlyDictionary<ulong, string> faction)
        {
            var result = new Dictionary<ulong, string>();
            foreach (var kv in faction) result[kv.Key] = kv.Value;
            foreach (var kv in own) result[kv.Key] = kv.Value;
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

        // SteamID -> {"d": designation, "dup": another pilot flies the same one}, the page's single
        // source for a pilot's callsign (state.pilots in /squad). Pass every pilot including self.
        internal static string PilotsJson(IReadOnlyDictionary<ulong, string> designations)
        {
            var count = new Dictionary<string, int>();
            foreach (string d in designations.Values)
            {
                string key = d.ToUpperInvariant();
                count[key] = count.TryGetValue(key, out int n) ? n + 1 : 1;
            }
            var sb = new StringBuilder("{");
            bool first = true;
            foreach (var kv in designations)
            {
                if (!first) sb.Append(',');
                first = false;
                sb.Append('"').Append(kv.Key.ToString(CultureInfo.InvariantCulture)).Append("\":{\"d\":\"")
                  .Append(JsonLite.EscapeJson(kv.Value)).Append("\",\"dup\":")
                  .Append(count[kv.Value.ToUpperInvariant()] > 1 ? "true" : "false").Append('}');
            }
            return sb.Append('}').ToString();
        }

        // What SQD shows of the faction beyond the viewer's own squad (state.faction in /squad):
        // every other squad with its members, and whether the viewer's own (callsign, flight) clashes
        // with one of them. The page marks the pairs in use in its pickers from the squads list. Steam
        // name and aircraft come from the local faction scan, not from the broadcast; a member's own
        // callsign comes from state.pilots.
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
                    ulong m = sq.Members[j];
                    if (j > 0) sb.Append(',');
                    sb.Append("{\"id\":\"").Append(m.ToString(CultureInfo.InvariantCulture))
                      .Append("\",\"leader\":").Append(m == sq.LeaderId ? "true" : "false")
                      .Append(",\"name\":\"").Append(JsonLite.EscapeJson(nameFor(m)))
                      .Append("\",\"aircraft\":\"").Append(JsonLite.EscapeJson(aircraftFor(m))).Append("\"}");
                }
                sb.Append("]}");
            }
            sb.Append("]");
            bool selfDup = own.HasValue && dup.Contains((own.Value.Callsign.ToUpperInvariant(), own.Value.Flight));
            return sb.Append(",\"selfDup\":").Append(selfDup ? "true" : "false").Append('}').ToString();
        }

        private static bool TryInt(Dictionary<string, object?> o, string key, int min, int max, out int value)
        {
            value = 0;
            if (!(o.TryGetValue(key, out object? v) && v is double d) || d != Math.Floor(d) || d < min || d > max) return false;
            value = (int)d;
            return true;
        }
    }
}
