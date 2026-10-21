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

        [Fact]
        public void SetFavorite_adds_in_order_up_to_the_cap_and_removes()
        {
            var favs = new List<string>();
            Assert.True(PilotCallsign.SetFavorite(favs, "VIPER", true));
            Assert.True(PilotCallsign.SetFavorite(favs, " TALON ", true));
            Assert.False(PilotCallsign.SetFavorite(favs, "viper", true));     // already in, any case
            Assert.True(PilotCallsign.SetFavorite(favs, "ARMY AIR", true));
            Assert.False(PilotCallsign.SetFavorite(favs, "COLT", true));      // full
            Assert.Equal(new[] { "VIPER", "TALON", "ARMY AIR" }, favs);

            Assert.True(PilotCallsign.SetFavorite(favs, "talon", false));
            Assert.False(PilotCallsign.SetFavorite(favs, "TALON", false));    // already out
            Assert.True(PilotCallsign.SetFavorite(favs, "COLT", true));
            Assert.Equal(new[] { "VIPER", "ARMY AIR", "COLT" }, favs);
        }

        [Theory]
        [InlineData("")]
        [InlineData("A,B")]
        [InlineData("TAL\nON")]
        public void SetFavorite_refuses_names_that_cannot_round_trip(string name)
        {
            var favs = new List<string>();
            Assert.False(PilotCallsign.SetFavorite(favs, name, true));
            Assert.Empty(favs);
        }

        [Fact]
        public void ParseFavorites_round_trips_and_tolerates_a_hand_edited_config()
        {
            Assert.Empty(PilotCallsign.ParseFavorites(null));
            Assert.Equal(new[] { "VIPER", "ARMY AIR" },
                PilotCallsign.ParseFavorites(PilotCallsign.FormatFavorites(new[] { "VIPER", "ARMY AIR" })));
            Assert.Equal(new[] { "VIPER", "TALON", "COLT" },
                PilotCallsign.ParseFavorites(" VIPER ,,viper,TALON,COLT,WOLF"));
        }
    }
}
