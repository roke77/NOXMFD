using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace NOXMFD
{
    // Cinematic full-screen TGP view (issue #70, docs/tgp-full-screen.md). Unlike the native
    // in-cockpit TGP screen (a small, fixed-resolution instrument texture projected onto a 3D
    // screen mesh — TargetCam.targetScreenRenderer, see TgpFeed.cs), this owns its own TgpMirrorCam
    // instance sized to the display, the same "independent high-res mirror" technique TgpMirrorCam
    // already uses for the web /tgp page's HIGH quality setting — just at a bigger target size and
    // a dedicated instance, since the web pipeline's own mirror (if engaged) wants its own size.
    //
    // Static class (like TgpManualControl), ticked every frame from TelemetryReader regardless of
    // whether any browser page is open — this is a native-only feature.
    internal static class TgpFullScreen
    {
        private const int OverlaySortingOrder = 40;

        internal static bool Active { get; private set; }
        internal static bool HudVisible { get; private set; } = true;

        private static readonly TgpMirrorCam Mirror = new TgpMirrorCam();
        private static readonly TgpOverlay Overlay = new TgpOverlay();

        private static GameObject? _canvasGo;
        private static Canvas? _canvas;
        private static RawImage? _feedImage;
        private static GameObject? _hudGroup;
        private static TextMeshProUGUI? _typeText;
        private static TextMeshProUGUI? _rangeText;
        private static TextMeshProUGUI? _altText;
        private static TextMeshProUGUI? _relAltText;
        private static TextMeshProUGUI? _closureText;
        private static TextMeshProUGUI? _headingText;
        private static TextMeshProUGUI? _bearingText;
        private static TextMeshProUGUI? _magText;
        private static TextMeshProUGUI? _modeText;
        private static TextMeshProUGUI? _gridText;

        internal static void Toggle()
        {
            if (Active) Exit();
            else Enter();
        }

        internal static void ToggleHud()
        {
            if (!Active) return;
            HudVisible = !HudVisible;
            if (_hudGroup != null) _hudGroup.SetActive(HudVisible);
        }

        // Called every frame from TelemetryReader alongside TgpFeed.Tick/TgpManualControl.Tick —
        // cheap no-op while off.
        internal static void Tick(float dt)
        {
            if (!Active) return;

            // Yield to the game's own UI first — a full-screen video feed with the pause menu or
            // the M-key map trying to render underneath it makes no sense.
            if (GameplayUI.GameIsPaused || DynamicMap.mapMaximized) { Exit(); return; }

            GameManager.GetLocalAircraft(out Aircraft ac);
            TargetCam? tc = ac != null ? ac.targetCam : null;
            if (ac == null || tc == null) { Exit(); return; }

            if (!TgpManualTargetCamAccess.Ensure()) { Exit(); return; }
            Camera? cam = TgpManualTargetCamAccess.GetCamera(tc);
            Transform? mount = TgpManualTargetCamAccess.GetMount(tc);
            if (cam == null || !cam.enabled || mount == null) { Exit(); return; }

            if (TgpManualTargetCamAccess.IsLandingMode(tc)) { Exit(); return; }

            // Matches the game's own current back-buffer size — a real "cockpit resolution" feed,
            // not a downscaled preview. Screen.width/height already reflect whatever display mode
            // the player has chosen (windowed, borderless, exclusive fullscreen).
            Mirror.Engage(tc, Screen.width, Screen.height);
            Mirror.SyncFromSource(cam);
            Mirror.SetInfrared(tc.UsingIR());
            if (_feedImage != null) _feedImage.texture = Mirror.Texture;

            if (HudVisible) PopulateOverlay(tc, mount, ac);
        }

        // Mirrors TgpNativeOverlay.Populate's own field set (the manual in-cockpit overlay) rather
        // than inventing a smaller one — same labels, same source data (TgpOverlay), just a plain
        // stacked column instead of that overlay's fixed corner-anchored layout.
        private static void PopulateOverlay(TargetCam tc, Transform mount, Aircraft ac)
        {
            List<Unit>? targets = ac.weaponManager != null ? ac.weaponManager.GetTargetList() : null;
            bool hasTargets = targets != null && targets.Count > 0;

            // Boxes (Overlay.Boxes) aren't drawn in this pass — WorldToViewport is still the correct
            // projection to feed Populate() with, for when per-target box rendering is added here.
            if (hasTargets)
                Overlay.Populate(tc, targets, ac, Mirror.WorldToViewport);
            else if (TgpManualControl.ManualMode)
                Overlay.PopulateManual(tc, mount, ac);
            else
                Overlay.Clear();

            if (_typeText == null) return;

            if (!hasTargets && !TgpManualControl.ManualMode)
            {
                _typeText.text = "NO TARGET";
                _rangeText!.text = "RNG -";
                _altText!.text = "ALT -";
                _relAltText!.text = "REL -";
                _closureText!.text = "CLO -";
                _headingText!.text = "";
                _bearingText!.text = "";
                _magText!.text = "";
                _modeText!.text = "";
                _gridText!.text = "GRID: -";
                return;
            }

            _typeText.text = hasTargets ? Overlay.TargetType : (Overlay.PointTrackActive ? "POINT TRACK" : "MANUAL");
            if (!string.IsNullOrEmpty(Overlay.Pilot)) _typeText.text += " — " + Overlay.Pilot;

            _rangeText!.text   = "RNG " + UnitConverter.DistanceReading(Overlay.RangeM);
            _altText!.text     = "ALT " + UnitConverter.AltitudeReading(Overlay.AltitudeM);
            _relAltText!.text  = "REL " + UnitConverter.AltitudeReading(Overlay.RelAltitudeM);
            _closureText!.text = "CLO " + UnitConverter.SpeedReading(Overlay.RelSpeedMps);
            // headingText slot is overloaded exactly like TgpNativeOverlay.Populate's own: a
            // locked target's own compass heading, or (manual mode, no lock) camera elevation.
            _headingText!.text = hasTargets ? $"HDG {Overlay.HeadingDeg:F0}°" : $"EL {Overlay.ElevationDeg:F0}°";
            _bearingText!.text = $"{Overlay.BearingDeg:F0}°";
            _magText!.text     = $"Mag x{Overlay.Mag:F1}";
            _modeText!.text    = Overlay.IR ? "MODE: IR" : "MODE: COLOR";
            _gridText!.text    = "GRID: " + Overlay.Grid;
        }

        private static void Enter()
        {
            if (GameplayUI.GameIsPaused || DynamicMap.mapMaximized) return;

            GameManager.GetLocalAircraft(out Aircraft ac);
            TargetCam? tc = ac != null ? ac.targetCam : null;
            if (tc == null || !TgpManualTargetCamAccess.Ensure()) return;
            Camera? cam = TgpManualTargetCamAccess.GetCamera(tc);
            if (cam == null || !cam.enabled) return;

            EnsureUi();
            Active = true;
            _canvasGo!.SetActive(true);
        }

        private static void Exit()
        {
            Active = false;
            Mirror.Disengage();
            if (_canvasGo != null) _canvasGo.SetActive(false);
        }

        private static void EnsureUi()
        {
            if (_canvasGo != null) return;

            _canvasGo = new GameObject("NOXMFD.TgpFullScreen");
            _canvas = _canvasGo.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = OverlaySortingOrder;
            var group = _canvasGo.AddComponent<CanvasGroup>();
            group.interactable = false;
            group.blocksRaycasts = false;

            var bgGo = new GameObject("Backdrop", typeof(RectTransform), typeof(Image));
            bgGo.transform.SetParent(_canvasGo.transform, false);
            Stretch((RectTransform)bgGo.transform);
            Image bg = bgGo.GetComponent<Image>();
            bg.color = Color.black;
            bg.raycastTarget = false;

            var feedGo = new GameObject("Feed", typeof(RectTransform), typeof(RawImage));
            feedGo.transform.SetParent(_canvasGo.transform, false);
            Stretch((RectTransform)feedGo.transform);
            _feedImage = feedGo.GetComponent<RawImage>();
            _feedImage.raycastTarget = false;

            _hudGroup = new GameObject("Hud", typeof(RectTransform));
            _hudGroup.transform.SetParent(_canvasGo.transform, false);
            Stretch((RectTransform)_hudGroup.transform);
            _hudGroup.SetActive(HudVisible);

            // One stacked column, top-left — plain and readable rather than the native overlay's
            // fixed corner layout, which has no equivalent screen real estate to anchor to here.
            const float margin = 20f;
            const float rowH = 30f;
            _typeText     = CreateRow(_hudGroup.transform, "Type",     margin, rowH * 0f);
            _rangeText    = CreateRow(_hudGroup.transform, "Range",    margin, rowH * 1f);
            _altText      = CreateRow(_hudGroup.transform, "Alt",      margin, rowH * 2f);
            _relAltText   = CreateRow(_hudGroup.transform, "RelAlt",   margin, rowH * 3f);
            _closureText  = CreateRow(_hudGroup.transform, "Closure",  margin, rowH * 4f);
            _headingText  = CreateRow(_hudGroup.transform, "Heading",  margin, rowH * 5f);
            _bearingText  = CreateRow(_hudGroup.transform, "Bearing",  margin, rowH * 6f);
            _magText      = CreateRow(_hudGroup.transform, "Mag",      margin, rowH * 7f);
            _modeText     = CreateRow(_hudGroup.transform, "Mode",     margin, rowH * 8f);
            _gridText     = CreateRow(_hudGroup.transform, "Grid",     margin, rowH * 9f);
        }

        // One row of a top-left stacked column, `rowOffset` pixels below the top edge.
        private static TextMeshProUGUI CreateRow(Transform parent, string name, float margin, float rowOffset)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.sizeDelta = new Vector2(500f, 28f);
            rt.anchoredPosition = new Vector2(margin, -margin - rowOffset);

            var text = go.AddComponent<TextMeshProUGUI>();
            text.alignment = TextAlignmentOptions.TopLeft;
            text.color = new Color(1f, 0.6667f, 0f, 1f);
            text.fontSize = 22f;
            return text;
        }

        private static void Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }
    }
}
