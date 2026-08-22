using System.Collections.Generic;
using NuclearOption.Networking;
using UnityEngine;

namespace NOXMFD
{
    // A plain class (not a MonoBehaviour), owned by TelemetryReader so it lives and dies with the
    // mission-scoped reader (MissionLifecycle.StartReader/StopReader) — exactly the session-reset
    // boundary this feature needs.
    //
    // Fed by Harmony patches (HarmonyPatches.cs): kill events and best-effort weapon attribution
    // from three missile terminal-sequence entry points, since a kill can reach any of them first,
    // in the same frame. Patches are static and have no other way to reach the live tracker, hence
    // the static Active pointer below.
    internal class AkfTracker
    {
        internal static AkfTracker? Active;

        private const int   MaxFeedLines = 50;
        private const float WeaponTtl    = 5f;   // window a recorded weapon hit stays attributable to a later kill

        private readonly List<AkfKillEntry> _allFeed    = new List<AkfKillEntry>();
        private readonly List<AkfKillEntry> _playerFeed = new List<AkfKillEntry>();

        private int _killsAircraft, _killsShip, _killsVehicle, _killsBuilding;
        private int _rank;

        private float _fundsGained, _fundsSpent, _prevFunds;
        private bool  _fundsInit;

        // dealerID -> (weapon unitName, time recorded). Populated by RecordWeaponHit.
        private readonly Dictionary<PersistentID, (string Name, float Time)> _lastWeaponByAttacker =
            new Dictionary<PersistentID, (string, float)>();

        public IReadOnlyList<AkfKillEntry> AllFeed    => _allFeed;
        public IReadOnlyList<AkfKillEntry> PlayerFeed => _playerFeed;
        public int   KillsAircraft => _killsAircraft;
        public int   KillsShip     => _killsShip;
        public int   KillsVehicle  => _killsVehicle;
        public int   KillsBuilding => _killsBuilding;
        public int   Rank          => _rank;
        public float FundsGained   => _fundsGained;
        public float FundsSpent    => _fundsSpent;

        // Called via Harmony postfix for every kill message — the same event that drives the game's
        // own HUD kill-feed ticker (MessageUI.KillFeed).
        internal void RecordKill(PersistentID killerID, PersistentID killedID, KillType killedType)
        {
            // Drops the kill from both feeds if the victim isn't resolvable via UnitRegistry at
            // kill-message time — the only way a real kill message goes missing here.
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
            if (isPlayerKill)
            {
                AddCapped(_playerFeed, entry);
                switch (killedType)
                {
                    case KillType.Aircraft: _killsAircraft++; break;
                    case KillType.Ship:     _killsShip++;     break;
                    case KillType.Vehicle:  _killsVehicle++;  break;
                    case KillType.Building: _killsBuilding++; break;
                }
                return;
            }

            // Incoming interactions — the player's aircraft was destroyed, or a munition the player
            // fired was intercepted — don't change the tally. PlayerIsVictim flags the entry so the
            // page renders it in full instead of the player-is-attacker abbreviated form.
            bool isPlayerVictim = localAc != null && killedID == localAc.persistentID;
            bool isPlayerOrdnance = !isPlayerVictim && killedType == KillType.Missile && localAc != null
                && killedUnit.unit is Missile firedMissile && firedMissile.ownerID == localAc.persistentID;
            if (isPlayerVictim || isPlayerOrdnance)
            {
                entry.PlayerIsVictim = true;
                AddCapped(_playerFeed, entry);
            }
        }

        // Called via Harmony prefix on DamageEffects.BlastFrag. This is a last-fired-by-attacker
        // heuristic, not per-victim tracking: BlastFrag carries the detonating missile's own
        // PersistentID (missileID) alongside the attacker's (dealerID) but never uses it otherwise.
        internal void RecordWeaponHit(PersistentID dealerID, PersistentID missileID)
        {
            if (!UnitRegistry.TryGetPersistentUnit(missileID, out PersistentUnit missileUnit) || missileUnit == null) return;
            string name = missileUnit.definition != null ? missileUnit.definition.unitName : missileUnit.unitName;
            if (string.IsNullOrEmpty(name)) return;
            _lastWeaponByAttacker[dealerID] = (name, Time.unscaledTime);
        }

        // Polled once per 1 Hz scan (TelemetryReader.BuildAkf) — funds aren't an event, just a value
        // that drifts; diff against the previous read and bucket the delta into GAINED or SPENT.
        //
        // `hq.factionFunds` is the WHOLE FACTION's balance, not a per-player figure — the game has no
        // such thing. In solo play this tracks 1:1 with the player's own actions; in multiplayer any
        // teammate's purchase or AI-earned kill reward shows up here too, misattributed as the
        // player's own gained/spent. There's no per-player funds figure in the game to read instead.
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

        // Player.PlayerRank is a live SyncVar, not an event this tracker needs to react to; a
        // snapshot read on each 1 Hz scan is all AKF's RANK card needs.
        internal void TickRank()
        {
            _rank = GameManager.GetLocalPlayer<Player>(out var player) ? player.PlayerRank : 0;
        }

        private static void AddCapped(List<AkfKillEntry> list, AkfKillEntry entry)
        {
            list.Add(entry);
            if (list.Count > MaxFeedLines) list.RemoveAt(0);
        }
    }
}
