using System;
using BepInEx;
using BepInEx.Logging;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace NOXMFD
{
    [BepInPlugin("com.roque.NOXMFD", "NO XMFD", MyPluginInfo.PLUGIN_VERSION)]
    [BepInProcess("NuclearOption.exe")]
    [BepInProcess("NuclearOptionServer.exe")]
    public class Plugin : BaseUnityPlugin
    {
        internal static ManualLogSource? Log;

        // BepInEx hosts the Plugin on its own GameObject and marks it DontDestroyOnLoad, but in
        // Nuclear Option / Unity 2022.3 that flag doesn't hold — the GameObject dies on the
        // boot -> MainMenu scene transition, and would take the HTTP server with it. So the
        // durable state lives off the Plugin GameObject:
        //   * the server is static and starts in Awake,
        //   * a static handler subscribes to the FIRST scene load,
        //   * from that callback (a real scene exists, so DontDestroyOnLoad holds here) we spawn
        //     our own GameObject and mark IT persistent.
        // Plugin itself can be torn down — the static state and the MissionLifecycle GameObject survive.

        private static MissionLifecycle? _lifecycle;
        private static bool _sceneSubscribed;
        private static bool _quitSubscribed;

        private void Awake()
        {
            Log = Logger;
            // Each wrapped separately: a config-system problem on the host (e.g. BepInEx missing a
            // type converter it needs — seen in the wild as ConfigFile.Bind<KeyboardShortcut>
            // throwing ArgumentException on a broken/mismatched BepInEx install) would otherwise take
            // the whole plugin down in Awake, silently disabling telemetry along with it. This way
            // one broken subsystem logs a clear cause and the rest of the mod still starts.
            TryBind("HUD declutter", () => HudDeclutterConfig.Bind(Config));   // HUD-declutter toggles (persisted + shown in the in-game config menu)
            TryBind("Keybinds", () => Keybinds.Bind(Config));                  // gameplay keybinds (countermeasures + gear) — configured on the /keybinds page
            TryBind("Immersion options", () => ImmersionConfig.Bind(Config)); // docs/radar-master-arms.md — radar/engine/master-arms start-state settings
            TryBind("Refresh rates", () => RatesConfig.Bind(Config));         // TLM/TGP rate + quality settings — MAP CFG and TGP CFG pages
            TryBind("Harmony", HarmonyPatches.Init);                          // docs/radar-master-arms.md — spawn-default + Master Arm patches
            TryBind("JSON self-check", JsonLite.SelfCheck);                     // docs/hud-waypoint-indicator.md — pure parser, no C# test runner exists in this repo
            RouteStore.ConfigDir = Paths.ConfigPath;                           // injected so RouteStore.cs stays BepInEx-free
            RouteStore.LogWarning = msg => Log?.LogWarning(msg);
            TryBind("Waypoint routes", RouteStore.Load);                       // docs/hud-waypoint-indicator.md — route library persisted to disk
            TryBind("Saved layouts", LayoutStore.Load);                        // issue #51 — SAVE/LOAD LAYOUT library persisted to disk
            TryBind("HUD presets", HudPresetStore.Load);                       // issue #50 follow-up — 5 numbered HUD-filter presets persisted to disk
            TryBind("HUD presets self-check", HudPresetStore.SelfCheck);        // docs/hud-presets.md — pure JSON round-trip, same reasoning as JsonLite above

            // Network: the port the tablet connects to, and whether to auto-open the Windows LAN
            // gates when the wildcard bind is denied (see docs/networking.md). Read once here —
            // the server binds at startup, so changing these needs a game restart.
            var netPort = Config.Bind("Network", "Port", 5005,
                "TCP port the mod's HTTP/SSE server listens on; the tablet connects to http://<pc-ip>:<port>/. Change only if 5005 is taken. Requires a game restart, and must match the URL you open on the tablet.");
            var autoLan = Config.Bind("Network", "AutoSetupLanAccess", true,
                "On first launch, if binding the LAN port is denied, automatically add the Windows URL reservation + firewall rule so a tablet can connect — ONLY works when the game is run as Administrator. Turn OFF to manage them yourself (see docs/networking.md). No effect once configured. Localhost always works regardless.");
            TelemetryServer.Configure(netPort.Value, autoLan.Value);

            // The BepInEx-owned Plugin component is destroyed during boot, so its OnDestroy cannot
            // own process cleanup. A static application handler survives with the static server.
            if (!_quitSubscribed)
            {
                Application.quitting += OnApplicationQuitting;
                _quitSubscribed = true;
            }
            TelemetryServer.Start();
            if (!_sceneSubscribed)
            {
                SceneManager.sceneLoaded += OnSceneLoaded;
                _sceneSubscribed = true;
            }
            Log.LogInfo("NO XMFD loaded. Waiting for a mission to start...");
        }

        private static void OnApplicationQuitting() => TelemetryServer.Stop();

        // Runs a config-binding step without letting its failure take the rest of Awake down.
        private static void TryBind(string what, Action bind)
        {
            try { bind(); }
            catch (Exception ex)
            {
                Log?.LogError($"[NOXMFD] {what} failed to initialize and will be unavailable this session: {ex.Message}\n" +
                    "This usually means BepInEx's config system on this install is missing a type converter it " +
                    "needs (a broken or mismatched BepInEx install) — try reinstalling BepInEx. The rest of " +
                    "NO XMFD will still run.");
            }
        }

        private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (_lifecycle != null) return;
            var go = new GameObject("NOXMFD_Lifecycle");
            UnityEngine.Object.DontDestroyOnLoad(go);
            _lifecycle = go.AddComponent<MissionLifecycle>();
            Log?.LogInfo("[NOXMFD] MissionLifecycle attached (scene='" + scene.name + "').");
        }
    }
}
