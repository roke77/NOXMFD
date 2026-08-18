using System.Globalization;
using System.Reflection;
using UnityEngine;
using UnityEngine.UI;

namespace NOXMFD
{
    // The in-game HUD waypoint cue (docs/hud-waypoint-indicator.md, design A): a bug riding the
    // native heading tape plus a two-line readout at its top right. Data comes from the browser via
    // HudWaypointState; this class is only the drawing.
    //
    // This is the mod's first ADDITIVE HUD change — HudDeclutter, the only other HUD toucher, just
    // finds existing components and disables them. Mission-scoped alongside it (created in
    // MissionLifecycle.StartReader), so there's nothing to tear down on mission end.
    //
    // Everything is built from untextured Images (an Image with no sprite draws a flat tinted quad),
    // so the cue ships no art and can't fail on a missing asset. The only borrowed resource is the
    // font, taken off one of the game's own HUD Texts so the readout matches the native readouts
    // rather than introducing a second typeface.
    internal class HudWaypointCue : MonoBehaviour
    {
        // The heading tape shows 0.25 of a texture that wraps 360° (FlightHud.Update:
        // compass.uvRect = new Rect((hdg + 135f) / 360f, 0f, 0.25f, 1f)), so the visible arc is 90°
        // and a bug is only meaningful within ±45° of the nose.
        private const float TapeArcDegrees = 90f;
        private const float HalfArc        = TapeArcDegrees * 0.5f;

        // Amber, not HUD green. The tick labels immediately behind the bug are green, and a green
        // bug competes with them at exactly the moment it matters. The cost is that this ignores the
        // player's hudColorR/G/B setting, unlike every native element — accepted (see the doc).
        private static readonly Color Amber = new Color(1f, 0.72f, 0.06f, 1f);

        private static FieldInfo? _compassField;
        private static Font?      _font;

        private RawImage?     _compass;    // the tape we're anchored to; fake-null after a respawn
        private RectTransform? _bug;       // the caret container — rotating it aims the chevron
        private Text?          _readout;

        private void LateUpdate()
        {
            // The HUD is rebuilt per aircraft spawn (HUDAppManager Destroys itself on
            // aircraft.onDisableUnit), taking our children with it. Unity fake-null on the tape we
            // cached is the signal to re-resolve and rebuild — same detection HudDeclutter uses for
            // its hidden graphics, just applied to the things we added instead of the ones we hid.
            if (_compass == null || _bug == null || _readout == null)
            {
                if (!Build()) return;
            }

            if (!HudWaypointState.Active || !ResolveOwnship(out Vector3 world, out float hdg))
            {
                SetVisible(false);
                return;
            }

            // Bearing math runs entirely in the floating-origin-corrected world frame the browser
            // stores waypoints in (TelemetryReader: position - Datum.originPosition). Raw Unity
            // positions drift as the world re-centers, so mixing the two frames would put the bug
            // progressively further off the longer a mission runs.
            float dx = HudWaypointState.X - world.x;
            float dz = HudWaypointState.Z - world.z;
            float bearing  = Mathf.Repeat(Mathf.Atan2(dx, dz) * Mathf.Rad2Deg, 360f);
            float relative = Mathf.DeltaAngle(hdg, bearing);           // -180..180, + = turn right
            float distanceKm = Mathf.Sqrt(dx * dx + dz * dz) / 1000f;

            SetVisible(true);
            PlaceBug(relative);

            string name = HudWaypointState.Name;
            _readout!.text = string.Format(CultureInfo.InvariantCulture,
                "WPT {0}{1}\n{2:0.0} km · brg {3:000}",
                HudWaypointState.Index + 1,
                name.Length > 0 ? " · " + name : string.Empty,
                distanceKm,
                Mathf.RoundToInt(bearing) % 360);
        }

        // On tape: a chevron at the bearing's own position. Off tape: the same chevron rotated a
        // quarter turn and pinned to the edge it left, so it reads as "turn this way, a lot" rather
        // than as a marker that stopped moving mid-turn — the ±45° window is narrow enough that a
        // silently clamped bug would be actively misleading.
        private void PlaceBug(float relative)
        {
            float halfWidth      = _compass!.rectTransform.rect.width * 0.5f;
            float pixelsPerDegree = _compass.rectTransform.rect.width / TapeArcDegrees;

            bool onTape = Mathf.Abs(relative) <= HalfArc;
            float x = onTape ? relative * pixelsPerDegree : (relative > 0f ? halfWidth : -halfWidth);

            _bug!.anchoredPosition = new Vector2(x, _compass.rectTransform.rect.height * 0.5f + 2f);
            // The chevron is built pointing down; -90 aims it right, +90 aims it left.
            _bug.localEulerAngles = new Vector3(0f, 0f, onTape ? 0f : (relative > 0f ? -90f : 90f));
        }

        private void SetVisible(bool on)
        {
            if (_bug != null && _bug.gameObject.activeSelf != on) _bug.gameObject.SetActive(on);
            if (_readout != null && _readout.enabled != on) _readout.enabled = on;
        }

        // Ownship's world position and the heading the TAPE is scrolled by. FlightHud drives the tape
        // off cockpitRB.transform.eulerAngles.y, NOT aircraft.transform.eulerAngles.y (which is what
        // TelemetryReader publishes) — the cockpit is its own rigidbody, so reading the aircraft's
        // heading here would leave the bug lagging the tick marks it sits among.
        private static bool ResolveOwnship(out Vector3 world, out float heading)
        {
            world = Vector3.zero;
            heading = 0f;
            if (!GameManager.GetLocalAircraft(out Aircraft aircraft) || aircraft == null) return false;
            if (aircraft.cockpit == null || aircraft.cockpit.rb == null) return false;

            world   = aircraft.transform.position - Datum.originPosition;
            heading = aircraft.cockpit.rb.transform.eulerAngles.y;
            return true;
        }

        // Resolve the tape and (re)create our children under it. Parenting to the compass itself
        // means the bug's offset is already in tape-local pixels — the uvRect scroll moves the
        // texture, not the RectTransform, so children stay put while the tape slides beneath them.
        private bool Build()
        {
            FlightHud hud = SceneSingleton<FlightHud>.i;
            if (hud == null) return false;

            if (_compassField == null)
                _compassField = typeof(FlightHud).GetField("compass", BindingFlags.Instance | BindingFlags.NonPublic);
            if (_compassField?.GetValue(hud) is not RawImage compass || compass == null) return false;

            _compass = compass;
            _bug     = BuildChevron(compass.rectTransform);
            _readout = BuildReadout(compass.rectTransform);
            return true;
        }

        // Two thin bars meeting at the container's origin, forming a downward chevron. Rotating the
        // container aims it; nothing else has to know which direction it points.
        private static RectTransform BuildChevron(RectTransform parent)
        {
            var root = new GameObject("NOXMFD_WaypointBug", typeof(RectTransform)).GetComponent<RectTransform>();
            root.SetParent(parent, false);
            root.anchorMin = root.anchorMax = new Vector2(0.5f, 0.5f);
            root.pivot = new Vector2(0.5f, 0.5f);
            root.sizeDelta = Vector2.zero;

            AddArm(root, +45f, new Vector2(-2.4f, 2.4f));
            AddArm(root, -45f, new Vector2(+2.4f, 2.4f));
            return root;
        }

        private static void AddArm(RectTransform parent, float angle, Vector2 offset)
        {
            var arm = new GameObject("Arm", typeof(RectTransform), typeof(Image)).GetComponent<RectTransform>();
            arm.SetParent(parent, false);
            arm.anchorMin = arm.anchorMax = new Vector2(0.5f, 0.5f);
            arm.pivot = new Vector2(0.5f, 0.5f);
            arm.sizeDelta = new Vector2(2f, 9f);
            arm.anchoredPosition = offset;
            arm.localEulerAngles = new Vector3(0f, 0f, angle);

            var img = arm.GetComponent<Image>();
            img.color = Amber;                 // no sprite — an Image with none draws a flat quad
            img.raycastTarget = false;         // the HUD canvas sits over click targets; never eat one
        }

        private static Text BuildReadout(RectTransform parent)
        {
            var go = new GameObject("NOXMFD_WaypointReadout", typeof(RectTransform), typeof(Text));
            var rt = go.GetComponent<RectTransform>();
            rt.SetParent(parent, false);
            // Top-right of the tape, growing downward — clear of the centre caret and of the boxed
            // readouts that flank the tape (which HideTopBoxes may or may not have hidden).
            rt.anchorMin = rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(0f, 0f);
            rt.anchoredPosition = new Vector2(8f, 4f);
            rt.sizeDelta = new Vector2(220f, 34f);

            var text = go.GetComponent<Text>();
            text.font = ResolveFont();
            text.fontSize = 13;
            text.lineSpacing = 1f;
            text.alignment = TextAnchor.UpperLeft;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.color = Amber;
            text.raycastTarget = false;
            return text;
        }

        // Borrow the font off any Text the game already has on screen, so the readout matches the
        // native HUD readouts (which are UnityEngine.UI.Text — Bearing.bearing, Altitude.radarAlt).
        // The builtin fallback covers the case where the scan runs before any of them exist; a Text
        // with a null font renders nothing at all, which would be a silent failure.
        private static Font? ResolveFont()
        {
            if (_font != null) return _font;

            foreach (Text t in UnityEngine.Object.FindObjectsByType<Text>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (t != null && t.font != null) { _font = t.font; return _font; }
            }
            // Renamed from Arial.ttf in Unity 2021.2; the game is on 2022.3.
            _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            return _font;
        }

        private void OnDestroy()
        {
            // Mission-scoped, and the HUD these are parented to is torn down with the aircraft — but
            // a mission can end without an aircraft teardown (leaving to the menu), so clean up the
            // objects we created rather than leaving them on a surviving canvas.
            if (_bug != null) Destroy(_bug.gameObject);
            if (_readout != null) Destroy(_readout.gameObject);
        }
    }
}
