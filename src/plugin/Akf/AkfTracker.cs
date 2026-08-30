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

        private readonly AkfTrackerLogic<PersistentID> _logic = new AkfTrackerLogic<PersistentID>();

        public IReadOnlyList<AkfKillEntry> AllFeed    => _logic.AllFeed;
        public IReadOnlyList<AkfKillEntry> PlayerFeed => _logic.PlayerFeed;
        public int   KillsAircraft => _logic.KillsAircraft;
        public int   KillsShip     => _logic.KillsShip;
        public int   KillsVehicle  => _logic.KillsVehicle;
        public int   KillsBuilding => _logic.KillsBuilding;
        public int   Rank          => _logic.Rank;
        public float FundsGained   => _logic.FundsGained;
        public float FundsSpent    => _logic.FundsSpent;

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

            GameManager.GetLocalAircraft(out Aircraft localAc);
            bool isPlayerKill = hasKiller && localAc != null && killerID == localAc.persistentID;

            // Incoming interactions — the player's aircraft was destroyed, or a munition the player
            // fired was intercepted — don't change the tally. PlayerIsVictim flags the entry so the
            // page renders it in full instead of the player-is-attacker abbreviated form.
            bool isPlayerVictim = localAc != null && killedID == localAc.persistentID;
            bool isPlayerOrdnance = !isPlayerVictim && killedType == KillType.Missile && localAc != null
                && killedUnit.unit is Missile firedMissile && firedMissile.ownerID == localAc.persistentID;

            _logic.RecordKill(
                killerID,
                hasKiller,
                killerUnit != null ? killerUnit.unitName : null,
                killerUnit != null && killerUnit.GetHQ() != localHq,
                killedUnit.unitName,
                victimHostile,
                ToKind(killedType),
                verb,
                Time.unscaledTime,
                isPlayerKill,
                isPlayerVictim,
                isPlayerOrdnance);
        }

        // Called via Harmony prefix on DamageEffects.BlastFrag. This is a last-fired-by-attacker
        // heuristic, not per-victim tracking: BlastFrag carries the detonating missile's own
        // PersistentID (missileID) alongside the attacker's (dealerID) but never uses it otherwise.
        internal void RecordWeaponHit(PersistentID dealerID, PersistentID missileID)
        {
            if (!UnitRegistry.TryGetPersistentUnit(missileID, out PersistentUnit missileUnit) || missileUnit == null) return;
            string name = missileUnit.definition != null ? missileUnit.definition.unitName : missileUnit.unitName;
            _logic.RecordWeaponHit(dealerID, name, Time.unscaledTime);
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
            bool hasFunds = GameManager.GetLocalHQ(out FactionHQ hq) && hq != null;
            float current = hasFunds ? hq!.factionFunds : 0f;
            _logic.TickFunds(hasFunds, current);
        }

        // Player.PlayerRank is a live SyncVar, not an event this tracker needs to react to; a
        // snapshot read on each 1 Hz scan is all AKF's RANK card needs.
        internal void TickRank()
        {
            _logic.SetRank(GameManager.GetLocalPlayer<Player>(out var player) ? player.PlayerRank : 0);
        }

        private static AkfKillKind ToKind(KillType type)
        {
            switch (type)
            {
                case KillType.Aircraft: return AkfKillKind.Aircraft;
                case KillType.Ship: return AkfKillKind.Ship;
                case KillType.Vehicle: return AkfKillKind.Vehicle;
                case KillType.Building: return AkfKillKind.Building;
                default: return AkfKillKind.Missile;
            }
        }
    }
}
