namespace NOXMFD.Tests;

public sealed class AkfTrackerLogicTests
{
    [Fact]
    public void RecordKill_AddsAllFeedEntryAndTalliesPlayerKill()
    {
        var logic = new AkfTrackerLogic<int>();

        logic.RecordKill(
            killerId: 1,
            hasKiller: true,
            killerName: "Player",
            killerHostile: false,
            victimName: "Bandit",
            victimHostile: true,
            killedKind: AkfKillKind.Aircraft,
            verb: "downed",
            now: 10f,
            killerIsPlayer: true,
            victimIsPlayer: false,
            victimIsPlayerOrdnance: false);

        Assert.Single(logic.AllFeed);
        Assert.Single(logic.PlayerFeed);
        Assert.Equal(1, logic.KillsAircraft);
        Assert.Equal(0, logic.KillsShip);
        Assert.Equal("Player", logic.AllFeed[0].Attacker);
        Assert.Equal("Bandit", logic.AllFeed[0].Victim);
        Assert.True(logic.AllFeed[0].VictimHostile);
        Assert.False(logic.PlayerFeed[0].PlayerIsVictim);
    }

    [Fact]
    public void RecordKill_NoKillerKeepsAttackerFieldsEmptyAndDoesNotEnterPlayerFeed()
    {
        var logic = new AkfTrackerLogic<int>();

        logic.RecordKill(
            killerId: 0,
            hasKiller: false,
            killerName: null,
            killerHostile: false,
            victimName: "Pilot",
            victimHostile: false,
            killedKind: AkfKillKind.Aircraft,
            verb: "crashed",
            now: 1f,
            killerIsPlayer: false,
            victimIsPlayer: false,
            victimIsPlayerOrdnance: false);

        Assert.Single(logic.AllFeed);
        Assert.Empty(logic.PlayerFeed);
        Assert.Null(logic.AllFeed[0].Attacker);
        Assert.False(logic.AllFeed[0].AttackerHostile);
        Assert.Equal("crashed", logic.AllFeed[0].Verb);
    }

    [Fact]
    public void RecordKill_PlayerVictimAddsIncomingPlayerFeedLineWithoutTally()
    {
        var logic = new AkfTrackerLogic<int>();

        logic.RecordKill(
            killerId: 2,
            hasKiller: true,
            killerName: "SAM",
            killerHostile: true,
            victimName: "Player",
            victimHostile: false,
            killedKind: AkfKillKind.Aircraft,
            verb: "shot down",
            now: 4f,
            killerIsPlayer: false,
            victimIsPlayer: true,
            victimIsPlayerOrdnance: false);

        Assert.Single(logic.AllFeed);
        Assert.Single(logic.PlayerFeed);
        Assert.True(logic.PlayerFeed[0].PlayerIsVictim);
        Assert.Equal(0, logic.KillsAircraft);
    }

    [Fact]
    public void RecordKill_PlayerOrdnanceInterceptAddsIncomingPlayerFeedLineWithoutTally()
    {
        var logic = new AkfTrackerLogic<int>();

        logic.RecordKill(
            killerId: 2,
            hasKiller: true,
            killerName: "Interceptor",
            killerHostile: true,
            victimName: "Player Missile",
            victimHostile: false,
            killedKind: AkfKillKind.Missile,
            verb: "intercepted",
            now: 4f,
            killerIsPlayer: false,
            victimIsPlayer: false,
            victimIsPlayerOrdnance: true);

        Assert.Single(logic.PlayerFeed);
        Assert.True(logic.PlayerFeed[0].PlayerIsVictim);
        Assert.Equal(0, logic.KillsAircraft);
        Assert.Equal(0, logic.KillsVehicle);
    }

    [Fact]
    public void RecordWeaponHit_AttributesWeaponWithinTtl()
    {
        var logic = new AkfTrackerLogic<int>();

        logic.RecordWeaponHit(dealerId: 7, weaponName: "AGM-48", now: 10f);
        logic.RecordKill(
            killerId: 7,
            hasKiller: true,
            killerName: "Player",
            killerHostile: false,
            victimName: "Vehicle",
            victimHostile: true,
            killedKind: AkfKillKind.Vehicle,
            verb: "destroyed",
            now: 14.9f,
            killerIsPlayer: true,
            victimIsPlayer: false,
            victimIsPlayerOrdnance: false);

        Assert.Equal("AGM-48", logic.AllFeed[0].Weapon);
    }

    [Fact]
    public void RecordWeaponHit_DoesNotAttributeWeaponAfterTtl()
    {
        var logic = new AkfTrackerLogic<int>();

        logic.RecordWeaponHit(dealerId: 7, weaponName: "AGM-48", now: 10f);
        logic.RecordKill(
            killerId: 7,
            hasKiller: true,
            killerName: "Player",
            killerHostile: false,
            victimName: "Vehicle",
            victimHostile: true,
            killedKind: AkfKillKind.Vehicle,
            verb: "destroyed",
            now: 15.1f,
            killerIsPlayer: true,
            victimIsPlayer: false,
            victimIsPlayerOrdnance: false);

        Assert.Null(logic.AllFeed[0].Weapon);
    }

    [Fact]
    public void RecordWeaponHit_ReplacesLastWeaponForAttacker()
    {
        var logic = new AkfTrackerLogic<int>();

        logic.RecordWeaponHit(dealerId: 7, weaponName: "AGM-48", now: 10f);
        logic.RecordWeaponHit(dealerId: 7, weaponName: "RKT", now: 11f);
        logic.RecordKill(
            killerId: 7,
            hasKiller: true,
            killerName: "Player",
            killerHostile: false,
            victimName: "Vehicle",
            victimHostile: true,
            killedKind: AkfKillKind.Vehicle,
            verb: "destroyed",
            now: 12f,
            killerIsPlayer: true,
            victimIsPlayer: false,
            victimIsPlayerOrdnance: false);

        Assert.Equal("RKT", logic.AllFeed[0].Weapon);
    }

    [Fact]
    public void FeedsAreCappedToMostRecentLines()
    {
        var logic = new AkfTrackerLogic<int>();

        for (int i = 0; i < AkfTrackerLogic<int>.MaxFeedLines + 3; i++)
        {
            logic.RecordKill(
                killerId: 1,
                hasKiller: true,
                killerName: "Player",
                killerHostile: false,
                victimName: "Victim " + i,
                victimHostile: true,
                killedKind: AkfKillKind.Vehicle,
                verb: "destroyed",
                now: i,
                killerIsPlayer: true,
                victimIsPlayer: false,
                victimIsPlayerOrdnance: false);
        }

        Assert.Equal(AkfTrackerLogic<int>.MaxFeedLines, logic.AllFeed.Count);
        Assert.Equal(AkfTrackerLogic<int>.MaxFeedLines, logic.PlayerFeed.Count);
        Assert.Equal("Victim 3", logic.AllFeed[0].Victim);
        Assert.Equal("Victim 52", logic.AllFeed[^1].Victim);
    }

    [Fact]
    public void TickFunds_FirstReadOnlyInitializesThenBucketsDeltas()
    {
        var logic = new AkfTrackerLogic<int>();

        logic.TickFunds(hasFunds: true, current: 100f);
        logic.TickFunds(hasFunds: true, current: 125.5f);
        logic.TickFunds(hasFunds: true, current: 90f);

        Assert.Equal(25.5f, logic.FundsGained, precision: 3);
        Assert.Equal(35.5f, logic.FundsSpent, precision: 3);
    }

    [Fact]
    public void TickFunds_MissingFundsResetsBaselineWithoutCountingGap()
    {
        var logic = new AkfTrackerLogic<int>();

        logic.TickFunds(hasFunds: true, current: 100f);
        logic.TickFunds(hasFunds: false, current: 0f);
        logic.TickFunds(hasFunds: true, current: 500f);
        logic.TickFunds(hasFunds: true, current: 450f);

        Assert.Equal(0f, logic.FundsGained, precision: 3);
        Assert.Equal(50f, logic.FundsSpent, precision: 3);
    }

    [Fact]
    public void SetRankStoresLatestRank()
    {
        var logic = new AkfTrackerLogic<int>();

        logic.SetRank(4);
        logic.SetRank(7);

        Assert.Equal(7, logic.Rank);
    }

    [Fact]
    public void PlayerTalliesOnlyCountKillCategoriesNotMissiles()
    {
        var logic = new AkfTrackerLogic<int>();

        logic.RecordKill(1, true, "Player", false, "Jet", true, AkfKillKind.Aircraft, "downed", 1f, true, false, false);
        logic.RecordKill(1, true, "Player", false, "Ship", true, AkfKillKind.Ship, "sank", 2f, true, false, false);
        logic.RecordKill(1, true, "Player", false, "Truck", true, AkfKillKind.Vehicle, "destroyed", 3f, true, false, false);
        logic.RecordKill(1, true, "Player", false, "Hangar", true, AkfKillKind.Building, "destroyed", 4f, true, false, false);
        logic.RecordKill(1, true, "Player", false, "Missile", true, AkfKillKind.Missile, "intercepted", 5f, true, false, false);

        Assert.Equal(1, logic.KillsAircraft);
        Assert.Equal(1, logic.KillsShip);
        Assert.Equal(1, logic.KillsVehicle);
        Assert.Equal(1, logic.KillsBuilding);
        Assert.Equal(5, logic.PlayerFeed.Count);
    }
}
