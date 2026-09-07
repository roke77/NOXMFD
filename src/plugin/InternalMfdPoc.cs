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
        // A live test showed the overlay painting on all three cockpit screens (center MFD + two
        // side sub-displays), not just the center one this POC targets. TacScreen.canvas's own
        // GameObject sits on layer 5 — Unity's built-in, project-wide-default "UI" layer — which
        // essentially every UI-rendering camera in a Unity project includes by convention, not by
        // anything specific to this one screen. The center screen's own camera (screenCam)
        // legitimately needs layer 5 for the native content already on it, so narrowing that mask
        // isn't an option — instead the overlay gets a dedicated layer nothing else uses, added to
        // screenCam's mask only while attached and removed exactly on teardown, so only screenCam
        // ever renders it. (A same-shaped guess — that the wide 1024x512 texture was a strip split
        // across the three screens by UV sub-rect, cropping the overlay to the center's own slice —
        // was tried and disproven live: the center screen's own material scale/offset came back
        // (1,1)/(0,0), i.e. it legitimately shows the full canvas, so that wasn't the mechanism.)
        private const int OverlayLayer = 30; // high, unlikely to collide with the game's own 8-31 range

        private static bool _enabled;

        private static FieldInfo? _tacScreenField;
        private static FieldInfo? _canvasField;
        private static FieldInfo? _camField;
        private static FieldInfo? _renderTextureField;

        private Canvas?     _tacCanvas;   // fake-null once its aircraft despawns
        private Camera?     _tacCam;      // screenCam — its cullingMask is widened while attached
        private int         _origCullingMask;
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
                if (!ResolveCanvas(aircraft, out _tacCanvas, out _tacCam)) return;
                _tacAircraft = aircraft;
                if (_tacCam != null)
                {
                    _origCullingMask = _tacCam.cullingMask;
                    _tacCam.cullingMask |= 1 << OverlayLayer;
                }
            }

            if (_overlay == null) BuildOverlay(aircraft, _tacCanvas!);
        }

        private void Teardown()
        {
            if (_overlay != null) Destroy(_overlay);
            _overlay = null;
            if (_tacCam != null) _tacCam.cullingMask = _origCullingMask; // exact restore, not just clear-the-bit
            _tacCam = null;
            _tacCanvas = null;
            _tacAircraft = null;
            _lastFailure = null; // a fresh attach attempt is worth re-logging even the same reason
        }

        // Cockpit.tacScreen and TacScreen.canvas are both private with no public accessor — the
        // insertion point docs/internal-mfd.md proposes.
        //
        // Two live-tested wrong guesses at where Cockpit actually lives, both silently finding
        // nothing (no exception either time — the only signal was LogFailure never advancing past
        // this step): aircraft.cockpit.GetComponent<Cockpit>() (that's the structural/damage
        // UnitPart EscapeCapsule sits on, a different object) and aircraft.GetComponentInChildren
        // <Cockpit>() (so Cockpit isn't under the Aircraft's own subtree either — likely a sibling
        // branch under a shared prefab root, e.g. an "Interior" branch next to a "Systems" branch
        // Aircraft itself sits on, rather than a parent/child relationship). Matching by the
        // component's OWN inspector-assigned `aircraft` reference sidesteps the hierarchy shape
        // entirely — however it's parented, this is how the game itself associates the two.
        private static FieldInfo? _cockpitAircraftField;

        private static bool ResolveCanvas(Aircraft aircraft, out Canvas? canvas, out Camera? cam)
        {
            canvas = null;
            cam = null;

            if (_cockpitAircraftField == null)
                _cockpitAircraftField = typeof(Cockpit).GetField("aircraft", BindingFlags.Instance | BindingFlags.NonPublic);

            Cockpit? cockpit = null;
            foreach (var candidate in Object.FindObjectsByType<Cockpit>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (ReferenceEquals(_cockpitAircraftField?.GetValue(candidate), aircraft)) { cockpit = candidate; break; }
            }
            if (cockpit == null) { LogFailure("no Cockpit component references this aircraft"); return false; }

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

            if (_camField == null)
                _camField = typeof(TacScreen).GetField("cam", BindingFlags.Instance | BindingFlags.NonPublic);
            cam = _camField?.GetValue(tacScreen) as Camera;

            canvas = c;
            _lastFailure = null;
            LogAttachDiagnostics(aircraft, tacScreen, c, cam);
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
        private static void LogAttachDiagnostics(Aircraft aircraft, TacScreen tacScreen, Canvas canvas, Camera? cam)
        {
            string unitName = aircraft.definition != null ? aircraft.definition.unitName : "?";

            Plugin.Log?.LogInfo(
                $"[NOXMFD] Internal MFD POC attached: aircraft={unitName}, canvas.renderMode={canvas.renderMode}, " +
                $"canvas.layer={canvas.gameObject.layer} ({LayerMask.LayerToName(canvas.gameObject.layer)}), " +
                $"cam={(cam != null ? cam.name : "null")}, cam.cullingMask={(cam != null ? cam.cullingMask : 0)}.");

            // Both attempted fixes (widen only screenCam's mask; crop to the center mesh's own UV)
            // failed to stop the bleed onto the two side screens — the remaining untested theory is
            // that those screens' materials reference this EXACT RenderTexture object too (a literal
            // shared "repeater" texture, not a shared layer or a cropped sub-region), in which case
            // no camera/layer trick can help: whatever screenCam draws into it shows everywhere that
            // texture is used. Enumerate every Renderer in the scene and report which ones share it.
            if (_renderTextureField == null)
                _renderTextureField = typeof(TacScreen).GetField("renderTexture", BindingFlags.Instance | BindingFlags.NonPublic);
            if (_renderTextureField?.GetValue(tacScreen) is not RenderTexture rt || rt == null)
            {
                Plugin.Log?.LogInfo("[NOXMFD] Internal MFD POC: TacScreen.renderTexture is null, can't check for sharing.");
                return;
            }

            int matches = 0;
            foreach (var renderer in Object.FindObjectsByType<Renderer>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (renderer.sharedMaterial == null || renderer.sharedMaterial.mainTexture != rt) continue;
                matches++;
                string path = renderer.gameObject.name;
                for (var t = renderer.transform.parent; t != null; t = t.parent) path = t.name + "/" + path;
                Plugin.Log?.LogInfo($"[NOXMFD] Internal MFD POC: renderer '{path}' (layer {renderer.gameObject.layer}) uses TacScreen.renderTexture.");
            }
            Plugin.Log?.LogInfo($"[NOXMFD] Internal MFD POC: {matches} renderer(s) total reference TacScreen.renderTexture.");
        }

        private void BuildOverlay(Aircraft aircraft, Canvas canvas)
        {
            // Build into a local first: on an exception partway through, the field stays null and
            // the next frame's LateUpdate retries cleanly, instead of caching a half-built overlay.
            var overlay = new GameObject("NOXMFD_InternalMfdPoc", typeof(RectTransform));
            // NOT canvas.gameObject.layer (5, "UI") — that's the layer every screen shares, which is
            // exactly the bleed this is working around. See the class-level comment.
            overlay.layer = OverlayLayer;

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
            labelGo.layer = OverlayLayer;
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
            if (_tacCam != null) _tacCam.cullingMask = _origCullingMask;
            if (_overlay != null) Destroy(_overlay);
        }
    }
}
