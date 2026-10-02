using System.Collections.Generic;
using NOXMFD;
using static NOXMFD.FactionIdentity;

namespace NOXMFD.Tests
{
    public class FactionIdentityTests
    {
        private const ulong Leader = 100, Member = 200, Other = 300;
        private static bool InFaction(ulong _) => true;

        // A pilot flying VIPER 1-2 in the TALON 1 squad led by `leader`.
        private static Identity InSquad(string pilot = "VIPER", int pf = 1, int pn = 2, string squad = "TALON", int sf = 1,
                                        ulong leader = Leader, int sv = SquadProtocolVersion, float? fuel = null) =>
            new Identity(pilot, pf, pn, squad, sf, leader, sv, fuel);

        private static Identity Solo(string pilot = "VIPER", int pf = 1, int pn = 2, int sv = SquadProtocolVersion, float? fuel = null) =>
            new Identity(pilot, pf, pn, "", 0, 0, sv, fuel);

        [Fact]
        public void Serialize_then_TryParse_round_trips_a_squad_member()
        {
            string wire = Serialize(InSquad(fuel: 0.7344f));
            Assert.True(TryParse(wire, out Identity id));
            Assert.Equal("VIPER 1-2", id.Designation);
            Assert.Equal("TALON", id.SquadCallsign);
            Assert.Equal(1, id.SquadFlight);
            Assert.Equal(Leader, id.LeaderId);
            Assert.Equal(SquadProtocolVersion, id.SquadVersion);
            Assert.Equal(0.734f, id.Fuel!.Value, 3);
            Assert.True(wire.Length <= MaxPayloadChars);
        }

        [Fact]
        public void A_pilot_outside_a_squad_keeps_the_callsign_and_is_the_explicit_squad_clear()
        {
            Assert.True(TryParse(Serialize(Solo(fuel: 0.5f)), out Identity id));
            Assert.True(id.HasCallsign);
            Assert.False(id.InSquad);
            Assert.Equal(0.5f, id.Fuel!.Value, 3);
        }

        [Fact]
        public void A_pilot_with_no_callsign_sends_none()
        {
            string wire = Serialize(new Identity("", 0, 0, "", 0, 0, SquadProtocolVersion, null));
            Assert.DoesNotContain("\"p\"", wire);
            Assert.True(TryParse(wire, out Identity id));
            Assert.False(id.HasCallsign);
            Assert.Equal("", id.Designation);
        }

        [Fact]
        public void The_longest_record_fits_the_cap()
        {
            string wire = Serialize(InSquad("ABCDEFGHIJKLMNOPQRST", 9, 9, "ABCDEFGHIJKLMNOPQRST", 9, ulong.MaxValue, 99, 1f));
            Assert.True(wire.Length <= MaxPayloadChars, wire.Length + " chars");
            Assert.True(TryParse(wire, out _));
        }

        [Theory]
        [InlineData("")]                                                                        // an older client's empty beat
        [InlineData("not json")]
        [InlineData("{\"v\":3,\"sv\":3}")]                                                      // newer schema
        [InlineData("{\"v\":2,\"sv\":2,\"p\":\"\",\"pf\":1,\"pn\":1}")]                         // empty callsign
        [InlineData("{\"v\":2,\"sv\":2,\"p\":\"VIPER\"}")]                                      // numbers missing
        [InlineData("{\"v\":2,\"sv\":2,\"p\":\"VIPER\",\"pf\":0,\"pn\":1}")]                    // flight below 1
        [InlineData("{\"v\":2,\"sv\":2,\"p\":\"VIPER\",\"pf\":1,\"pn\":10}")]                   // number above 9
        [InlineData("{\"v\":2,\"sv\":2,\"p\":\"VIPER\",\"pf\":1.5,\"pn\":1}")]                  // fractional
        [InlineData("{\"v\":2,\"sv\":2,\"p\":\"VI\\nPER\",\"pf\":1,\"pn\":1}")]            // control character
        [InlineData("{\"v\":2,\"sv\":2,\"p\":\"ABCDEFGHIJKLMNOPQRSTU\",\"pf\":1,\"pn\":1}")]    // 21 chars
        [InlineData("{\"v\":2,\"sv\":2,\"c\":\"TALON\",\"f\":1}")]                              // leader missing
        [InlineData("{\"v\":2,\"sv\":2,\"c\":\"TALON\",\"f\":1,\"l\":\"0\"}")]                  // no leader
        [InlineData("{\"v\":2,\"sv\":2,\"c\":\"TALON\",\"f\":1,\"l\":\"abc\"}")]
        [InlineData("{\"v\":2,\"sv\":2,\"c\":\"TALON\",\"f\":10,\"l\":\"100\"}")]               // squad flight above 9
        [InlineData("{\"v\":2,\"sv\":2,\"c\":\"\",\"f\":1,\"l\":\"100\"}")]                     // empty squad callsign
        public void TryParse_rejects_malformed_records(string wire)
        {
            Assert.False(TryParse(wire, out _));
        }

        [Fact]
        public void TryParse_rejects_oversize_payloads()
        {
            string wire = "{\"v\":2,\"sv\":2,\"x\":\"" + new string('a', 400) + "\"}";
            Assert.False(TryParse(wire, out _));
        }

        [Fact]
        public void An_older_record_gives_fuel_only_and_squad_version_0()
        {
            Assert.True(TryParse("{\"v\":1,\"c\":\"TALON\",\"f\":1,\"s\":2,\"l\":\"100\",\"fu\":0.4}", out Identity id));
            Assert.Equal(0, id.SquadVersion);
            Assert.False(id.HasCallsign);
            Assert.False(id.InSquad);
            Assert.Equal(0.4f, id.Fuel!.Value, 3);
        }

        [Fact]
        public void A_missing_or_wild_squad_version_counts_as_older()
        {
            Assert.True(TryParse("{\"v\":2}", out Identity none));
            Assert.Equal(0, none.SquadVersion);
            Assert.True(TryParse("{\"v\":2,\"sv\":-4}", out Identity neg));
            Assert.Equal(0, neg.SquadVersion);
            Assert.True(TryParse("{\"v\":2,\"sv\":\"2\"}", out Identity text));
            Assert.Equal(0, text.SquadVersion);
        }

        [Fact]
        public void Fuel_is_clamped_and_a_non_number_is_dropped()
        {
            Assert.True(TryParse("{\"v\":2,\"sv\":2,\"fu\":7.5}", out Identity high));
            Assert.Equal(1f, high.Fuel!.Value);
            Assert.True(TryParse("{\"v\":2,\"sv\":2,\"fu\":\"lots\"}", out Identity junk));
            Assert.Null(junk.Fuel);
        }

        [Fact]
        public void Table_keeps_a_designation_through_a_missed_beat_but_not_past_the_ttl()
        {
            var t = new Table();
            t.Note(Member, InSquad(), 0f);
            Assert.Equal("VIPER 1-2", t.Designations(60f, InFaction)[Member]);   // many beats missed, still held
            Assert.Empty(t.Designations(DesignationTtlSeconds + 1f, InFaction));
        }

        [Fact]
        public void Table_drops_a_pilot_who_left_the_faction()
        {
            var t = new Table();
            t.Note(Member, InSquad(), 0f);
            Assert.Empty(t.Designations(1f, id => false));
        }

        [Fact]
        public void Designations_cover_pilots_with_a_callsign_in_or_out_of_a_squad()
        {
            var t = new Table();
            t.Note(Member, InSquad(), 0f);
            t.Note(Other, Solo("COLT", 3, 1), 0f);
            t.Note(500, new Identity("", 0, 0, "TALON", 1, Leader, SquadProtocolVersion, null), 0f);   // in a squad, no callsign
            var d = t.Designations(1f, InFaction);
            Assert.Equal("VIPER 1-2", d[Member]);
            Assert.Equal("COLT 3-1", d[Other]);
            Assert.Equal(2, d.Count);
        }

        [Fact]
        public void A_changed_callsign_replaces_the_old_one_at_once()
        {
            var t = new Table();
            t.Note(Member, Solo("VIPER", 1, 2), 0f);
            t.Note(Member, Solo("COLT", 4, 4), 1f);
            Assert.Equal("COLT 4-4", t.Designations(1f, InFaction)[Member]);
        }

        [Fact]
        public void Fuel_goes_stale_much_faster_than_a_designation()
        {
            var t = new Table();
            t.Note(Member, InSquad(fuel: 0.4f), 0f);
            Assert.Equal(0.4f, t.FuelFor(Member, 30f)!.Value, 3);
            Assert.Null(t.FuelFor(Member, FuelTtlSeconds + 1f));
            Assert.Null(t.FuelFor(Other, 0f));
        }

        [Fact]
        public void SquadVersion_is_0_for_a_pilot_never_heard_or_heard_only_through_an_older_record()
        {
            var t = new Table();
            t.Note(Member, Solo(), 0f);
            t.Note(Other, Solo(sv: 0), 0f);
            Assert.Equal(SquadProtocolVersion, t.SquadVersion(Member));
            Assert.Equal(0, t.SquadVersion(Other));
            Assert.Equal(0, t.SquadVersion(999));
        }

        [Fact]
        public void Merge_prefers_the_own_callsign()
        {
            var own = new Dictionary<ulong, string> { [Member] = "VIPER 1-2" };
            var faction = new Dictionary<ulong, string> { [Member] = "STALE 9-9", [Other] = "COLT 2-1" };
            var merged = Merge(own, faction);
            Assert.Equal("VIPER 1-2", merged[Member]);
            Assert.Equal("COLT 2-1", merged[Other]);
        }

        [Fact]
        public void Squads_groups_by_leader_not_by_callsign_and_skips_the_own_squad()
        {
            var t = new Table();
            t.Note(100, InSquad("A", leader: 100), 0f);                         // TALON 1, led by 100
            t.Note(101, InSquad("B", leader: 100), 0f);
            t.Note(102, InSquad("C", leader: 100), 0f);
            t.Note(200, InSquad("D", leader: 200), 0f);                         // also TALON 1, led by 200
            t.Note(300, InSquad("E", squad: "VIPER", sf: 2, leader: 300), 0f);

            var squads = t.Squads(1f, InFaction, excludeLeader: 300);           // viewer is in VIPER 2
            Assert.Equal(2, squads.Count);
            Assert.Equal(100UL, squads[0].LeaderId);                            // equal callsign and flight: by leader id
            Assert.Equal(200UL, squads[1].LeaderId);
        }

        [Fact]
        public void Squad_members_list_the_leader_first_then_in_the_order_they_were_first_heard()
        {
            var t = new Table();
            t.Note(103, InSquad("C", leader: 100), 0f);                         // heard first, but not the leader
            t.Note(101, InSquad("A", leader: 100), 0f);
            t.Note(100, InSquad("L", leader: 100), 0f);
            t.Note(102, InSquad("B", leader: 100), 0f);
            t.Note(103, InSquad("C", leader: 100), 5f);                         // a repeat beat does not move them
            Assert.Equal(new[] { 100UL, 103UL, 101UL, 102UL }, t.Squads(6f, InFaction, 0)[0].Members);
        }

        [Fact]
        public void A_pilot_who_changes_squad_goes_to_the_back_of_the_new_one()
        {
            var t = new Table();
            t.Note(101, InSquad("A", leader: 100), 0f);
            t.Note(102, InSquad("B", leader: 100), 0f);
            t.Note(101, InSquad("A", leader: 200), 1f);                         // 101 moves to squad 200 and back
            t.Note(101, InSquad("A", leader: 100), 2f);
            t.Note(100, InSquad("L", leader: 100), 2f);
            Assert.Equal(new[] { 100UL, 102UL, 101UL }, t.Squads(3f, InFaction, 0)[0].Members);
        }

        [Fact]
        public void Squads_takes_callsign_and_flight_from_the_leader_during_a_rename()
        {
            var t = new Table();
            t.Note(101, InSquad("A", squad: "OLD", leader: 100), 0f);           // member hasn't caught up
            t.Note(100, InSquad("L", squad: "NEW", sf: 3, leader: 100), 0f);
            var sq = t.Squads(1f, InFaction, 0)[0];
            Assert.Equal("NEW", sq.Callsign);
            Assert.Equal(3, sq.Flight);
        }

        [Fact]
        public void A_squad_can_hold_callsigns_that_do_not_match_its_name()
        {
            var t = new Table();
            t.Note(100, InSquad("VIPER", 1, 2, leader: 100), 0f);
            t.Note(101, InSquad("ENFIELD", 3, 1, leader: 100), 0f);
            var d = t.Designations(1f, InFaction);
            Assert.Equal("VIPER 1-2", d[100]);
            Assert.Equal("ENFIELD 3-1", d[101]);
            Assert.Equal("TALON", t.Squads(1f, InFaction, 0)[0].Callsign);
        }

        [Fact]
        public void PilotsJson_marks_pilots_sharing_a_callsign_case_insensitively()
        {
            string json = PilotsJson(new Dictionary<ulong, string> { [1] = "COLT 1-1", [2] = "colt 1-1", [3] = "COLT 1-2" });
            var root = (Dictionary<string, object?>)JsonLite.Parse(json)!;
            Assert.Equal(true, ((Dictionary<string, object?>)root["1"]!)["dup"]);
            Assert.Equal(true, ((Dictionary<string, object?>)root["2"]!)["dup"]);
            Assert.Equal(false, ((Dictionary<string, object?>)root["3"]!)["dup"]);
            Assert.Equal("COLT 1-2", ((Dictionary<string, object?>)root["3"]!)["d"]);
        }

        [Fact]
        public void PilotsJson_with_nobody_is_an_empty_object()
        {
            Assert.Equal("{}", PilotsJson(new Dictionary<ulong, string>()));
        }

        [Fact]
        public void FactionJson_lists_other_squads_with_names_aircraft_leader_flag_and_duplicate_flags()
        {
            var t = new Table();
            t.Note(100, InSquad("A", leader: 100), 0f);       // another TALON 1
            t.Note(101, InSquad("B", leader: 100), 0f);
            t.Note(300, InSquad("E", squad: "VIPER", sf: 2, leader: 300), 0f);
            var others = t.Squads(1f, InFaction, excludeLeader: 900);

            string json = FactionJson(others, ("TALON", 1, 900UL), id => "n" + id, id => id == 101 ? "Ifrit" : "");
            var root = (Dictionary<string, object?>)JsonLite.Parse(json)!;
            var squads = (List<object?>)root["squads"]!;
            var talon = (Dictionary<string, object?>)squads[0]!;
            Assert.Equal("TALON", talon["callsign"]);
            Assert.Equal(true, talon["dup"]);                  // the viewer's own squad is TALON 1 too
            Assert.Equal(true, root["selfDup"]);
            var members = (List<object?>)talon["members"]!;
            Assert.Equal("Ifrit", ((Dictionary<string, object?>)members[1]!)["aircraft"]);
            Assert.Equal("n100", ((Dictionary<string, object?>)members[0]!)["name"]);
            Assert.Equal(true, ((Dictionary<string, object?>)members[0]!)["leader"]);
            Assert.Equal(false, ((Dictionary<string, object?>)members[1]!)["leader"]);
            Assert.False(((Dictionary<string, object?>)members[0]!).ContainsKey("slot"));
            Assert.Equal(false, ((Dictionary<string, object?>)squads[1]!)["dup"]);
            Assert.Equal(2, squads.Count);
        }

        [Fact]
        public void FactionJson_with_no_squads_and_no_own_squad_is_empty_and_valid()
        {
            var root = (Dictionary<string, object?>)JsonLite.Parse(FactionJson(new List<FactionSquad>(), null, id => "", id => ""))!;
            Assert.Empty((List<object?>)root["squads"]!);
            Assert.Equal(false, root["selfDup"]);
        }

        [Fact]
        public void Duplicates_needs_two_different_leaders_on_the_same_callsign_and_flight()
        {
            var d = Duplicates(new[]
            {
                ("TALON", 1, 100UL), ("talon", 1, 200UL),   // clash, case-insensitive
                ("TALON", 2, 300UL),                        // same callsign, other flight
                ("VIPER", 1, 400UL), ("VIPER", 1, 400UL),   // one leader twice is not a clash
            });
            Assert.Single(d);
            Assert.Contains(("TALON", 1), d);
        }
    }
}
