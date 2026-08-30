using NOXMFD;

namespace NOXMFD.Tests
{
    public class HudDirectionCueMathTests
    {
        [Fact]
        public void Visible_point_keeps_projected_position()
        {
            Assert.True(Place(1200f, 700f, behind: false, out var p));
            Assert.True(p.OnScreen);
            Near(1200f, p.X);
            Near(700f, p.Y);
        }

        [Theory]
        [InlineData(2500f, 540f, 1870f, 540f, 0f)]
        [InlineData(-500f, 540f, 50f, 540f, 180f)]
        [InlineData(960f, 1500f, 960f, 1030f, 90f)]
        [InlineData(960f, -500f, 960f, 50f, -90f)]
        public void Offscreen_point_clamps_to_inset_edge(float x, float y,
            float expectedX, float expectedY, float expectedAngle)
        {
            Assert.True(Place(x, y, behind: false, out var p));
            Assert.False(p.OnScreen);
            Near(expectedX, p.X);
            Near(expectedY, p.Y);
            Near(expectedAngle, p.AngleDeg);
        }

        [Fact]
        public void Corner_uses_first_intersected_safe_edge()
        {
            Assert.True(Place(2500f, 1500f, behind: false, out var p));
            Assert.False(p.OnScreen);
            Near(1746.04f, p.X);
            Near(1030f, p.Y);
        }

        [Fact]
        public void Behind_camera_flips_projected_direction()
        {
            Assert.True(Place(1400f, 540f, behind: true, out var p));
            Assert.False(p.OnScreen);
            Near(50f, p.X);
            Near(540f, p.Y);
            Near(-180f, p.AngleDeg);
        }

        [Fact]
        public void Exact_rear_uses_bottom_fallback_without_history()
        {
            Assert.True(HudDirectionCueMath.TryPlace(960f, 540f, true,
                1920f, 1080f, 50f, 0f, 0f, out var p));
            Assert.False(p.OnScreen);
            Near(960f, p.X);
            Near(50f, p.Y);
            Near(-90f, p.AngleDeg);
        }

        [Fact]
        public void Exact_rear_retains_previous_stable_direction()
        {
            Assert.True(HudDirectionCueMath.TryPlace(960f, 540f, true,
                1920f, 1080f, 50f, 1f, 1f, out var p));
            Assert.False(p.OnScreen);
            Near(1450f, p.X);
            Near(1030f, p.Y);
            Near(45f, p.AngleDeg);
        }

        [Theory]
        [InlineData(0f, 1080f, 50f)]
        [InlineData(1920f, 0f, 50f)]
        [InlineData(100f, 100f, 50f)]
        [InlineData(1920f, 1080f, -1f)]
        public void Invalid_screen_or_inset_is_rejected(float width, float height, float inset)
        {
            Assert.False(HudDirectionCueMath.TryPlace(0f, 0f, false,
                width, height, inset, 0f, 0f, out _));
        }

        [Fact]
        public void Non_finite_projection_is_rejected()
        {
            Assert.False(HudDirectionCueMath.TryPlace(float.NaN, 10f, false,
                1920f, 1080f, 50f, 0f, 0f, out _));
            Assert.False(HudDirectionCueMath.TryPlace(10f, float.PositiveInfinity, false,
                1920f, 1080f, 50f, 0f, 0f, out _));
        }

        private static bool Place(float x, float y, bool behind,
            out HudDirectionCueMath.Placement placement) =>
            HudDirectionCueMath.TryPlace(x, y, behind, 1920f, 1080f, 50f, 0f, 0f, out placement);

        private static void Near(float expected, float actual, float tolerance = 0.2f) =>
            Assert.InRange(actual, expected - tolerance, expected + tolerance);
    }
}
