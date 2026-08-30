using NOXMFD;

namespace NOXMFD.Tests
{
    public class TgpFullScreenMathTests
    {
        // Regression test for the bug this branch shipped and fixed: mechanically copying tgp.js's
        // web needle formula (bearing + 180) double-flips this needle, since it's built with the
        // opposite pivot/growth direction. Locks in the correct formula (-bearing, no +180) so a
        // future edit reaching for the web page's own convention gets caught here instead of
        // in-game.
        [Theory]
        [InlineData(0f, 0f)]
        [InlineData(90f, -90f)]
        [InlineData(-90f, 90f)]
        [InlineData(180f, -180f)]
        public void Needle_rotation_is_the_negated_bearing_not_bearing_plus_180(float bearingDeg, float expectedRotationDeg)
        {
            Assert.Equal(expectedRotationDeg, TgpFullScreenMath.NeedleRotationDegrees(bearingDeg));
        }
    }
}
