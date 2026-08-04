using HarmonyLib;

namespace NOXMFD
{
    // docs/radar-master-arms.md, plan step 1 — Harmony (HarmonyX) setup. It's already a transitive
    // compile/runtime dependency of BepInEx.Core (confirmed in obj/project.assets.json, resolved for
    // netstandard2.1), so no new .csproj reference or user install step was needed to add it.
    //
    // This is a throwaway probe patch only — it exists to confirm Harmony actually applies and fires
    // at runtime in this BepInEx 5 setup before any real logic (spawn-default overrides, Master Arms
    // enforcement) depends on it. Replace/remove as those real patches land.
    internal static class HarmonyPatches
    {
        internal static void Init()
        {
            new Harmony("com.roque.NOXMFD").PatchAll(typeof(HarmonyPatches).Assembly);
        }

        [HarmonyPatch(typeof(Aircraft), "OnStartServer")]
        private static class OnStartServer_Probe
        {
            private static void Postfix()
            {
                Plugin.Log?.LogInfo("[NOXMFD] Harmony probe patch fired: Aircraft.OnStartServer.");
            }
        }
    }
}
