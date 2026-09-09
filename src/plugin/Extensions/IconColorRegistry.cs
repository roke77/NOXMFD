using System;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace NOXMFD
{
    // Backing store for Api.cs's icon-color override surface (docs/vanilla-icons-plus-extension.md).
    // Lets a registered extension push a live faction-color override — replacing
    // TelemetryReader's once-per-session GameAssets read — and per-unit-type overrides keyed by
    // the same type name contacts already carry as "t" and icon lookup already keys by. Same
    // latest-write-wins style as ExtensionRegistry's slices; expected entry count is tiny (a
    // handful of AA-style unit types), so no snapshot caching beyond the empty-dictionary shortcut.
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

        internal static void SetFactionOverride(string? friendlyHex, string? enemyHex, string? neutralHex)
        {
            lock (_factionLock) { _friendlyHex = friendlyHex; _enemyHex = enemyHex; _neutralHex = neutralHex; }
        }

        internal static void ClearFactionOverride() => SetFactionOverride(null, null, null);

        internal static (string? Friendly, string? Enemy, string? Neutral) FactionOverride
        {
            get { lock (_factionLock) return (_friendlyHex, _enemyHex, _neutralHex); }
        }

        private static readonly ConcurrentDictionary<string, TypeOverride> _typeOverrides =
            new ConcurrentDictionary<string, TypeOverride>(StringComparer.Ordinal);
        private static readonly Dictionary<string, TypeOverride> _emptyTypeOverrides =
            new Dictionary<string, TypeOverride>(StringComparer.Ordinal);

        internal static void SetTypeOverride(string unitType, string hex, int? factionFilter)
        {
            if (string.IsNullOrEmpty(unitType) || string.IsNullOrEmpty(hex)) return;
            _typeOverrides[unitType] = new TypeOverride(hex, factionFilter);
        }

        internal static void ClearTypeOverride(string unitType)
        {
            if (string.IsNullOrEmpty(unitType)) return;
            _typeOverrides.TryRemove(unitType, out _);
        }

        // A defensive copy — the caller (TelemetryReader, at ~1 Hz) iterates it while an extension
        // could concurrently add/remove entries from its own thread.
        internal static Dictionary<string, TypeOverride> TypeOverridesSnapshot()
            => _typeOverrides.IsEmpty
                ? _emptyTypeOverrides
                : new Dictionary<string, TypeOverride>(_typeOverrides, StringComparer.Ordinal);
    }
}
