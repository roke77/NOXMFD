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
        private static string? _softGun;         // gun-bucket entry name, or null
        private static string? _softRel;         // missile/bomb-bucket entry name, or null
        private static string? _softJam;         // jammer-pod-bucket entry name, or null
        private static WeaponStation? _lastActive;

        private static readonly List<WeaponSelectorLoadoutItem> _loadout = new List<WeaponSelectorLoadoutItem>(16);   // reused scratch

        // ── Classification ───────────────────────────────────────────────────────────────────────
        private static bool IsGun(WeaponInfo i)  => i.gun;
        private static bool IsBomb(WeaponInfo i) => i.bomb || i.glideBomb;
        // Missiles by flag, plus flagless launched ordnance (stock rockets set none of the class
        // flags). An 'energy' weapon would also fall in here if flagless — none known to exist as a
        // non-gun station; revisit the classification if one appears.
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
        // case). Names must match the live WeaponInfo name exactly (Encyclopedia/UnitDefinition names,
        // _scratch/units.json); "IRM-S1" hasn't turned up in a loadout yet and is still provisional.
        private static readonly HashSet<string> AirToAirMissiles = new HashSet<string>(StringComparer.Ordinal)
        {
            "AAM-29 Scythe", "AAM-36 Scimitar", "IRM-S2", "MMR-S3", "IRM-S1",
        };
        internal static bool IsAirToAir(WeaponInfo i) => AirToAirMissiles.Contains(EntryName(i));

        internal static string EntryName(WeaponInfo info) =>
            !string.IsNullOrEmpty(info.weaponName) ? info.weaponName : info.shortName;

        // ── Cycle keys ───────────────────────────────────────────────────────────────────────────
        // Each cycle key SELECTS (not just highlights): with the active weapon already in this class
        // it advances to the next entry and makes it active; with another class active it recalls the
        // class's remembered soft selection (or the first entry) and makes THAT active — so the first
        // press is "switch into this class where I left it", the second press cycles.
        public static void CycleGun(Aircraft ac)     => _softGun = CycleAndSelect(ac, WeaponSelectorBucket.Gun, _softGun);

        // Combat mode (docs/radar-master-arms.md, issue #32) narrows which missiles Cycle Missile
        // reaches: unrestricted in ALL, air-to-air-only or air-to-ground-only otherwise. Guns are
        // unaffected by combat mode (see CycleGun above); bombs are handled below.
        public static void CycleMissile(Aircraft ac) => _softRel = CycleAndSelect(ac, WeaponSelectorBucket.Missile, _softRel);

        // Bombs are disabled entirely while in A/A mode — no-op, doesn't touch _softRel.
        public static void CycleBomb(Aircraft ac)
        {
            if (ImmersionState.CombatMode == CombatMode.AirToAir) return;
            _softRel = CycleAndSelect(ac, WeaponSelectorBucket.Bomb, _softRel);
        }

        // Same restriction, applied to Weapon Release's own switch-then-fire stage: a GUN selected
        // in A/G mode with only A/A missiles loaded must not switch to one on Weapon Release.
        // Bombs stay excluded entirely in A/A (mirrors CycleBomb's own no-op there); a Bomb already
        // selected when entering A/A is handled separately by OnCombatModeChanged.
        private static string? CycleAndSelect(Aircraft ac, WeaponSelectorBucket bucket, string? soft)
        {
            Follow(ac);
            BuildLoadout(ac);

            WeaponManager wm = ac.weaponManager;
            WeaponStation? cur = wm != null ? wm.currentWeaponStation : null;
            string? curName = cur != null && cur.WeaponInfo != null ? EntryName(cur.WeaponInfo) : null;

            // Cycling never lands on a depleted entry (fire keys can still commit one — the game
            // allows selecting an empty weapon; Ready() just won't fire it).
            WeaponSelectorCycleResult result = WeaponSelectorLogic.Cycle(_loadout, bucket, CurrentMode(), curName, soft);
            if (result.TargetName == null) return result.SoftName;   // wrong class, empty, or depleted — no-op

            WeaponStation? target = FindStationByName(ac, result.TargetName);
            if (target != null && !ReferenceEquals(cur, target))
            {
                SelectStation(ac, target);
                _lastActive = target;   // our own commit is not a pilot change — don't re-snap on it
            }
            return result.SoftName;
        }

        // ── Fire keys ────────────────────────────────────────────────────────────────────────────
        // Both are HELD-driven with a two-stage cross-class press: if the active weapon isn't this
        // key's effective selection, the press only SWITCHES to it (bringing up the right reticle) and
        // that same hold never fires — release and press again to fire, then hold to keep firing
        // (WeaponManager.Fire() every frame; WeaponStation.Ready() rate-limits, matching the stock
        // trigger's hold behaviour, guns-linked included).
        //
        // Press-vs-hold is derived from Time.frameCount continuity: the drive runs every held frame,
        // so a gap in frames = a fresh press. Misses a release+re-press within one frame — physically
        // impossible on a real key.
        private static int  _gunFrame = -10, _relFrame = -10, _relSingleFrame = -10, _jamFrame = -10;
        private static bool _gunSwitchHold,  _relSwitchHold,  _relSingleSwitchHold,  _jamSwitchHold;

        public static void FireGun(Aircraft ac)       => Fire(ac, EffectiveGun(ac),       ref _gunFrame, ref _gunSwitchHold, wm => wm.Fire());
        public static void FireRelease(Aircraft ac)   => Fire(ac, EffectiveRelease(ac),   ref _relFrame, ref _relSwitchHold, wm => wm.Fire());
        public static void FireJammerPod(Aircraft ac) => Fire(ac, EffectiveJammerPod(ac), ref _jamFrame, ref _jamSwitchHold, wm => wm.Fire());

        // Single Target Weapon Release (issue #68): same two-stage switch-then-fire as FireRelease
        // above, sharing its EffectiveRelease selection (so both binds agree on "which weapon"), but
        // its own frame/switchHold pair — a distinct keybind a pilot can press without having
        // pressed Weapon Release first, so it needs its own independent press/hold tracking rather
        // than one shared with FireRelease's. Commits via FireSingleAtFocused instead of the stock
        // wm.Fire(), which — with 2+ locks — fires one round per lock in a staggered salvo
        // (WeaponManager.Fire(), _scratch/full/WeaponManager.cs); this always fires exactly one
        // round, and only ever at the focused lock.
        public static void FireReleaseSingle(Aircraft ac) =>
            Fire(ac, EffectiveRelease(ac), ref _relSingleFrame, ref _relSingleSwitchHold, wm => FireSingleAtFocused(ac, wm));

        // The entry a fire key would commit right now: the soft selection if it's still a live entry
        // of the class, else the first entry of the class, else null. Also what the WPN page outlines.
        public static string? EffectiveGun(Aircraft ac)       => Effective(ac, WeaponSelectorBucket.Gun,       _softGun);
        public static string? EffectiveRelease(Aircraft ac)   => Effective(ac, WeaponSelectorBucket.Release,   _softRel);
        public static string? EffectiveJammerPod(Aircraft ac) => Effective(ac, WeaponSelectorBucket.JammerPod, _softJam);

        private static string? Effective(Aircraft ac, WeaponSelectorBucket bucket, string? soft)
        {
            Follow(ac);
            BuildLoadout(ac);
            return WeaponSelectorLogic.Effective(_loadout, bucket, CurrentMode(), soft);
        }

        // `commit` is what stage 2 (already on the right weapon) actually does, so the
        // switch-then-fire arbitration below stays shared instead of copy-pasted per bind: wm.Fire()
        // for the three stock binds — the stock trigger's own entry point, covering safety,
        // guns-linked, salvo, and the network path in one call — or FireSingleAtFocused for issue
        // #68's, which reimplements just enough of that to redirect the target.
        private static void Fire(Aircraft ac, string? name, ref int lastFrame, ref bool switchHold, Action<WeaponManager> commit)
        {
            bool fresh = UnityEngine.Time.frameCount != lastFrame + 1;   // gap = new press
            lastFrame = UnityEngine.Time.frameCount;
            if (fresh) switchHold = false;

            WeaponManager wm = ac.weaponManager;
            if (wm == null || name == null) return;

            WeaponStation? cur = wm.currentWeaponStation;
            string? curName = cur != null && cur.WeaponInfo != null ? EntryName(cur.WeaponInfo) : null;
            if (!string.Equals(curName, name, StringComparison.Ordinal))
            {
                WeaponStation? target = FindStationByName(ac, name);
                if (target == null) return;
                SelectStation(ac, target);
                _lastActive = target;   // our own commit is not a pilot change — don't re-snap on it
                switchHold = true;      // stage 1: this press switched; it must not also fire
                return;
            }
            if (switchHold) return;     // still the hold that did the switch
            commit(wm);
        }

        // Single-target counterpart to WeaponManager.Fire() (_scratch/full/WeaponManager.cs, issue
        // #68): mirrors its guard chain and its single-target branches (Fire() for a zero-interval/
        // sling weapon, LaunchMount() otherwise) exactly, but resolves the target to TargetFocus.Id
        // and always takes that single-target path — never the stock trigger's own
        // `targetList.Count > 1` branch, which staggers one round across every locked target
        // instead of just the one the pilot is focused on. Guns are never reached here (this only
        // ever runs via FireReleaseSingle, whose EffectiveRelease selection excludes the gun
        // bucket), so there's no need to replicate Fire()'s own gun/guns-linked special case.
        private static void FireSingleAtFocused(Aircraft ac, WeaponManager wm)
        {
            WeaponStation? ws = wm.currentWeaponStation;
            if (ws == null || ws.SafetyIsOn(ac) || ac.weaponStations.Count == 0) return;
            if (ac.remoteSim || !ws.Ready() || ws.SalvoInProgress) return;

            // Reconcile (TargetFocus.cs) only ever leaves this 0 when nothing is locked at all, so
            // there's genuinely nothing to release at rather than an unpicked default to fall back to.
            uint focusedId = TargetFocus.Id;
            if (focusedId == 0) return;
            if (!UnitRegistry.TryGetUnit(new PersistentID { Id = focusedId }, out Unit target) ||
                target == null || target.disabled)
                return;
            if (!wm.GetTargetList().Contains(target)) return;   // focus stale relative to this station's own locks

            if (ws.WeaponInfo.fireInterval == 0f || ws.WeaponInfo.sling)
                ws.Fire(ac, target);
            else
                ws.LaunchMount(ac, target, ac.GlobalPosition() + ac.transform.forward * 50000f);
        }

        // ── Follow the active selection ──────────────────────────────────────────────────────────
        private static void Follow(Aircraft ac)
        {
            WeaponManager wm = ac.weaponManager;
            if (wm == null) return;
            WeaponStation? cur = wm.currentWeaponStation;
            if (ReferenceEquals(cur, _lastActive)) return;
            _lastActive = cur;
            WeaponInfo? info = cur != null ? cur.WeaponInfo : null;
            if (info == null || info.hideInDisplay) return;
            if (IsGun(info))            _softGun = EntryName(info);
            else if (IsRelease(info))   _softRel = EntryName(info);
            else if (IsJammerPod(info)) _softJam = EntryName(info);
        }

        // ── Combat-mode auto-switch ──────────────────────────────────────────────────────────────
        // Entering A/A while a bomb or A/G missile is selected snaps to the first available A/A
        // missile, and entering A/G while an A/A missile is selected snaps to the first available
        // A/G missile — falling back to a bomb if no A/G missile has ammo — (docs/radar-master-arms.md,
        // issue #32) — a combat-mode press shouldn't leave the pilot lined up on a weapon it just
        // disabled. If nothing in the new mode has ammo at all, falls back to the first gun — always
        // fireable, combat mode or not. Guns are exempt as the CURRENT selection (combat mode never
        // touches a gun that's already selected, matching CycleGun's own independence from it);
        // anything else already valid for the new mode (a bomb entering A/G, a jammer pod, an
        // already-matching missile) is left alone.
        internal static void OnCombatModeChanged(Aircraft ac, CombatMode mode)
        {
            Follow(ac);
            WeaponManager wm = ac.weaponManager;
            WeaponStation? cur = wm != null ? wm.currentWeaponStation : null;
            WeaponInfo? info = cur != null ? cur.WeaponInfo : null;
            if (info == null || IsGun(info)) return;

            if (mode == CombatMode.AirToAir && (IsBomb(info) || (IsMissile(info) && !IsAirToAir(info))))
            {
                if (!SelectFirstAvailable(ac, WeaponSelectorBucket.Missile, WeaponSelectorCombatMode.AirToAir))
                    SelectFirstAvailable(ac, WeaponSelectorBucket.Gun, WeaponSelectorCombatMode.All);
            }
            else if (mode == CombatMode.AirToGround && IsMissile(info) && IsAirToAir(info))
            {
                if (!SelectFirstAvailable(ac, WeaponSelectorBucket.Missile, WeaponSelectorCombatMode.AirToGround) &&
                    !SelectFirstAvailable(ac, WeaponSelectorBucket.Bomb, WeaponSelectorCombatMode.AirToGround))
                    SelectFirstAvailable(ac, WeaponSelectorBucket.Gun, WeaponSelectorCombatMode.All);
            }
        }

        // Selects the first class entry with ammo — not "next after current" like CycleAndSelect,
        // since there's no meaningful "current" once the active weapon sits outside the new mode's
        // allowed set. Returns false (no-op) if the class is empty or fully depleted, so callers can
        // fall back to the next class in line.
        private static bool SelectFirstAvailable(Aircraft ac, WeaponSelectorBucket bucket, WeaponSelectorCombatMode mode)
        {
            BuildLoadout(ac);
            string? name = WeaponSelectorLogic.FirstAvailable(_loadout, bucket, mode);
            if (name == null) return false;

            WeaponStation? target = FindStationByName(ac, name);
            if (target == null) return false;
            SelectStation(ac, target);
            _lastActive = target;
            SnapSoft(target.WeaponInfo, name);
            return true;
        }

        // Mirrors Follow's own classification — sets whichever soft selector matches the newly
        // selected entry's class, same as a pilot manually switching to it would.
        private static void SnapSoft(WeaponInfo info, string name)
        {
            if (IsGun(info))            _softGun = name;
            else if (IsRelease(info))   _softRel = name;
            else if (IsJammerPod(info)) _softJam = name;
        }

        // ── Shared station lookup + select (also used by CommandDispatcher.WeaponSelect) ─────────
        // First visible station whose entry name matches — the same aggregation BuildLoadout uses;
        // the game cycles duplicate stations of one type itself.
        internal static WeaponStation? FindStationByName(Aircraft ac, string name)
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

        private static WeaponSelectorCombatMode CurrentMode()
        {
            switch (ImmersionState.CombatMode)
            {
                case CombatMode.AirToAir: return WeaponSelectorCombatMode.AirToAir;
                case CombatMode.AirToGround: return WeaponSelectorCombatMode.AirToGround;
                default: return WeaponSelectorCombatMode.All;
            }
        }

        // Live-game adapter: turns weapon stations into a plain loadout list for the pure selector
        // logic. Aggregation and combat-mode filtering happen in WeaponSelectorLogic, so this layer
        // stays responsible only for reading WeaponInfo flags and preserving station order.
        private static void BuildLoadout(Aircraft ac)
        {
            _loadout.Clear();
            if (ac.weaponStations == null) return;
            foreach (WeaponStation st in ac.weaponStations)
            {
                if (st == null) continue;
                WeaponInfo info = st.WeaponInfo;
                if (info == null || info.hideInDisplay) continue;
                string name = EntryName(info);
                if (string.IsNullOrEmpty(name)) continue;

                WeaponSelectorRole role;
                if (IsGun(info)) role = WeaponSelectorRole.Gun;
                else if (IsBomb(info)) role = WeaponSelectorRole.Bomb;
                else if (IsMissile(info)) role = WeaponSelectorRole.Missile;
                else if (IsJammerPod(info)) role = WeaponSelectorRole.JammerPod;
                else continue;

                _loadout.Add(new WeaponSelectorLoadoutItem(name, role, st.Ammo, IsAirToAir(info)));
            }
        }
    }
}
