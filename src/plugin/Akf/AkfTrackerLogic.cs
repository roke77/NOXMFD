using System;
using System.Collections.Generic;

namespace NOXMFD
{
    internal enum AkfKillKind
    {
        Aircraft,
        Ship,
        Vehicle,
        Building,
        Missile,
    }

    // Pure AKF session state. The game adapter supplies resolved names, hostility, ids, and
    // timestamps so this class can preserve the feed/tally invariants without Unity or game objects.
    internal sealed class AkfTrackerLogic<TId> where TId : notnull
    {
        public const int MaxFeedLines = 50;
        public const float WeaponTtl = 5f;

        private readonly List<AkfKillEntry> _allFeed = new List<AkfKillEntry>();
        private readonly List<AkfKillEntry> _playerFeed = new List<AkfKillEntry>();
        private readonly Dictionary<TId, WeaponHit> _lastWeaponByAttacker = new Dictionary<TId, WeaponHit>();

        private int _killsAircraft;
        private int _killsShip;
        private int _killsVehicle;
        private int _killsBuilding;
        private int _rank;

        private float _fundsGained;
        private float _fundsSpent;
        private float _prevFunds;
        private bool _fundsInit;

        public IReadOnlyList<AkfKillEntry> AllFeed => _allFeed;
        public IReadOnlyList<AkfKillEntry> PlayerFeed => _playerFeed;
        public int KillsAircraft => _killsAircraft;
        public int KillsShip => _killsShip;
        public int KillsVehicle => _killsVehicle;
        public int KillsBuilding => _killsBuilding;
        public int Rank => _rank;
        public float FundsGained => _fundsGained;
        public float FundsSpent => _fundsSpent;

        public void RecordKill(
            TId killerId,
            bool hasKiller,
            string? killerName,
            bool killerHostile,
            string victimName,
            bool victimHostile,
            AkfKillKind killedKind,
            string verb,
            float now,
            bool killerIsPlayer,
            bool victimIsPlayer,
            bool victimIsPlayerOrdnance)
        {
            string? weapon = null;
            if (hasKiller &&
                _lastWeaponByAttacker.TryGetValue(killerId, out WeaponHit hit) &&
                now - hit.Time <= WeaponTtl)
            {
                weapon = hit.Name;
            }

            var entry = new AkfKillEntry
            {
                Attacker = hasKiller ? killerName : null,
                AttackerHostile = hasKiller && killerHostile,
                Victim = victimName,
                VictimHostile = victimHostile,
                Verb = verb,
                Weapon = weapon,
            };
            AddCapped(_allFeed, entry);

            // The PLAYER feed contains outgoing player kills plus incoming interactions involving
            // the player's aircraft or fired ordnance; only outgoing kills affect the tally cards.
            if (killerIsPlayer)
            {
                AddCapped(_playerFeed, entry);
                CountPlayerKill(killedKind);
                return;
            }

            if (victimIsPlayer || victimIsPlayerOrdnance)
            {
                entry.PlayerIsVictim = true;
                AddCapped(_playerFeed, entry);
            }
        }

        public void RecordWeaponHit(TId dealerId, string weaponName, float now)
        {
            if (string.IsNullOrEmpty(weaponName)) return;
            _lastWeaponByAttacker[dealerId] = new WeaponHit(weaponName, now);
        }

        public void TickFunds(bool hasFunds, float current)
        {
            if (!hasFunds)
            {
                // A missing HQ breaks the diff chain; the next visible balance is a new baseline,
                // not a giant gain/loss accumulated while the local faction was unavailable.
                _fundsInit = false;
                return;
            }

            if (_fundsInit)
            {
                float delta = current - _prevFunds;
                if (delta > 0f) _fundsGained += delta;
                else if (delta < 0f) _fundsSpent += -delta;
            }

            _prevFunds = current;
            _fundsInit = true;
        }

        public void SetRank(int rank) => _rank = rank;

        private void CountPlayerKill(AkfKillKind kind)
        {
            switch (kind)
            {
                case AkfKillKind.Aircraft: _killsAircraft++; break;
                case AkfKillKind.Ship: _killsShip++; break;
                case AkfKillKind.Vehicle: _killsVehicle++; break;
                case AkfKillKind.Building: _killsBuilding++; break;
            }
        }

        private static void AddCapped(List<AkfKillEntry> list, AkfKillEntry entry)
        {
            list.Add(entry);
            if (list.Count > MaxFeedLines) list.RemoveAt(0);
        }

        private readonly struct WeaponHit
        {
            public readonly string Name;
            public readonly float Time;

            public WeaponHit(string name, float time)
            {
                Name = name;
                Time = time;
            }
        }
    }
}
