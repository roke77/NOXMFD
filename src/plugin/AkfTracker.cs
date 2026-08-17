using System.Collections.Generic;
using UnityEngine;

namespace NOXMFD
{
    // Session-scoped kill-feed/session-stat tracker (issue #34, docs/akf-page.md). A plain class
    // (not a MonoBehaviour) owned by TelemetryReader, so it lives and dies with it — TelemetryReader
    // is already mission-scoped (MissionLifecycle.StartReader/StopReader), which is exactly the
    // session-reset boundary this feature needs, with nothing extra to clear here.
    //
    // Fed by two Harmony patches (HarmonyPatches.cs): kill events (MessageManager's kill-message RPC)
    // and best-effort weapon attribution (DamageEffects.BlastFrag) — see docs/akf-page.md for why
    // each hook was chosen. Both patches are static and have no other way to reach the live
    // mission's tracker, hence the static Active pointer below.
    internal class AkfTracker
    {
        internal static AkfTracker? Active;

        private const int   MaxFeedLines = 50;
        private const float WeaponTtl    = 5f;   // seconds a "last fired weapon" stays attributable to a kill

        private readonly List<AkfKillEntry> _allFeed    = new List<AkfKillEntry>();
        private readonly List<AkfKillEntry> _playerFeed = new List<AkfKillEntry>();

        private int   _killsAircraft, _killsShip, _killsVehicle, _killsBuilding;
        private float _valueLost;

        private float _fundsGained, _fundsSpent, _prevFunds;
        private bool  _fundsInit;

        // dealerID -> (weapon unitName, Time.unscaledTime it was recorded). See RecordWeaponHit.
        private readonly Dictionary<PersistentID, (string Name, float Time)> _lastWeaponByAttacker =
            new Dictionary<PersistentID, (string, float)>();

        public IReadOnlyList<AkfKillEntry> AllFeed    => _allFeed;
        public IReadOnlyList<AkfKillEntry> PlayerFeed => _playerFeed;
        public int   KillsAircraft => _killsAircraft;
        public int   KillsShip     => _killsShip;
        public int   KillsVehicle  => _killsVehicle;
        public int   KillsBuilding => _killsBuilding;
        public float ValueLost     => _valueLost;
        public float FundsGained   => _fundsGained;
        public float FundsSpent    => _fundsSpent;

        // Harmony postfix on MessageManager's kill-message handler calls this for every kill —
        // exactly the event that drives the game's own HUD kill-feed ticker (MessageUI.KillFeed).
        internal void RecordKill(PersistentID killerID, PersistentID killedID, KillType killedType)
        {
            if (!UnitRegistry.TryGetPersistentUnit(killedID, out PersistentUnit killedUnit) || killedUnit == null) return;

            bool hasKiller = UnitRegistry.TryGetPersistentUnit(killerID, out PersistentUnit killerUnit) && killerUnit != null;
            string verb = killedType.GetVerb(hasKiller);

            GameManager.GetLocalHQ(out FactionHQ localHq);
            bool victimHostile = killedUnit.GetHQ() != localHq;

            string? weapon = null;
            if (hasKiller && _lastWeaponByAttacker.TryGetValue(killerID, out var hit)
                && Time.unscaledTime - hit.Time <= WeaponTtl)
                weapon = hit.Name;

            var entry = new AkfKillEntry
            {
                Attacker        = hasKiller ? killerUnit.unitName : null,
                AttackerHostile = hasKiller && killerUnit.GetHQ() != localHq,
                Victim          = killedUnit.unitName,
                VictimHostile   = victimHostile,
                Verb            = verb,
                Weapon          = weapon,
            };
            AddCapped(_allFeed, entry);

            GameManager.GetLocalAircraft(out Aircraft localAc);
            bool isPlayerKill = hasKiller && localAc != null && killerID == localAc.persistentID;
            if (!isPlayerKill) return;

            AddCapped(_playerFeed, entry);
            if (killedType != KillType.Missile && killedUnit.definition != null)
                _valueLost += killedUnit.definition.value;
            switch (killedType)
            {
                case KillType.Aircraft: _killsAircraft++; break;
                case KillType.Ship:     _killsShip++;     break;
                case KillType.Vehicle:  _killsVehicle++;  break;
                case KillType.Building: _killsBuilding++; break;
            }
        }

        // Harmony prefix on DamageEffects.BlastFrag — see docs/akf-page.md's "Weapon attribution"
        // section for why this is a last-fired-by-attacker heuristic, not per-victim tracking:
        // BlastFrag receives the detonating missile's own PersistentID (missileID) alongside the
        // attacking aircraft's (dealerID) but never uses it for anything else.
        internal void RecordWeaponHit(PersistentID dealerID, PersistentID missileID)
        {
            if (!UnitRegistry.TryGetPersistentUnit(missileID, out PersistentUnit missileUnit) || missileUnit == null) return;
            string name = missileUnit.definition != null ? missileUnit.definition.unitName : missileUnit.unitName;
            if (string.IsNullOrEmpty(name)) return;
            _lastWeaponByAttacker[dealerID] = (name, Time.unscaledTime);
        }

        // Polled once per 1 Hz scan (TelemetryReader.BuildAkf) — funds aren't an event, just a value
        // that drifts; diff against the previous read and bucket the delta into GAINED or SPENT.
        internal void TickFunds()
        {
            if (!GameManager.GetLocalHQ(out FactionHQ hq) || hq == null) { _fundsInit = false; return; }
            float current = hq.factionFunds;
            if (_fundsInit)
            {
                float delta = current - _prevFunds;
                if (delta > 0f) _fundsGained += delta;
                else if (delta < 0f) _fundsSpent += -delta;
            }
            _prevFunds = current;
            _fundsInit = true;
        }

        private static void AddCapped(List<AkfKillEntry> list, AkfKillEntry entry)
        {
            list.Add(entry);
            if (list.Count > MaxFeedLines) list.RemoveAt(0);
        }
    }
}
