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
