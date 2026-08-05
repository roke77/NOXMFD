using System;
using System.Collections.Generic;

namespace NOXMFD
{
    // The two weapon "soft selectors" behind the weapon keybinds (docs/keybinds-page.md):
    // alongside the game's single active-weapon selection, the mod remembers a GUN and a
    // MISSILE-OR-BOMB choice per class. Cycle keys select within their class (first press from
    // another class recalls where you left it, further presses advance); fire keys are two-stage
    // across classes (a press first switches to the class's weapon, the next press fires) — so the
    // gun trigger always means "my gun" and weapon release "my missile/bomb", from anywhere.
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
    //   jammer → jammer-pod bucket (a weapon station, e.g. the Medusa's Radar Jamming Pod — distinct
    //            from the airframe-mounted RadarJammer countermeasure behind the "Jammer" keybind)
    //   cargo / troops / sling / hideInDisplay → not selectable ordnance
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
        private static string _softJam;         // jammer-pod-bucket entry name, or null
        private static WeaponStation _lastActive;

        private static readonly List<string> _entries = new List<string>(8);   // reused scratch
        private static readonly List<int>    _ammo    = new List<int>(8);      // remaining ammo per entry (parallel)

        // ── Classification ───────────────────────────────────────────────────────────────────────
        private static bool IsGun(WeaponInfo i)  => i.gun;
        private static bool IsBomb(WeaponInfo i) => i.bomb || i.glideBomb;
        // Missiles by flag, plus flagless launched ordnance (stock rockets set none of the class
        // flags). ponytail: 'energy' weapons are included here if flagless — none known to exist as a
        // non-gun station; revisit if one appears.
        private static bool IsMissile(WeaponInfo i) =>
            !i.gun && !i.bomb && !i.glideBomb && !i.jammer && !i.cargo && !i.troops && !i.sling;
        private static bool IsRelease(WeaponInfo i) => IsMissile(i) || IsBomb(i);
        // Weapon-mounted ECM (e.g. the Medusa's Radar Jamming Pod) — its own bucket, same shape as
        // guns: one soft selection, first press switches/selects, held presses activate it. Distinct
        // from the countermeasureManager-driven "Jammer" keybind, which drives an airframe-mounted
        // RadarJammer countermeasure, not a weapon station.
        private static bool IsJammerPod(WeaponInfo i) => i.jammer;

        // Air-to-air missiles (docs/radar-master-arms.md, issue #32) — a maintained, exhaustive list;
        // every other IsMissile entry counts as air-to-ground by default (new A/A weapons from future
        // game updates land as A/G until this list is updated — accepted, A/G additions are the common
        // case). Names are Encyclopedia/UnitDefinition names (_scratch/units.json); four of five —
        // "AAM-29 Scythe", "AAM-36 Scimitar", "IRM-S2", "MMR-S3" — are confirmed matching the live
        // WeaponInfo name from real session logs. "IRM-S1" hasn't turned up in a loadout yet and is
        // still provisional.
        private static readonly HashSet<string> AirToAirMissiles = new HashSet<string>(StringComparer.Ordinal)
        {
            "AAM-29 Scythe", "AAM-36 Scimitar", "IRM-S2", "MMR-S3", "IRM-S1",
        };
        private static bool IsAirToAir(WeaponInfo i) => AirToAirMissiles.Contains(EntryName(i));

        internal static string EntryName(WeaponInfo info) =>
            !string.IsNullOrEmpty(info.weaponName) ? info.weaponName : info.shortName;

        // ── Cycle keys ───────────────────────────────────────────────────────────────────────────
        // Each cycle key SELECTS (not just highlights): with the active weapon already in this class
        // it advances to the next entry and makes it active; with another class active it recalls the
        // class's remembered soft selection (or the first entry) and makes THAT active — so the first
        // press is "switch into this class where I left it", the second press cycles.
        public static void CycleGun(Aircraft ac)     => _softGun = CycleAndSelect(ac, IsGun, _softGun);

        // Combat mode (docs/radar-master-arms.md, issue #32) narrows which missiles Cycle Missile
        // reaches: unrestricted in ALL, air-to-air-only or air-to-ground-only otherwise. Guns are
        // unaffected by combat mode (see CycleGun above); bombs are handled below.
        public static void CycleMissile(Aircraft ac) => _softRel = CycleAndSelect(ac, MissileFilter(), _softRel);

        // Bombs are disabled entirely while in A/A mode — no-op, doesn't touch _softRel.
        public static void CycleBomb(Aircraft ac)
        {
            if (ImmersionState.CombatMode == CombatMode.AirToAir) return;
            _softRel = CycleAndSelect(ac, IsBomb, _softRel);
        }

        private static Func<WeaponInfo, bool> MissileFilter() => ImmersionState.CombatMode switch
        {
            CombatMode.AirToAir    => i => IsMissile(i) && IsAirToAir(i),
            CombatMode.AirToGround => i => IsMissile(i) && !IsAirToAir(i),
            _                      => IsMissile,
        };

        private static string CycleAndSelect(Aircraft ac, Func<WeaponInfo, bool> cls, string soft)
        {
            Follow(ac);
            BuildEntries(ac, cls);
            if (_entries.Count == 0) return soft;   // wrong-class press: keep the remembered name

            WeaponManager wm = ac.weaponManager;
            WeaponStation cur = wm != null ? wm.currentWeaponStation : null;
            string curName = cur != null && cur.WeaponInfo != null ? EntryName(cur.WeaponInfo) : null;

            // Cycling never lands on a depleted entry (fire keys can still commit one — the game
            // allows selecting an empty weapon; Ready() just won't fire it).
            string next;
            int idx = curName != null ? _entries.IndexOf(curName) : -1;
            if (idx >= 0)
            {
                // in-class: advance to the next entry with ammo, wrapping past depleted ones
                next = null;
                for (int k = 1; k <= _entries.Count; k++)
                {
                    int cand = (idx + k) % _entries.Count;
                    if (_ammo[cand] > 0) { next = _entries[cand]; break; }
                }
            }
            else
            {
                // cross-class: recall the remembered entry if it still has ammo, else the first with ammo
                int si = soft != null ? _entries.IndexOf(soft) : -1;
                next = si >= 0 && _ammo[si] > 0 ? soft : null;
                for (int k = 0; next == null && k < _entries.Count; k++)
                    if (_ammo[k] > 0) next = _entries[k];
            }
            if (next == null) return soft;   // whole class depleted — no-op

            WeaponStation target = FindStationByName(ac, next);
            if (target != null && !ReferenceEquals(cur, target))
            {
                SelectStation(ac, target);
                _lastActive = target;   // our own commit is not a pilot change — don't re-snap on it
            }
            return next;
        }

        // ── Fire keys ────────────────────────────────────────────────────────────────────────────
        // Both are HELD-driven with a two-stage cross-class press: if the active weapon isn't this
        // key's effective selection, the press only SWITCHES to it (bringing up the right reticle) and
        // that same hold never fires — release and press again to fire, then hold to keep firing
        // (WeaponManager.Fire() every frame; WeaponStation.Ready() rate-limits, matching the stock
        // trigger's hold behaviour, guns-linked included).
        //
        // Press-vs-hold is derived from Time.frameCount continuity: the drive runs every held frame,
        // so a gap in frames = a fresh press. ponytail: misses a release+re-press within one frame —
        // physically impossible on a real key.
        private static int  _gunFrame = -10, _relFrame = -10, _jamFrame = -10;
        private static bool _gunSwitchHold,  _relSwitchHold,  _jamSwitchHold;

        public static void FireGun(Aircraft ac)       => Fire(ac, EffectiveGun(ac),       ref _gunFrame, ref _gunSwitchHold);
        public static void FireRelease(Aircraft ac)   => Fire(ac, EffectiveRelease(ac),   ref _relFrame, ref _relSwitchHold);
        public static void FireJammerPod(Aircraft ac) => Fire(ac, EffectiveJammerPod(ac), ref _jamFrame, ref _jamSwitchHold);

        // The entry a fire key would commit right now: the soft selection if it's still a live entry
        // of the class, else the first entry of the class, else null. Also what the WPN page outlines.
        public static string EffectiveGun(Aircraft ac)       => Effective(ac, IsGun,       _softGun);
        public static string EffectiveRelease(Aircraft ac)   => Effective(ac, IsRelease,   _softRel);
        public static string EffectiveJammerPod(Aircraft ac) => Effective(ac, IsJammerPod, _softJam);

        private static string Effective(Aircraft ac, Func<WeaponInfo, bool> cls, string soft)
        {
            Follow(ac);
            BuildEntries(ac, cls);
            if (_entries.Count == 0) return null;
            return soft != null && _entries.Contains(soft) ? soft : _entries[0];
        }

        private static void Fire(Aircraft ac, string name, ref int lastFrame, ref bool switchHold)
        {
            bool fresh = UnityEngine.Time.frameCount != lastFrame + 1;   // gap = new press
            lastFrame = UnityEngine.Time.frameCount;
            if (fresh) switchHold = false;

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
                switchHold = true;      // stage 1: this press switched; it must not also fire
                return;
            }
            if (switchHold) return;     // still the hold that did the switch
            wm.Fire();                  // the stock trigger's entry: safety, guns-linked, salvo, network path
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
            if (IsGun(info))            _softGun = EntryName(info);
            else if (IsRelease(info))   _softRel = EntryName(info);
            else if (IsJammerPod(info)) _softJam = EntryName(info);
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
        // order), into the reused _entries scratch list, with per-entry remaining ammo summed across
        // stations into the parallel _ammo list (so cycling can skip depleted entries).
        private static void BuildEntries(Aircraft ac, Func<WeaponInfo, bool> cls)
        {
            _entries.Clear();
            _ammo.Clear();
            if (ac.weaponStations == null) return;
            foreach (WeaponStation st in ac.weaponStations)
            {
                if (st == null) continue;
                WeaponInfo info = st.WeaponInfo;
                if (info == null || info.hideInDisplay || !cls(info)) continue;
                string name = EntryName(info);
                if (string.IsNullOrEmpty(name)) continue;
                int i = _entries.IndexOf(name);
                if (i < 0) { _entries.Add(name); _ammo.Add(st.Ammo); }
                else _ammo[i] += st.Ammo;
            }
        }
    }
}
