using UnityEngine;
using UnityEngine.UI;

namespace NOXMFD
{
    // Full-screen, head-relative TGP line-of-sight cue (issue #59). Unlike HudWaypointCue, this is
    // parented to CombatHUD.iconLayer rather than aircraft-fixed HUDCenter/compass symbology, so
    // mainCamera projection follows free-look, TrackIR, padlock and cockpit FOV changes naturally.
    internal sealed class HudTgpCue : MonoBehaviour
    {
        private const float ProjectionDistance = 10000f;
        private const float MarkerSize = 31f;
        private const float CornerArmLength = 7.5f;
        private const float StrokeWidth = 1f;
        private const float EdgeArrowLength = 9.5f;
        private const float EdgeArrowAngle = 35f;
        // The label stays at a readable size while the brackets/caret are half-scale, so its lower
        // edge remains the controlling margin when the cue is pinned near a screen boundary.
        private const float EdgeInset = 36f;

        // Same amber family as the WPT heading bug; deliberately distinct from the game's green
        // centre-view diamond and unit markers when all three overlap.
        private static readonly Color Amber = new Color(1f, 0.6667f, 0f, 1f);
        private static Font? _font;

        private Transform? _iconLayer;
        private RectTransform? _root;
        private RectTransform? _brackets;
        private RectTransform? _edgeArrow;
        private float _stableDirectionX;
        private float _stableDirectionY;

        private void LateUpdate()
        {
            if (!TgpManualControl.ManualMode || CameraStateManager.cameraMode != CameraMode.cockpit ||
                !GameManager.GetLocalAircraft(out Aircraft localAircraft) || localAircraft == null ||
                !TgpManualControl.TryGetAimDirection(out Vector3 aimDirection))
            {
                Hide(resetDirection: true);
                return;
            }

            CombatHUD hud = SceneSingleton<CombatHUD>.i;
            CameraStateManager cameraState = SceneSingleton<CameraStateManager>.i;
            Camera? camera = cameraState != null ? cameraState.mainCamera : null;
            if (hud == null || hud.iconLayer == null || camera == null)
            {
                Hide(resetDirection: false);
                return;
            }

            // The native HUD is rebuilt per aircraft spawn. Its iconLayer takes our children with
            // it, and Unity fake-null on either cached reference triggers a clean rebuild here.
            if (_root == null || _iconLayer == null || _iconLayer != hud.iconLayer)
            {
                if (!Build(hud.iconLayer)) return;
            }

            Vector3 projectionPoint = camera.transform.position + aimDirection * ProjectionDistance;
            Vector3 projected = camera.WorldToScreenPoint(projectionPoint);
            if (!HudDirectionCueMath.TryPlace(projected.x, projected.y, projected.z <= 0f,
                Screen.width, Screen.height, EdgeInset,
                _stableDirectionX, _stableDirectionY, out HudDirectionCueMath.Placement placement))
            {
                Hide(resetDirection: false);
                return;
            }

            _stableDirectionX = placement.StableDirectionX;
            _stableDirectionY = placement.StableDirectionY;
            _root!.position = new Vector3(placement.X, placement.Y, 0f);
            _brackets!.gameObject.SetActive(placement.OnScreen);
            _edgeArrow!.gameObject.SetActive(!placement.OnScreen);
            _edgeArrow.localEulerAngles = new Vector3(0f, 0f, placement.AngleDeg);
            SetVisible(true);
        }

        private bool Build(Transform iconLayer)
        {
            ClearVisuals();

            _iconLayer = iconLayer;
            _root = NewRect("NOXMFD_TgpHudCue", iconLayer, new Vector2(MarkerSize, MarkerSize), Vector2.zero);
            _root.SetAsLastSibling();

            _brackets = NewRect("Brackets", _root, new Vector2(MarkerSize, MarkerSize), Vector2.zero);
            AddCorner(_brackets, -1f, +1f, "TopLeft");
            AddCorner(_brackets, +1f, +1f, "TopRight");
            AddCorner(_brackets, -1f, -1f, "BottomLeft");
            AddCorner(_brackets, +1f, -1f, "BottomRight");

            _edgeArrow = NewRect("EdgeArrow", _root, Vector2.zero, Vector2.zero);
            AddArrowArm(_edgeArrow, +EdgeArrowAngle);
            AddArrowArm(_edgeArrow, -EdgeArrowAngle);
            _edgeArrow.gameObject.SetActive(false);

            var labelObject = new GameObject("TGP", typeof(RectTransform), typeof(Text));
            RectTransform labelRect = labelObject.GetComponent<RectTransform>();
            labelRect.SetParent(_root, false);
            labelRect.anchorMin = labelRect.anchorMax = new Vector2(0.5f, 0.5f);
            labelRect.pivot = new Vector2(0.5f, 0.5f);
            labelRect.anchoredPosition = new Vector2(0f, -24f);
            labelRect.sizeDelta = new Vector2(70f, 18f);

            Text label = labelObject.GetComponent<Text>();
            label.text = "TGP";
            label.font = ResolveFont();
            label.fontSize = 12;
            label.alignment = TextAnchor.MiddleCenter;
            label.horizontalOverflow = HorizontalWrapMode.Overflow;
            label.verticalOverflow = VerticalWrapMode.Overflow;
            label.color = Amber;
            label.raycastTarget = false;

            SetVisible(false);
            return true;
        }

        private static void AddCorner(RectTransform parent, float xSign, float ySign, string name)
        {
            float half = MarkerSize * 0.5f;
            AddBar(parent, name + "Horizontal",
                new Vector2(CornerArmLength, StrokeWidth),
                new Vector2(xSign * (half - CornerArmLength * 0.5f), ySign * half), 0f);
            AddBar(parent, name + "Vertical",
                new Vector2(StrokeWidth, CornerArmLength),
                new Vector2(xSign * half, ySign * (half - CornerArmLength * 0.5f)), 0f);
        }

        private static void AddArrowArm(RectTransform parent, float angle)
        {
            var arm = new GameObject("Arm", typeof(RectTransform), typeof(Image)).GetComponent<RectTransform>();
            arm.SetParent(parent, false);
            arm.anchorMin = arm.anchorMax = new Vector2(0.5f, 0.5f);
            // Both bars terminate at the root origin, forming an arrow whose tip points right at 0°.
            arm.pivot = new Vector2(1f, 0.5f);
            arm.anchoredPosition = Vector2.zero;
            arm.sizeDelta = new Vector2(EdgeArrowLength, StrokeWidth);
            arm.localEulerAngles = new Vector3(0f, 0f, angle);
            ConfigureImage(arm.GetComponent<Image>());
        }

        private static void AddBar(RectTransform parent, string name, Vector2 size, Vector2 position, float angle)
        {
            RectTransform bar = NewRect(name, parent, size, position);
            bar.localEulerAngles = new Vector3(0f, 0f, angle);
            var image = bar.gameObject.AddComponent<Image>();
            ConfigureImage(image);
        }

        private static RectTransform NewRect(string name, Transform parent, Vector2 size, Vector2 position)
        {
            RectTransform rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
            return rect;
        }

        private static void ConfigureImage(Image image)
        {
            image.color = Amber;
            image.raycastTarget = false;
        }

        private static Font ResolveFont()
        {
            if (_font != null) return _font;
            foreach (Text text in UnityEngine.Object.FindObjectsByType<Text>(
                FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (text != null && text.font != null)
                {
                    _font = text.font;
                    return _font;
                }
            }
            _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            return _font;
        }

        private void SetVisible(bool visible)
        {
            if (_root != null && _root.gameObject.activeSelf != visible)
                _root.gameObject.SetActive(visible);
        }

        private void Hide(bool resetDirection)
        {
            SetVisible(false);
            if (!resetDirection) return;
            _stableDirectionX = 0f;
            _stableDirectionY = 0f;
        }

        private void ClearVisuals()
        {
            if (_root != null) Destroy(_root.gameObject);
            _root = null;
            _brackets = null;
            _edgeArrow = null;
            _iconLayer = null;
            _stableDirectionX = 0f;
            _stableDirectionY = 0f;
        }

        private void OnDestroy() => ClearVisuals();
    }
}
