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
        // ponytail: a fixed cap, not the user's actual desktop resolution or a configurable
        // setting — keeps the mirror camera's GPU cost predictable without a new settings surface.
        // Upgrade path: promote to a RatesConfig-style hidden ConfigEntry if 1080p ever feels like
        // the wrong ceiling in practice.
        private const int MaxWidth  = 1920;
        private const int MaxHeight = 1080;
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
        private static TextMeshProUGUI? _headingText;
        private static TextMeshProUGUI? _modeText;

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

            int w = Mathf.Min(Screen.width, MaxWidth);
            int h = Mathf.Min(Screen.height, MaxHeight);
            Mirror.Engage(tc, w, h);
            Mirror.SyncFromSource(cam);
            if (_feedImage != null) _feedImage.texture = Mirror.Texture;

            if (HudVisible) PopulateOverlay(tc, mount, ac);
        }

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
                _headingText!.text = "HDG -";
                _modeText!.text = "";
                return;
            }

            _typeText.text = hasTargets ? Overlay.TargetType : (Overlay.PointTrackActive ? "POINT TRACK" : "MANUAL");
            _rangeText!.text = "RNG " + UnitConverter.DistanceReading(Overlay.RangeM);
            _altText!.text = "ALT " + UnitConverter.AltitudeReading(Overlay.AltitudeM);
            _headingText!.text = hasTargets ? $"BRG {Overlay.BearingDeg:F0}°" : $"EL {Overlay.ElevationDeg:F0}°";
            _modeText!.text = Overlay.IR ? "IR" : "COLOR";
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

            const float margin = 20f;
            const float rowH = 32f;
            _typeText    = CreateLabel(_hudGroup.transform, "Type", TextAlignmentOptions.TopLeft,
                new Vector2(0f, 1f), new Vector2(margin, -margin));
            _rangeText   = CreateLabel(_hudGroup.transform, "Range", TextAlignmentOptions.BottomLeft,
                new Vector2(0f, 0f), new Vector2(margin, margin));
            _altText     = CreateLabel(_hudGroup.transform, "Alt", TextAlignmentOptions.BottomLeft,
                new Vector2(0f, 0f), new Vector2(margin, margin + rowH));
            _headingText = CreateLabel(_hudGroup.transform, "Heading", TextAlignmentOptions.BottomRight,
                new Vector2(1f, 0f), new Vector2(-margin, margin));
            _modeText    = CreateLabel(_hudGroup.transform, "Mode", TextAlignmentOptions.TopRight,
                new Vector2(1f, 1f), new Vector2(-margin, -margin));
        }

        // anchor = which screen corner (0/1 per axis); offsetFromCorner = pixel offset from that
        // corner toward the screen's center (sign already baked in by the caller per corner).
        private static TextMeshProUGUI CreateLabel(Transform parent, string name, TextAlignmentOptions alignment,
            Vector2 anchor, Vector2 offsetFromCorner)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = anchor;
            rt.pivot = anchor;
            rt.sizeDelta = new Vector2(400f, 32f);
            rt.anchoredPosition = offsetFromCorner;

            var text = go.AddComponent<TextMeshProUGUI>();
            text.alignment = alignment;
            text.color = new Color(1f, 0.6667f, 0f, 1f);
            text.fontSize = 24f;
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
