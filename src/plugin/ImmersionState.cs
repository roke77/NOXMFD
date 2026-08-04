namespace NOXMFD
{
    // Which missiles Cycle Missile reaches (docs/radar-master-arms.md, issue #32). AirToAir/
    // AirToGround are enforced in WeaponSelectors; All is today's unrestricted behaviour.
    internal enum CombatMode { All, AirToAir, AirToGround }

    // Mod-only runtime state — MasterArmsOn and CombatMode have no game-side field to patch (unlike
    // Radar/Engine, whose spawn defaults are set at the source by HarmonyPatches instead), so they're
    // reset reactively here on every new aircraft. Neither persists across a restart; only the
    // *settings* that seed them (ImmersionConfig) do.
    internal static class ImmersionState
    {
        public static bool MasterArmsOn = true;
        public static CombatMode CombatMode = CombatMode.All;

        private static Aircraft? _spawnDefaultsAircraft;

        // Called once per PushSnapshot tick (TelemetryReader), same ReferenceEquals-guarded shape as
        // EnsureRwrSubscription/EnsureAfterburnerCache — a no-op until the aircraft reference changes.
        public static void EnsureSpawnDefaults(Aircraft ac)
        {
            if (ReferenceEquals(ac, _spawnDefaultsAircraft)) return;
            _spawnDefaultsAircraft = ac;
            MasterArmsOn = ImmersionConfig.MasterArmsOnOnStart;
            CombatMode = CombatMode.All;
        }
    }
}
