using NOXMFD;

namespace NOXMFD.Tests
{
    public class TgpFeedSettingsTests
    {
        [Theory]
        [InlineData("native", 0, "native")]
        [InlineData("hq", 1, "mid")]
        [InlineData("mid", 1, "mid")]
        [InlineData("high", 2, "high")]
        [InlineData("unknown", 0, "native")]
        public void Resolution_names_normalize_with_legacy_hq_migrating_to_mid(
            string input, int expected, string normalized)
        {
            Assert.Equal((TgpResolution)expected, TgpFeedSettings.ParseResolution(input));
            Assert.Equal(normalized, TgpFeedSettings.NormalizeResolutionName(input));
        }

        [Theory]
        [InlineData(0, 0, 0, 360, false)]
        [InlineData(1, 720, 480, 720, true)]
        [InlineData(2, 1080, 720, 1080, true)]
        public void Resolution_tiers_resolve_to_the_planned_dimensions(
            int resolution, int width, int height, int maxDimension, bool usesMirror)
        {
            TgpCaptureSettings settings = TgpFeedSettings.Resolve((TgpResolution)resolution, TgpJpegQuality.Medium);

            Assert.Equal(width, settings.Width);
            Assert.Equal(height, settings.Height);
            Assert.Equal(maxDimension, settings.MaxDimension);
            Assert.Equal(usesMirror, settings.UsesMirror);
        }

        [Theory]
        [InlineData("low", 0, 30)]
        [InlineData("mid", 1, 50)]
        [InlineData("high", 2, 90)]
        [InlineData("unknown", 1, 50)]
        public void Jpeg_quality_names_map_to_encoder_values(
            string input, int expected, int encoderValue)
        {
            TgpJpegQuality quality = TgpFeedSettings.ParseJpegQuality(input);

            Assert.Equal((TgpJpegQuality)expected, quality);
            Assert.Equal(encoderValue, TgpFeedSettings.JpegQualityValue(quality));
        }

        [Theory]
        [InlineData(0, "native")]
        [InlineData(1, "hq")]
        [InlineData(2, "hq")]
        public void Legacy_quality_alias_only_distinguishes_native_from_mirror(
            int resolution, string expected)
        {
            Assert.Equal(expected, TgpFeedSettings.LegacyQualityName((TgpResolution)resolution));
        }
    }
}
