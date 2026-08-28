namespace NOXMFD.Tests
{
    public class HudTtiMathTests
    {
        [Fact]
        public void Head_on_closure_is_range_over_combined_speed()
        {
            // Weapon at origin doing 300 m/s toward the target (+X); target 3000m away doing
            // 100 m/s back toward the weapon (-X). Relative velocity (weapon minus target) is
            // 400 m/s along the closing line -> 3000 / 400 = 7.5s to impact.
            float tti = HudTtiMath.TimeToImpact(0, 0, 0, 3000, 0, 0, 400, 0, 0);
            Assert.Equal(7.5f, tti, 3);
        }

        [Fact]
        public void Stationary_target_uses_the_weapons_own_closing_component()
        {
            // Target sits 1000m along +Z, motionless. Weapon does 250 m/s straight at it.
            float tti = HudTtiMath.TimeToImpact(0, 0, 0, 0, 0, 1000, 0, 0, 250);
            Assert.Equal(4f, tti, 3);
        }

        [Fact]
        public void Sideways_motion_does_not_count_toward_closing_speed()
        {
            // Weapon closes at 200 m/s along the line to the target; its sideways component (Y)
            // shouldn't speed up or slow down the estimate.
            float tti = HudTtiMath.TimeToImpact(0, 0, 0, 2000, 0, 0, 200, 500, 0);
            Assert.Equal(10f, tti, 3);
        }

        [Fact]
        public void Non_closing_geometry_floors_at_the_minimum_closing_speed()
        {
            // Target is outrunning the weapon (negative closing speed) — flooring at 1 m/s keeps
            // this a very large but finite number instead of blowing up or going negative.
            float tti = HudTtiMath.TimeToImpact(0, 0, 0, 1000, 0, 0, -50, 0, 0);
            Assert.Equal(1000f, tti, 3);
        }

        [Fact]
        public void Coincident_points_have_nothing_to_divide_by()
        {
            Assert.Equal(-1f, HudTtiMath.TimeToImpact(5, 5, 5, 5, 5, 5, 10, 0, 0));
        }

        [Theory]
        [InlineData(0f, "0:00")]
        [InlineData(7.4f, "0:07")]
        [InlineData(7.6f, "0:08")]
        [InlineData(59.9f, "1:00")]
        [InlineData(125f, "2:05")]
        public void FormatTti_renders_minutes_and_seconds(float seconds, string expected)
        {
            Assert.Equal(expected, HudTtiMath.FormatTti(seconds));
        }
    }
}
