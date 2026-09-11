using System;
using System.Collections.Concurrent;
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

        // Injected by Plugin.cs (RouteStore.LogWarning's own seam) so this file stays BepInEx-free —
        // it's compiled standalone into tools/tests/NOXMFD.Tests.csproj.
        internal static Action<string>? LogWarning;

        private static readonly ConcurrentDictionary<string, byte> _invalidOverrideWarnings =
            new ConcurrentDictionary<string, byte>(StringComparer.Ordinal);

        // One warning per rejecting key for the life of the session — an extension retrying the same
        // bad call every frame (Update()) must not flood the log the way ExtensionRegistry.cs's own
        // WarnInvalidPayload already guards against for ITS publish surface.
        private static void WarnRejected(string key, string message)
        {
            if (_invalidOverrideWarnings.TryAdd(key, 0)) LogWarning?.Invoke("[NOXMFD] " + message);
        }

        private static string? _friendlyHex;
        private static string? _enemyHex;
        private static string? _neutralHex;
        private static readonly object _factionLock = new object();

        internal static bool SetFactionOverride(string? friendlyHex, string? enemyHex, string? neutralHex)
        {
            if (!IsValidOptionalHex(friendlyHex) || !IsValidOptionalHex(enemyHex) ||
                !IsValidOptionalHex(neutralHex))
            {
                WarnRejected("faction",
                    $"extension faction-color override rejected (friendly={friendlyHex ?? "null"}, " +
                    $"enemy={enemyHex ?? "null"}, neutral={neutralHex ?? "null"}): each must be null or " +
                    "#RRGGBB/#RRGGBBAA; keeping the current override.");
                return false;
            }
            lock (_factionLock) { _friendlyHex = friendlyHex; _enemyHex = enemyHex; _neutralHex = neutralHex; }
            return true;
        }

        internal static void ClearFactionOverride() => SetFactionOverride(null, null, null);

        internal static (string? Friendly, string? Enemy, string? Neutral) FactionOverride
        {
            get { lock (_factionLock) return (_friendlyHex, _enemyHex, _neutralHex); }
        }

        private static readonly object _typeLock = new object();
        // volatile, not lock-guarded on read: a write always swaps in a whole new, already-built
        // dictionary (never mutates the published one in place), so a lock-free read either sees the
        // old or the new instance in full — the same "immutable value republished through a volatile
        // field" shape RouteStore.cs/TdStore.cs/HudPresetStore.cs already use for this exact scenario.
        // _typeLock still serializes writers' own read-modify-write.
        private static volatile Dictionary<string, TypeOverride> _typeOverrides =
            new Dictionary<string, TypeOverride>(StringComparer.Ordinal);

        internal static bool SetTypeOverride(string unitType, string hex, int? factionFilter)
        {
            if (string.IsNullOrWhiteSpace(unitType) || !IsValidHex(hex) ||
                (factionFilter.HasValue && (factionFilter.Value < 0 || factionFilter.Value > 2)))
            {
                WarnRejected("type:" + (unitType ?? ""),
                    $"extension unit-color override for '{unitType}' rejected (hex={hex ?? "null"}, " +
                    $"factionFilter={(factionFilter.HasValue ? factionFilter.Value.ToString() : "null")}): " +
                    "unitType is required, color must be #RRGGBB/#RRGGBBAA, and factionFilter must be 0-2 " +
                    "or null; keeping the current override.");
                return false;
            }

            lock (_typeLock)
            {
                var current = _typeOverrides;
                if (current.TryGetValue(unitType, out TypeOverride existing) &&
                    existing.Hex == hex && existing.FactionFilter == factionFilter) return true;
                var next = new Dictionary<string, TypeOverride>(current, StringComparer.Ordinal)
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
                var current = _typeOverrides;
                if (!current.ContainsKey(unitType)) return;
                var next = new Dictionary<string, TypeOverride>(current, StringComparer.Ordinal);
                next.Remove(unitType);
                _typeOverrides = next;
            }
        }

        internal static IReadOnlyDictionary<string, TypeOverride> TypeOverridesSnapshot() => _typeOverrides;

        private static bool IsValidOptionalHex(string? value) => value == null || IsValidHex(value);

        // Canvas colors use the explicit extension contract: RGB or RGBA hex, never arbitrary CSS.
        internal static bool IsValidHex(string? value)
        {
            if (value == null || (value.Length != 7 && value.Length != 9) || value[0] != '#') return false;
            for (int i = 1; i < value.Length; i++)
                if (!Uri.IsHexDigit(value[i])) return false;
            return true;
        }
    }
}
