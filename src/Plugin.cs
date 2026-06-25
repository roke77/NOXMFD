using BepInEx;
using BepInEx.Logging;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace NOXMFD
{
    [BepInPlugin("com.roque.NOXMFD", "NO XMFD", "0.1.0")]
    [BepInProcess("NuclearOption.exe")]
    [BepInProcess("NuclearOptionServer.exe")]
    public class Plugin : BaseUnityPlugin
    {
        internal static ManualLogSource? Log;

        // BepInEx puts our Plugin on its own GameObject and tries to mark it DontDestroyOnLoad,
        // but in Nuclear Option / Unity 2022.3 that doesn't stick — the GameObject dies on the
        // boot -> MainMenu scene transition, taking the HTTP server with it. We sidestep by:
        //   * starting the server (already static) in Awake,
        //   * subscribing statically to the FIRST scene load,
        //   * then spawning OUR OWN GameObject (and marking IT persistent) from that callback,
        //     when a real scene exists and DontDestroyOnLoad actually works.
        // Plugin itself can be torn down — the static state and the Worker GameObject survive.

        private static Worker? _worker;
        private static bool    _sceneSubscribed;

        private void Awake()
        {
            Log = Logger;
            HudConfig.Bind(Config);   // bind HUD-declutter toggles (persisted + shown in the in-game config menu)
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
            if (_worker != null) return;
            var go = new GameObject("NOXMFD_Worker");
            Object.DontDestroyOnLoad(go);
            _worker = go.AddComponent<Worker>();
            Log?.LogInfo("[NOXMFD] Worker attached (scene='" + scene.name + "').");
        }
    }
}
