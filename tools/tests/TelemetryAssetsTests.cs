namespace NOXMFD.Tests
{
    public class TelemetryAssetsTests
    {
        [Theory]
        [InlineData("gzip", true)]
        [InlineData("gzip, deflate, br", true)]
        [InlineData("deflate, gzip", true)]
        [InlineData("GZIP", true)]                 // header values are case-insensitive
        [InlineData("deflate, br", false)]
        [InlineData("", false)]
        [InlineData(null, false)]
        // Full-token semantics, not a substring search:
        [InlineData("gzip;q=1", true)]
        [InlineData("gzip;q=1.0", true)]
        [InlineData("gzip;q=0", false)]              // explicitly refused
        [InlineData("gzip;q=0.0", false)]
        [InlineData("gzip; q=0", false)]              // whitespace before the parameter
        [InlineData("br, gzip;q=0.5", true)]
        [InlineData("GZIP;Q=0", false)]               // casing on both the token and the parameter
        [InlineData("x-gzip-experimental", false)]    // a coding that merely contains "gzip"
        [InlineData("gzip-experimental, deflate", false)]
        public void AcceptsGzip_parses_tokens_and_respects_qvalue_zero(string? acceptEncoding, bool expected)
        {
            Assert.Equal(expected, TelemetryAssets.AcceptsGzip(acceptEncoding));
        }

        [Theory]
        [InlineData("text/html; charset=utf-8", true)]
        [InlineData("text/css; charset=utf-8", true)]
        [InlineData("text/javascript; charset=utf-8", true)]
        [InlineData("application/json; charset=utf-8", true)]
        [InlineData("image/svg+xml", true)]
        [InlineData("text/plain; charset=utf-8", true)]
        [InlineData("image/png", false)]
        [InlineData("image/jpeg", false)]
        [InlineData("font/woff2", false)]
        [InlineData("font/woff", false)]
        [InlineData("application/octet-stream", false)]
        public void IsCompressibleContentType_excludes_already_compressed_binary_types(string contentType, bool expected)
        {
            Assert.Equal(expected, TelemetryAssets.IsCompressibleContentType(contentType));
        }

        [Theory]
        [InlineData("pages/td/td.html", "text/html; charset=utf-8")]
        [InlineData("shell/classic/mfd.js", "text/javascript; charset=utf-8")]
        [InlineData("shared/font.css", "text/css; charset=utf-8")]
        [InlineData("shared/icon.svg", "image/svg+xml")]
        [InlineData("shared/logo.png", "image/png")]
        [InlineData("shared/font.woff2", "font/woff2")]
        public void ContentTypeFor_matches_extension(string path, string expected)
        {
            Assert.Equal(expected, TelemetryAssets.ContentTypeFor(path));
        }
    }
}
