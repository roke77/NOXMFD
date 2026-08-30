namespace NOXMFD
{
    // Restricts which missiles Cycle Missile reaches; AirToAir/AirToGround enforced in WeaponSelectors.
    internal enum CombatMode { All, AirToAir, AirToGround }

    // MasterArmsOn/PowerOn/CombatMode have no game-side field to patch, so they're reset here per
    // aircraft instead of at spawn via HarmonyPatches. None persist across restart; only ImmersionConfig does.
    internal static class ImmersionState
    {
        public static bool MasterArmsOn = true;
        public static bool PowerOn = true;
        public static CombatMode CombatMode = CombatMode.All;

        private static Aircraft? _spawnDefaultsAircraft;

        // No-op until the aircraft reference changes.
        public static void EnsureSpawnDefaults(Aircraft ac)
        {
            if (ReferenceEquals(ac, _spawnDefaultsAircraft)) return;
            _spawnDefaultsAircraft = ac;
            MasterArmsOn = ImmersionConfig.MasterArmsOnOnStart;
            PowerOn = ImmersionConfig.PowerOnOnStart;
            // Via SetCombatMode so a respawn restores the HUD baseline instead of leaving a stale forced preset applied.
            Keybinds.SetCombatMode(CombatMode.All);
        }
    }
}
