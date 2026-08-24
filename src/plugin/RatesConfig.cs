using BepInEx.Configuration;
using UnityEngine;

namespace NOXMFD
{
    // Live-adjustable refresh rates: FastHz (MAP CFG page) drives TelemetryReader.FastInterval
    // (own-ship, weapons, contacts, TGT, BDF/PAL); TgpHz/TgpQuality/TgpSuppressNative (TGP CFG page)
    // drive TgpFeed. Persisted, hidden from the F1 menu. Setting any of them writes directly into
    // the reader's/feed's own field, live.
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
        private static ConfigEntry<float>?  _tgpHz;
        private static ConfigEntry<string>? _tgpQuality;
        private static ConfigEntry<bool>?   _tgpSuppressNative;

        public static float FastHz => _fastHz?.Value ?? 10f;
        public static float TgpHz  => _tgpHz?.Value  ?? 15f;
        public static string TgpQualityName => _tgpQuality?.Value ?? "native";
        public static bool TgpSuppressNative => _tgpSuppressNative?.Value ?? false;

        public static void SetFastHz(float hz)
        {
            hz = Mathf.Clamp(hz, MinHz, MaxHz);
            if (_fastHz != null) _fastHz.Value = hz;
            TelemetryReader.FastInterval = 1f / hz;
        }

        public static void SetTgpHz(float hz)
        {
            hz = Mathf.Clamp(hz, MinHz, TgpMaxHz);
            if (_tgpHz != null) _tgpHz.Value = hz;
            TgpFeed.Interval = 1f / hz;
        }

        // Any input other than "hq" normalizes to native. See TgpQuality in TgpMirrorCam.cs for what
        // HQ actually does. Persists the normalized value, not the raw input — otherwise a stale
        // config value (e.g. a dropped tier name from an older build) would keep reading back out of
        // TgpQualityName unchanged even though SetTgpQuality itself already treats it as native, and
        // the CFG page's LOW/HIGH buttons would then both show inactive.
        public static void SetTgpQuality(string name)
        {
            TgpQuality quality = name == "hq" ? TgpQuality.HighQuality : TgpQuality.Native;
            string normalized = quality == TgpQuality.HighQuality ? "hq" : "native";
            if (_tgpQuality != null) _tgpQuality.Value = normalized;
            TgpFeed.Quality = quality;
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
                new ConfigDescription("Main telemetry tick (own-ship, weapons, contacts, TGT, BDF/PAL). 1-30 Hz.", null, Hidden));
            _tgpHz = config.Bind(section, "TgpHz", 15f,
                new ConfigDescription("TGP camera feed capture rate. 1-60 Hz.", null, Hidden));
            _tgpQuality = config.Bind(section, "TgpQuality", "native",
                new ConfigDescription("TGP camera feed source: native or hq.", null, Hidden));
            _tgpSuppressNative = config.Bind(section, "TgpSuppressNative", false,
                new ConfigDescription("When the TGP feed is active, hide the native in-cockpit TGP overlay so the normal cockpit display remains visible.", null, Hidden));

            SetFastHz(_fastHz.Value);
            SetTgpHz(_tgpHz.Value);
            SetTgpQuality(_tgpQuality.Value);
            SetTgpSuppressNative(_tgpSuppressNative.Value);
        }
    }
}
