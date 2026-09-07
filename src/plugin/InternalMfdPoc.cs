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
        private static FieldInfo? _camField;
        private static FieldInfo? _renderTextureField;

        private Canvas?     _tacCanvas;   // fake-null once its aircraft despawns
        private Aircraft?   _tacAircraft; // which aircraft _tacCanvas belongs to, so a switch is caught
                                           // even if the old Canvas hasn't gone fake-null yet
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
                Teardown();
                return;
            }

            if (!GameManager.GetLocalAircraft(out Aircraft aircraft) || aircraft == null)
            {
                Teardown();
                return;
            }

            if (_tacCanvas == null || !ReferenceEquals(aircraft, _tacAircraft))
            {
                Teardown();
                if (!ResolveCanvas(aircraft, out _tacCanvas)) return;
                _tacAircraft = aircraft;
            }

            if (_overlay == null) BuildOverlay(aircraft, _tacCanvas!);
        }

        private void Teardown()
        {
            if (_overlay != null) Destroy(_overlay);
            _overlay = null;
            _tacCanvas = null;
            _tacAircraft = null;
            _lastFailure = null; // a fresh attach attempt is worth re-logging even the same reason
        }

        // Cockpit.tacScreen and TacScreen.canvas are both private with no public accessor — the
        // insertion point docs/internal-mfd.md proposes.
        //
        // aircraft.cockpit is the structural/damage UnitPart (what EscapeCapsule sits on in
        // Aircraft.decompiled.cs — a DIFFERENT GameObject, confirmed the hard way: GetComponent
        // there silently found nothing, every frame, with no exception). Cockpit is the
        // interior rig (joystick/throttle animation, tacScreenUIPrefab) wired to its owning Aircraft
        // via its own inspector-assigned field, not the other way round, so it has to be found by
        // searching the aircraft's hierarchy rather than assumed to sit on aircraft.cockpit.
        private static bool ResolveCanvas(Aircraft aircraft, out Canvas? canvas)
        {
            canvas = null;

            Cockpit cockpit = aircraft.GetComponentInChildren<Cockpit>(includeInactive: true);
            if (cockpit == null) { LogFailure("no Cockpit component found under the aircraft"); return false; }

            // tacScreen is only ever instantiated for the local player's own aircraft, and only
            // after Aircraft.onInitialize fires — null here just means "not ready yet this frame".
            if (_tacScreenField == null)
                _tacScreenField = typeof(Cockpit).GetField("tacScreen", BindingFlags.Instance | BindingFlags.NonPublic);
            if (_tacScreenField?.GetValue(cockpit) is not TacScreen tacScreen || tacScreen == null)
            { LogFailure("Cockpit.tacScreen not yet instantiated"); return false; }

            if (_canvasField == null)
                _canvasField = typeof(TacScreen).GetField("canvas", BindingFlags.Instance | BindingFlags.NonPublic);
            if (_canvasField?.GetValue(tacScreen) is not Canvas c || c == null)
            { LogFailure("TacScreen.canvas field missing/null"); return false; }

            canvas = c;
            _lastFailure = null;
            LogAttachDiagnostics(aircraft, tacScreen, c);
            return true;
        }

        private static string? _lastFailure;

        // Logged once per DISTINCT failure reason, not per frame (this runs every LateUpdate while
        // enabled and unresolved) — enough to diagnose a silent "nothing happened" without spamming
        // Player.log at 60-90Hz.
        private static void LogFailure(string reason)
        {
            if (_lastFailure == reason) return;
            _lastFailure = reason;
            Plugin.Log?.LogInfo($"[NOXMFD] Internal MFD POC: {reason}.");
        }

        // One-shot, on successful attach only — this is the evidence a manual test needs to tell
        // "nothing rendered because the canvas wasn't found" apart from "found it, but the camera/
        // layer/render setup doesn't show it", without spamming a per-frame log.
        private static void LogAttachDiagnostics(Aircraft aircraft, TacScreen tacScreen, Canvas canvas)
        {
            if (_camField == null)
                _camField = typeof(TacScreen).GetField("cam", BindingFlags.Instance | BindingFlags.NonPublic);
            if (_renderTextureField == null)
                _renderTextureField = typeof(TacScreen).GetField("renderTexture", BindingFlags.Instance | BindingFlags.NonPublic);

            var cam = _camField?.GetValue(tacScreen) as Camera;
            var rt = _renderTextureField?.GetValue(tacScreen) as RenderTexture;
            string unitName = aircraft.definition != null ? aircraft.definition.unitName : "?";

            Plugin.Log?.LogInfo(
                $"[NOXMFD] Internal MFD POC attached: aircraft={unitName}, canvas.renderMode={canvas.renderMode}, " +
                $"canvas.layer={canvas.gameObject.layer} ({LayerMask.LayerToName(canvas.gameObject.layer)}), " +
                $"cam={(cam != null ? cam.name : "null")}, cam.cullingMask={(cam != null ? cam.cullingMask : 0)}, " +
                $"renderTexture={(rt != null ? $"{rt.width}x{rt.height}" : "null")}.");
        }

        private void BuildOverlay(Aircraft aircraft, Canvas canvas)
        {
            // Build into a local first: on an exception partway through, the field stays null and
            // the next frame's LateUpdate retries cleanly, instead of caching a half-built overlay.
            var overlay = new GameObject("NOXMFD_InternalMfdPoc", typeof(RectTransform));
            overlay.layer = canvas.gameObject.layer; // SetParent does NOT inherit the parent's layer

            var rt = overlay.GetComponent<RectTransform>();
            rt.SetParent(canvas.transform, false);
            rt.SetAsLastSibling(); // top of paint order — the exact claim under test
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;

            // No sprite — an Image with none draws a flat tinted quad, same trick HudWaypointCue
            // uses, so this ships no art and can't fail on a missing asset. Fully opaque: this is a
            // paint-order test, so any native content still visible must mean the insertion point
            // isn't actually on top, not "alpha blending is working as intended".
            var bg = overlay.AddComponent<Image>();
            bg.color = Color.magenta;
            bg.raycastTarget = false;

            var labelGo = new GameObject("Label", typeof(RectTransform), typeof(Text));
            labelGo.layer = canvas.gameObject.layer;
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

            _overlay = overlay;
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
            // Mission end is the only thing that destroys this component (MissionLifecycle tears
            // down the whole reader object) — reset the static toggle here too, or a POC left ON
            // reappears unasked on the next mission without the key being pressed again.
            _enabled = false;
            if (_overlay != null) Destroy(_overlay);
        }
    }
}
