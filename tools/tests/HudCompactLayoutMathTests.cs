namespace NOXMFD.Tests
{
    public class HudCompactLayoutMathTests
    {
        [Fact]
        public void Half_the_fov_maps_to_half_the_canvas_height()
        {
            Assert.Equal(540f, HudCompactLayoutMath.DegToUnits(45f, 1080f, 90f), 2);
            Assert.Equal(-540f, HudCompactLayoutMath.DegToUnits(-30f, 1080f, 60f), 2);
        }

        [Fact]
        public void A_narrower_fov_spreads_the_same_angle_over_more_units()
        {
            float wide = HudCompactLayoutMath.DegToUnits(16f, 1080f, 90f);
            float narrow = HudCompactLayoutMath.DegToUnits(16f, 1080f, 60f);
            Assert.True(narrow > wide);
        }

        [Fact]
        public void Every_preset_block_moves_its_own_reference()
        {
            foreach (HudCompactLayoutMath.Block b in HudCompactLayoutMath.F16Preset)
                Assert.True(System.Array.Exists(b.Members, m => b.Reference == m || b.Reference.StartsWith(m + "/")),
                    $"{b.Reference} is not inside any member of its block");
        }

        [Fact]
        public void True_ladder_scale_matches_the_games_at_60_and_shrinks_it_at_90()
        {
            // The game's ladder: a 450-unit window onto 0.126 of the texture, scaled 50/fov.
            Assert.InRange(HudCompactLayoutMath.TrueLadderScale(0.126f, 450f, 1080f, 60f) / (50f / 60f), 0.97f, 1.0f);
            Assert.InRange(HudCompactLayoutMath.TrueLadderScale(0.126f, 450f, 1080f, 90f) / (50f / 90f), 0.84f, 0.88f);
        }

        [Fact]
        public void Every_preset_group_has_sliders()
        {
            foreach (HudCompactLayoutMath.Block b in HudCompactLayoutMath.F16Preset)
                Assert.True(b.Group == "" || System.Array.IndexOf(HudCompactLayoutMath.Groups, b.Group) >= 0, b.Group);
        }
    }
}
