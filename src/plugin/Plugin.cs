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

        // Master switch for the mod's active per-frame work (telemetry scan/serve, asset capture,
        // HUD declutter). Read by TelemetryReader + HudDeclutter each tick. When false they idle
        // but the reader still samples FPS, so PerfLogging can capture a no-features baseline.
        internal static bool FeaturesActive = true;

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
            HudDeclutterConfig.Bind(Config);   // bind HUD-declutter toggles (persisted + shown in the in-game config menu)
            Keybinds.Bind(Config);             // bind the gameplay keybinds (countermeasures + gear) — rebindable in the F1 menu

            // Perf measurement (docs/performance.md). Defaults OFF for normal play; flip it on
            // live in the F1 menu to re-capture timings to LogOutput.log when investigating perf.
            var perfLog = Config.Bind("Diagnostics", "PerfLogging", false,
                "When on, every 5 s logs avg/1%-low/min FPS, GC collection deltas, and avg/max ms for ScanWorld, BuildUnits, PushSnapshot and Serialize (with payload size + unit/contact counts). For performance measurement — leave OFF for normal play.");
            PerfDiag.Enabled = perfLog.Value;
            perfLog.SettingChanged += (_, __) => PerfDiag.Enabled = perfLog.Value;

            // Master switch to idle the mod's active work for a no-features FPS baseline (see field).
            var featuresActive = Config.Bind("Diagnostics", "FeaturesActive", true,
                "Master switch for the mod's active work (telemetry scan/serve, asset capture, HUD declutter). Turn OFF to capture a no-features FPS baseline with PerfLogging in the SAME mission — the reader still samples FPS but does no mod work, and the native HUD is left intact. Leave ON for normal play.");
            FeaturesActive = featuresActive.Value;
            featuresActive.SettingChanged += (_, __) => FeaturesActive = featuresActive.Value;

            // Throwaway probe for the TGT-page plan (docs/tgt-page.md) — logs whether the game's
            // TargetListSelector singleton exists. Remove once that question is answered.
            TgtProbe.Bind(Config);

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

        private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (_lifecycle != null) return;
            var go = new GameObject("NOXMFD_Lifecycle");
            Object.DontDestroyOnLoad(go);
            _lifecycle = go.AddComponent<MissionLifecycle>();
            Log?.LogInfo("[NOXMFD] MissionLifecycle attached (scene='" + scene.name + "').");
        }
    }
}
