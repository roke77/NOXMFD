using NOXMFD;

namespace NOXMFD.Tests
{
    public class PilotCallsignTests
    {
        [Theory]
        [InlineData("TALON", "TALON")]
        [InlineData("  ARMY AIR ", "ARMY AIR")]
        [InlineData("ABCDEFGHIJKLMNOPQRST", "ABCDEFGHIJKLMNOPQRST")]   // exactly 20
        public void CleanName_trims_and_accepts_a_plain_name(string input, string expected)
        {
            Assert.Equal(expected, PilotCallsign.CleanName(input));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("ABCDEFGHIJKLMNOPQRSTU")]   // 21
        [InlineData("TAL\u0007ON")]
        [InlineData("TAL\nON")]
        [InlineData("TAL\u007fON")]
        public void CleanName_rejects_empty_long_and_control_characters(string? input)
        {
            Assert.Null(PilotCallsign.CleanName(input));
        }

        [Theory]
        [InlineData("TALON", 1, 1, true)]
        [InlineData("TALON", 9, 9, true)]
        [InlineData("TALON", 0, 1, false)]
        [InlineData("TALON", 1, 10, false)]
        [InlineData("TALON", -1, 5, false)]
        [InlineData("", 1, 1, false)]
        public void TryCreate_needs_a_name_and_both_numbers_in_1_to_9(string name, int flight, int number, bool ok)
        {
            Assert.Equal(ok, PilotCallsign.TryCreate(name, flight, number, out string callsign));
            Assert.Equal(ok ? name : "", callsign);
        }
    }
}
