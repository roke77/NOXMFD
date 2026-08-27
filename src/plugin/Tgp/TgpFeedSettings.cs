using System;

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
            TgpJpegQuality.High => 90,
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

        // Scales (width, height) down to fit within maxDimension's longer side, preserving aspect
        // ratio; a source already at or under the cap (or maxDimension <= 0, "no cap") passes
        // through unchanged. Shared by TgpFeed's per-frame downscale target and SpriteCapture's
        // one-shot asset resize — same job, previously two independently-written copies.
        internal static (int Width, int Height) FitWithinMaxDimension(int width, int height, int maxDimension)
        {
            int w = Math.Max(1, width);
            int h = Math.Max(1, height);
            if (maxDimension <= 0) return (w, h);

            int maxSide = Math.Max(w, h);
            if (maxSide <= maxDimension) return (w, h);

            if (w >= h)
                return (maxDimension, Math.Max(1, (int)Math.Round(maxDimension * (double)h / w)));
            return (Math.Max(1, (int)Math.Round(maxDimension * (double)w / h)), maxDimension);
        }

        // Auto-levels stretch for the IR (thermal) look: tracks the frame's own min/max luma with an
        // EMA (so a sudden bright/dark object doesn't snap contrast frame-to-frame) and remaps that
        // range to the full 0..255 spread. px is RGBA8 bytes, mutated in place; minEma/maxEma are the
        // caller's persisted smoothing state (negative minEma means "not seeded yet" — the first call
        // after a reset snaps straight to that frame's own min/max instead of easing in from zero).
        //
        // ponytail: a single global min/max stretch, not per-region/histogram-equalized — the frame's
        // own extremes can be skewed by one hot/cold outlier pixel (e.g. a flare or the sun clipping
        // into frame), washing out the rest of the picture's contrast. Upgrade path: clip the min/max
        // search to a percentile (e.g. 1st/99th) instead of the true extremes, if that's ever seen in
        // live testing.
        internal static void ApplyIrAutoLevels(byte[] px, ref float minEma, ref float maxEma, float smoothing)
        {
            byte rawMin = 255, rawMax = 0;
            for (int i = 0; i + 3 < px.Length; i += 4)
            {
                byte luma = (byte)(0.299f * px[i] + 0.587f * px[i + 1] + 0.114f * px[i + 2]);
                if (luma < rawMin) rawMin = luma;
                if (luma > rawMax) rawMax = luma;
            }

            if (minEma < 0f) { minEma = rawMin; maxEma = rawMax; }
            else
            {
                minEma += (rawMin - minEma) * smoothing;
                maxEma += (rawMax - maxEma) * smoothing;
            }

            float range = Math.Max(1f, maxEma - minEma);
            for (int i = 0; i + 3 < px.Length; i += 4)
            {
                float luma = 0.299f * px[i] + 0.587f * px[i + 1] + 0.114f * px[i + 2];
                float stretched = (luma - minEma) / range * 255f;
                byte gray = (byte)Math.Max(0f, Math.Min(255f, stretched));
                px[i] = px[i + 1] = px[i + 2] = gray;
            }
        }
    }
}
