using System.Collections.Generic;
using BepInEx.Configuration;

namespace NOXMFD
{
    // Settings for HudCompactLayout, all shown in the F1 menu (the HUD page has no controls for
    // them) and applied live: Version bumps on every change so the layout re-applies without
    // polling each entry. Slider defaults are the tuned layout (HudCompactLayoutMath.Groups), so
    // Enabled alone gives it and each slider's Reset returns to it.
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
        private static ConfigEntry<float>? _ladderSize;
        private static readonly Dictionary<string, (ConfigEntry<float> x, ConfigEntry<float> y, ConfigEntry<float> size)> _groups =
            new Dictionary<string, (ConfigEntry<float>, ConfigEntry<float>, ConfigEntry<float>)>();

        internal static int Version { get; private set; }

        internal static bool Enabled => _enabled?.Value ?? false;
        // Ladder window and waterline size.
        internal static float LadderSize => _ladderSize?.Value ?? 1f;

        // Blocks outside a group (fuel/throttle arcs, clocks) sit at their preset spot at game size.
        internal static Adjust ForGroup(string group)
        {
            if (_groups.TryGetValue(group, out var g)) return new Adjust(g.x.Value, g.y.Value, g.size.Value);
            foreach (var d in HudCompactLayoutMath.Groups)
                if (d.Name == group) return new Adjust(d.OffsetX, d.OffsetY, d.Size);
            return new Adjust(0f, 0f, 1f);
        }

        public static void Bind(ConfigFile config)
        {
            const string section = "HUD Compact Layout";
            _enabled = Watch(config.Bind(section, "Enabled", false,
                "Pull the nose-following HUD readouts (speed, altitude, heading tape, fuel/throttle, clocks) into a compact F-16-style cluster around the pitch ladder."));
            _ladderSize = Watch(Float(config, section, "LadderSize", 1f, 0.4f, 1.5f,
                "Size of the pitch ladder window and the waterline. The rungs keep their true spacing against the horizon, so a smaller window shows fewer rungs."));
            foreach (var d in HudCompactLayoutMath.Groups)
            {
                string name = d.Name.ToLowerInvariant();
                _groups[d.Name] = (
                    Watch(Float(config, section, d.Name + "OffsetX", d.OffsetX, -20f, 20f, $"Shift the {name} readout left (-) or right (+), in degrees.")),
                    Watch(Float(config, section, d.Name + "OffsetY", d.OffsetY, -20f, 20f, $"Shift the {name} readout down (-) or up (+), in degrees.")),
                    Watch(Float(config, section, d.Name + "Size", d.Size, 0.5f, 1.5f, $"Size of the {name} readout. 1 is the game's size.")));
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
