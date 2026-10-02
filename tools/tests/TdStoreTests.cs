using NOXMFD;

namespace NOXMFD.Tests
{
    // A fresh TdStoreTests instance is constructed before every [Fact] (xUnit's default) — the
    // reset here keeps TdStore's static state from leaking between tests, same shape
    // RouteStoreTests uses for RouteStore.
    public class TdStoreTests
    {
        // SteamIDs of a squad: the leader (A) and two members. Real ones are 17 digits, past what
        // JavaScript holds exactly, so the state carries them as strings.
        private const ulong A = 76561198000000001, B = 76561198000000002, C = 76561198000000003, D = 76561198000000004;
        private static readonly List<ulong> Members = new List<ulong> { A, B, C };
        private const string Empty = "{\"assignments\":{},\"sent\":{},\"designated\":[]}";

        public TdStoreTests()
        {
            TdStore.ResetForTests();
        }

        [Fact]
        public void ToggleCell_assigns_then_a_second_tap_unassigns()
        {
            Assert.True(TdStore.ToggleCell(7, B));
            Assert.Contains("\"7\":[\"76561198000000002\"]", TdStore.StateJson);
            TdStore.ToggleCell(7, B);
            Assert.Contains("\"assignments\":{}", TdStore.StateJson);
        }

        [Fact]
        public void ToggleCell_rejects_id_zero_and_member_zero()
        {
            Assert.False(TdStore.ToggleCell(0, B));
            Assert.False(TdStore.ToggleCell(7, 0));
            Assert.Equal(Empty, TdStore.StateJson);
        }

        [Fact]
        public void ToggleCell_allows_the_same_target_on_several_members()
        {
            TdStore.ToggleCell(9, B);
            TdStore.ToggleCell(9, C);
            Assert.Contains("\"9\":[\"76561198000000002\",\"76561198000000003\"]", TdStore.StateJson);
        }

        // A name tap is all-or-nothing, not a per-member flip: a partly-assigned target gains the
        // missing members rather than losing the one it had, and a full row empties.
        [Fact]
        public void ToggleRow_fills_the_gaps_then_a_second_tap_empties_the_row()
        {
            TdStore.ToggleCell(1, B);
            Assert.True(TdStore.ToggleRow(1, Members));
            Assert.Contains("\"1\":[\"76561198000000002\",\"76561198000000001\",\"76561198000000003\"]", TdStore.StateJson);

            TdStore.ToggleRow(1, Members);
            Assert.Contains("\"assignments\":{}", TdStore.StateJson);
        }

        [Fact]
        public void ToggleColumn_fills_the_gaps_then_a_second_tap_empties_the_column()
        {
            var ids = new List<uint> { 1, 2 };
            TdStore.ToggleCell(1, C);
            TdStore.ToggleCell(2, B);   // another member on the same target is left alone
            Assert.True(TdStore.ToggleColumn(C, ids));
            Assert.Contains("\"1\":[\"76561198000000003\"]", TdStore.StateJson);
            Assert.Contains("\"2\":[\"76561198000000002\",\"76561198000000003\"]", TdStore.StateJson);

            TdStore.ToggleColumn(C, ids);
            Assert.DoesNotContain("\"1\":", TdStore.StateJson);
            Assert.Contains("\"2\":[\"76561198000000002\"]", TdStore.StateJson);
        }

        [Fact]
        public void ToggleColumn_with_no_targets_is_a_no_op()
        {
            Assert.False(TdStore.ToggleColumn(B, new List<uint>()));
        }

        [Fact]
        public void MarkSent_records_each_member_sorted_and_a_resend_replaces_it()
        {
            TdStore.MarkSent(B, new uint[] { 5, 1 });
            Assert.Contains("\"sent\":{\"76561198000000002\":[1,5]}", TdStore.StateJson);
            TdStore.MarkSent(B, new uint[0]);
            Assert.Contains("\"sent\":{\"76561198000000002\":[]}", TdStore.StateJson);
        }

        [Fact]
        public void ClearMember_drops_only_the_departed_member()
        {
            TdStore.ToggleCell(1, B);
            TdStore.ToggleCell(2, C);   // only the departed member — dropped entirely
            TdStore.ToggleCell(3, D);
            TdStore.ToggleCell(4, C);
            TdStore.ToggleCell(4, D);
            TdStore.MarkSent(C, new uint[] { 2 });

            TdStore.ClearMember(C);

            Assert.Contains("\"1\":[\"76561198000000002\"]", TdStore.StateJson);
            Assert.DoesNotContain("\"2\":", TdStore.StateJson);
            Assert.Contains("\"3\":[\"76561198000000004\"]", TdStore.StateJson);
            Assert.Contains("\"4\":[\"76561198000000004\"]", TdStore.StateJson);
            Assert.Contains("\"sent\":{}", TdStore.StateJson);
        }

        [Fact]
        public void An_assignment_does_not_follow_a_pilot_who_left_and_rejoined()
        {
            TdStore.ToggleCell(1, C);
            TdStore.ClearMember(C);
            Assert.Equal(Empty, TdStore.StateJson);
        }

        [Fact]
        public void ClearMember_is_a_safe_no_op_with_nothing_to_clear()
        {
            TdStore.ClearMember(0);   // invalid
            TdStore.ClearMember(C);   // no assignments at all yet
            Assert.Equal(Empty, TdStore.StateJson);
        }

        // CLEAR discards the matrix but not what members already have, so those members read CHANGED.
        [Fact]
        public void ClearOwn_wipes_assignments_but_keeps_what_was_sent()
        {
            TdStore.ToggleCell(1, B);
            TdStore.MarkSent(B, new uint[] { 1 });

            Assert.True(TdStore.ClearOwn());
            Assert.Contains("\"assignments\":{}", TdStore.StateJson);
            Assert.Contains("\"sent\":{\"76561198000000002\":[1]}", TdStore.StateJson);
        }

        [Fact]
        public void ReceiveDesignation_replaces_rather_than_merges()
        {
            TdStore.ReceiveDesignation("[{\"id\":1,\"n\":\"A\",\"g\":\"G1\",\"r\":1.0,\"f\":2,\"dl\":false}]");
            TdStore.ReceiveDesignation("[{\"id\":2,\"n\":\"B\",\"g\":\"G2\",\"r\":2.0,\"f\":1,\"dl\":true}]");

            Assert.Single(TdStore.Designated);
            Assert.Equal(2u, TdStore.Designated[0].Id);
            Assert.DoesNotContain("\"id\":1,", TdStore.StateJson);
        }

        [Fact]
        public void ReceiveDesignation_rejects_malformed_json()
        {
            Assert.False(TdStore.ReceiveDesignation("not json"));
            Assert.False(TdStore.ReceiveDesignation(null));
        }

        [Fact]
        public void ClearDesignated_dismisses_without_accepting()
        {
            TdStore.ReceiveDesignation("[{\"id\":1,\"n\":\"A\",\"g\":\"G1\",\"r\":1.0,\"f\":2,\"dl\":false}]");
            Assert.True(TdStore.ClearDesignated());
            Assert.Empty(TdStore.Designated);
        }

        [Fact]
        public void AcceptDesignated_closes_the_designation()
        {
            TdStore.ReceiveDesignation("[{\"id\":4,\"n\":\"A\",\"g\":\"G1\",\"r\":1.0,\"f\":2,\"dl\":false}]");
            Assert.True(TdStore.AcceptDesignated());
            Assert.Empty(TdStore.Designated);
            Assert.False(TdStore.AcceptDesignated());   // nothing pending any more
        }

        [Fact]
        public void OnSquadEnded_clears_everything()
        {
            TdStore.ToggleCell(1, B);
            TdStore.MarkSent(B, new uint[] { 1 });
            TdStore.ReceiveDesignation("[{\"id\":8,\"n\":\"Y\",\"g\":\"G\",\"r\":1.0,\"f\":0,\"dl\":false}]");

            TdStore.OnSquadEnded();

            Assert.Equal(Empty, TdStore.StateJson);
            Assert.Empty(TdStore.Designated);
        }
    }
}
