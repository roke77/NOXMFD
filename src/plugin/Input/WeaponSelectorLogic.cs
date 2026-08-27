using System;
using System.Collections.Generic;

namespace NOXMFD
{
    internal enum WeaponSelectorRole
    {
        Gun,
        Missile,
        Bomb,
        JammerPod,
    }

    internal enum WeaponSelectorBucket
    {
        Gun,
        Missile,
        Bomb,
        Release,
        JammerPod,
    }

    internal enum WeaponSelectorCombatMode
    {
        All,
        AirToAir,
        AirToGround,
    }

    internal readonly struct WeaponSelectorLoadoutItem
    {
        public readonly string Name;
        public readonly WeaponSelectorRole Role;
        public readonly int Ammo;
        public readonly bool AirToAir;

        public WeaponSelectorLoadoutItem(string name, WeaponSelectorRole role, int ammo, bool airToAir = false)
        {
            Name = name;
            Role = role;
            Ammo = ammo;
            AirToAir = airToAir;
        }
    }

    internal readonly struct WeaponSelectorCycleResult
    {
        public readonly string? SoftName;
        public readonly string? TargetName;

        public WeaponSelectorCycleResult(string? softName, string? targetName)
        {
            SoftName = softName;
            TargetName = targetName;
        }
    }

    internal static class WeaponSelectorLogic
    {
        // Cycle keys update the remembered soft selection only when they find a live target with
        // ammo. Empty or fully depleted buckets are a no-op: preserve the old soft name and do not
        // select anything.
        public static WeaponSelectorCycleResult Cycle(
            IReadOnlyList<WeaponSelectorLoadoutItem> loadout,
            WeaponSelectorBucket bucket,
            WeaponSelectorCombatMode mode,
            string? currentName,
            string? softName)
        {
            List<Entry> entries = BuildEntries(loadout, bucket, mode);
            if (entries.Count == 0) return new WeaponSelectorCycleResult(softName, null);

            string? next;
            int idx = IndexOf(entries, currentName);
            if (idx >= 0)
            {
                next = null;
                for (int k = 1; k <= entries.Count; k++)
                {
                    int cand = (idx + k) % entries.Count;
                    if (entries[cand].Ammo > 0)
                    {
                        next = entries[cand].Name;
                        break;
                    }
                }
            }
            else
            {
                int softIdx = IndexOf(entries, softName);
                next = softIdx >= 0 && entries[softIdx].Ammo > 0 ? softName : null;
                for (int k = 0; next == null && k < entries.Count; k++)
                    if (entries[k].Ammo > 0) next = entries[k].Name;
            }

            return next == null
                ? new WeaponSelectorCycleResult(softName, null)
                : new WeaponSelectorCycleResult(next, next);
        }

        // Fire keys and WPN-page outlines can still point at an empty entry: the game can select it,
        // it just will not fire. So Effective only requires a live entry, not ammo.
        public static string? Effective(
            IReadOnlyList<WeaponSelectorLoadoutItem> loadout,
            WeaponSelectorBucket bucket,
            WeaponSelectorCombatMode mode,
            string? softName)
        {
            List<Entry> entries = BuildEntries(loadout, bucket, mode);
            if (entries.Count == 0) return null;
            return IndexOf(entries, softName) >= 0 ? softName : entries[0].Name;
        }

        public static string? FirstAvailable(
            IReadOnlyList<WeaponSelectorLoadoutItem> loadout,
            WeaponSelectorBucket bucket,
            WeaponSelectorCombatMode mode)
        {
            List<Entry> entries = BuildEntries(loadout, bucket, mode);
            for (int i = 0; i < entries.Count; i++)
                if (entries[i].Ammo > 0) return entries[i].Name;
            return null;
        }

        private static List<Entry> BuildEntries(
            IReadOnlyList<WeaponSelectorLoadoutItem> loadout,
            WeaponSelectorBucket bucket,
            WeaponSelectorCombatMode mode)
        {
            var entries = new List<Entry>(loadout != null ? loadout.Count : 0);
            if (loadout == null) return entries;

            for (int i = 0; i < loadout.Count; i++)
            {
                WeaponSelectorLoadoutItem item = loadout[i];
                if (string.IsNullOrEmpty(item.Name) || !Matches(item, bucket, mode)) continue;

                int existing = IndexOf(entries, item.Name);
                if (existing < 0) entries.Add(new Entry(item.Name, item.Ammo));
                else entries[existing] = new Entry(entries[existing].Name, entries[existing].Ammo + item.Ammo);
            }
            return entries;
        }

        private static bool Matches(WeaponSelectorLoadoutItem item, WeaponSelectorBucket bucket, WeaponSelectorCombatMode mode)
        {
            switch (bucket)
            {
                case WeaponSelectorBucket.Gun:
                    return item.Role == WeaponSelectorRole.Gun;
                case WeaponSelectorBucket.Missile:
                    return item.Role == WeaponSelectorRole.Missile && MissileAllowed(item, mode);
                case WeaponSelectorBucket.Bomb:
                    return mode != WeaponSelectorCombatMode.AirToAir && item.Role == WeaponSelectorRole.Bomb;
                case WeaponSelectorBucket.Release:
                    if (mode == WeaponSelectorCombatMode.AirToAir)
                        return item.Role == WeaponSelectorRole.Missile && item.AirToAir;
                    if (mode == WeaponSelectorCombatMode.AirToGround)
                        return (item.Role == WeaponSelectorRole.Missile && !item.AirToAir) ||
                               item.Role == WeaponSelectorRole.Bomb;
                    return item.Role == WeaponSelectorRole.Missile || item.Role == WeaponSelectorRole.Bomb;
                case WeaponSelectorBucket.JammerPod:
                    return item.Role == WeaponSelectorRole.JammerPod;
                default:
                    return false;
            }
        }

        private static bool MissileAllowed(WeaponSelectorLoadoutItem item, WeaponSelectorCombatMode mode)
        {
            switch (mode)
            {
                case WeaponSelectorCombatMode.AirToAir:
                    return item.AirToAir;
                case WeaponSelectorCombatMode.AirToGround:
                    return !item.AirToAir;
                default:
                    return true;
            }
        }

        private static int IndexOf(List<Entry> entries, string? name)
        {
            if (name == null) return -1;
            for (int i = 0; i < entries.Count; i++)
                if (string.Equals(entries[i].Name, name, StringComparison.Ordinal)) return i;
            return -1;
        }

        private readonly struct Entry
        {
            public readonly string Name;
            public readonly int Ammo;

            public Entry(string name, int ammo)
            {
                Name = name;
                Ammo = ammo;
            }
        }
    }
}
