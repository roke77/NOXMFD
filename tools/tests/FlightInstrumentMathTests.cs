namespace NOXMFD.Tests
{
    public class FlightInstrumentMathTests
    {
        [Theory]
        [InlineData(0f, 0f)]
        [InlineData(10f, 10f)]
        [InlineData(180f, 180f)]
        [InlineData(190f, -170f)]
        [InlineData(350f, -10f)]
        [InlineData(-190f, 170f)]
        public void Wrap_signed_maps_euler_angles_into_plus_minus_180(float deg, float expected)
        {
            Assert.Equal(expected, FlightInstrumentMath.WrapSigned(deg), 3);
        }

        [Fact]
        public void Nose_up_is_positive_pitch()
        {
            // Unity reports a 10° nose-up attitude as eulerAngles.x = 350.
            Assert.Equal(10f, FlightInstrumentMath.PitchDeg(350f), 3);
            Assert.Equal(-15f, FlightInstrumentMath.PitchDeg(15f), 3);
        }

        [Fact]
        public void Right_wing_down_is_positive_roll()
        {
            // A 30° right bank is eulerAngles.z = 330 (left-wing-down positive in Unity).
            Assert.Equal(30f, FlightInstrumentMath.RollDeg(330f), 3);
            Assert.Equal(-45f, FlightInstrumentMath.RollDeg(45f), 3);
        }

        [Fact]
        public void Aoa_is_positive_when_the_air_comes_from_below_the_nose()
        {
            // Velocity points slightly below the nose (negative local Y): positive AoA, as AoADisplay.
            float aoa = FlightInstrumentMath.AoaDeg(-10f, 100f, 100f);
            Assert.Equal(5.711f, aoa, 2);
        }

        [Fact]
        public void Aoa_and_sideslip_are_unavailable_below_the_minimum_speed()
        {
            Assert.True(float.IsNaN(FlightInstrumentMath.AoaDeg(-1f, 5f, 5f)));
            Assert.True(float.IsNaN(FlightInstrumentMath.BetaDeg(1f, 5f, 5f)));
        }

        [Fact]
        public void Sideslip_is_positive_when_moving_right_of_the_nose()
        {
            Assert.Equal(45f, FlightInstrumentMath.BetaDeg(100f, 100f, 141f), 2);
            Assert.Equal(-45f, FlightInstrumentMath.BetaDeg(-100f, 100f, 141f), 2);
        }

        [Fact]
        public void Mach_guards_a_missing_speed_of_sound()
        {
            Assert.Equal(0.5f, FlightInstrumentMath.Mach(170f, 340f), 3);
            Assert.Equal(0f, FlightInstrumentMath.Mach(170f, 0f));
        }

        [Fact]
        public void Turn_rate_converts_to_degrees_per_second_right_positive()
        {
            Assert.Equal(3f, FlightInstrumentMath.TurnRateDeg((float)(3 * System.Math.PI / 180)), 3);
            Assert.Equal(-1.5f, FlightInstrumentMath.TurnRateDeg((float)(-1.5 * System.Math.PI / 180)), 3);
        }
    }
}
