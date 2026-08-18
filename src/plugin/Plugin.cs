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
        private static bool    _sceneSubscribed;

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
            TryBind("Refresh rates", () => RatesConfig.Bind(Config));         // cfg-rates experiment (issue #39) — TLM/TGP sliders on the RTS page
            TryBind("Harmony", HarmonyPatches.Init);                          // docs/radar-master-arms.md — spawn-default + Master Arms patches
            TryBind("Waypoint routes", RouteStore.Load);                       // docs/hud-waypoint-indicator.md — route library persisted to disk

            // Network: the port the tablet connects to, and whether to auto-open the Windows LAN
            // gates when the wildcard bind is denied (see docs/networking.md). Read once here —
            // the server binds at startup, so changing these needs a game restart.
            var netPort = Config.Bind("Network", "Port", 5005,
                "TCP port the mod's HTTP/SSE server listens on; the tablet connects to http://<pc-ip>:<port>/. Change only if 5005 is taken. Requires a game restart, and must match the URL you open on the tablet.");
            var autoLan = Config.Bind("Network", "AutoSetupLanAccess", true,
                "On first launch, if binding the LAN port is denied, automatically add the Windows URL reservation + firewall rule so a tablet can connect — ONLY works when the game is run as Administrator. Turn OFF to manage them yourself (see docs/networking.md). No effect once configured. Localhost always works regardless.");
            TelemetryServer.Configure(netPort.Value, autoLan.Value);

            TelemetryServer.Start();
            if (!_sceneSubscribed)
            {
                SceneManager.sceneLoaded += OnSceneLoaded;
                _sceneSubscribed = true;
            }
            Log.LogInfo("NO XMFD loaded. Waiting for a mission to start...");
        }

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
