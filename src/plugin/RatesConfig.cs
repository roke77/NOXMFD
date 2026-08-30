using BepInEx.Configuration;
using UnityEngine;

namespace NOXMFD
{
    // Live-adjustable refresh rates: FastHz (MAP CFG page) drives TelemetryReader.FastInterval
    // (own-ship, weapons, RWR/MW, TGT, BDF/PAL); ContactHz (MAP CFG page's own CONTACTS slider)
    // drives TelemetryReader.ContactInterval (MAP/RDR/HSD contact + pitbull snapshots, split off
    // FastHz — docs/performance.md item #4); the TGP settings drive TgpFeed. Persisted and hidden
    // from the F1 menu, setting any of them writes directly into the reader's/feed's own field, live.
    internal static class RatesConfig
    {
        private sealed class ConfigurationManagerAttributes { public bool? Browsable; }
        private static readonly ConfigurationManagerAttributes Hidden =
            new ConfigurationManagerAttributes { Browsable = false };

        private const float MinHz = 1f;
        private const float MaxHz = 30f;
        // TGP's own ceiling, separate from MaxHz — raised for perf experimentation (docs/
        // performance.md already documents heavy drop rates above 15 Hz at 30; this just lets
        // that same measurement be taken up to 60 without also raising FastHz's cap).
        private const float TgpMaxHz = 60f;

        private static ConfigEntry<float>?  _fastHz;
        private static ConfigEntry<float>?  _contactHz;
        private static ConfigEntry<float>?  _tgpHz;
        private static ConfigEntry<string>? _tgpQuality;
        private static ConfigEntry<string>? _tgpJpegQuality;
        private static ConfigEntry<bool>?   _tgpSuppressNative;

        public static float FastHz    => _fastHz?.Value    ?? 10f;
        public static float ContactHz => _contactHz?.Value ?? 4f;
        public static float TgpHz     => _tgpHz?.Value      ?? 15f;
        public static string TgpResolutionName =>
            TgpFeedSettings.NormalizeResolutionName(_tgpQuality?.Value);
        public static string TgpJpegQualityName =>
            TgpFeedSettings.NormalizeJpegQualityName(_tgpJpegQuality?.Value);
        public static string TgpLegacyQualityName =>
            TgpFeedSettings.LegacyQualityName(TgpFeedSettings.ParseResolution(TgpResolutionName));
        public static bool TgpSuppressNative => _tgpSuppressNative?.Value ?? false;

        public static void SetFastHz(float hz)
        {
            hz = Mathf.Clamp(hz, MinHz, MaxHz);
            if (_fastHz != null) _fastHz.Value = hz;
            TelemetryReader.FastInterval = 1f / hz;
        }

        public static void SetContactHz(float hz)
        {
            hz = Mathf.Clamp(hz, MinHz, MaxHz);
            if (_contactHz != null) _contactHz.Value = hz;
            TelemetryReader.ContactInterval = 1f / hz;
        }

        public static void SetTgpHz(float hz)
        {
            hz = Mathf.Clamp(hz, MinHz, TgpMaxHz);
            if (_tgpHz != null) _tgpHz.Value = hz;
            TgpFeed.Interval = 1f / hz;
        }

        public static void SetTgpResolution(string name)
        {
            string normalized = TgpFeedSettings.NormalizeResolutionName(name);
            if (_tgpQuality != null) _tgpQuality.Value = normalized;
            TgpFeed.SetResolution(TgpFeedSettings.ParseResolution(normalized));
        }

        public static void SetTgpJpegQuality(string name)
        {
            string normalized = TgpFeedSettings.NormalizeJpegQualityName(name);
            if (_tgpJpegQuality != null) _tgpJpegQuality.Value = normalized;
            TgpFeed.SetJpegQuality(TgpFeedSettings.ParseJpegQuality(normalized));
        }

        public static void SetTgpSuppressNative(bool on)
        {
            if (_tgpSuppressNative != null) _tgpSuppressNative.Value = on;
            TgpFeed.SuppressNativeDisplay = on;
            Plugin.Log?.LogInfo($"[NOXMFD] TGP cockpit feed hide = {on}.");
        }

        // Applies the persisted (or default) Hz to the reader/feed immediately on bind.
        public static void Bind(ConfigFile config)
        {
            const string section = "Refresh Rates";
            _fastHz = config.Bind(section, "FastHz", 10f,
                new ConfigDescription("Main telemetry tick (own-ship, weapons, RWR/MW, TGT, BDF/PAL). 1-30 Hz.", null, Hidden));
            _contactHz = config.Bind(section, "ContactHz", 4f,
                new ConfigDescription("MAP/RDR/HSD contact + pitbull refresh rate. 1-30 Hz.", null, Hidden));
            _tgpHz = config.Bind(section, "TgpHz", 15f,
                new ConfigDescription("TGP camera feed capture rate. 1-60 Hz.", null, Hidden));
            _tgpQuality = config.Bind(section, "TgpQuality", "native",
                new ConfigDescription("TGP feed resolution: native, mid, or high. The key name is retained for compatibility.", null, Hidden));
            _tgpJpegQuality = config.Bind(section, "TgpJpegQuality", "mid",
                new ConfigDescription("TGP JPEG quality: low, mid, or high.", null, Hidden));
            _tgpSuppressNative = config.Bind(section, "TgpSuppressNative", false,
                new ConfigDescription("When the TGP feed is active, hide the native in-cockpit TGP overlay so the normal cockpit display remains visible.", null, Hidden));

            SetFastHz(_fastHz.Value);
            SetContactHz(_contactHz.Value);
            SetTgpHz(_tgpHz.Value);
            SetTgpResolution(_tgpQuality.Value);
            SetTgpJpegQuality(_tgpJpegQuality.Value);
            SetTgpSuppressNative(_tgpSuppressNative.Value);
        }
    }
}
