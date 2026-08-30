namespace NOXMFD.Tests
{
    public class HudWaypointCueMathTests
    {
        [Theory]
        [InlineData(0f, 1000f, 0f)]
        [InlineData(1000f, 0f, 90f)]
        [InlineData(0f, -1000f, 180f)]
        [InlineData(-1000f, 0f, 270f)]
        public void Bearing_uses_route_space_xz_with_zero_as_north(float wx, float wz, float expected)
        {
            Assert.Equal(expected, HudWaypointCueMath.BearingDeg(0f, 0f, wx, wz), precision: 4);
        }

        [Theory]
        [InlineData(350f, 10f, 20f)]
        [InlineData(10f, 350f, -20f)]
        [InlineData(0f, 180f, 180f)]
        [InlineData(180f, 0f, 180f)]
        public void Relative_angle_matches_unity_delta_angle_wrap(float heading, float bearing, float expected)
        {
            Assert.Equal(expected, HudWaypointCueMath.RelativeDeg(heading, bearing), precision: 4);
        }

        [Fact]
        public void Distance_is_flat_xz_kilometres()
        {
            Assert.Equal(5f, HudWaypointCueMath.DistanceKm(0f, 0f, 3000f, 4000f), precision: 4);
        }

        [Theory]
        [InlineData(0f, 0f, 0f)]
        [InlineData(45f, 100f, 0f)]
        [InlineData(-45f, -100f, 0f)]
        public void On_tape_bug_tracks_relative_bearing(float relative, float expectedX, float expectedRot)
        {
            var p = HudWaypointCueMath.PlaceBug(relative, tapeWidth: 200f, tapeHeight: 20f);

            Assert.True(p.OnTape);
            Assert.Equal(expectedX, p.X, precision: 4);
            Assert.Equal(12f, p.Y, precision: 4);
            Assert.Equal(expectedRot, p.RotationZ, precision: 4);
        }

        [Theory]
        [InlineData(46f, 100f, -90f)]
        [InlineData(170f, 100f, -90f)]
        [InlineData(-46f, -100f, 90f)]
        [InlineData(-170f, -100f, 90f)]
        public void Off_tape_bug_pins_to_edge_and_points_toward_turn(float relative, float expectedX, float expectedRot)
        {
            var p = HudWaypointCueMath.PlaceBug(relative, tapeWidth: 200f, tapeHeight: 20f);

            Assert.False(p.OnTape);
            Assert.Equal(expectedX, p.X, precision: 4);
            Assert.Equal(12f, p.Y, precision: 4);
            Assert.Equal(expectedRot, p.RotationZ, precision: 4);
        }
    }
}
