using System.Collections.Generic;
using NOXMFD;
using static NOXMFD.FactionIdentity;

namespace NOXMFD.Tests
{
    public class FactionIdentityTests
    {
        private const ulong Leader = 100, Member = 200, Other = 300;
        private static bool InFaction(ulong _) => true;

        private static Identity Squad(string cs = "TALON", int flight = 1, int slot = 2, ulong leader = Leader, float? fuel = null) =>
            new Identity(cs, flight, slot, leader, fuel);

        [Fact]
        public void Serialize_then_TryParse_round_trips_a_squad_member()
        {
            string wire = Serialize(Squad(fuel: 0.7344f));
            Assert.True(TryParse(wire, Member, out Identity id));
            Assert.Equal("TALON 1-2", id.Designation);
            Assert.Equal(Leader, id.LeaderId);
            Assert.Equal(0.734f, id.Fuel!.Value, 3);
        }

        [Fact]
        public void A_pilot_outside_a_squad_is_the_explicit_clear()
        {
            Assert.True(TryParse(Serialize(new Identity("", 0, 0, 0, 0.5f)), Member, out Identity id));
            Assert.False(id.InSquad);
            Assert.Equal(0.5f, id.Fuel!.Value, 3);
        }

        [Theory]
        [InlineData("")]                                                            // an older client's empty beat
        [InlineData("not json")]
        [InlineData("{\"v\":2,\"c\":\"TALON\",\"f\":1,\"s\":2,\"l\":\"100\"}")]     // newer schema
        [InlineData("{\"v\":1,\"c\":\"\",\"f\":1,\"s\":2,\"l\":\"100\"}")]          // empty callsign
        [InlineData("{\"v\":1,\"c\":\"TALON\",\"f\":0,\"s\":2,\"l\":\"100\"}")]     // flight below 1
        [InlineData("{\"v\":1,\"c\":\"TALON\",\"f\":10,\"s\":2,\"l\":\"100\"}")]    // flight above 9
        [InlineData("{\"v\":1,\"c\":\"TALON\",\"f\":1.5,\"s\":2,\"l\":\"100\"}")]   // fractional flight
        [InlineData("{\"v\":1,\"c\":\"TALON\",\"f\":1,\"s\":100,\"l\":\"100\"}")]   // slot above the cap
        [InlineData("{\"v\":1,\"c\":\"TALON\",\"f\":1,\"s\":2,\"l\":\"0\"}")]       // no leader
        [InlineData("{\"v\":1,\"c\":\"TALON\",\"f\":1,\"s\":2,\"l\":\"abc\"}")]
        [InlineData("{\"v\":1,\"c\":\"TALON\",\"f\":1,\"s\":2}")]                   // leader missing
        [InlineData("{\"v\":1,\"c\":\"ABCDEFGHIJKLMNOPQRSTU\",\"f\":1,\"s\":2,\"l\":\"100\"}")]   // 21 chars
        public void TryParse_rejects_malformed_records(string wire)
        {
            Assert.False(TryParse(wire, Member, out _));
        }

        [Fact]
        public void TryParse_rejects_oversize_payloads()
        {
            string wire = "{\"v\":1,\"c\":\"TALON\",\"f\":1,\"s\":2,\"l\":\"100\",\"x\":\"" + new string('a', 300) + "\"}";
            Assert.False(TryParse(wire, Member, out _));
        }

        [Fact]
        public void Slot_1_must_be_the_leader_and_only_the_leader()
        {
            Assert.True(TryParse(Serialize(Squad(slot: 1, leader: Leader)), Leader, out _));
            Assert.False(TryParse(Serialize(Squad(slot: 1, leader: Leader)), Member, out _));   // claims to lead as someone else
            Assert.False(TryParse(Serialize(Squad(slot: 2, leader: Member)), Member, out _));   // leads itself at slot 2
        }

        [Fact]
        public void Fuel_is_clamped_and_a_non_number_is_dropped()
        {
            Assert.True(TryParse("{\"v\":1,\"fu\":7.5}", Member, out Identity high));
            Assert.Equal(1f, high.Fuel!.Value);
            Assert.True(TryParse("{\"v\":1,\"fu\":\"lots\"}", Member, out Identity junk));
            Assert.Null(junk.Fuel);
        }

        [Fact]
        public void Table_keeps_a_designation_through_a_missed_beat_but_not_past_the_ttl()
        {
            var t = new Table();
            t.Note(Member, Squad(), 0f);
            Assert.Equal("TALON 1-2", t.Designations(60f, InFaction)[Member]);   // many beats missed, still held
            Assert.Empty(t.Designations(DesignationTtlSeconds + 1f, InFaction));
        }

        [Fact]
        public void Table_drops_a_pilot_who_left_the_faction()
        {
            var t = new Table();
            t.Note(Member, Squad(), 0f);
            Assert.Empty(t.Designations(1f, id => false));
        }

        [Fact]
        public void A_record_without_a_callsign_clears_the_designation_at_once()
        {
            var t = new Table();
            t.Note(Member, Squad(), 0f);
            t.Note(Member, new Identity("", 0, 0, 0, null), 1f);
            Assert.Empty(t.Designations(1f, InFaction));
        }

        [Fact]
        public void Fuel_goes_stale_much_faster_than_a_designation()
        {
            var t = new Table();
            t.Note(Member, Squad(fuel: 0.4f), 0f);
            Assert.Equal(0.4f, t.FuelFor(Member, 30f)!.Value, 3);
            Assert.Null(t.FuelFor(Member, FuelTtlSeconds + 1f));
            Assert.Null(t.FuelFor(Other, 0f));
        }

        [Fact]
        public void Merge_prefers_the_own_squad_roster()
        {
            var own = new Dictionary<ulong, string> { [Member] = "TALON 1-2" };
            var faction = new Dictionary<ulong, string> { [Member] = "STALE 9-9", [Other] = "VIPER 2-1" };
            var merged = Merge(own, faction);
            Assert.Equal("TALON 1-2", merged[Member]);
            Assert.Equal("VIPER 2-1", merged[Other]);
        }

        [Fact]
        public void Squads_groups_by_leader_not_by_callsign_and_skips_the_own_squad()
        {
            var t = new Table();
            t.Note(100, Squad(slot: 1, leader: 100), 0f);                       // TALON 1, led by 100
            t.Note(101, Squad(slot: 3, leader: 100), 0f);
            t.Note(102, Squad(slot: 2, leader: 100), 0f);
            t.Note(200, Squad(slot: 1, leader: 200), 0f);                       // also TALON 1, led by 200
            t.Note(300, Squad("VIPER", 2, 1, 300), 0f);

            var squads = t.Squads(1f, InFaction, excludeLeader: 300);           // viewer is in VIPER 2
            Assert.Equal(2, squads.Count);
            Assert.Equal(100UL, squads[0].LeaderId);                            // equal callsign and flight: by leader id
            Assert.Equal(new[] { 100UL, 102UL, 101UL }, squads[0].Members.ConvertAll(m => m.Id));   // by slot
            Assert.Equal(200UL, squads[1].LeaderId);
        }

        [Fact]
        public void Squads_takes_callsign_and_flight_from_the_leader_during_a_rename()
        {
            var t = new Table();
            t.Note(101, Squad("OLD", 1, 2, 100), 0f);                           // member hasn't caught up
            t.Note(100, Squad("NEW", 3, 1, 100), 0f);
            var sq = t.Squads(1f, InFaction, 0)[0];
            Assert.Equal("NEW", sq.Callsign);
            Assert.Equal(3, sq.Flight);
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
