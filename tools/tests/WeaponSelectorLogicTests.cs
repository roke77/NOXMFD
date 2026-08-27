namespace NOXMFD.Tests;

public sealed class WeaponSelectorLogicTests
{
    [Fact]
    public void Cycle_InClass_AdvancesToNextEntryWithAmmo()
    {
        WeaponSelectorLoadoutItem[] loadout =
        [
            Missile("AGM-48", 2),
            Missile("RKT", 12),
        ];

        WeaponSelectorCycleResult result = WeaponSelectorLogic.Cycle(
            loadout,
            WeaponSelectorBucket.Missile,
            WeaponSelectorCombatMode.All,
            currentName: "AGM-48",
            softName: "AGM-48");

        Assert.Equal("RKT", result.SoftName);
        Assert.Equal("RKT", result.TargetName);
    }

    [Fact]
    public void Cycle_InClass_SkipsDepletedEntriesAndWraps()
    {
        WeaponSelectorLoadoutItem[] loadout =
        [
            Missile("AGM-48", 2),
            Missile("RKT", 0),
            Missile("AGM-68", 4),
        ];

        WeaponSelectorCycleResult result = WeaponSelectorLogic.Cycle(
            loadout,
            WeaponSelectorBucket.Missile,
            WeaponSelectorCombatMode.All,
            currentName: "AGM-48",
            softName: "AGM-48");

        Assert.Equal("AGM-68", result.SoftName);
        Assert.Equal("AGM-68", result.TargetName);
    }

    [Fact]
    public void Cycle_CrossClass_RecallsSoftEntryWhenStillLoaded()
    {
        WeaponSelectorLoadoutItem[] loadout =
        [
            Gun("20mm", 500),
            Missile("AGM-48", 2),
            Missile("RKT", 8),
        ];

        WeaponSelectorCycleResult result = WeaponSelectorLogic.Cycle(
            loadout,
            WeaponSelectorBucket.Missile,
            WeaponSelectorCombatMode.All,
            currentName: "20mm",
            softName: "RKT");

        Assert.Equal("RKT", result.SoftName);
        Assert.Equal("RKT", result.TargetName);
    }

    [Fact]
    public void Cycle_CrossClass_FallsBackToFirstLoadedEntryWhenSoftIsMissingOrEmpty()
    {
        WeaponSelectorLoadoutItem[] loadout =
        [
            Missile("AGM-48", 0),
            Missile("RKT", 8),
        ];

        WeaponSelectorCycleResult result = WeaponSelectorLogic.Cycle(
            loadout,
            WeaponSelectorBucket.Missile,
            WeaponSelectorCombatMode.All,
            currentName: "20mm",
            softName: "AGM-48");

        Assert.Equal("RKT", result.SoftName);
        Assert.Equal("RKT", result.TargetName);
    }

    [Fact]
    public void Cycle_WhenBucketFullyDepleted_PreservesSoftAndSelectsNothing()
    {
        WeaponSelectorLoadoutItem[] loadout =
        [
            Missile("AGM-48", 0),
            Missile("RKT", 0),
        ];

        WeaponSelectorCycleResult result = WeaponSelectorLogic.Cycle(
            loadout,
            WeaponSelectorBucket.Missile,
            WeaponSelectorCombatMode.All,
            currentName: "20mm",
            softName: "RKT");

        Assert.Equal("RKT", result.SoftName);
        Assert.Null(result.TargetName);
    }

    [Fact]
    public void Cycle_AggregatesDuplicateEntriesBeforeCheckingAmmo()
    {
        WeaponSelectorLoadoutItem[] loadout =
        [
            Missile("RKT", 0),
            Missile("RKT", 6),
            Missile("AGM-48", 2),
        ];

        WeaponSelectorCycleResult result = WeaponSelectorLogic.Cycle(
            loadout,
            WeaponSelectorBucket.Missile,
            WeaponSelectorCombatMode.All,
            currentName: "AGM-48",
            softName: "AGM-48");

        Assert.Equal("RKT", result.SoftName);
        Assert.Equal("RKT", result.TargetName);
    }

    [Fact]
    public void Effective_ReturnsSoftEntryEvenWhenItIsEmpty()
    {
        WeaponSelectorLoadoutItem[] loadout =
        [
            Missile("AGM-48", 2),
            Missile("RKT", 0),
        ];

        string? effective = WeaponSelectorLogic.Effective(
            loadout,
            WeaponSelectorBucket.Release,
            WeaponSelectorCombatMode.All,
            softName: "RKT");

        Assert.Equal("RKT", effective);
    }

    [Fact]
    public void Effective_FallsBackToFirstLiveEntryWhenSoftIsStale()
    {
        WeaponSelectorLoadoutItem[] loadout =
        [
            Bomb("Mk82", 4),
            Missile("AGM-48", 2),
        ];

        string? effective = WeaponSelectorLogic.Effective(
            loadout,
            WeaponSelectorBucket.Release,
            WeaponSelectorCombatMode.All,
            softName: "Old Weapon");

        Assert.Equal("Mk82", effective);
    }

    [Fact]
    public void MissileBucket_RespectsCombatMode()
    {
        WeaponSelectorLoadoutItem[] loadout =
        [
            Missile("AGM-48", 2, airToAir: false),
            Missile("AAM-29 Scythe", 2, airToAir: true),
        ];

        Assert.Equal(
            "AAM-29 Scythe",
            WeaponSelectorLogic.FirstAvailable(loadout, WeaponSelectorBucket.Missile, WeaponSelectorCombatMode.AirToAir));
        Assert.Equal(
            "AGM-48",
            WeaponSelectorLogic.FirstAvailable(loadout, WeaponSelectorBucket.Missile, WeaponSelectorCombatMode.AirToGround));
    }

    [Fact]
    public void ReleaseBucket_AirToAirExcludesBombsAndAirToGroundMissiles()
    {
        WeaponSelectorLoadoutItem[] loadout =
        [
            Bomb("Mk82", 4),
            Missile("AGM-48", 2, airToAir: false),
            Missile("AAM-29 Scythe", 2, airToAir: true),
        ];

        string? effective = WeaponSelectorLogic.Effective(
            loadout,
            WeaponSelectorBucket.Release,
            WeaponSelectorCombatMode.AirToAir,
            softName: "Mk82");

        Assert.Equal("AAM-29 Scythe", effective);
    }

    [Fact]
    public void BombBucket_AirToAirActsEmpty()
    {
        WeaponSelectorLoadoutItem[] loadout =
        [
            Bomb("Mk82", 4),
        ];

        WeaponSelectorCycleResult result = WeaponSelectorLogic.Cycle(
            loadout,
            WeaponSelectorBucket.Bomb,
            WeaponSelectorCombatMode.AirToAir,
            currentName: "20mm",
            softName: "Mk82");

        Assert.Equal("Mk82", result.SoftName);
        Assert.Null(result.TargetName);
    }

    [Fact]
    public void FirstAvailable_ReturnsNullWhenOnlyMatchingEntriesAreDepleted()
    {
        WeaponSelectorLoadoutItem[] loadout =
        [
            Gun("20mm", 500),
            Missile("AGM-48", 0),
        ];

        Assert.Null(WeaponSelectorLogic.FirstAvailable(
            loadout,
            WeaponSelectorBucket.Missile,
            WeaponSelectorCombatMode.All));
    }

    private static WeaponSelectorLoadoutItem Gun(string name, int ammo) =>
        new(name, WeaponSelectorRole.Gun, ammo);

    private static WeaponSelectorLoadoutItem Missile(string name, int ammo, bool airToAir = false) =>
        new(name, WeaponSelectorRole.Missile, ammo, airToAir);

    private static WeaponSelectorLoadoutItem Bomb(string name, int ammo) =>
        new(name, WeaponSelectorRole.Bomb, ammo);
}
