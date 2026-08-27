using System;
using System.Reflection;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace NOXMFD
{
    // Harmony (HarmonyX) is already a transitive compile/runtime dependency of BepInEx.Core, so no
    // new .csproj reference or install step is needed to use it.
    internal static class HarmonyPatches
    {
        // Patches each nested class individually, rather than PatchAll(Assembly): the AKF kill-feed
        // patch below resolves its target by reflection (a generated method whose exact name isn't
        // guaranteed stable across a game update — see its own comment), which can throw at
        // patch-apply time. PatchAll(Assembly) applies every patch class in one pass, and a single
        // failure there can abort the whole pass, silently taking the other patches down with it.
        // Patching one class at a time, each in its own try/catch, keeps a failure contained to that
        // one feature.
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

        // Radar start state. Patching Radar.AttachToUnit doesn't work: that method only fires for a
        // hardpoint-MOUNTED radar pod (Hardpoint.SpawnMount, gated on weaponMount.radar). A normal
        // aircraft's built-in radar attaches via Radar.Awake() instead, which hardcodes
        // `activated = true` with no way to gate it in place — Awake() runs the instant the prefab is
        // instantiated, before Player.SetAircraft(this) has run, so GameManager.GetLocalAircraft can't
        // even resolve correctly at that point (it still reports the PREVIOUS aircraft, or nothing).
        //
        // Patching Aircraft.OnStartClient() instead — as a postfix — runs after the whole method
        // body, including Player.SetAircraft(this) (partway through) AND the InitializeUnit() call at
        // the very end that triggers Hardpoint.SpawnMount for a radar pod, if the loadout has one. By
        // the time this postfix runs, __instance.radar is populated correctly regardless of WHICH path
        // attached it, and GetLocalAircraft resolves correctly — one patch covers both radar shapes.
        // Still gated to the local player's own aircraft; never observably flickers on because
        // OnStartClient completes synchronously, well before the first Update() that would otherwise
        // render/scan with it on.
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
        // EVERY networked aircraft — same local-only gate as radar above. Postfix, not prefix:
        // Player.SetAircraft(this) already ran earlier in the same method body, so GetLocalAircraft
        // correctly resolves this aircraft as local by the time this runs.
        //
        // OnStartServer only executes on the network SERVER. In single-player or a player-hosted
        // lobby the host process is both client and server, so this works correctly. Connecting as a
        // plain client to someone else's dedicated NuclearOptionServer.exe means this patch never
        // fires for your own aircraft — that method runs on their machine, not yours, and a mod
        // normally isn't installed server-side — so the setting silently has no effect in that
        // topology.
        [HarmonyPatch(typeof(Aircraft), "OnStartServer")]
        private static class Aircraft_OnStartServer_Patch
        {
            private static void Postfix(Aircraft __instance)
            {
                if (!GameManager.GetLocalAircraft(out Aircraft local) || !ReferenceEquals(__instance, local)) return;
                __instance.NetworkIgnition = ImmersionConfig.EngineOnOnStart;
            }
        }

        // Master Arm enforcement. A prefix on WeaponManager.Fire(), short-circuiting (skipping the
        // original) when ImmersionState.MasterArmsOn is false. Covers the mod's own keybinds AND the
        // game's own stock trigger/mouse/joystick input in one patch, since both paths call this
        // same method underneath.
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

        // MessageManager.RpcKillMessage is the ClientRpc that drives the game's own kill-feed ticker,
        // but the public method only runs on the sending/host side — a remote client receiving the
        // RPC never calls it, going straight from the network Skeleton_... reader into the generated
        // UserCode_RpcKillMessage_... method instead. THAT is the method every observer's kill
        // actually reaches, so it's the patch target — resolved by name PREFIX rather than the full
        // mangled name, since the numeric suffix is a Mirage-weaver hash tied to the RPC's signature
        // and isn't guaranteed stable across a game update. If it can't be found, TargetMethod
        // returns null, Harmony throws when applying this one patch class, and Init() above logs and
        // moves on — every other patch still applies.
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

        // Best-effort weapon attribution. DamageEffects.BlastFrag receives the detonating missile's
        // own PersistentID (missileID) alongside the attacking aircraft's (dealerID) but never uses
        // it — recording it here, before the kill-message patch above fires, lets a PLAYER-feed kill
        // quote the attacker's last-fired weapon. Per-ATTACKER, not per-victim.
        [HarmonyPatch(typeof(DamageEffects), nameof(DamageEffects.BlastFrag))]
        private static class DamageEffects_BlastFrag_Patch
        {
            private static void Prefix(PersistentID dealerID, PersistentID missileID)
            {
                AkfTracker.Active?.RecordWeaponHit(dealerID, missileID);
            }
        }

        // BlastFrag's own missileID is only correct AFTER an armed missile detonates, but for a
        // pierce-fuze warhead (impactFuseDelay == 0) the kill happens INSIDE Missile.PenetrateObject
        // via DamageEffects.ArmorPenetrate, synchronously and before the missile ever calls
        // Detonate/BlastFrag at all. A proximity/blast-fuze warhead's own BlastFrag call is also
        // deferred a full physics tick (Missile.ExplosionForceOnPhysicsFrame awaits WaitForFixedUpdate
        // before calling it) — late enough that a second missile fired moments later can have its own
        // kill race ahead of the first missile's weapon record, so a salvo's later kills can quote the
        // wrong missile's weapon.
        //
        // Recording weapon identity at the missile's own terminal-sequence entry points instead avoids
        // this: both run synchronously, before ArmorPenetrate/RpcDetonate/BlastFrag, for every missile
        // kind.
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

        // TGP manual camera control (docs/tgp-manual-control.md). While TgpManualControl.ManualMode
        // is on, these two prefixes skip TargetCam's own auto-pointing so a pilot's pan/tilt/zoom
        // input (applied directly by TgpManualControl.Tick, every frame) isn't fought: Update()
        // would otherwise keep lerping fieldOfView toward its own targetFOV, switching currentMount
        // between forward/rear based on angle-to-target (unstable with no real target locked), and
        // counting camTimeout down toward auto-disable; AimCamera() would keep slerping the mount
        // toward a stale/empty targetPosition. SwitchIRState is not patched; manual IR toggling
        // calls that private method directly when requested.
        [HarmonyPatch(typeof(TargetCam), "Update")]
        private static class TargetCam_Update_ManualGate
        {
            // TgpManualControl.Tick() calls UpdateExposure directly while this skips the rest of
            // Update()'s mount-switch/FOV-lerp/camTimeout behavior.
            private static bool Prefix() => !TgpManualControl.ManualMode;
        }

        [HarmonyPatch(typeof(TargetCam), "AimCamera")]
        private static class TargetCam_AimCamera_ManualGate
        {
            private static bool Prefix() => !TgpManualControl.ManualMode;
        }

        // Real (native) unit lock's own CLR/IR override (docs/tgp-manual-control.md's NAV
        // additions). SetTargetCam's own auto color/IR switch (time-of-day, target distance, or the
        // "always IR" PlayerSettings.tacScreenIR setting) runs unconditionally every frame a real
        // target is locked — unlike ManualMode, nothing skips it. Re-assert the player's own CLR/IR
        // choice right after, so this mod's control has the last word instead of getting overwritten
        // next frame. No-ops until the player has used CLR/IR outside manual mode at least once
        // (TgpManualControl.NativeIrOverride is what that sets), or while ManualMode owns IR
        // directly (SetIR/ToggleIR flip it immediately there instead — this native lock is never
        // the thing rendering during manual mode anyway).
        [HarmonyPatch(typeof(TargetCam), "SetTargetCam")]
        private static class TargetCam_SetTargetCam_IrOverride
        {
            private static void Postfix(TargetCam __instance)
            {
                if (TgpManualControl.ManualMode) return;
                bool? ir = TgpManualControl.NativeIrOverride;
                if (ir == null || __instance.UsingIR() == ir.Value || !TgpManualTargetCamAccess.Ensure()) return;
                TgpManualTargetCamAccess.SwitchIR(__instance, ir.Value);
            }
        }

        // In-cockpit TGP overlay for manual control (docs/tgp-manual-control.md's "In-cockpit
        // overlay"). TargetScreenUI.UpdateTargetInfo already runs on its own 0.1s StartSlowUpdate
        // timer regardless of manual mode, but early-returns to "NO LOCK" the instant
        // targetList.Count == 0 — always true while ManualMode is on, since Tick() auto-exits the
        // instant a real lock exists (no "which one wins" race with real lock data). Prefix skips
        // the original entirely and drives the same Text/Image elements from TgpManualControl's own
        // state instead, via Harmony's ___field injection — reaches TargetScreenUI's private
        // serialized fields directly, no separate reflection cache needed.
        [HarmonyPatch(typeof(TargetScreenUI), "UpdateTargetInfo")]
        private static class TargetScreenUI_UpdateTargetInfo_ManualOverlay
        {
            // TextMeshProUGUI, not UnityEngine.UI.Text — see TgpNativeOverlay.Populate's
            // own comment. Harmony may not fail loudly if a private UI field type is stale.
            private static bool Prefix(TargetCam ___targetCam, Canvas ___displayCanvas,
                TextMeshProUGUI ___typeText, TextMeshProUGUI ___pilotText, TextMeshProUGUI ___noLock,
                TextMeshProUGUI ___distance, TextMeshProUGUI ___heading, TextMeshProUGUI ___altitude,
                TextMeshProUGUI ___rel_altitude, TextMeshProUGUI ___speed, TextMeshProUGUI ___rel_speed,
                TextMeshProUGUI ___magText, TextMeshProUGUI ___modeText, TextMeshProUGUI ___bearingText,
                TextMeshProUGUI ___gridText, Image ___bearingImg)
            {
                // Always synced, regardless of ManualMode, so the crosshair is hidden the instant
                // manual mode ends (the very next native-path tick) instead of staying stuck on.
                // IsNativeTgpSoi, not the broader IsTgpSoi: the in-cockpit tag should only light up
                // when the camera's own ring entry is literally focused, not whenever a real TGP pane
                // is SOI — those are two independent ring members, and the web ring already shows the
                // pane's own focus, so lighting up both at once would read as "wait, which one is
                // actually SOI?" (docs/tgp-manual-control.md's PAD Cursor consolidation plan).
                // Keybinds.cs's PAD Cursor input routing still uses IsTgpSoi (functional, not visual)
                // so pointing control keeps working through either route.
                TgpNativeOverlay.SyncCrosshair(___displayCanvas, TgpManualControl.ManualMode, TgpManualControl.PointTrackActive, TelemetryServer.IsNativeTgpSoi);

                if (!TgpManualControl.ManualMode || ___targetCam == null) return true;
                Transform mount = ___targetCam.GetCamMount();
                if (mount == null) return true;
                TgpNativeOverlay.Populate(___targetCam, mount, ___typeText, ___pilotText,
                    ___noLock, ___distance, ___heading, ___altitude, ___rel_altitude, ___speed,
                    ___rel_speed, ___magText, ___modeText, ___bearingText, ___gridText, ___bearingImg);
                return false;
            }
        }
    }
}
