using NOXMFD;

namespace NOXMFD.Tests
{
    // A fresh TdStoreTests instance is constructed before every [Fact] (xUnit's default) — the
    // reset here keeps TdStore's static overlay from leaking between tests, same shape
    // RouteStoreTests uses for RouteStore.
    public class TdStoreTests
    {
        public TdStoreTests()
        {
            TdStore.ResetForTests();
        }

        [Fact]
        public void ToggleSelect_adds_then_removes_on_a_repeat_call()
        {
            Assert.True(TdStore.ToggleSelect(7));
            Assert.Contains("\"selected\":[7]", TdStore.StateJson);
            TdStore.ToggleSelect(7);
            Assert.Contains("\"selected\":[]", TdStore.StateJson);
        }

        [Fact]
        public void ToggleSelect_rejects_id_zero()
        {
            Assert.False(TdStore.ToggleSelect(0));
        }

        [Fact]
        public void Assign_toggles_slot_membership_and_clears_selection()
        {
            TdStore.ToggleSelect(1);
            TdStore.ToggleSelect(2);
            Assert.True(TdStore.Assign(3));
            Assert.Contains("\"selected\":[]", TdStore.StateJson);   // selection cleared after assign
            Assert.Contains("\"1\":[3]", TdStore.StateJson);
            Assert.Contains("\"2\":[3]", TdStore.StateJson);
        }

        [Fact]
        public void Assign_a_second_time_for_the_same_slot_unassigns_it()
        {
            TdStore.ToggleSelect(5);
            TdStore.Assign(2);
            TdStore.ToggleSelect(5);   // re-select the same target
            TdStore.Assign(2);         // toggle the same slot off
            Assert.DoesNotContain("\"5\":", TdStore.StateJson);
        }

        [Fact]
        public void Assign_allows_the_same_target_on_multiple_slots()
        {
            TdStore.ToggleSelect(9);
            TdStore.Assign(2);
            TdStore.ToggleSelect(9);
            TdStore.Assign(3);
            Assert.Contains("\"9\":[2,3]", TdStore.StateJson);
        }

        [Fact]
        public void Assign_with_nothing_selected_is_a_no_op()
        {
            Assert.False(TdStore.Assign(1));
        }

        [Fact]
        public void Assign_retain_true_keeps_selection_for_a_second_assign()
        {
            // issue #47 follow-up: long-pressing a squad button while assigning lets a leader
            // designate the same selection to multiple slots in a row without re-selecting.
            TdStore.ToggleSelect(1);
            TdStore.ToggleSelect(2);
            Assert.True(TdStore.Assign(3, retain: true));
            Assert.Contains("\"selected\":[1,2]", TdStore.StateJson);   // NOT cleared, unlike the default
            TdStore.Assign(4);   // retain defaults false — this one clears
            Assert.Contains("\"selected\":[]", TdStore.StateJson);
            Assert.Contains("\"1\":[3,4]", TdStore.StateJson);
            Assert.Contains("\"2\":[3,4]", TdStore.StateJson);
        }

        [Fact]
        public void RenumberAfterMemberRemoved_shifts_higher_slots_down_and_drops_the_removed_one()
        {
            // issue #47 follow-up audit's gap #3: slot = member index + 2, so kicking/losing the
            // member at slot 3 must drop slot 3 and shift every slot above it down by one.
            TdStore.ToggleSelect(1);
            TdStore.Assign(2);        // target 1 -> slot 2 (below the removed slot — untouched)
            TdStore.ToggleSelect(2);
            TdStore.Assign(3);        // target 2 -> slot 3 (the departing member — dropped)
            TdStore.ToggleSelect(3);
            TdStore.Assign(4);        // target 3 -> slot 4 (shifts down to 3)
            TdStore.ToggleSelect(4);
            TdStore.Assign(3);        // target 4 -> slot 3 (Assign clears the selection afterward...
            TdStore.ToggleSelect(4);  // ...so target 4 needs re-selecting for the second assign)
            TdStore.Assign(4);        // target 4 -> slots {3,4}; slot 4 shifts to 3, landing on the same slot twice

            TdStore.RenumberAfterMemberRemoved(3);

            Assert.Contains("\"1\":[2]", TdStore.StateJson);     // untouched, below the removed slot
            Assert.DoesNotContain("\"2\":", TdStore.StateJson);  // was ONLY slot 3 — dropped entirely
            Assert.Contains("\"3\":[3]", TdStore.StateJson);     // was slot 4 — shifted down to 3
            Assert.Contains("\"4\":[3]", TdStore.StateJson);     // was {3,4} — slot 3 dropped, slot 4 shifted to 3, de-duplicated
        }

        [Fact]
        public void RenumberAfterMemberRemoved_is_a_safe_no_op_with_nothing_to_renumber()
        {
            Assert.Equal("{\"selected\":[],\"assignments\":{},\"designated\":[]}", TdStore.StateJson);
            TdStore.RenumberAfterMemberRemoved(0);    // invalid slot
            TdStore.RenumberAfterMemberRemoved(3);    // no assignments at all yet
            Assert.Equal("{\"selected\":[],\"assignments\":{},\"designated\":[]}", TdStore.StateJson);
        }

        [Fact]
        public void ClearOwn_wipes_selection_and_assignments()
        {
            TdStore.ToggleSelect(1);
            TdStore.Assign(2);
            TdStore.ToggleSelect(3);   // still selected, not yet assigned

            Assert.True(TdStore.ClearOwn());
            Assert.Contains("\"selected\":[]", TdStore.StateJson);
            Assert.Contains("\"assignments\":{}", TdStore.StateJson);
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
        public void ClearDesignated_empties_the_member_table()
        {
            TdStore.ReceiveDesignation("[{\"id\":1,\"n\":\"A\",\"g\":\"G1\",\"r\":1.0,\"f\":2,\"dl\":false}]");
            Assert.True(TdStore.ClearDesignated());
            Assert.Empty(TdStore.Designated);
        }

        [Fact]
        public void OnSquadEnded_clears_selection_assignments_and_designated_rows()
        {
            TdStore.ToggleSelect(1);
            TdStore.Assign(2);
            TdStore.ReceiveDesignation("[{\"id\":9,\"n\":\"X\",\"g\":\"G\",\"r\":1.0,\"f\":0,\"dl\":false}]");

            TdStore.OnSquadEnded();

            Assert.Contains("\"selected\":[]", TdStore.StateJson);
            Assert.Contains("\"assignments\":{}", TdStore.StateJson);
            Assert.Empty(TdStore.Designated);
        }
    }
}
