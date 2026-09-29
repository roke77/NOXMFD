using NOXMFD;

namespace NOXMFD.Tests
{
    // A fresh TdStoreTests instance is constructed before every [Fact] (xUnit's default) — the
    // reset here keeps TdStore's static state from leaking between tests, same shape
    // RouteStoreTests uses for RouteStore.
    public class TdStoreTests
    {
        private static readonly List<int> Slots = new List<int> { 1, 2, 3 };
        private const string Empty = "{\"assignments\":{},\"sent\":{},\"accepted\":[],\"designated\":[]}";

        public TdStoreTests()
        {
            TdStore.ResetForTests();
        }

        [Fact]
        public void ToggleCell_assigns_then_a_second_tap_unassigns()
        {
            Assert.True(TdStore.ToggleCell(7, 2));
            Assert.Contains("\"7\":[2]", TdStore.StateJson);
            TdStore.ToggleCell(7, 2);
            Assert.Contains("\"assignments\":{}", TdStore.StateJson);
        }

        [Fact]
        public void ToggleCell_rejects_id_zero_and_slot_zero()
        {
            Assert.False(TdStore.ToggleCell(0, 2));
            Assert.False(TdStore.ToggleCell(7, 0));
            Assert.Equal(Empty, TdStore.StateJson);
        }

        [Fact]
        public void ToggleCell_allows_the_same_target_on_several_slots()
        {
            TdStore.ToggleCell(9, 2);
            TdStore.ToggleCell(9, 3);
            Assert.Contains("\"9\":[2,3]", TdStore.StateJson);
        }

        // A name tap is all-or-nothing, not a per-slot flip: a partly-assigned target gains the
        // missing slots rather than losing the one it had, and a full row empties.
        [Fact]
        public void ToggleRow_fills_the_gaps_then_a_second_tap_empties_the_row()
        {
            TdStore.ToggleCell(1, 2);
            Assert.True(TdStore.ToggleRow(1, Slots));
            Assert.Contains("\"1\":[2,1,3]", TdStore.StateJson);

            TdStore.ToggleRow(1, Slots);
            Assert.Contains("\"assignments\":{}", TdStore.StateJson);
        }

        [Fact]
        public void ToggleColumn_fills_the_gaps_then_a_second_tap_empties_the_column()
        {
            var ids = new List<uint> { 1, 2 };
            TdStore.ToggleCell(1, 3);
            TdStore.ToggleCell(2, 2);   // another slot on the same target is left alone
            Assert.True(TdStore.ToggleColumn(3, ids));
            Assert.Contains("\"1\":[3]", TdStore.StateJson);
            Assert.Contains("\"2\":[2,3]", TdStore.StateJson);

            TdStore.ToggleColumn(3, ids);
            Assert.DoesNotContain("\"1\":", TdStore.StateJson);
            Assert.Contains("\"2\":[2]", TdStore.StateJson);
        }

        [Fact]
        public void ToggleColumn_with_no_targets_is_a_no_op()
        {
            Assert.False(TdStore.ToggleColumn(2, new List<uint>()));
        }

        [Fact]
        public void MarkSent_records_each_slot_sorted_and_a_resend_replaces_it()
        {
            TdStore.MarkSent(2, new uint[] { 5, 1 });
            Assert.Contains("\"sent\":{\"2\":[1,5]}", TdStore.StateJson);
            TdStore.MarkSent(2, new uint[0]);
            Assert.Contains("\"sent\":{\"2\":[]}", TdStore.StateJson);
        }

        [Fact]
        public void ClearSlot_drops_only_the_departed_slot_and_shifts_nobody()
        {
            // A departure leaves a hole: slot 3's assignments go, slots 2 and 4 keep theirs.
            TdStore.ToggleCell(1, 2);
            TdStore.ToggleCell(2, 3);   // only slot 3 — dropped entirely
            TdStore.ToggleCell(3, 4);
            TdStore.ToggleCell(4, 3);
            TdStore.ToggleCell(4, 4);
            TdStore.MarkSent(3, new uint[] { 2 });

            TdStore.ClearSlot(3);

            Assert.Contains("\"1\":[2]", TdStore.StateJson);
            Assert.DoesNotContain("\"2\":", TdStore.StateJson);
            Assert.Contains("\"3\":[4]", TdStore.StateJson);
            Assert.Contains("\"4\":[4]", TdStore.StateJson);
            Assert.Contains("\"sent\":{}", TdStore.StateJson);
        }

        [Fact]
        public void SwapSlots_moves_each_assignment_and_sent_list_with_its_member()
        {
            TdStore.ToggleCell(1, 2);
            TdStore.ToggleCell(2, 3);
            TdStore.ToggleCell(3, 4);   // not part of the swap
            TdStore.MarkSent(2, new uint[] { 1 });

            TdStore.SwapSlots(2, 3);

            Assert.Contains("\"1\":[3]", TdStore.StateJson);
            Assert.Contains("\"2\":[2]", TdStore.StateJson);
            Assert.Contains("\"3\":[4]", TdStore.StateJson);
            Assert.Contains("\"sent\":{\"3\":[1]}", TdStore.StateJson);
        }

        [Fact]
        public void ClearSlot_is_a_safe_no_op_with_nothing_to_clear()
        {
            TdStore.ClearSlot(0);    // invalid slot
            TdStore.ClearSlot(3);    // no assignments at all yet
            Assert.Equal(Empty, TdStore.StateJson);
        }

        // CLEAR discards the matrix but not what members already have, so those slots read CHANGED.
        [Fact]
        public void ClearOwn_wipes_assignments_but_keeps_what_was_sent()
        {
            TdStore.ToggleCell(1, 2);
            TdStore.MarkSent(2, new uint[] { 1 });

            Assert.True(TdStore.ClearOwn());
            Assert.Contains("\"assignments\":{}", TdStore.StateJson);
            Assert.Contains("\"sent\":{\"2\":[1]}", TdStore.StateJson);
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
            Assert.Contains("\"accepted\":[]", TdStore.StateJson);
        }

        [Fact]
        public void AcceptDesignated_closes_the_designation_and_remembers_its_ids()
        {
            TdStore.ReceiveDesignation("[{\"id\":4,\"n\":\"A\",\"g\":\"G1\",\"r\":1.0,\"f\":2,\"dl\":false}]");
            Assert.True(TdStore.AcceptDesignated());
            Assert.Empty(TdStore.Designated);
            Assert.Contains("\"accepted\":[4]", TdStore.StateJson);
            Assert.False(TdStore.AcceptDesignated());   // nothing pending any more
        }

        [Fact]
        public void OnSquadEnded_clears_everything()
        {
            TdStore.ToggleCell(1, 2);
            TdStore.MarkSent(2, new uint[] { 1 });
            TdStore.ReceiveDesignation("[{\"id\":9,\"n\":\"X\",\"g\":\"G\",\"r\":1.0,\"f\":0,\"dl\":false}]");
            TdStore.AcceptDesignated();
            TdStore.ReceiveDesignation("[{\"id\":8,\"n\":\"Y\",\"g\":\"G\",\"r\":1.0,\"f\":0,\"dl\":false}]");

            TdStore.OnSquadEnded();

            Assert.Equal(Empty, TdStore.StateJson);
            Assert.Empty(TdStore.Designated);
        }
    }
}
