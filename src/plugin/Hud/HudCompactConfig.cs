using System.Collections.Generic;
using BepInEx.Configuration;

namespace NOXMFD
{
    // Settings for HudCompactLayout, all shown in the F1 menu (the HUD page has no controls for
    // them) and applied live: Version bumps on every change so the layout re-applies without
    // polling each entry.
    internal static class HudCompactConfig
    {
        internal readonly struct Adjust
        {
            internal Adjust(float offsetX, float offsetY, float size)
            {
                OffsetX = offsetX;
                OffsetY = offsetY;
                Size = size;
            }

            internal float OffsetX { get; }
            internal float OffsetY { get; }
            internal float Size { get; }
        }

        private static ConfigEntry<bool>? _enabled;
        private static ConfigEntry<float>? _spread;
        private static ConfigEntry<float>? _offsetX;
        private static ConfigEntry<float>? _offsetY;
        private static ConfigEntry<float>? _size;
        private static ConfigEntry<float>? _ladderSize;
        private static readonly Dictionary<string, (ConfigEntry<float> x, ConfigEntry<float> y, ConfigEntry<float> size)> _groups =
            new Dictionary<string, (ConfigEntry<float>, ConfigEntry<float>, ConfigEntry<float>)>();

        internal static int Version { get; private set; }

        internal static bool Enabled => _enabled?.Value ?? false;
        // Multiplier on the preset's angles: below 1 pulls every block closer to the nose.
        internal static float Spread => _spread?.Value ?? 1f;
        internal static Adjust All => new Adjust(_offsetX?.Value ?? 0f, _offsetY?.Value ?? 0f, _size?.Value ?? 1f);
        // Ladder window and waterline size, multiplied by the global Size.
        internal static float LadderSize => _ladderSize?.Value ?? 1f;

        internal static Adjust ForGroup(string group) =>
            _groups.TryGetValue(group, out var g) ? new Adjust(g.x.Value, g.y.Value, g.size.Value) : new Adjust(0f, 0f, 1f);

        public static void Bind(ConfigFile config)
        {
            const string section = "HUD Compact Layout";
            _enabled = Watch(config.Bind(section, "Enabled", false,
                "Pull the nose-following HUD readouts (speed, altitude, heading tape, fuel/throttle, clocks) into a compact F-16-style cluster around the pitch ladder."));
            _spread = Watch(Float(config, section, "Spread", 1f, 0.4f, 1.2f,
                "How tight the cluster is: 1 is the preset, lower pulls every readout closer to the nose."));
            _offsetX = Watch(Float(config, section, "OffsetX", 0f, -20f, 20f, "Shift the whole cluster left (-) or right (+), in degrees."));
            _offsetY = Watch(Float(config, section, "OffsetY", 0f, -20f, 20f, "Shift the whole cluster down (-) or up (+), in degrees."));
            _size = Watch(Float(config, section, "Size", 1f, 0.5f, 1.5f, "Size of every compact readout. 1 is the game's size."));
            _ladderSize = Watch(Float(config, section, "LadderSize", 1f, 0.4f, 1.5f,
                "Size of the pitch ladder window and the waterline. The rungs keep their true spacing against the horizon, so a smaller window shows fewer rungs."));
            foreach (string g in HudCompactLayoutMath.Groups)
            {
                _groups[g] = (
                    Watch(Float(config, section, g + "OffsetX", 0f, -20f, 20f, $"Shift the {g.ToLowerInvariant()} readout left (-) or right (+), in degrees, on top of OffsetX.")),
                    Watch(Float(config, section, g + "OffsetY", 0f, -20f, 20f, $"Shift the {g.ToLowerInvariant()} readout down (-) or up (+), in degrees, on top of OffsetY.")),
                    Watch(Float(config, section, g + "Size", 1f, 0.5f, 1.5f, $"Size of the {g.ToLowerInvariant()} readout, multiplied by Size.")));
            }
        }

        private static ConfigEntry<float> Float(ConfigFile config, string section, string key, float def, float min, float max, string desc) =>
            config.Bind(section, key, def, new ConfigDescription(desc, new AcceptableValueRange<float>(min, max)));

        private static ConfigEntry<T> Watch<T>(ConfigEntry<T> entry)
        {
            entry.SettingChanged += (_, __) => Version++;
            return entry;
        }
    }
}
