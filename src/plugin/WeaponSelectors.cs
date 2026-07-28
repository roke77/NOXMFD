using System;
using System.Collections.Generic;

namespace NOXMFD
{
    // The two weapon "soft selectors" behind the weapon keybinds (docs/keybinds-page-plan.md):
    // independent of the game's single active-weapon selection, the mod keeps a background pointer
    // over a GUN and another over a MISSILE-OR-BOMB. Cycle keys move them; the fire keys commit them
    // (make that weapon active, then fire) — so a pilot can keep e.g. a bomb selected on the HUD while
    // still having the gun trigger mean "the gun I chose".
    //
    // Selectors point at LOADOUT ENTRIES (stations aggregated by display name — the same list the WPN
    // page shows, see TelemetryReader.BuildLoadout), stored as the entry name. Everything else is
    // resolved live per use, so stale names (aircraft change, rearm) just fall back to the first entry
    // of the class instead of needing lifecycle events.
    //
    // Classification comes from the game's own public WeaponInfo flags:
    //   gun     → gun bucket
    //   bomb / glideBomb → bomb bucket
    //   missile, plus unclassified ordnance (rockets carry no flag at all) → missile bucket
    //   jammer / cargo / troops / sling / hideInDisplay → not selectable ordnance
    // The release selector serves the union of the missile and bomb buckets; Cycle Missiles and
    // Cycle Bombs each move it constrained to their own bucket, and Weapon Release fires whichever
    // was chosen last.
    //
    // The selectors FOLLOW the active selection: when the game's current weapon CHANGES (stock cycle,
    // WPN page tap, or our own commit) to a gun, the gun selector snaps to it; a missile/bomb snaps
    // the release selector. So the fire keys always commit the pilot's most recent choice — made by
    // ANY means — and never surprise-switch away from a manually selected weapon. Only a change
    // snaps (tracked via _lastActive), so cycling a soft selector away from the active weapon sticks.
    //
    // All methods run on the main thread with a live aircraft (called from Keybinds drives and the
    // telemetry reader).
    internal static class WeaponSelectors
    {
        private static string _softGun;         // gun-bucket entry name, or null
        private static string _softRel;         // missile/bomb-bucket entry name, or null
        private static WeaponStation _lastActive;

        private static readonly List<string> _entries = new List<string>(8);   // reused scratch

        // ── Classification ───────────────────────────────────────────────────────────────────────
        private static bool IsGun(WeaponInfo i)  => i.gun;
        private static bool IsBomb(WeaponInfo i) => i.bomb || i.glideBomb;
        // Missiles by flag, plus flagless launched ordnance (stock rockets set none of the class
        // flags). ponytail: 'energy' weapons are included here if flagless — none known to exist as a
        // non-gun station; revisit if one appears.
        private static bool IsMissile(WeaponInfo i) =>
            !i.gun && !i.bomb && !i.glideBomb && !i.jammer && !i.cargo && !i.troops && !i.sling;
        private static bool IsRelease(WeaponInfo i) => IsMissile(i) || IsBomb(i);

        internal static string EntryName(WeaponInfo info) =>
            !string.IsNullOrEmpty(info.weaponName) ? info.weaponName : info.shortName;

        // ── Cycle keys ───────────────────────────────────────────────────────────────────────────
        public static void CycleGun(Aircraft ac)     => _softGun = CycleIn(ac, IsGun,     _softGun);
        public static void CycleMissile(Aircraft ac) => _softRel = CycleIn(ac, IsMissile, _softRel);
        public static void CycleBomb(Aircraft ac)    => _softRel = CycleIn(ac, IsBomb,    _softRel);

        // Advance within the class's entry list: the entry after `current`, wrapping; the first entry
        // when current isn't in this class (or is stale). Returns current unchanged if the class is
        // empty so a stale-but-displayable name isn't wiped by pressing the wrong cycle key.
        private static string CycleIn(Aircraft ac, Func<WeaponInfo, bool> cls, string current)
        {
            Follow(ac);
            BuildEntries(ac, cls);
            if (_entries.Count == 0) return current;
            int idx = _entries.IndexOf(current);
            return _entries[idx < 0 ? 0 : (idx + 1) % _entries.Count];
        }

        // ── Fire keys ────────────────────────────────────────────────────────────────────────────
        // Gun Trigger: held — commit the effective gun and fire every frame (WeaponStation.Ready()
        // rate-limits, so this behaves like the stock gun trigger, guns-linked included via Fire()).
        public static void FireGun(Aircraft ac)     => CommitAndFire(ac, EffectiveGun(ac));

        // Weapon Release: edge — commit the effective missile/bomb, one press = one release.
        public static void FireRelease(Aircraft ac) => CommitAndFire(ac, EffectiveRelease(ac));

        // The entry a fire key would commit right now: the soft selection if it's still a live entry
        // of the class, else the first entry of the class, else null. Also what the WPN page outlines.
        public static string EffectiveGun(Aircraft ac)     => Effective(ac, IsGun,     _softGun);
        public static string EffectiveRelease(Aircraft ac) => Effective(ac, IsRelease, _softRel);

        private static string Effective(Aircraft ac, Func<WeaponInfo, bool> cls, string soft)
        {
            Follow(ac);
            BuildEntries(ac, cls);
            if (_entries.Count == 0) return null;
            return soft != null && _entries.Contains(soft) ? soft : _entries[0];
        }

        private static void CommitAndFire(Aircraft ac, string name)
        {
            WeaponManager wm = ac.weaponManager;
            if (wm == null || name == null) return;

            WeaponStation cur = wm.currentWeaponStation;
            string curName = cur != null && cur.WeaponInfo != null ? EntryName(cur.WeaponInfo) : null;
            if (!string.Equals(curName, name, StringComparison.Ordinal))
            {
                WeaponStation target = FindStationByName(ac, name);
                if (target == null) return;
                SelectStation(ac, target);
                _lastActive = target;   // our own commit is not a pilot change — don't re-snap on it
            }
            wm.Fire();   // the stock trigger's entry: safety, guns-linked, salvo, network path
        }

        // ── Follow the active selection ──────────────────────────────────────────────────────────
        private static void Follow(Aircraft ac)
        {
            WeaponManager wm = ac.weaponManager;
            if (wm == null) return;
            WeaponStation cur = wm.currentWeaponStation;
            if (ReferenceEquals(cur, _lastActive)) return;
            _lastActive = cur;
            WeaponInfo info = cur != null ? cur.WeaponInfo : null;
            if (info == null || info.hideInDisplay) return;
            if (IsGun(info))          _softGun = EntryName(info);
            else if (IsRelease(info)) _softRel = EntryName(info);
        }

        // ── Shared station lookup + select (also used by CommandDispatcher.WeaponSelect) ─────────
        // First visible station whose entry name matches — the same aggregation BuildLoadout uses;
        // the game cycles duplicate stations of one type itself.
        internal static WeaponStation FindStationByName(Aircraft ac, string name)
        {
            if (ac.weaponStations == null) return null;
            foreach (WeaponStation st in ac.weaponStations)
            {
                if (st == null) continue;
                WeaponInfo info = st.WeaponInfo;
                if (info == null || info.hideInDisplay) continue;
                if (string.Equals(EntryName(info), name, StringComparison.Ordinal)) return st;
            }
            return null;
        }

        // Replays the game's own NextWeaponStation() sequence — point the manager at the station,
        // activate it (network-aware), sync the cockpit HUD — so the marker + select beep come along.
        internal static void SelectStation(Aircraft ac, WeaponStation target)
        {
            ac.weaponManager.currentWeaponStation = target;
            ac.SetActiveStation(target.Number);
            CombatHUD hud = SceneSingleton<CombatHUD>.i;
            if (hud != null && ReferenceEquals(hud.aircraft, ac)) hud.ShowWeaponStation(target);
        }

        // Entry names of the class, aggregated by name in first-appearance order (BuildLoadout's
        // order), into the reused _entries scratch list.
        private static void BuildEntries(Aircraft ac, Func<WeaponInfo, bool> cls)
        {
            _entries.Clear();
            if (ac.weaponStations == null) return;
            foreach (WeaponStation st in ac.weaponStations)
            {
                if (st == null) continue;
                WeaponInfo info = st.WeaponInfo;
                if (info == null || info.hideInDisplay || !cls(info)) continue;
                string name = EntryName(info);
                if (string.IsNullOrEmpty(name) || _entries.Contains(name)) continue;
                _entries.Add(name);
            }
        }
    }
}
