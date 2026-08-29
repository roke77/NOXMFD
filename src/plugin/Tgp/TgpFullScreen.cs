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
    // Overlay layout/labels/colors mirror the web TGP page's own corner-group design (tgp.js's
    // applyOverlay/applyManualOverlay, tgp.css's .tgp-ov-* classes) field for field, rather than a
    // new arrangement — same reference the user asked to match visually.
    //
    // Static class (like TgpManualControl), ticked every frame from TelemetryReader regardless of
    // whether any browser page is open — this is a native-only feature.
    internal static class TgpFullScreen
    {
        private const int OverlaySortingOrder = 40;

        // No CanvasScaler on this ScreenSpaceOverlay canvas — RectTransform sizes are real screen
        // pixels, so this is a flat multiplier on every base size below (fonts, padding, spacing,
        // compass/needle) rather than the web TGP page's own fixed 15-18px CSS values, which read
        // as tiny on a large/high-resolution monitor. ponytail: a fixed 5x, not resolution-aware
        // scaling (a CanvasScaler would need the crosshair's own Screen.width/height-based sizing
        // reworked to match its reference-resolution space) — revisit if 5x is wrong for some other
        // display size, not just the one this was tuned against.
        private const float UiScale = 5f;
        private const float TitleFontSize = 20f * UiScale;
        private const float ChipFontSize  = 20f * UiScale;
        private const float PilotFontSize = 15f * UiScale;
        private const float ChipPaddingH  = 8f * UiScale;
        private const float ChipPaddingV  = 3f * UiScale;
        private const float StackPadding  = 8f * UiScale;
        private const float StackSpacing  = 3f * UiScale;
        private const float StackMargin   = 16f * UiScale;
        private const float TitleSpacing  = 5f * UiScale;
        private const float CompassSize   = 40f * UiScale;
        private const float NeedleWidth   = 2f * UiScale;
        private const float NeedleHeight  = 18f * UiScale;
        private const float BottomLeftMargin  = 8f * UiScale;
        private const float BottomLeftSpacing = 8f * UiScale;

        // Matches theme.css's --no-white/--no-red/--no-blue tokens (the web TGP page's own overlay
        // colors) so the native version reads the same, not this mod's usual amber HUD-cue color.
        private static readonly Color White  = new Color32(230, 235, 239, 255);
        private static readonly Color Red    = new Color32(255, 64, 64, 255);
        private static readonly Color Blue   = new Color32(77, 159, 255, 255);
        private static readonly Color Amber  = new Color32(255, 170, 0, 255);
        private static readonly Color ChipBackground = new Color(0f, 0f, 0f, 0.6f);

        internal static bool Active { get; private set; }
        internal static bool HudVisible { get; private set; } = true;

        private static readonly TgpMirrorCam Mirror = new TgpMirrorCam();
        private static readonly TgpOverlay Overlay = new TgpOverlay();

        private static GameObject? _canvasGo;
        private static Canvas? _canvas;
        private static RawImage? _feedImage;
        private static GameObject? _hudGroup;
        private static RectTransform? _crosshair;
        private static GameObject? _pointTrackBox;

        // Top-left: title (+ status tag), pilot, RNG/ALT/SPD.
        private static TextMeshProUGUI? _typeText;
        private static TextMeshProUGUI? _tagText;
        private static TextMeshProUGUI? _pilotText;
        private static TextMeshProUGUI? _rngText;
        private static TextMeshProUGUI? _altText;
        private static GameObject? _spdRow;
        private static TextMeshProUGUI? _spdText;

        // Top-right: HDG-or-EL, REL (altitude), REL-or-CLO (speed/closure).
        private static TextMeshProUGUI? _hdgText;
        private static TextMeshProUGUI? _relAltText;
        private static TextMeshProUGUI? _relSpdText;

        // Bottom-left: compass needle + numeric bearing.
        private static RectTransform? _needle;
        private static TextMeshProUGUI? _bearingText;

        // Bottom-right: GRID, MODE, MAG.
        private static TextMeshProUGUI? _gridText;
        private static TextMeshProUGUI? _modeText;
        private static TextMeshProUGUI? _magText;

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

        // Mirrors tgp.js's applyOverlay/applyManualOverlay field-for-field — same labels, same
        // corner grouping, same source data (TgpOverlay) — just rendered natively instead of into
        // that page's DOM.
        private static void PopulateOverlay(TargetCam tc, Transform mount, Aircraft ac)
        {
            List<Unit>? targets = ac.weaponManager != null ? ac.weaponManager.GetTargetList() : null;
            bool hasTargets = targets != null && targets.Count > 0;
            bool manual = !hasTargets && TgpManualControl.ManualMode;

            // Boxes (Overlay.Boxes) aren't drawn in this pass — WorldToViewport is still the correct
            // projection to feed Populate() with, for when per-target box rendering is added here.
            if (hasTargets)
                Overlay.Populate(tc, targets, ac, Mirror.WorldToViewport);
            else if (manual)
                Overlay.PopulateManual(tc, mount, ac);
            else
                Overlay.Clear();

            // Boresight crosshair — same shape as the native in-cockpit manual overlay
            // (TgpNativeOverlay.SyncCrosshair): only meaningful while manual mode owns the camera,
            // not over a real lock (the lock box, not drawn here yet, is that case's own reference).
            if (_crosshair != null) _crosshair.gameObject.SetActive(manual);
            if (_pointTrackBox != null) _pointTrackBox.SetActive(manual && Overlay.PointTrackActive);

            if (_typeText == null) return;

            if (!hasTargets && !manual)
            {
                _typeText.text = "NO TARGET";
                _typeText.color = White;
                _tagText!.text = "";
                _pilotText!.text = "";
                _spdRow!.SetActive(false);
                _rngText!.text = "RNG -";
                _altText!.text = "ALT -";
                _hdgText!.text = "HDG -";
                _relAltText!.text = "REL -";
                _relSpdText!.text = "REL -";
                _bearingText!.text = "";
                _needle!.gameObject.SetActive(false);
                _gridText!.text = "GRID: -";
                _modeText!.text = "";
                _magText!.text = "";
                return;
            }

            if (hasTargets)
            {
                _typeText.text = Overlay.TargetType;
                _typeText.color = Overlay.Status == "friendly" ? Blue : Red;
                _tagText!.text = Overlay.Status switch
                {
                    "jammed" => "[JAM]",
                    "lased" => "[LASE]",
                    "outdated" => "[OLD]",
                    _ => "",
                };
                _pilotText!.text = Overlay.Pilot ?? "";
                _spdRow!.SetActive(true);
                _spdText!.text = "SPD " + UnitConverter.SpeedReading(Overlay.SpeedMps);
                _hdgText!.text = $"HDG {Overlay.HeadingDeg:F0}°";
            }
            else
            {
                _typeText.text = Overlay.PointTrackActive ? "POINT TRACK" : "MANUAL";
                _typeText.color = White;
                _tagText!.text = "";
                _pilotText!.text = "";
                _spdRow!.SetActive(false);
                _hdgText!.text = $"EL {Overlay.ElevationDeg:F0}°";
            }

            _rngText!.text     = "RNG " + UnitConverter.DistanceReading(Overlay.RangeM);
            _altText!.text     = "ALT " + UnitConverter.AltitudeReading(Overlay.AltitudeM);
            _relAltText!.text  = "REL " + UnitConverter.AltitudeReading(Overlay.RelAltitudeM);
            _relSpdText!.text  = (hasTargets ? "REL " : "CLO ") + UnitConverter.SpeedReading(Overlay.RelSpeedMps);

            _needle!.gameObject.SetActive(true);
            _needle.localRotation = Quaternion.Euler(0f, 0f, -(Overlay.BearingDeg + 180f));
            _bearingText!.text = $"{Overlay.BearingDeg:F0}°";
            _gridText!.text    = "GRID: " + Overlay.Grid;
            _modeText!.text    = Overlay.IR ? "MODE: IR" : "MODE: COLOR";
            _magText!.text     = $"Mag x{Overlay.Mag:F1}";
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

            BuildCrosshair(_hudGroup.transform);
            BuildTopLeft(_hudGroup.transform);
            BuildTopRight(_hudGroup.transform);
            BuildBottomLeft(_hudGroup.transform);
            BuildBottomRight(_hudGroup.transform);
        }

        // tgp.css .tgp-ov-tl: title (+ tag), pilot (dim, smaller), RNG, ALT, SPD.
        private static void BuildTopLeft(Transform parent)
        {
            RectTransform stack = CreateStack(parent, "TopLeft", new Vector2(0f, 1f));

            // tgp.css gives .tgp-ov-title the same chip background as .tgp-ov-stat — title and tag
            // share one background here too, not two separate chips.
            GameObject titleRow = new GameObject("Title", typeof(RectTransform), typeof(Image),
                typeof(HorizontalLayoutGroup), typeof(ContentSizeFitter));
            titleRow.transform.SetParent(stack, false);
            titleRow.GetComponent<Image>().color = ChipBackground;
            HorizontalLayoutGroup titleLayout = titleRow.GetComponent<HorizontalLayoutGroup>();
            titleLayout.padding = new RectOffset((int)ChipPaddingH, (int)ChipPaddingH, (int)ChipPaddingV, (int)ChipPaddingV);
            titleLayout.spacing = TitleSpacing;
            titleLayout.childControlWidth = titleLayout.childControlHeight = true;
            titleLayout.childForceExpandWidth = titleLayout.childForceExpandHeight = false;
            ContentSizeFitter titleFitter = titleRow.GetComponent<ContentSizeFitter>();
            titleFitter.horizontalFit = titleFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            _typeText = CreatePlainLabel(titleRow.transform, "Type", TitleFontSize, FontStyles.Bold);
            _tagText  = CreatePlainLabel(titleRow.transform, "Tag", TitleFontSize, FontStyles.Bold);
            _tagText.color = Amber;

            // No chip background here — tgp.css's .tgp-ov-pilot is plain dim text under the title
            // chip, not its own pill.
            _pilotText = CreatePlainLabel(stack, "Pilot", PilotFontSize, FontStyles.Normal);
            _pilotText.color = new Color(0.7f, 0.75f, 0.78f, 1f);

            _rngText = CreateChip(stack, "Rng");
            _altText = CreateChip(stack, "Alt");
            _spdText = CreateChip(stack, "Spd");
            _spdRow  = _spdText.transform.parent.gameObject;
        }

        // tgp.css .tgp-ov-tr: HDG-or-EL, REL (altitude), REL-or-CLO (speed).
        private static void BuildTopRight(Transform parent)
        {
            RectTransform stack = CreateStack(parent, "TopRight", new Vector2(1f, 1f));
            _hdgText    = CreateChip(stack, "Hdg");
            _relAltText = CreateChip(stack, "RelAlt");
            _relSpdText = CreateChip(stack, "RelSpd");
        }

        // tgp.css .tgp-ov-bl: a compass ring (approximated as a square chip — no circular sprite
        // asset available at runtime) with a rotating needle, plus a separate bearing-degrees chip.
        private static void BuildBottomLeft(Transform parent)
        {
            var row = new GameObject("BottomLeft", typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(ContentSizeFitter));
            row.transform.SetParent(parent, false);
            var rt = (RectTransform)row.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 0f);
            rt.pivot = new Vector2(0f, 0f);
            rt.anchoredPosition = new Vector2(BottomLeftMargin, BottomLeftMargin);

            HorizontalLayoutGroup layout = row.GetComponent<HorizontalLayoutGroup>();
            layout.spacing = BottomLeftSpacing;
            layout.childAlignment = TextAnchor.MiddleLeft;
            layout.childControlWidth = layout.childControlHeight = true;
            layout.childForceExpandWidth = layout.childForceExpandHeight = false;
            ContentSizeFitter fitter = row.GetComponent<ContentSizeFitter>();
            fitter.horizontalFit = fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var compassGo = new GameObject("Compass", typeof(RectTransform), typeof(Image), typeof(LayoutElement));
            compassGo.transform.SetParent(row.transform, false);
            compassGo.GetComponent<Image>().color = ChipBackground;
            LayoutElement compassLayout = compassGo.GetComponent<LayoutElement>();
            compassLayout.minWidth = compassLayout.preferredWidth = CompassSize;
            compassLayout.minHeight = compassLayout.preferredHeight = CompassSize;

            var needleGo = new GameObject("Needle", typeof(RectTransform), typeof(Image));
            needleGo.transform.SetParent(compassGo.transform, false);
            needleGo.GetComponent<Image>().color = White;
            _needle = (RectTransform)needleGo.transform;
            _needle.anchorMin = _needle.anchorMax = new Vector2(0.5f, 0.5f);
            _needle.pivot = new Vector2(0.5f, 0f);
            _needle.sizeDelta = new Vector2(NeedleWidth, NeedleHeight);
            _needle.anchoredPosition = Vector2.zero;

            _bearingText = CreateChip(row.transform, "Bearing");
        }

        // tgp.css .tgp-ov-br: GRID, MODE, MAG.
        private static void BuildBottomRight(Transform parent)
        {
            RectTransform stack = CreateStack(parent, "BottomRight", new Vector2(1f, 0f));
            _gridText = CreateChip(stack, "Grid");
            _modeText = CreateChip(stack, "Mode");
            _magText  = CreateChip(stack, "Mag");
        }

        // A corner-anchored vertical stack (tgp.css .tgp-ov-stack) — children lay themselves out
        // top-to-bottom via VerticalLayoutGroup, sized to their own content via ContentSizeFitter,
        // so callers never compute row offsets by hand.
        private static RectTransform CreateStack(Transform parent, string name, Vector2 anchor)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = anchor;
            rt.pivot = anchor;
            rt.anchoredPosition = new Vector2((anchor.x - 0.5f) * -StackMargin, (anchor.y - 0.5f) * -StackMargin);

            VerticalLayoutGroup layout = go.GetComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset((int)StackPadding, (int)StackPadding, (int)StackPadding, (int)StackPadding);
            layout.spacing = StackSpacing;
            layout.childAlignment = anchor.x > 0.5f ? TextAnchor.UpperRight : TextAnchor.UpperLeft;
            layout.childControlWidth = layout.childControlHeight = true;
            layout.childForceExpandWidth = layout.childForceExpandHeight = false;

            ContentSizeFitter fitter = go.GetComponent<ContentSizeFitter>();
            fitter.horizontalFit = fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            return rt;
        }

        // One translucent black pill (tgp.css .tgp-ov-stat) sized to its own text.
        private static TextMeshProUGUI CreateChip(Transform parent, string name)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image),
                typeof(HorizontalLayoutGroup), typeof(ContentSizeFitter));
            go.transform.SetParent(parent, false);
            go.GetComponent<Image>().color = ChipBackground;

            HorizontalLayoutGroup layout = go.GetComponent<HorizontalLayoutGroup>();
            layout.padding = new RectOffset((int)ChipPaddingH, (int)ChipPaddingH, (int)ChipPaddingV, (int)ChipPaddingV);
            layout.childControlWidth = layout.childControlHeight = true;
            layout.childForceExpandWidth = layout.childForceExpandHeight = false;

            ContentSizeFitter fitter = go.GetComponent<ContentSizeFitter>();
            fitter.horizontalFit = fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            return CreatePlainLabel(go.transform, "Text", ChipFontSize, FontStyles.Normal);
        }

        // Bare TextMeshProUGUI, no background — used both standalone (title, pilot) and nested
        // inside a chip's own background (CreateChip).
        private static TextMeshProUGUI CreatePlainLabel(Transform parent, string name, float fontSize, FontStyles style)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var text = go.AddComponent<TextMeshProUGUI>();
            text.alignment = TextAlignmentOptions.TopLeft;
            text.color = White;
            text.fontSize = fontSize;
            text.fontStyle = style;
            text.enableWordWrapping = false;
            return text;
        }

        // Boresight crosshair + Point Track box for manual mode — same bar layout/proportions as
        // TgpNativeOverlay.SyncCrosshair's own in-cockpit version, built independently rather than
        // shared: that class keeps a single static instance meant for one active consumer (the
        // native screen) at a time, and full screen + manual mode can both be active together.
        private static void BuildCrosshair(Transform parent)
        {
            const float gap = 0.028125f;
            const float armLength = 4f * gap;
            const float armEnd = 0.5f + gap + armLength;
            const float thickness = 0.005f;
            float half = thickness / 2f;

            var root = new GameObject("Crosshair", typeof(RectTransform));
            root.transform.SetParent(parent, false);
            _crosshair = (RectTransform)root.transform;
            // A centered SQUARE frame, not the full (16:9) screen rect — the same fractional
            // anchors below would otherwise stretch to the screen's own aspect ratio, making the
            // horizontal bars visibly longer/thicker than the vertical ones. Sized to a fraction of
            // the smaller screen dimension so it scales sensibly across resolutions.
            float size = Mathf.Min(Screen.width, Screen.height) * 0.6f;
            _crosshair.anchorMin = _crosshair.anchorMax = new Vector2(0.5f, 0.5f);
            _crosshair.pivot = new Vector2(0.5f, 0.5f);
            _crosshair.anchoredPosition = Vector2.zero;
            _crosshair.sizeDelta = new Vector2(size, size);

            CreateBar(_crosshair, "Top",    new Vector2(0.5f - half, 0.5f + gap),  new Vector2(0.5f + half, armEnd));
            CreateBar(_crosshair, "Bottom", new Vector2(0.5f - half, 1f - armEnd), new Vector2(0.5f + half, 0.5f - gap));
            CreateBar(_crosshair, "Left",   new Vector2(1f - armEnd, 0.5f - half), new Vector2(0.5f - gap, 0.5f + half));
            CreateBar(_crosshair, "Right",  new Vector2(0.5f + gap, 0.5f - half),  new Vector2(armEnd, 0.5f + half));

            _pointTrackBox = new GameObject("PointTrackBox", typeof(RectTransform));
            _pointTrackBox.transform.SetParent(_crosshair, false);
            var boxRt = (RectTransform)_pointTrackBox.transform;
            Stretch(boxRt);
            CreateBar(boxRt, "BoxTop",    new Vector2(0.5f - gap, 0.5f + gap - half), new Vector2(0.5f + gap, 0.5f + gap + half));
            CreateBar(boxRt, "BoxBottom", new Vector2(0.5f - gap, 0.5f - gap - half), new Vector2(0.5f + gap, 0.5f - gap + half));
            CreateBar(boxRt, "BoxLeft",   new Vector2(0.5f - gap - half, 0.5f - gap), new Vector2(0.5f - gap + half, 0.5f + gap));
            CreateBar(boxRt, "BoxRight",  new Vector2(0.5f + gap - half, 0.5f - gap), new Vector2(0.5f + gap + half, 0.5f + gap));
        }

        private static void CreateBar(Transform parent, string name, Vector2 anchorMin, Vector2 anchorMax)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            Image img = go.GetComponent<Image>();
            img.color = White;
            RectTransform rt = img.rectTransform;
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
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
