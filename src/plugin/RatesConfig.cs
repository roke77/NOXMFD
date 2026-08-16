using BepInEx.Configuration;
using UnityEngine;

namespace NOXMFD
{
    // cfg-rates experiment (issue #39): live-adjustable refresh rates for the RTS page's two
    // sliders. Same shape as HudDeclutterConfig — hidden ConfigEntry<float> (Hz), bound once from
    // Plugin.Awake, persisted to the .cfg, and read/written at runtime via the rates.set command.
    //
    //   * TLM — the whole 10 Hz PushSnapshot tick (own-ship, weapons, contacts, TGT, BDF/PAL);
    //     drives TelemetryReader.FastInterval.
    //   * TGP — the targeting-pod camera feed; drives TgpFeed.Interval.
    //
    // Setting either writes straight into the reader's/feed's own interval field, so the change is
    // live immediately — no restart, no waiting for the next .cfg reload.
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

        // Called once from Plugin.Awake. Applies the persisted (or default) Hz to the reader/feed
        // immediately, so a saved non-default rate takes effect without needing a slider touch first.
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
