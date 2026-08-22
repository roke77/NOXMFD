using BepInEx.Configuration;
using UnityEngine;

namespace NOXMFD
{
    // Live-adjustable refresh rates for the RTS page: FastHz drives TelemetryReader.FastInterval
    // (own-ship, weapons, contacts, TGT, BDF/PAL); TgpHz drives TgpFeed.Interval. Persisted, hidden
    // from the F1 menu. Setting either writes directly into the reader's/feed's interval field, live.
    internal static class RatesConfig
    {
        private sealed class ConfigurationManagerAttributes { public bool? Browsable; }
        private static readonly ConfigurationManagerAttributes Hidden =
            new ConfigurationManagerAttributes { Browsable = false };

        private const float MinHz = 1f;
        private const float MaxHz = 30f;

        private static ConfigEntry<float>? _fastHz;
        private static ConfigEntry<float>? _tgpHz;

        public static float FastHz => _fastHz?.Value ?? 10f;
        public static float TgpHz  => _tgpHz?.Value  ?? 15f;

        public static void SetFastHz(float hz)
        {
            hz = Mathf.Clamp(hz, MinHz, MaxHz);
            if (_fastHz != null) _fastHz.Value = hz;
            TelemetryReader.FastInterval = 1f / hz;
        }

        public static void SetTgpHz(float hz)
        {
            hz = Mathf.Clamp(hz, MinHz, MaxHz);
            if (_tgpHz != null) _tgpHz.Value = hz;
            TgpFeed.Interval = 1f / hz;
        }

        // Applies the persisted (or default) Hz to the reader/feed immediately on bind.
        public static void Bind(ConfigFile config)
        {
            const string section = "Refresh Rates";
            _fastHz = config.Bind(section, "FastHz", 10f,
                new ConfigDescription("Main telemetry tick (own-ship, weapons, contacts, TGT, BDF/PAL). 1-30 Hz.", null, Hidden));
            _tgpHz = config.Bind(section, "TgpHz", 15f,
                new ConfigDescription("TGP camera feed capture rate. 1-30 Hz.", null, Hidden));

            SetFastHz(_fastHz.Value);
            SetTgpHz(_tgpHz.Value);
        }
    }
}
