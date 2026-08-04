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

        // Radar start state. CONFIRMED (in-game bug report + decompiled-source investigation) that
        // patching Radar.AttachToUnit — the original approach — was wrong: that method only fires for
        // a hardpoint-MOUNTED radar pod (Hardpoint.SpawnMount, gated on weaponMount.radar). A normal
        // aircraft's built-in radar attaches via Radar.Awake() instead, which hardcodes
        // `activated = true` with no way to gate it in place — Awake() runs the instant the prefab is
        // instantiated, before Player.SetAircraft(this) has run, so GameManager.GetLocalAircraft can't
        // even resolve correctly at that point (it still reports the PREVIOUS aircraft, or nothing).
        //
        // Fix: patch Aircraft.OnStartClient() instead — a postfix, so it runs after the whole method
        // body, including Player.SetAircraft(this) (partway through) AND the InitializeUnit() call at
        // the very end that triggers Hardpoint.SpawnMount for a radar pod, if the loadout has one. By
        // the time this postfix runs, __instance.radar is populated correctly regardless of WHICH path
        // attached it, and GetLocalAircraft resolves correctly — one patch covers both radar shapes.
        // Still gated to the local player's own aircraft; still never observably flickers on for the
        // same reason as before (OnStartClient completes synchronously, well before the first Update()
        // that would otherwise render/scan with it on).
        [HarmonyPatch(typeof(Aircraft), "OnStartClient")]
        private static class Aircraft_OnStartClient_Patch
        {
            private static void Postfix(Aircraft __instance)
            {
                if (!GameManager.GetLocalAircraft(out Aircraft local) || !ReferenceEquals(__instance, local)) return;
                if (__instance.radar != null) __instance.radar.activated = ImmersionConfig.RadarOnOnStart;
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
