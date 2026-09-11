using System;
using System.Collections.Generic;

namespace NOXMFD
{
    // Backing store for Api.cs's icon-color override surface (docs/vanilla-icons-plus-extension.md).
    // Lets a registered extension push a live faction-color override — replacing
    // TelemetryReader's once-per-session GameAssets read — and per-unit-type overrides keyed by
    // the same type name contacts already carry as "t" and icon lookup already keys by. Published
    // dictionaries are copy-on-write snapshots so the 10 Hz telemetry path can enumerate them
    // without allocating or racing an extension update.
    internal static class IconColorRegistry
    {
        internal readonly struct TypeOverride
        {
            public readonly string Hex;
            public readonly int? FactionFilter;   // null = any faction; else 0 neutral/1 friendly/2 enemy
            public TypeOverride(string hex, int? factionFilter) { Hex = hex; FactionFilter = factionFilter; }
        }

        private static string? _friendlyHex;
        private static string? _enemyHex;
        private static string? _neutralHex;
        private static readonly object _factionLock = new object();

        internal static bool SetFactionOverride(string? friendlyHex, string? enemyHex, string? neutralHex)
        {
            if (!IsValidOptionalHex(friendlyHex) || !IsValidOptionalHex(enemyHex) ||
                !IsValidOptionalHex(neutralHex)) return false;
            lock (_factionLock) { _friendlyHex = friendlyHex; _enemyHex = enemyHex; _neutralHex = neutralHex; }
            return true;
        }

        internal static void ClearFactionOverride() => SetFactionOverride(null, null, null);

        internal static (string? Friendly, string? Enemy, string? Neutral) FactionOverride
        {
            get { lock (_factionLock) return (_friendlyHex, _enemyHex, _neutralHex); }
        }

        private static readonly object _typeLock = new object();
        private static Dictionary<string, TypeOverride> _typeOverrides =
            new Dictionary<string, TypeOverride>(StringComparer.Ordinal);
        private static readonly Dictionary<string, TypeOverride> _emptyTypeOverrides =
            new Dictionary<string, TypeOverride>(StringComparer.Ordinal);

        internal static bool SetTypeOverride(string unitType, string hex, int? factionFilter)
        {
            if (string.IsNullOrWhiteSpace(unitType) || !IsValidHex(hex) ||
                (factionFilter.HasValue && (factionFilter.Value < 0 || factionFilter.Value > 2)))
                return false;

            lock (_typeLock)
            {
                if (_typeOverrides.TryGetValue(unitType, out TypeOverride current) &&
                    current.Hex == hex && current.FactionFilter == factionFilter) return true;
                var next = new Dictionary<string, TypeOverride>(_typeOverrides, StringComparer.Ordinal)
                {
                    [unitType] = new TypeOverride(hex, factionFilter),
                };
                _typeOverrides = next;
            }
            return true;
        }

        internal static void ClearTypeOverride(string unitType)
        {
            if (string.IsNullOrEmpty(unitType)) return;
            lock (_typeLock)
            {
                if (!_typeOverrides.ContainsKey(unitType)) return;
                var next = new Dictionary<string, TypeOverride>(_typeOverrides, StringComparer.Ordinal);
                next.Remove(unitType);
                _typeOverrides = next;
            }
        }

        internal static IReadOnlyDictionary<string, TypeOverride> TypeOverridesSnapshot()
        {
            lock (_typeLock) return _typeOverrides.Count == 0 ? _emptyTypeOverrides : _typeOverrides;
        }

        private static bool IsValidOptionalHex(string? value) => value == null || IsValidHex(value);

        // Canvas colors use the explicit extension contract: RGB or RGBA hex, never arbitrary CSS.
        internal static bool IsValidHex(string? value)
        {
            if (value == null || (value.Length != 7 && value.Length != 9) || value[0] != '#') return false;
            for (int i = 1; i < value.Length; i++)
            {
                char c = value[i];
                bool hex = (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F');
                if (!hex) return false;
            }
            return true;
        }
    }
}
