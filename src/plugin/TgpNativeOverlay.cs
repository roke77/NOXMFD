using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace NOXMFD
{
    // Manual-mode overlay for the game's native in-cockpit TGP screen. TgpManualControl owns camera
    // state; this class owns TargetScreenUI text/crosshair population.
    internal static class TgpNativeOverlay
    {
        internal delegate bool LookPointProvider(Transform mount, out Vector3 hitPointLocal, out float rangeM);

        private static GameObject? _crosshairRoot;
        private static GameObject? _pointTrackBox;
        private static Canvas? _crosshairCanvas;
        private static float _overlayDiagLastLog;

        // Boresight crosshair + Point Track marker for the in-cockpit feed. Built from anchor-
        // stretched Image bars so it stays proportional on whatever display canvas the game uses.
        internal static void SyncCrosshair(Canvas displayCanvas, bool visible, bool pointTrackActive)
        {
            if (displayCanvas == null) return;
            if (_crosshairRoot != null && _crosshairCanvas != null && _crosshairCanvas != displayCanvas)
            {
                UnityEngine.Object.Destroy(_crosshairRoot);
                _crosshairRoot = null;
                _pointTrackBox = null;
                _crosshairCanvas = null;
            }
            if (_crosshairRoot == null)
            {
                // Box side length = 2 * gap. Arm length is exactly 2x that box side.
                const float gap = 0.028125f;
                const float armLength = 4f * gap;
                const float armEnd = 0.5f + gap + armLength;
                const float thickness = 0.005f;
                float half = thickness / 2f;

                _crosshairRoot = new GameObject("NOXMFD_ManualCrosshair", typeof(RectTransform));
                _crosshairRoot.transform.SetParent(displayCanvas.transform, false);
                _crosshairCanvas = displayCanvas;
                var rootRt = (RectTransform)_crosshairRoot.transform;
                rootRt.anchorMin = Vector2.zero;
                rootRt.anchorMax = Vector2.one;
                rootRt.offsetMin = rootRt.offsetMax = Vector2.zero;

                CreateBar(rootRt, "Top",    new Vector2(0.5f - half, 0.5f + gap),  new Vector2(0.5f + half, armEnd));
                CreateBar(rootRt, "Bottom", new Vector2(0.5f - half, 1f - armEnd), new Vector2(0.5f + half, 0.5f - gap));
                CreateBar(rootRt, "Left",   new Vector2(1f - armEnd, 0.5f - half), new Vector2(0.5f - gap, 0.5f + half));
                CreateBar(rootRt, "Right",  new Vector2(0.5f + gap, 0.5f - half),  new Vector2(armEnd, 0.5f + half));

                _pointTrackBox = new GameObject("PointTrackBox", typeof(RectTransform));
                _pointTrackBox.transform.SetParent(rootRt, false);
                var boxRt = (RectTransform)_pointTrackBox.transform;
                boxRt.anchorMin = Vector2.zero;
                boxRt.anchorMax = Vector2.one;
                boxRt.offsetMin = boxRt.offsetMax = Vector2.zero;

                CreateBar(boxRt, "BoxTop",    new Vector2(0.5f - gap, 0.5f + gap - half), new Vector2(0.5f + gap, 0.5f + gap + half));
                CreateBar(boxRt, "BoxBottom", new Vector2(0.5f - gap, 0.5f - gap - half), new Vector2(0.5f + gap, 0.5f - gap + half));
                CreateBar(boxRt, "BoxLeft",   new Vector2(0.5f - gap - half, 0.5f - gap), new Vector2(0.5f - gap + half, 0.5f + gap));
                CreateBar(boxRt, "BoxRight",  new Vector2(0.5f + gap - half, 0.5f - gap), new Vector2(0.5f + gap + half, 0.5f + gap));
            }

            _crosshairRoot.SetActive(visible);
            if (_pointTrackBox != null) _pointTrackBox.SetActive(visible && pointTrackActive);
        }

        private static void CreateBar(Transform parent, string name, Vector2 anchorMin, Vector2 anchorMax)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            Image img = go.GetComponent<Image>();
            img.color = Color.white;
            RectTransform rt = img.rectTransform;
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
        }

        // TextMeshProUGUI, not UnityEngine.UI.Text: TargetScreenUI uses TMP in the live game build.
        // Harmony field injection will not necessarily fail loudly if the declared type is stale.
        internal static void Populate(TargetCam tc, Transform mount,
            TextMeshProUGUI typeText, TextMeshProUGUI pilotText, TextMeshProUGUI noLock,
            TextMeshProUGUI distanceText, TextMeshProUGUI headingText, TextMeshProUGUI altitudeText,
            TextMeshProUGUI relAltitudeText, TextMeshProUGUI speedText, TextMeshProUGUI relSpeedText,
            TextMeshProUGUI magText, TextMeshProUGUI modeText, TextMeshProUGUI bearingText,
            TextMeshProUGUI gridText, Image bearingImg,
            bool pointTrackActive, Vector3 panDir, float desiredFov, LookPointProvider lookPointProvider)
        {
            noLock.gameObject.SetActive(false);
            typeText.gameObject.SetActive(true);
            pilotText.gameObject.SetActive(false);
            distanceText.gameObject.SetActive(true);
            headingText.gameObject.SetActive(true);
            altitudeText.gameObject.SetActive(true);
            relAltitudeText.gameObject.SetActive(true);
            speedText.gameObject.SetActive(false);
            relSpeedText.gameObject.SetActive(true);
            magText.gameObject.SetActive(true);
            modeText.gameObject.SetActive(true);
            bearingText.gameObject.SetActive(true);
            bearingImg.gameObject.SetActive(true);
            gridText.gameObject.SetActive(true);

            typeText.text = pointTrackActive ? "POINT TRACK" : "MANUAL";
            typeText.color = Color.white;

            magText.text = $"Mag x{10f / desiredFov:F1}";
            modeText.text = tc.UsingIR() ? "MODE: IR" : "MODE: COLOR";

            GameManager.GetLocalAircraft(out Aircraft ac);

            Vector3 localDir = ac != null ? ac.transform.InverseTransformDirection(panDir) : panDir;
            (float az, float el) = TgpManualAimMath.ToAzimuthElevation(localDir.x, localDir.y, localDir.z);
            bearingText.text = $"{az:F0}°";
            bearingImg.rectTransform.localEulerAngles = new Vector3(0f, 0f, -az);
            headingText.text = $"EL {el:F0}°";

            Vector3 hitLocal = default;
            float rangeM = 0f;
            bool hasHit = ac != null && lookPointProvider(mount, out hitLocal, out rangeM);
            if (hasHit && ac != null)
            {
                GlobalPosition hitGlobal = hitLocal.ToGlobalPosition();
                Vector3 rel = hitGlobal - ac.GlobalPosition();
                Vector3 velocity = ac.rb != null ? ac.rb.velocity : Vector3.zero;
                float closure = Vector3.Dot(velocity, rel.normalized);

                distanceText.text    = "RNG " + UnitConverter.DistanceReading(rangeM);
                altitudeText.text    = "ALT " + UnitConverter.AltitudeReading(hitGlobal.y);
                relAltitudeText.text = "REL " + UnitConverter.AltitudeReading(rel.y);
                relSpeedText.text    = "CLO " + UnitConverter.SpeedReading(closure);
                gridText.text        = "GRID: " + SceneSingleton<DynamicMap>.i.gridLabels.GetGridPosition(hitGlobal);
            }
            else
            {
                distanceText.text    = "RNG -";
                altitudeText.text    = "ALT -";
                relAltitudeText.text = "REL -";
                relSpeedText.text    = "CLO -";
                gridText.text        = "GRID: -";
            }

            if (Time.time - _overlayDiagLastLog > 1f)
            {
                _overlayDiagLastLog = Time.time;
                Plugin.Log?.LogDebug($"[NOXMFD] TGP native overlay diag: az={az:0.0} el={el:0.0} pointTrack={pointTrackActive} hit={hasHit} rangeM={(hasHit ? rangeM.ToString("0") : "-")}.");
            }
        }
    }
}
