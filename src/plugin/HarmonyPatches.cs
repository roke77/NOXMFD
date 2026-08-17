using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace NOXMFD
{
    // docs/radar-master-arms.md — Harmony (HarmonyX) patches. Already a transitive compile/runtime
    // dependency of BepInEx.Core (confirmed in obj/project.assets.json, resolved for netstandard2.1),
    // so no new .csproj reference or user install step was needed to add it. Verified in-game: patches
    // apply and fire correctly in this BepInEx 5 setup (see the removed probe patch's log line).
    internal static class HarmonyPatches
    {
        // Patches each nested class individually, rather than PatchAll(Assembly) (issue #34 follow-up):
        // the AKF kill-feed patch below resolves its target by reflection (a generated method whose
        // exact name isn't guaranteed stable across a game update — see its own comment), which can
        // throw at patch-apply time. PatchAll(Assembly) applies every patch class in one pass and a
        // single failure there can abort the whole pass — this would silently take the pre-existing
        // radar/engine/master-arms patches down with it. Patching one class at a time, each in its own
        // try/catch, means a failure stays contained to that one feature.
        internal static void Init()
        {
            var harmony = new Harmony("com.roque.NOXMFD");
            foreach (Type patchClass in typeof(HarmonyPatches).GetNestedTypes(BindingFlags.NonPublic | BindingFlags.Static))
            {
                try { harmony.CreateClassProcessor(patchClass).Patch(); }
                catch (Exception e)
                {
                    Plugin.Log?.LogWarning($"[NOXMFD] Harmony patch '{patchClass.Name}' failed to apply: {e.Message}");
                }
            }
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

        // Master Arms enforcement. A prefix on WeaponManager.Fire(), short-circuiting (skipping the
        // original) when ImmersionState.MasterArmsOn is false. Covers the mod's own keybinds
        // (WeaponSelectors.Fire() calls wm.Fire()) AND the game's own stock trigger/mouse/joystick
        // input in one patch, since both paths call this same method underneath — this is the whole
        // reason Master Arms needed Harmony at all.
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

        // Kill-feed capture (issue #34, docs/akf-page.md). MessageManager.RpcKillMessage is the
        // ClientRpc that drives the game's own kill-feed ticker, but the public method only runs on
        // the sending/host side — a remote client receiving the RPC never calls it, going straight
        // from the network Skeleton_... reader into the generated UserCode_RpcKillMessage_... method
        // instead. THAT is the method every observer's kill actually reaches, so it's the patch
        // target — resolved by name PREFIX rather than the full mangled name, since the numeric
        // suffix is a Mirage-weaver hash tied to the RPC's signature and isn't guaranteed stable
        // across a game update. If it can't be found, TargetMethod returns null, Harmony throws when
        // applying this one patch class, and Init() above logs and moves on — every other patch
        // (including the two below and the three above) still applies.
        [HarmonyPatch]
        private static class MessageManager_RpcKillMessage_Patch
        {
            private static MethodBase? TargetMethod()
            {
                foreach (MethodInfo m in typeof(MessageManager).GetMethods(
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                {
                    if (m.Name.StartsWith("UserCode_RpcKillMessage")) return m;
                }
                return null;
            }

            private static void Postfix(PersistentID killerID, PersistentID killedID, KillType killedType)
            {
                AkfTracker.Active?.RecordKill(killerID, killedID, killedType);
            }
        }

        // Weapon attribution (issue #34, docs/akf-page.md), best-effort. DamageEffects.BlastFrag
        // receives the detonating missile's own PersistentID (missileID) alongside the attacking
        // aircraft's (dealerID) but never uses it — recording it here, before the kill-message patch
        // above fires, lets a PLAYER-feed kill quote the attacker's last-fired weapon. Per-ATTACKER,
        // not per-victim — see the doc's "known ceiling" note.
        [HarmonyPatch(typeof(DamageEffects), nameof(DamageEffects.BlastFrag))]
        private static class DamageEffects_BlastFrag_Patch
        {
            private static void Prefix(PersistentID dealerID, PersistentID missileID)
            {
                AkfTracker.Active?.RecordWeaponHit(dealerID, missileID);
            }
        }

        // Weapon attribution, part 2 (root-caused after the diagnostic build above). BlastFrag's own
        // missileID is only correct AFTER an armed missile detonates, but for a pierce-fuze warhead
        // (the AGM-48's kind — impactFuseDelay == 0) the kill itself happens INSIDE
        // Missile.PenetrateObject via DamageEffects.ArmorPenetrate, synchronously and before the
        // missile ever calls Detonate/BlastFrag at all. Worse, a proximity/blast-fuze warhead's own
        // BlastFrag call is deferred a full physics tick (Missile.ExplosionForceOnPhysicsFrame awaits
        // WaitForFixedUpdate before calling it) — late enough that a second missile fired moments
        // later can have its own kill race ahead of the first missile's weapon record. Net effect:
        // in a salvo, each kill was showing the PREVIOUS missile's weapon (looked right only because
        // salvos repeat the same weapon type), and the first kill had nothing to borrow at all.
        //
        // Fix: record weapon identity at the missile's own terminal-sequence entry points instead —
        // both run synchronously, before ArmorPenetrate/RpcDetonate/BlastFrag, for every missile kind.
        [HarmonyPatch(typeof(Missile), "PenetrateObject")]
        private static class Missile_PenetrateObject_Patch
        {
            private static void Prefix(Missile __instance)
            {
                AkfTracker.Active?.RecordWeaponHit(__instance.ownerID, __instance.persistentID);
            }
        }

        [HarmonyPatch(typeof(Missile), nameof(Missile.Detonate), new[] { typeof(Vector3), typeof(bool), typeof(bool) })]
        private static class Missile_Detonate_Patch
        {
            private static void Prefix(Missile __instance)
            {
                AkfTracker.Active?.RecordWeaponHit(__instance.ownerID, __instance.persistentID);
            }
        }
    }
}
