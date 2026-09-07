using System.Reflection;
using UnityEngine;
using UnityEngine.UI;

namespace NOXMFD
{
    // Proof-of-concept for issue #43 (docs/internal-mfd.md): settles the doc's open "does a
    // last-sibling child of TacScreen's own Canvas actually paint on top of the native cockpit MFD
    // content" question with a real in-game check, before any NOXMFD page gets a native
    // reimplementation. Draws nothing but an unmissable flat-color panel + label — this file is
    // meant to be replaced once that question is settled, not extended.
    //
    // Mission-scoped (added in MissionLifecycle.StartReader, same as the Hud* cues), because the
    // Cockpit/TacScreen chain only exists for a live local-player aircraft.
    internal class InternalMfdPoc : MonoBehaviour
    {
        private static bool _enabled;

        private static FieldInfo? _tacScreenField;
        private static FieldInfo? _canvasField;

        private Canvas?     _tacCanvas; // fake-null once its aircraft despawns
        private GameObject? _overlay;

        internal static void Toggle()
        {
            _enabled = !_enabled;
            Plugin.Log?.LogInfo($"[NOXMFD] Internal MFD POC = {_enabled}.");
        }

        private void LateUpdate()
        {
            if (!_enabled)
            {
                if (_overlay != null) Destroy(_overlay);
                _overlay = null;
                _tacCanvas = null;
                return;
            }

            if (_tacCanvas == null && !ResolveCanvas(out _tacCanvas)) return;
            if (_overlay == null) BuildOverlay(_tacCanvas!);
        }

        // Cockpit.tacScreen and TacScreen.canvas are both private with no public accessor — the
        // insertion point docs/internal-mfd.md proposes. aircraft.cockpit is a UnitPart, not the
        // Cockpit MonoBehaviour; the game's own code reaches sibling components the same way
        // (Aircraft.decompiled.cs: aircraft.cockpit.GetComponent<EscapeCapsule>()).
        private static bool ResolveCanvas(out Canvas? canvas)
        {
            canvas = null;
            if (!GameManager.GetLocalAircraft(out Aircraft aircraft) || aircraft == null) return false;
            if (aircraft.cockpit == null) return false;

            Cockpit cockpit = aircraft.cockpit.GetComponent<Cockpit>();
            if (cockpit == null) return false;

            // tacScreen is only ever instantiated for the local player's own aircraft, and only
            // after Aircraft.onInitialize fires — null here just means "not ready yet this frame".
            if (_tacScreenField == null)
                _tacScreenField = typeof(Cockpit).GetField("tacScreen", BindingFlags.Instance | BindingFlags.NonPublic);
            if (_tacScreenField?.GetValue(cockpit) is not TacScreen tacScreen || tacScreen == null) return false;

            if (_canvasField == null)
                _canvasField = typeof(TacScreen).GetField("canvas", BindingFlags.Instance | BindingFlags.NonPublic);
            if (_canvasField?.GetValue(tacScreen) is not Canvas c || c == null) return false;

            canvas = c;
            return true;
        }

        private void BuildOverlay(Canvas canvas)
        {
            _overlay = new GameObject("NOXMFD_InternalMfdPoc", typeof(RectTransform));
            var rt = _overlay.GetComponent<RectTransform>();
            rt.SetParent(canvas.transform, false);
            rt.SetAsLastSibling(); // top of paint order — the exact claim under test
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;

            // No sprite — an Image with none draws a flat tinted quad, same trick HudWaypointCue
            // uses, so this ships no art and can't fail on a missing asset.
            var bg = _overlay.AddComponent<Image>();
            bg.color = new Color(1f, 0f, 1f, 0.85f); // unmissable magenta
            bg.raycastTarget = false;

            var labelGo = new GameObject("Label", typeof(RectTransform), typeof(Text));
            var labelRt = labelGo.GetComponent<RectTransform>();
            labelRt.SetParent(rt, false);
            labelRt.anchorMin = Vector2.zero;
            labelRt.anchorMax = Vector2.one;
            labelRt.offsetMin = Vector2.zero;
            labelRt.offsetMax = Vector2.zero;

            var text = labelGo.GetComponent<Text>();
            text.font = ResolveFont();
            text.fontSize = 24;
            text.alignment = TextAnchor.MiddleCenter;
            text.color = Color.white;
            text.text = "NOXMFD POC";
            text.raycastTarget = false;
        }

        // Borrow the font off any Text the game already has on screen, same as HudWaypointCue —
        // avoids shipping a font asset for a file this short-lived.
        private static Font? ResolveFont()
        {
            foreach (Text t in Object.FindObjectsByType<Text>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (t != null && t.font != null) return t.font;
            }
            return Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        }

        private void OnDestroy()
        {
            if (_overlay != null) Destroy(_overlay);
        }
    }
}
