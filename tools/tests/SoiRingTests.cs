using System.Collections.Generic;

namespace NOXMFD.Tests
{
    // SoiRing is pure and stateless (no static mutable state, unlike SoiFocus/TargetFocus) — every
    // test just calls a function with plain data and asserts the result, no seeding/reset dance.
    // This is the ACTUAL production logic SoiFocus.cs's RingLocked/Cycle/SetPaneCount delegate to
    // (an external review of issue #58 flagged that tools/soi-focus.test.js only exercised a
    // hand-copied JS mirror of it, which is exactly how the shrink-plus-exclusion bug below slipped
    // through once already).
    public class SoiRingTests
    {
        private static readonly HashSet<(string, int)> NoExclusions = new HashSet<(string, int)>();

        [Fact]
        public void Build_flattens_every_instance_surface_in_order()
        {
            var instances = new List<(string cid, int paneCount)> { ("a", 2), ("b", 1) };
            var ring = SoiRing.Build(instances, NoExclusions);
            Assert.Equal(new (string, int)[] { ("a", 0), ("a", 1), ("b", 0) }, ring);
        }

        [Fact]
        public void Build_dedupes_a_twin_connection_sharing_the_same_cid()
        {
            var instances = new List<(string cid, int paneCount)> { ("a", 1), ("a", 1), ("b", 1) };
            var ring = SoiRing.Build(instances, NoExclusions);
            Assert.Equal(new (string, int)[] { ("a", 0), ("b", 0) }, ring);
        }

        [Fact]
        public void Build_skips_excluded_surfaces_but_keeps_others_on_the_same_display()
        {
            var instances = new List<(string cid, int paneCount)> { ("glass", 2) };
            var excluded = new HashSet<(string, int)> { ("glass", 1) };
            var ring = SoiRing.Build(instances, excluded);
            Assert.Equal(new (string, int)[] { ("glass", 0) }, ring);
        }

        [Fact]
        public void Build_appends_the_extra_entry_last()
        {
            var instances = new List<(string cid, int paneCount)> { ("a", 1) };
            var ring = SoiRing.Build(instances, NoExclusions, (" tgp-camera", 0));
            Assert.Equal(new (string, int)[] { ("a", 0), (" tgp-camera", 0) }, ring);
        }

        [Fact]
        public void Step_from_no_focus_takes_first_forward_and_last_backward()
        {
            var ring = new List<(string, int)> { ("a", 0), ("b", 0), ("c", 0) };
            Assert.Equal(("a", 0), SoiRing.Step(ring, "", -1, 1));
            Assert.Equal(("c", 0), SoiRing.Step(ring, "", -1, -1));
        }

        [Fact]
        public void Step_wraps_at_both_ends()
        {
            var ring = new List<(string, int)> { ("a", 0), ("b", 0), ("c", 0) };
            Assert.Equal(("a", 0), SoiRing.Step(ring, "c", 0, 1));
            Assert.Equal(("c", 0), SoiRing.Step(ring, "a", 0, -1));
        }

        [Fact]
        public void Step_on_an_empty_ring_returns_no_focus()
        {
            Assert.Equal((string.Empty, -1), SoiRing.Step(new List<(string, int)>(), "a", 0, 1));
        }

        [Fact]
        public void TryClampToIncludedPane_prefers_the_highest_surviving_included_index()
        {
            var excluded = new HashSet<(string, int)> { ("glass", 1) };
            // Shrinking to 3 panes (0,1,2 survive); 1 is excluded, so 2 should win over 0.
            Assert.Equal(("glass", 2), SoiRing.TryClampToIncludedPane("glass", 3, excluded));
        }

        [Fact]
        public void TryClampToIncludedPane_returns_null_when_every_surviving_pane_is_excluded()
        {
            var excluded = new HashSet<(string, int)> { ("glass", 0) };
            Assert.Null(SoiRing.TryClampToIncludedPane("glass", 1, excluded));
        }

        [Fact]
        public void FirstOrNone_is_none_for_an_empty_ring_and_the_first_entry_otherwise()
        {
            Assert.Equal((string.Empty, -1), SoiRing.FirstOrNone(new List<(string, int)>()));
            Assert.Equal(("a", 0), SoiRing.FirstOrNone(new List<(string, int)> { ("a", 0), ("b", 0) }));
        }

        // The exact review-flagged scenario (SoiFocus.cs's SetPaneCount, review's [P2] finding):
        // 4 portals, portal 2 (index 1) excluded, focus on portal 4 (index 3), merged down to 2
        // portals. The old code clamped straight to n-1 (index 1) — landing on the excluded pane.
        // Reproduces SetPaneCount's exact call sequence: try the same-display clamp first, only
        // fall through to the full ring if nothing survives there.
        [Fact]
        public void Shrink_clamp_never_lands_on_an_excluded_pane_on_the_same_display()
        {
            var excluded = new HashSet<(string, int)> { ("glass", 1) };
            var local = SoiRing.TryClampToIncludedPane("glass", 2, excluded);   // shrink to 2 panes: 0,1 survive
            Assert.NotNull(local);
            Assert.Equal(("glass", 0), local!.Value);   // skips excluded 1, lands on 0 — not on 1
        }

        // Same scenario, but the only surviving pane on that display is ALSO excluded — must fall
        // through to another display's included surface, never settle on the excluded one.
        [Fact]
        public void Shrink_clamp_falls_through_to_the_full_ring_when_the_whole_display_is_excluded()
        {
            var excluded = new HashSet<(string, int)> { ("glass", 0) };
            var local = SoiRing.TryClampToIncludedPane("glass", 1, excluded);   // only pane 0 survives, excluded
            Assert.Null(local);

            var instances = new List<(string cid, int paneCount)> { ("glass", 1), ("other", 1) };
            var ring = SoiRing.Build(instances, excluded);
            Assert.Equal(("other", 0), SoiRing.FirstOrNone(ring));
        }
    }
}
