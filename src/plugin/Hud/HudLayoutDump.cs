using System;
using System.IO;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace NOXMFD
{
    // ponytail: one-shot diagnostic for the compact-HUD investigation. The HUD's layout lives in the
    // game's prefab, not in code, so this writes the FlightHud canvas tree (names, components, local
    // positions, current text) to BepInEx/NOXMFD_hud_layout.txt once per mission. Remove once the
    // compact layout ships.
    internal class HudLayoutDump : MonoBehaviour
    {
        // Weapon UIs are instantiated under HUDCenter after spawn; wait so they're in the dump.
        private const float Delay = 3f;
        private float _timer;
        private bool _done;

        private void Update()
        {
            if (_done) return;
            FlightHud hud = SceneSingleton<FlightHud>.i;
            if (hud == null || SceneSingleton<CombatHUD>.i == null || SceneSingleton<CombatHUD>.i.aircraft == null) return;
            _timer += Time.unscaledDeltaTime;
            if (_timer < Delay) return;
            _done = true;
            try { Dump(hud); }
            catch (Exception ex) { Plugin.Log?.LogWarning($"[NOXMFD] HUD layout dump failed: {ex}"); }
        }

        private static void Dump(FlightHud hud)
        {
            Transform hudCenter = hud.GetHUDCenter();
            Canvas canvas = hudCenter.GetComponentInParent<Canvas>(true).rootCanvas;
            var sb = new StringBuilder();
            sb.AppendLine($"aircraft={SceneSingleton<CombatHUD>.i.aircraft.definition.unitName} screen={Screen.width}x{Screen.height} " +
                          $"canvasScale={canvas.scaleFactor} fov={SceneSingleton<CameraStateManager>.i?.mainCamera.fieldOfView}");
            sb.AppendLine($"hmd: width={PlayerSettings.hmdWidth} height={PlayerSettings.hmdHeight} sideDist={PlayerSettings.hmdSideDist} " +
                          $"sideAngle={PlayerSettings.hmdSideAngle} topHeight={PlayerSettings.hmdTopHeight} hideDist={PlayerSettings.hmdHideDist}");
            sb.AppendLine("format: name [tags] active | components | localPos | rect anchoredPos/size/anchorMin-Max | text");
            Walk(canvas.transform, 0, sb, hud, hudCenter);

            string path = Path.Combine(BepInEx.Paths.BepInExRootPath, "NOXMFD_hud_layout.txt");
            File.WriteAllText(path, sb.ToString());
            Plugin.Log?.LogInfo($"[NOXMFD] HUD layout dumped to {path}");
        }

        private static void Walk(Transform t, int depth, StringBuilder sb, FlightHud hud, Transform hudCenter)
        {
            sb.Append(' ', depth * 2).Append(t.name);
            if (t == hudCenter) sb.Append(" [HUDCenter]");
            if (t == hud.HMDCenter) sb.Append(" [HMDCenter]");
            if (t == hud.statusAnchor) sb.Append(" [statusAnchor]");
            sb.Append(t.gameObject.activeSelf ? " on" : " OFF").Append(" |");

            foreach (Component c in t.GetComponents<Component>())
                if (c != null && !(c is Transform) && !(c is CanvasRenderer))
                    sb.Append(' ').Append(c.GetType().Name);

            Vector3 p = t.localPosition;
            sb.Append($" | ({p.x:F0},{p.y:F0})");
            if (t is RectTransform r)
                sb.Append($" | ({r.anchoredPosition.x:F0},{r.anchoredPosition.y:F0}) {r.sizeDelta.x:F0}x{r.sizeDelta.y:F0} " +
                          $"{r.anchorMin.x:F2},{r.anchorMin.y:F2}-{r.anchorMax.x:F2},{r.anchorMax.y:F2}");

            string? text = t.GetComponent<TMP_Text>()?.text ?? t.GetComponent<Text>()?.text;
            if (!string.IsNullOrEmpty(text))
                sb.Append(" | \"").Append(text!.Replace("\n", "\\n")).Append('"');
            sb.AppendLine();

            for (int i = 0; i < t.childCount; i++)
                Walk(t.GetChild(i), depth + 1, sb, hud, hudCenter);
        }
    }
}
