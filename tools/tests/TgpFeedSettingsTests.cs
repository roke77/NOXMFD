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

        // Every existing dimension test above only ever exercises TgpJpegQuality.Medium — this pins
        // that the two axes genuinely combine independently (JPEG value never affected by resolution
        // and vice versa), not just that each axis works in isolation.
        [Theory]
        [InlineData(0, 0, 30)] [InlineData(0, 1, 50)] [InlineData(0, 2, 90)]
        [InlineData(1, 0, 30)] [InlineData(1, 1, 50)] [InlineData(1, 2, 90)]
        [InlineData(2, 0, 30)] [InlineData(2, 1, 50)] [InlineData(2, 2, 90)]
        public void Resolution_and_jpeg_quality_combine_independently(
            int resolution, int quality, int expectedJpegValue)
        {
            var res = (TgpResolution)resolution;
            var qual = (TgpJpegQuality)quality;
            TgpCaptureSettings withQuality = TgpFeedSettings.Resolve(res, qual);
            TgpCaptureSettings baseline = TgpFeedSettings.Resolve(res, TgpJpegQuality.Medium);

            Assert.Equal(expectedJpegValue, withQuality.JpegQuality);
            Assert.Equal(baseline.Width, withQuality.Width);
            Assert.Equal(baseline.Height, withQuality.Height);
            Assert.Equal(baseline.MaxDimension, withQuality.MaxDimension);
            Assert.Equal(baseline.UsesMirror, withQuality.UsesMirror);
        }

        [Theory]
        [InlineData(1280, 720, 720, 720, 405)]     // wide source, longer side capped
        [InlineData(720, 1280, 720, 405, 720)]     // tall source, longer side capped
        [InlineData(360, 240, 720, 360, 240)]      // already under the cap: unchanged
        [InlineData(1000, 1000, 500, 500, 500)]    // square source
        [InlineData(2000, 1000, 0, 2000, 1000)]    // maxDimension <= 0 means no cap
        public void FitWithinMaxDimension_preserves_aspect_and_caps_the_longer_side(
            int width, int height, int maxDimension, int expectedWidth, int expectedHeight)
        {
            (int w, int h) = TgpFeedSettings.FitWithinMaxDimension(width, height, maxDimension);

            Assert.Equal(expectedWidth, w);
            Assert.Equal(expectedHeight, h);
        }

        [Fact]
        public void ApplyIrAutoLevels_seeds_from_the_first_frames_own_min_max()
        {
            // Two pixels: luma 50 and luma 200 (approximately, via the 0.299/0.587/0.114 weights —
            // using pure R so luma == R * 0.299, avoids floating rounding across channels).
            byte[] px = { 100, 0, 0, 255, 200, 0, 0, 255 };
            float minEma = -1f, maxEma = -1f;

            TgpFeedSettings.ApplyIrAutoLevels(px, ref minEma, ref maxEma, smoothing: 0.25f);

            Assert.True(minEma >= 0f, "first call should seed from the frame's own min, not ease in from 0");
            // The darker pixel's channels should end up darker than the brighter pixel's after the stretch.
            Assert.True(px[0] < px[4], $"expected darker input to stay darker after stretch, got {px[0]} vs {px[4]}");
        }

        [Fact]
        public void ApplyIrAutoLevels_does_not_divide_by_zero_on_a_flat_frame()
        {
            byte[] px = { 128, 128, 128, 255, 128, 128, 128, 255 };
            float minEma = -1f, maxEma = -1f;

            TgpFeedSettings.ApplyIrAutoLevels(px, ref minEma, ref maxEma, smoothing: 0.25f);

            Assert.InRange(px[0], (byte)0, (byte)255);
            Assert.InRange(px[4], (byte)0, (byte)255);
        }

        [Fact]
        public void ApplyIrAutoLevels_smooths_min_max_across_calls_instead_of_snapping()
        {
            byte[] first = { 100, 0, 0, 255 };
            float minEma = -1f, maxEma = -1f;
            TgpFeedSettings.ApplyIrAutoLevels(first, ref minEma, ref maxEma, smoothing: 0.25f);
            float minAfterFirst = minEma;

            byte[] second = { 0, 0, 0, 255 };   // a sudden, much darker single frame
            TgpFeedSettings.ApplyIrAutoLevels(second, ref minEma, ref maxEma, smoothing: 0.25f);

            // A 0.25 EMA moves 25% of the way toward the new value, not all the way to it.
            Assert.True(minEma > 0f && minEma < minAfterFirst,
                $"expected the EMA to ease toward 0 rather than snap to it, got {minEma} (was {minAfterFirst})");
        }
    }
}
