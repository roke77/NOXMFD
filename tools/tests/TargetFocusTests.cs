using System.Collections.Generic;

namespace NOXMFD.Tests
{
    // TargetFocus is internal static, process-wide mutable state (like SoiFocus) — every test starts
    // by driving it to a known state via Reconcile/Cycle rather than resetting a field directly, so
    // tests stay independent of xunit's run order without needing a [Collection]/lock dance.
    public class TargetFocusTests
    {
        [Fact]
        public void Reconcile_with_no_locks_clears_focus()
        {
            TargetFocus.Cycle(1, new List<uint> { 5, 6 });   // seed a nonzero focus first
            TargetFocus.Reconcile(new List<uint>());
            Assert.Equal(0u, TargetFocus.Id);
        }

        [Fact]
        public void Reconcile_with_exactly_one_lock_always_focuses_it()
        {
            TargetFocus.Reconcile(new List<uint> { 42 });
            Assert.Equal(42u, TargetFocus.Id);

            // Even if focus was pointing somewhere else (a stale id from a prior, larger lock set).
            TargetFocus.Cycle(1, new List<uint> { 42, 99 });
            TargetFocus.Reconcile(new List<uint> { 7 });
            Assert.Equal(7u, TargetFocus.Id);
        }

        // Multiple locks can appear before the pilot ever presses Next/Previous; focus should still
        // seed from the game's own first target so dependent readouts have a lock to describe.
        [Fact]
        public void Reconcile_with_multiple_locks_and_no_prior_focus_defaults_to_the_first()
        {
            TargetFocus.Reconcile(new List<uint>());   // start from definitely-unfocused
            Assert.Equal(0u, TargetFocus.Id);

            TargetFocus.Reconcile(new List<uint> { 123, 125 });   // two locks appear together
            Assert.Equal(123u, TargetFocus.Id);
        }

        [Fact]
        public void Reconcile_drops_focus_when_the_focused_lock_is_lost_but_others_remain()
        {
            TargetFocus.Cycle(1, new List<uint> { 10, 20, 30 });   // focuses 10 (nothing focused yet)
            Assert.Equal(10u, TargetFocus.Id);

            TargetFocus.Reconcile(new List<uint> { 20, 30 });      // 10 lost, 20/30 remain
            Assert.Equal(0u, TargetFocus.Id);
        }

        [Fact]
        public void Reconcile_leaves_focus_alone_when_it_is_still_locked()
        {
            TargetFocus.Cycle(1, new List<uint> { 10, 20, 30 });
            uint before = TargetFocus.Id;

            TargetFocus.Reconcile(new List<uint> { 10, 20, 30 });
            Assert.Equal(before, TargetFocus.Id);
        }

        [Fact]
        public void Cycle_from_no_focus_takes_the_first_forward_and_last_backward()
        {
            TargetFocus.Reconcile(new List<uint>());   // clear
            TargetFocus.Cycle(1, new List<uint> { 100, 200, 300 });
            Assert.Equal(100u, TargetFocus.Id);

            TargetFocus.Reconcile(new List<uint>());   // clear again
            TargetFocus.Cycle(-1, new List<uint> { 100, 200, 300 });
            Assert.Equal(300u, TargetFocus.Id);
        }

        [Fact]
        public void Cycle_wraps_at_both_ends()
        {
            var ids = new List<uint> { 1, 2, 3 };
            TargetFocus.Reconcile(new List<uint>());
            TargetFocus.Cycle(-1, ids);   // -> 3 (wrap backward from none)
            Assert.Equal(3u, TargetFocus.Id);

            TargetFocus.Cycle(1, ids);    // 3 -> wrap forward -> 1
            Assert.Equal(1u, TargetFocus.Id);

            TargetFocus.Cycle(-1, ids);   // 1 -> wrap backward -> 3
            Assert.Equal(3u, TargetFocus.Id);
        }

        // CommandDispatcher.TargetDeselect's fix (issue: Cursor Select on the focused TGT lock used
        // to jump focus to lockedIds[0] instead of the next target): when the id being removed is
        // currently focused, it calls Cycle(1, oldIds) — the list still including the doomed id —
        // BEFORE removing it, then the next contact scan's Reconcile(newIds) sees the stepped-to id
        // is still locked and leaves it alone. These two tests pin that sequence down at the
        // TargetFocus level, independent of CommandDispatcher's Unity-only surroundings.
        [Fact]
        public void Deselecting_the_focused_lock_advances_to_the_next_one_instead_of_the_first()
        {
            var before = new List<uint> { 10, 20, 30 };
            TargetFocus.Cycle(1, before);   // focuses 10
            TargetFocus.Cycle(1, before);   // 10 -> 20 (the one about to be deselected)
            Assert.Equal(20u, TargetFocus.Id);

            TargetFocus.Cycle(1, before);            // step off 20 while it's still in the list
            TargetFocus.Reconcile(new List<uint> { 10, 30 });   // 20 now actually removed
            Assert.Equal(30u, TargetFocus.Id);        // next after 20, not lockedIds[0] (10)
        }

        [Fact]
        public void Deselecting_the_last_focused_lock_wraps_to_the_first()
        {
            var before = new List<uint> { 10, 20, 30 };
            TargetFocus.Cycle(-1, before);   // focuses 30 (last, wrapping backward from none)
            Assert.Equal(30u, TargetFocus.Id);

            TargetFocus.Cycle(1, before);            // step off 30 -> wraps to 10
            TargetFocus.Reconcile(new List<uint> { 10, 20 });   // 30 now actually removed
            Assert.Equal(10u, TargetFocus.Id);
        }

        [Fact]
        public void Cycle_steps_forward_and_backward_through_the_middle()
        {
            var ids = new List<uint> { 1, 2, 3 };
            TargetFocus.Cycle(1, ids);
            TargetFocus.Reconcile(ids);
            uint start = TargetFocus.Id;

            TargetFocus.Cycle(1, ids);
            int startIdx = ids.IndexOf(start);
            Assert.Equal(ids[(startIdx + 1) % 3], TargetFocus.Id);

            TargetFocus.Cycle(-1, ids);
            Assert.Equal(start, TargetFocus.Id);
        }
    }
}
