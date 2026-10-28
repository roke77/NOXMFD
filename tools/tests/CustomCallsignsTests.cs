using NOXMFD;

namespace NOXMFD.Tests
{
    public class CustomCallsignsTests
    {
        [Fact]
        public void Parse_splits_on_commas_upcases_dedupes_and_sorts()
        {
            var skipped = new List<string>();
            var names = CustomCallsigns.Parse("viper, Ghost ,\r\n  army   air,\nGHOST,,talon,\n", skipped);
            Assert.Equal(new[] { "ARMY AIR", "GHOST", "TALON", "VIPER" }, names);
            Assert.Empty(skipped);
        }

        [Theory]
        [InlineData("VIPER1")]
        [InlineData("F-35")]
        [InlineData("TAL'ON")]
        [InlineData("ÉCLAIR")]
        [InlineData("ABCDEFGHIJ KLMNOPQRST")]   // 21 with the space
        [InlineData("VIPER\nTALON")]            // missing comma
        public void Parse_skips_whole_values_that_break_the_rules(string value)
        {
            var skipped = new List<string>();
            Assert.Equal(new[] { "WOLF" }, CustomCallsigns.Parse(value + ",WOLF", skipped));
            Assert.Single(skipped);
        }

        [Fact]
        public void Parse_accepts_exactly_the_length_cap()
        {
            Assert.Equal(new[] { "ABCDEFGHIJ KLMNOPQRS" }, CustomCallsigns.Parse("abcdefghij klmnopqrs", new List<string>()));
        }

        [Fact]
        public void Load_is_null_for_a_missing_or_empty_file_and_the_list_otherwise()
        {
            string dir = Path.Combine(Path.GetTempPath(), "noxmfd-callsigns-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            string path = Path.Combine(dir, "callsigns.txt");
            var warnings = new List<string>();
            CustomCallsigns.LogWarning = warnings.Add;
            try
            {
                CustomCallsigns.Load(path);
                Assert.Null(CustomCallsigns.Names);
                Assert.Empty(warnings);

                File.WriteAllText(path, "123, F-35");
                CustomCallsigns.Load(path);
                Assert.Null(CustomCallsigns.Names);
                Assert.Equal(3, warnings.Count);   // two skipped values + the fallback

                File.WriteAllText(path, "storm, ghost");
                CustomCallsigns.Load(path);
                Assert.Equal(new[] { "GHOST", "STORM" }, CustomCallsigns.Names);
            }
            finally
            {
                CustomCallsigns.LogWarning = null;
                Directory.Delete(dir, true);
            }
        }
    }
}
