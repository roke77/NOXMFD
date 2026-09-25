using System.Collections.Generic;

namespace NOXMFD.Tests
{
    // TargetSort is process-wide static state (like TargetFocus) — every test sets the key it needs
    // first. Shares TargetFocusTests' collection so Cycle's use of the last sorted order can't race.
    [Collection("TargetFocus")]
    public class TargetSortTests : System.IDisposable
    {
        public void Dispose() => TargetSort.Set("", 1);   // leave lock order for TargetFocusTests

        private static readonly Dictionary<uint, TargetSort.Row> Rows = new Dictionary<uint, TargetSort.Row>
        {
            [1] = new TargetSort.Row { Name = "Tor",    Src = "SENSOR",   Range = 5000 },
            [2] = new TargetSort.Row { Name = "abrams", Src = "STALE",    Range = 1000 },
            [3] = new TargetSort.Row { Name = "Buk",    Src = "DATALINK", Range = float.NaN },
            [4] = new TargetSort.Row { Name = "Tor",    Src = "SENSOR",   Range = 3000 },
            // 5: locked but no disclosed contact row
        };
        private static readonly uint[] Locked = { 1, 2, 3, 4, 5 };

        private static uint[] Sorted(string key, int dir)
        {
            Assert.True(TargetSort.Set(key, dir));
            return TargetSort.Sort(Locked, id => Rows.TryGetValue(id, out var r) ? r : (TargetSort.Row?)null);
        }

        [Fact]
        public void No_key_keeps_lock_order()
            => Assert.Equal(Locked, Sorted("", 1));

        [Fact]
        public void Name_is_case_insensitive_reverses_and_ties_keep_lock_order()
        {
            Assert.Equal(new uint[] { 2, 3, 1, 4, 5 }, Sorted("n", 1));
            Assert.Equal(new uint[] { 1, 4, 3, 2, 5 }, Sorted("n", -1));   // Tor/Tor tie stays 1,4
        }

        [Fact]
        public void Src_is_alphabetical()
            => Assert.Equal(new uint[] { 3, 1, 4, 2, 5 }, Sorted("src", 1));

        [Fact]
        public void Unknown_range_sorts_last_in_both_directions()
        {
            Assert.Equal(new uint[] { 2, 4, 1, 3, 5 }, Sorted("r", 1));
            Assert.Equal(new uint[] { 1, 4, 2, 3, 5 }, Sorted("r", -1));
        }

        [Fact]
        public void Unknown_key_is_rejected()
            => Assert.False(TargetSort.Set("grid", 1));

        // Next/Previous steps in the visible (sorted) order, not the game's lock order.
        [Fact]
        public void Cycle_steps_in_sorted_order()
        {
            Sorted("r", 1);                                   // 2, 4, 1, 3, 5
            TargetFocus.Reconcile(new List<uint>());          // start unfocused
            TargetFocus.Cycle(1, Locked);
            Assert.Equal(2u, TargetFocus.Id);
            TargetFocus.Cycle(1, Locked);
            Assert.Equal(4u, TargetFocus.Id);
            TargetFocus.Cycle(-1, Locked);
            Assert.Equal(2u, TargetFocus.Id);
            TargetFocus.Cycle(-1, Locked);                    // wraps to the last row
            Assert.Equal(5u, TargetFocus.Id);
        }
    }
}
