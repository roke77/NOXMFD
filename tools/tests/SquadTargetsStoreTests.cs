using System.Collections.Generic;
using NOXMFD;

namespace NOXMFD.Tests
{
    // A fresh SquadTargetsStoreTests instance is constructed before every [Fact] (xUnit's default) —
    // the reset here keeps the static aggregation state from leaking between tests, same shape
    // TdStoreTests/RouteStoreTests use for their own stores.
    public class SquadTargetsStoreTests
    {
        public SquadTargetsStoreTests()
        {
            SquadTargetsStore.ResetForTests();
        }

        [Fact]
        public void SetSelfIds_reports_change_only_when_the_set_actually_differs()
        {
            Assert.True(SquadTargetsStore.SetSelfIds(new HashSet<uint> { 1, 2 }));
            Assert.False(SquadTargetsStore.SetSelfIds(new HashSet<uint> { 2, 1 }));   // same set, different order
            Assert.True(SquadTargetsStore.SetSelfIds(new HashSet<uint> { 1 }));
        }

        [Fact]
        public void Leader_answers_from_its_own_live_state()
        {
            SquadTargetsStore.SetSelfIds(new HashSet<uint> { 42 });
            Assert.True(SquadTargetsStore.IsLeaderTargeting(42, isLeader: true));
            Assert.False(SquadTargetsStore.IsLeaderTargeting(99, isLeader: true));
        }

        [Fact]
        public void Leader_sees_any_member_targeting_a_unit_regardless_of_which_member()
        {
            SquadTargetsStore.SetMemberIds(100, new uint[] { 5 });
            SquadTargetsStore.SetMemberIds(200, new uint[] { 6 });
            Assert.True(SquadTargetsStore.IsOtherMemberTargeting(5, isLeader: true));
            Assert.True(SquadTargetsStore.IsOtherMemberTargeting(6, isLeader: true));
            Assert.False(SquadTargetsStore.IsOtherMemberTargeting(7, isLeader: true));
        }

        [Fact]
        public void BuildAggregateJson_includes_leader_ids_and_every_members_ids()
        {
            SquadTargetsStore.SetSelfIds(new HashSet<uint> { 1 });
            SquadTargetsStore.SetMemberIds(100, new uint[] { 2, 3 });
            string json = SquadTargetsStore.BuildAggregateJson();
            Assert.Contains("\"leader\":[1]", json);
            Assert.Contains("\"100\":[2,3]", json);
        }

        [Fact]
        public void RemoveMember_drops_them_from_the_aggregate()
        {
            SquadTargetsStore.SetMemberIds(100, new uint[] { 2 });
            SquadTargetsStore.RemoveMember(100);
            Assert.False(SquadTargetsStore.IsOtherMemberTargeting(2, isLeader: true));
            Assert.DoesNotContain("\"100\":", SquadTargetsStore.BuildAggregateJson());
        }

        [Fact]
        public void Member_applies_the_relayed_aggregate_and_excludes_its_own_entry()
        {
            const ulong self = 555;
            string json = "{\"leader\":[1,2],\"members\":{\"555\":[9],\"777\":[3]}}";
            Assert.True(SquadTargetsStore.ApplyAggregate(json, self));

            Assert.True(SquadTargetsStore.IsLeaderTargeting(1, isLeader: false));
            Assert.True(SquadTargetsStore.IsLeaderTargeting(2, isLeader: false));
            Assert.False(SquadTargetsStore.IsLeaderTargeting(3, isLeader: false));

            // 777's id (3) shows as "another member"; this pilot's own relayed entry (555 -> [9])
            // must NOT — a member never sees its own targets flagged as "someone else's".
            Assert.True(SquadTargetsStore.IsOtherMemberTargeting(3, isLeader: false));
            Assert.False(SquadTargetsStore.IsOtherMemberTargeting(9, isLeader: false));
        }

        [Fact]
        public void ApplyAggregate_rejects_malformed_json()
        {
            Assert.False(SquadTargetsStore.ApplyAggregate("not json", 1));
            Assert.False(SquadTargetsStore.ApplyAggregate(null, 1));
        }

        [Fact]
        public void OnSquadEnded_clears_everything()
        {
            SquadTargetsStore.SetSelfIds(new HashSet<uint> { 1 });
            SquadTargetsStore.SetMemberIds(100, new uint[] { 2 });
            SquadTargetsStore.ApplyAggregate("{\"leader\":[1],\"members\":{}}", 555);

            SquadTargetsStore.OnSquadEnded();

            Assert.False(SquadTargetsStore.IsLeaderTargeting(1, isLeader: true));
            Assert.False(SquadTargetsStore.IsLeaderTargeting(1, isLeader: false));
            Assert.False(SquadTargetsStore.IsOtherMemberTargeting(2, isLeader: true));
            Assert.DoesNotContain("\"100\":", SquadTargetsStore.BuildAggregateJson());
        }
    }
}
