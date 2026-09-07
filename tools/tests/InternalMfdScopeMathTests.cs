using NOXMFD;

namespace NOXMFD.Tests
{
    public class InternalMfdScopeMathTests
    {
        // Locks in the axis/sign pairing every scope-style page's contact placement depends on:
        // azimuth 0 (dead ahead) plots straight up (+Y, since Unity UI's +Y is up), 90 (right of the
        // nose) plots right (+X), 180 (behind) plots straight down, 270 (left of the nose) plots
        // left — clockwise-positive, matching InternalMfdUi.Azimuth's own convention.
        [Theory]
        [InlineData(0f, 0f, 100f)]
        [InlineData(90f, 100f, 0f)]
        [InlineData(180f, 0f, -100f)]
        [InlineData(270f, -100f, 0f)]
        public void PolarOffset_places_azimuth_0_forward_and_rotates_clockwise(float azimuthDeg, float expectedX, float expectedY)
        {
            (float x, float y) = InternalMfdScopeMath.PolarOffset(azimuthDeg, distFrac: 1f, radiusPx: 100f);
            Assert.Equal(expectedX, x, 3);
            Assert.Equal(expectedY, y, 3);
        }

        [Fact]
        public void PolarOffset_at_zero_distance_is_the_origin_regardless_of_azimuth()
        {
            (float x, float y) = InternalMfdScopeMath.PolarOffset(azimuthDeg: 137f, distFrac: 0f, radiusPx: 250f);
            Assert.Equal(0f, x, 3);
            Assert.Equal(0f, y, 3);
        }

        [Fact]
        public void PolarOffset_scales_linearly_with_distFrac()
        {
            (float halfX, float halfY) = InternalMfdScopeMath.PolarOffset(45f, distFrac: 0.5f, radiusPx: 200f);
            (float fullX, float fullY) = InternalMfdScopeMath.PolarOffset(45f, distFrac: 1f, radiusPx: 200f);
            Assert.Equal(fullX / 2f, halfX, 3);
            Assert.Equal(fullY / 2f, halfY, 3);
        }

        [Theory]
        [InlineData(0f, "000")]
        [InlineData(45.4f, "045")]
        [InlineData(45.5f, "046")]
        [InlineData(359.6f, "000")]   // rounds to 360, wraps to 0
        [InlineData(725f, "005")]     // two full turns past 5
        [InlineData(-10f, "350")]     // a naive `% 360` on a negative value would print "-10"
        public void Pad3Heading_wraps_and_zero_pads(float headingDeg, string expected)
        {
            Assert.Equal(expected, InternalMfdScopeMath.Pad3Heading(headingDeg));
        }
    }
}
