using HarmonyLib;

namespace NOXMFD
{
    // docs/radar-master-arms.md — Harmony (HarmonyX) patches. Already a transitive compile/runtime
    // dependency of BepInEx.Core (confirmed in obj/project.assets.json, resolved for netstandard2.1),
    // so no new .csproj reference or user install step was needed to add it. Verified in-game: patches
    // apply and fire correctly in this BepInEx 5 setup (see the removed probe patch's log line).
    internal static class HarmonyPatches
    {
        internal static void Init()
        {
            new Harmony("com.roque.NOXMFD").PatchAll(typeof(HarmonyPatches).Assembly);
        }

        // Radar start state. Radar.AttachToUnit sets `activated = true` unconditionally for EVERY
        // unit's radar (AI/enemy included, not just the player's own) — so this only overrides it once
        // confirmed the attached unit IS the local player's own aircraft. GameManager.GetLocalAircraft
        // is a per-client lookup (not server state), so it's safe to call from any code path.
        //
        // Does NOT cover Radar.Awake's separate attach path (attachedUnit pre-wired before Awake runs)
        // — believed unused for a dynamically-spawned player aircraft (that path looks built for
        // scene-placed AI units with a serialized radar reference), but unconfirmed. If a player
        // aircraft's radar turns out to attach that way instead, RadarOnOnStart would silently have no
        // effect — worth an in-game check the first time this ships.
        [HarmonyPatch(typeof(Radar), nameof(Radar.AttachToUnit))]
        private static class Radar_AttachToUnit_Patch
        {
            private static void Postfix(Radar __instance, Unit unit)
            {
                if (unit is not Aircraft ac) return;
                if (!GameManager.GetLocalAircraft(out Aircraft local) || !ReferenceEquals(ac, local)) return;
                __instance.activated = ImmersionConfig.RadarOnOnStart;
            }
        }

        // Engine start state. Aircraft.OnStartServer unconditionally sets NetworkIgnition = true for
        // EVERY networked aircraft (same AI/enemy concern as radar above) — same local-only gate.
        // Postfix, not prefix: Player.SetAircraft(this) already ran earlier in the same method body,
        // so GetLocalAircraft correctly resolves this aircraft as local by the time this runs.
        //
        // Known limitation, accepted: OnStartServer only executes on the network SERVER. In
        // single-player or a player-hosted lobby the host process is both client and server, so this
        // works correctly. Connecting as a plain client to someone else's dedicated
        // NuclearOptionServer.exe means this patch never fires for your own aircraft at all — that
        // method runs on their machine, not yours, and a mod normally isn't installed server-side — so
        // the setting silently has no effect in that topology. This mod's primary use case is
        // single-player/host play; revisit only if dedicated-server support becomes a real ask.
        [HarmonyPatch(typeof(Aircraft), "OnStartServer")]
        private static class Aircraft_OnStartServer_Patch
        {
            private static void Postfix(Aircraft __instance)
            {
                if (!GameManager.GetLocalAircraft(out Aircraft local) || !ReferenceEquals(__instance, local)) return;
                __instance.NetworkIgnition = ImmersionConfig.EngineOnOnStart;
            }
        }

        // Master Arms enforcement. A prefix on WeaponManager.Fire() and CountermeasureManager.
        // DeployCountermeasure(), both short-circuiting (skipping the original) when
        // ImmersionState.MasterArmsOn is false. Covers the mod's own keybinds (WeaponSelectors.Fire()
        // calls wm.Fire(); Keybinds.Drive(...) calls mgr.DeployCountermeasure(ac) directly) AND the
        // game's own stock trigger/mouse/joystick input in one patch each, since both paths call these
        // same two methods underneath — this is the whole reason Master Arms needed Harmony at all.
        //
        // Same local-only gate as the spawn-default patches above: MasterArmsOn is a personal,
        // client-side preference, never something that should block AI or another player's weapons.
        [HarmonyPatch(typeof(WeaponManager), nameof(WeaponManager.Fire))]
        private static class WeaponManager_Fire_Patch
        {
            // ___aircraft: Harmony's convention for injecting a target type's private field by name.
            private static bool Prefix(Aircraft ___aircraft)
            {
                if (!GameManager.GetLocalAircraft(out Aircraft local) || !ReferenceEquals(___aircraft, local)) return true;
                return ImmersionState.MasterArmsOn;
            }
        }

        [HarmonyPatch(typeof(CountermeasureManager), nameof(CountermeasureManager.DeployCountermeasure))]
        private static class CountermeasureManager_DeployCountermeasure_Patch
        {
            private static bool Prefix(Aircraft aircraft)
            {
                if (!GameManager.GetLocalAircraft(out Aircraft local) || !ReferenceEquals(aircraft, local)) return true;
                return ImmersionState.MasterArmsOn;
            }
        }
    }
}
