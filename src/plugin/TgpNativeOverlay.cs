using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace NOXMFD
{
    // Manual-mode overlay for the game's native in-cockpit TGP screen. TgpManualControl owns camera
    // state; this class owns TargetScreenUI text/crosshair population.
    internal static class TgpNativeOverlay
    {
        private static GameObject? _crosshairRoot;
        private static GameObject? _pointTrackBox;
        private static GameObject? _soiLabel;
        private static Canvas? _crosshairCanvas;
        private static float _overlayDiagLastLog;

        // Boresight crosshair + Point Track marker for the in-cockpit feed. Built from anchor-
        // stretched Image bars so it stays proportional on whatever display canvas the game uses.
        internal static void SyncCrosshair(Canvas displayCanvas, bool visible, bool pointTrackActive, bool isTgpSoi)
        {
            if (displayCanvas == null) return;
            if (_crosshairRoot != null && _crosshairCanvas != null && _crosshairCanvas != displayCanvas)
            {
                UnityEngine.Object.Destroy(_crosshairRoot);
                _crosshairRoot = null;
                _pointTrackBox = null;
                _soiLabel = null;
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

                // "SOI" tag (docs/tgp-manual-control.md's PAD Cursor consolidation plan) — centered
                // horizontally, vertically centered between the bottom of the camera feed (y=0) and
                // the bottom edge of the Bottom arm above (y = 1 - armEnd), so it reads as attached
                // to the crosshair without overlapping it. A tight chip (~12% wide), not a wide bar —
                // sized to the 3-letter text, matching the other data fields' own translucent
                // background (see tgp.css's rgba(0,0,0,0.6) chips on the web TGP page — same look,
                // separate rendering surface). Auto-sized to its box rather than a fixed point size,
                // since this canvas's real pixel scale isn't known here. Uses TMP's default font
                // rather than copying the game's own TargetScreenUI style — a known simplification;
                // revisit if it looks visually mismatched next to the real fields.
                float lowerArmBottom = 1f - armEnd;
                float soiY = lowerArmBottom / 2f;
                const float soiHalfWidth = 0.06f;
                const float soiHalfHeight = 0.025f;
                _soiLabel = new GameObject("NOXMFD_SoiLabel", typeof(RectTransform));
                _soiLabel.transform.SetParent(rootRt, false);
                var soiRt = (RectTransform)_soiLabel.transform;
                soiRt.anchorMin = new Vector2(0.5f - soiHalfWidth, soiY - soiHalfHeight);
                soiRt.anchorMax = new Vector2(0.5f + soiHalfWidth, soiY + soiHalfHeight);
                soiRt.offsetMin = soiRt.offsetMax = Vector2.zero;

                var soiBg = new GameObject("Bg", typeof(RectTransform), typeof(Image));
                soiBg.transform.SetParent(soiRt, false);
                var soiBgRt = (RectTransform)soiBg.transform;
                soiBgRt.anchorMin = Vector2.zero;
                soiBgRt.anchorMax = Vector2.one;
                soiBgRt.offsetMin = soiBgRt.offsetMax = Vector2.zero;
                soiBg.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.6f);

                var soiTextGo = new GameObject("Text", typeof(RectTransform));
                soiTextGo.transform.SetParent(soiRt, false);
                var soiTextRt = (RectTransform)soiTextGo.transform;
                soiTextRt.anchorMin = Vector2.zero;
                soiTextRt.anchorMax = Vector2.one;
                soiTextRt.offsetMin = soiTextRt.offsetMax = Vector2.zero;
                var soiText = soiTextGo.AddComponent<TextMeshProUGUI>();
                soiText.text = "SOI";
                soiText.alignment = TextAlignmentOptions.Center;
                soiText.color = Color.white;
                soiText.enableAutoSizing = true;
                soiText.fontSizeMin = 8f;
                soiText.fontSizeMax = 36f;
            }

            _crosshairRoot.SetActive(visible);
            if (_pointTrackBox != null) _pointTrackBox.SetActive(visible && pointTrackActive);
            if (_soiLabel != null) _soiLabel.SetActive(visible && isTgpSoi);
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
        //
        // The actual numbers come from TgpManualControl.ComputeOverlaySample — shared with
        // TgpOverlay.PopulateManual (the external web TGP page gets the same data, docs/tgp-
        // manual-control.md's "In-cockpit overlay") so neither one re-derives the az/el/raycast
        // math independently.
        internal static void Populate(TargetCam tc, Transform mount,
            TextMeshProUGUI typeText, TextMeshProUGUI pilotText, TextMeshProUGUI noLock,
            TextMeshProUGUI distanceText, TextMeshProUGUI headingText, TextMeshProUGUI altitudeText,
            TextMeshProUGUI relAltitudeText, TextMeshProUGUI speedText, TextMeshProUGUI relSpeedText,
            TextMeshProUGUI magText, TextMeshProUGUI modeText, TextMeshProUGUI bearingText,
            TextMeshProUGUI gridText, Image bearingImg)
        {
            noLock.gameObject.SetActive(false);
            typeText.gameObject.SetActive(true);
            pilotText.gameObject.SetActive(false);
            distanceText.gameObject.SetActive(true);
            headingText.gameObject.SetActive(true);
            altitudeText.gameObject.SetActive(true);
            relAltitudeText.gameObject.SetActive(true);
            // speedText (own-aircraft SPD) duplicates the flight HUD (same call the web TGP page's
            // applyManualOverlay makes), so it's always hidden in manual mode.
            speedText.gameObject.SetActive(false);
            relSpeedText.gameObject.SetActive(true);
            magText.gameObject.SetActive(true);
            modeText.gameObject.SetActive(true);
            bearingText.gameObject.SetActive(true);
            bearingImg.gameObject.SetActive(true);
            gridText.gameObject.SetActive(true);

            GameManager.GetLocalAircraft(out Aircraft ac);
            TgpManualControl.ManualOverlaySample s = TgpManualControl.ComputeOverlaySample(tc, mount, ac);

            typeText.text = s.PointTrackActive ? "POINT TRACK" : "MANUAL";
            typeText.color = Color.white;

            magText.text = $"Mag x{s.Mag:F1}";
            modeText.text = s.IR ? "MODE: IR" : "MODE: COLOR";

            bearingText.text = $"{s.AzimuthDeg:F0}°";
            bearingImg.rectTransform.localEulerAngles = new Vector3(0f, 0f, -s.AzimuthDeg);
            headingText.text = $"EL {s.ElevationDeg:F0}°";

            if (s.HasHit)
            {
                distanceText.text    = "RNG " + UnitConverter.DistanceReading(s.RangeM);
                altitudeText.text    = "ALT " + UnitConverter.AltitudeReading(s.AltitudeM);
                relAltitudeText.text = "REL " + UnitConverter.AltitudeReading(s.RelAltitudeM);
                relSpeedText.text    = "CLO " + UnitConverter.SpeedReading(s.ClosureMps);
                gridText.text        = "GRID: " + s.Grid;
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
                Plugin.Log?.LogDebug($"[NOXMFD] TGP native overlay diag: az={s.AzimuthDeg:0.0} el={s.ElevationDeg:0.0} pointTrack={s.PointTrackActive} hit={s.HasHit} rangeM={(s.HasHit ? s.RangeM.ToString("0") : "-")}.");
            }
        }
    }
}
