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
    }
}
