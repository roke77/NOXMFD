using System.Collections.Generic;
using NuclearOption.Networking;
using UnityEngine;

namespace NOXMFD
{
    // Session-scoped kill-feed/session-stat tracker (issue #34, docs/akf-page.md). A plain class
    // (not a MonoBehaviour) owned by TelemetryReader, so it lives and dies with it — TelemetryReader
    // is already mission-scoped (MissionLifecycle.StartReader/StopReader), which is exactly the
    // session-reset boundary this feature needs, with nothing extra to clear here.
    //
    // Fed by Harmony patches (HarmonyPatches.cs): kill events (MessageManager's kill-message RPC) and
    // best-effort weapon attribution (Missile.PenetrateObject / Missile.Detonate / DamageEffects.
    // BlastFrag — three entry points because a missile's terminal sequence can reach a kill through
    // any of them, at different points in the same frame) — see docs/akf-page.md for why each hook
    // was chosen. All patches are static and have no other way to reach the live mission's tracker,
    // hence the static Active pointer below.
    internal class AkfTracker
    {
        internal static AkfTracker? Active;

        private const int   MaxFeedLines = 50;
        private const float WeaponTtl    = 5f;   // seconds a "last fired weapon" stays attributable to a kill

        private readonly List<AkfKillEntry> _allFeed    = new List<AkfKillEntry>();
        private readonly List<AkfKillEntry> _playerFeed = new List<AkfKillEntry>();

        private int _killsAircraft, _killsShip, _killsVehicle, _killsBuilding;
        private int _rank;

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
        public int   Rank          => _rank;
        public float FundsGained   => _fundsGained;
        public float FundsSpent    => _fundsSpent;

        // Harmony postfix on MessageManager's kill-message handler calls this for every kill —
        // exactly the event that drives the game's own HUD kill-feed ticker (MessageUI.KillFeed).
        internal void RecordKill(PersistentID killerID, PersistentID killedID, KillType killedType)
        {
            // The only point a real kill message can be silently dropped from both feeds: the victim
            // must already be resolvable via UnitRegistry at kill-message time. Checked against a real
            // session's log (no drops observed) after an earlier report of missing kills turned out to
            // be the feed-overlap CSS bug instead (see akf.css's .akf-line flex fix).
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

            // Incoming interactions (not the player's own scored kills, so no tally changes — see
            // docs/akf-page.md): the player's own aircraft was destroyed, or a munition the player
            // fired was intercepted before reaching its target. Same entry shape as ALL (attacker is
            // whatever/whoever did this to the player, not the player), flagged so the page renders
            // it in full instead of the player-is-attacker abbreviated form.
            bool isPlayerVictim = localAc != null && killedID == localAc.persistentID;
            bool isPlayerOrdnance = !isPlayerVictim && killedType == KillType.Missile && localAc != null
                && killedUnit.unit is Missile firedMissile && firedMissile.ownerID == localAc.persistentID;
            if (isPlayerVictim || isPlayerOrdnance)
            {
                entry.PlayerIsVictim = true;
                AddCapped(_playerFeed, entry);
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
        //
        // ponytail: `hq.factionFunds` is the WHOLE FACTION's balance (the same field BDF/PAL read for
        // BOSCALI/PRIMEVA), not a per-player figure — Nuclear Option has no such thing. In solo play
        // this tracks 1:1 with the local player's own actions; in multiplayer any teammate's purchase
        // or AI-earned kill reward shows up here too, misattributed as "the player's own" gained/spent.
        // Known ceiling, accepted: there's no per-player funds figure in the game to read instead.
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

        // Polled once per 1 Hz scan (TelemetryReader.BuildAkf) — Player.PlayerRank is a live SyncVar,
        // not an event this tracker needs to react to; a snapshot read is all AKF's RANK card needs.
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
