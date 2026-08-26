namespace NOXMFD
{
    internal enum TgpResolution { Native, Medium, High }
    internal enum TgpJpegQuality { Low, Medium, High }

    internal readonly struct TgpCaptureSettings
    {
        internal TgpCaptureSettings(bool usesMirror, int width, int height, int maxDimension,
                                    int jpegQuality)
        {
            UsesMirror = usesMirror;
            Width = width;
            Height = height;
            MaxDimension = maxDimension;
            JpegQuality = jpegQuality;
        }

        internal bool UsesMirror { get; }
        internal int Width { get; }
        internal int Height { get; }
        internal int MaxDimension { get; }
        internal int JpegQuality { get; }
    }

    internal static class TgpFeedSettings
    {
        internal static string NormalizeResolutionName(string? name) => name switch
        {
            "hq" or "mid" => "mid",
            "high" => "high",
            _ => "native",
        };

        internal static TgpResolution ParseResolution(string? name) => NormalizeResolutionName(name) switch
        {
            "mid" => TgpResolution.Medium,
            "high" => TgpResolution.High,
            _ => TgpResolution.Native,
        };

        internal static string ResolutionName(TgpResolution resolution) => resolution switch
        {
            TgpResolution.Medium => "mid",
            TgpResolution.High => "high",
            _ => "native",
        };

        // Older clients only distinguish the baked native overlay from a mirror feed.
        internal static string LegacyQualityName(TgpResolution resolution) =>
            resolution == TgpResolution.Native ? "native" : "hq";

        internal static string NormalizeJpegQualityName(string? name) => name switch
        {
            "low" => "low",
            "high" => "high",
            _ => "mid",
        };

        internal static TgpJpegQuality ParseJpegQuality(string? name) => NormalizeJpegQualityName(name) switch
        {
            "low" => TgpJpegQuality.Low,
            "high" => TgpJpegQuality.High,
            _ => TgpJpegQuality.Medium,
        };

        internal static string JpegQualityName(TgpJpegQuality quality) => quality switch
        {
            TgpJpegQuality.Low => "low",
            TgpJpegQuality.High => "high",
            _ => "mid",
        };

        internal static int JpegQualityValue(TgpJpegQuality quality) => quality switch
        {
            TgpJpegQuality.Low => 30,
            TgpJpegQuality.High => 75,
            _ => 50,
        };

        internal static TgpCaptureSettings Resolve(TgpResolution resolution, TgpJpegQuality quality)
        {
            int jpegQuality = JpegQualityValue(quality);
            return resolution switch
            {
                TgpResolution.Medium => new TgpCaptureSettings(true, 720, 480, 720, jpegQuality),
                TgpResolution.High => new TgpCaptureSettings(true, 1080, 720, 1080, jpegQuality),
                _ => new TgpCaptureSettings(false, 0, 0, 360, jpegQuality),
            };
        }
    }
}
