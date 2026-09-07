using System.Reflection;
using UnityEngine;
using UnityEngine.UI;

namespace NOXMFD
{
    // Proof-of-concept for issue #43 (docs/internal-mfd.md): AVN-style (speed/altitude/fuel) and
    // RWR-style (contact count/bearing/tier) readouts drawn natively on the T/A-30 Compass's center
    // cockpit screen — split left/right on this screen's wide aspect ratio (docs/internal-mfd.md's
    // "Split-screen layout" requirement), driven straight from the live Aircraft object and the
    // plugin's own already-aggregated telemetry (TelemetryServer.TryGetLatestSnapshot) — no HTTP/
    // JSON round trip. AVN formatting reuses the game's own UnitConverter (same SPD/ALT text the
    // native HUD already shows) rather than a reimplemented unit-conversion table; RWR reuses
    // TelemetryReader's own contact aggregation (decay/tiering) rather than resubscribing to
    // Aircraft.onRadarWarning and redoing it here.
    //
    // Mission-scoped (added in MissionLifecycle.StartReader, same as the Hud* cues), because the
    // Cockpit/TacScreen chain only exists for a live local-player aircraft.
    internal class InternalMfdPoc : MonoBehaviour
    {
        // A live test showed the overlay painting on all three cockpit screens (center MFD + two
        // side sub-displays), not just the center one this POC targets. Two wrong theories tried and
        // disproven live: a shared "UI" layer causing multiple cameras to render it (a dedicated
        // layer + widened-and-restored screenCam.cullingMask didn't help), and a material-level UV
        // transform (Renderer.material.mainTextureScale/Offset came back trivial (1,1)/(0,0)).
        //
        // The real mechanism, confirmed by directly inspecting the game's mesh/material assets
        // (not just decompiled C#): all three screens are ONE mesh ("tacscreen"), ONE Renderer, ONE
        // material — camera/layer tricks were never going to separate them, there's only one
        // renderer to begin with. The three screens are different vertical bands of the SAME
        // 1024x512 texture, with the crop baked directly into the mesh's own per-vertex UVs — a
        // layer beneath what mainTextureScale/Offset can see at all, which is why that check came
        // back trivial without actually meaning "uncropped". For the T/A-30 Compass specifically,
        // the large center screen occupies roughly the top 71% of the texture (V ~0.29068-1.0, full
        // width); the two small screens split the bottom ~29% horizontally. This is per-aircraft
        // mesh data, not guaranteed to hold for any other airframe (docs/internal-mfd.md's open
        // per-airframe-geometry question) — only applied when the T/A-30 is confirmed, full canvas
        // (today's known-imperfect fallback) otherwise.
        private const string CenterScreenAircraft = "T/A-30 Compass";
        private static readonly Vector2 CenterScreenUvMin = new Vector2(0f, 0.29068f);

        private static bool _enabled;

        private static FieldInfo? _tacScreenField;
        private static FieldInfo? _canvasField;
        private static FieldInfo? _camField;
        private static FieldInfo? _renderTextureField;

        private Canvas?     _tacCanvas;   // fake-null once its aircraft despawns
        private Aircraft?   _tacAircraft; // which aircraft _tacCanvas belongs to, so a switch is caught
                                           // even if the old Canvas hasn't gone fake-null yet
        private GameObject? _overlay;
        private Text?       _spdText;
        private Text?       _altText;
        private Text?       _fuelText;
        private Text?       _rwrCountText;
        private Text?       _rwrBearingText;
        private Text?       _rwrDetailText;
        private float       _lastRefresh;

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
                if (!ResolveCanvas(aircraft, out _tacCanvas, out _)) return;
                _tacAircraft = aircraft;
            }

            if (_overlay == null) BuildOverlay(aircraft, _tacCanvas!);
            RefreshReadout(aircraft);
        }

        private void Teardown()
        {
            if (_overlay != null) Destroy(_overlay);
            _overlay = null;
            _spdText = null;
            _altText = null;
            _fuelText = null;
            _rwrCountText = null;
            _rwrBearingText = null;
            _rwrDetailText = null;
            _tacCanvas = null;
            _tacAircraft = null;
            _lastFailure = null; // a fresh attach attempt is worth re-logging even the same reason
        }

        // ~10Hz, same idea as TacScreen's own 0.05s per-frame throttle — plenty for gauges a human
        // is reading, and avoids setting Text.text (a layout-rebuild trigger) every single frame.
        private void RefreshReadout(Aircraft aircraft)
        {
            if (_spdText == null || _altText == null || _fuelText == null) return;
            if (Time.time - _lastRefresh < 0.1f) return;
            _lastRefresh = Time.time;

            _spdText.text = "SPD " + UnitConverter.SpeedReading(aircraft.speed);
            _altText.text = "ALT " + UnitConverter.AltitudeReading(aircraft.radarAlt);
            _fuelText.text = $"FUEL {aircraft.GetFuelLevel() * 100f:F0}%";

            if (_rwrCountText != null && _rwrBearingText != null && _rwrDetailText != null)
                RefreshRwr();
        }

        // RWR half of the split layout (docs/internal-mfd.md "Split-screen layout"). Reads the same
        // already-aggregated contact list (decay/tiering already handled) TelemetryReader builds for
        // the external /stream RWR page — TryGetLatestSnapshot instead of resubscribing to
        // Aircraft.onRadarWarning and redoing that aggregation here. Own-ship WorldX/WorldZ/Heading
        // come from the SAME snapshot pass that built the contacts, so the bearing math below can't
        // drift into a different floating-origin frame than the contacts themselves are in.
        private void RefreshRwr()
        {
            if (!TelemetryServer.TryGetLatestSnapshot(out TelemetrySnapshot snap) ||
                snap.Rwr == null || snap.Rwr.Length == 0)
            {
                _rwrCountText!.text = "RWR CLEAR";
                _rwrBearingText!.text = string.Empty;
                _rwrDetailText!.text = string.Empty;
                return;
            }

            // Highest tier first (2 lock > 1 track > 0 search), closest (higher Power) breaks ties —
            // the single contact a pilot would look at first.
            RwrContact best = snap.Rwr[0];
            for (int i = 1; i < snap.Rwr.Length; i++)
            {
                RwrContact c = snap.Rwr[i];
                if (c.Tier > best.Tier || (c.Tier == best.Tier && c.Power > best.Power)) best = c;
            }

            float bearing = HudWaypointCueMath.BearingDeg(snap.WorldX, snap.WorldZ, best.X, best.Z);
            float az = ((bearing - snap.Heading) % 360f + 360f) % 360f; // clockwise from nose, 0..360
            string tier = best.Tier == 2 ? "LOCK" : best.Tier == 1 ? "TRACK" : "SEARCH";

            _rwrCountText!.text = snap.Rwr.Length == 1 ? "1 CONTACT" : $"{snap.Rwr.Length} CONTACTS";
            _rwrBearingText!.text = $"BRG {Mathf.RoundToInt(az):000}";
            _rwrDetailText!.text = $"{tier} {best.Name}";
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

            // Confirms, every attach, that the mesh/material story hasn't changed: exactly one
            // Renderer should reference this RenderTexture (see the class-level comment — all three
            // screens are one mesh, one material). A second match, or zero, would mean an aircraft
            // whose cockpit doesn't follow the T/A-30's layout this POC was calibrated against.
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
                Material? mat = renderer.sharedMaterial;
                if (mat == null) continue;

                // Not just .mainTexture (the shader's default/albedo slot) — TacScreen.Update() sets
                // _EmissionColor on this material, so an emissive screen shader most likely binds the
                // RT to an emission texture slot instead, which .mainTexture alone would miss (the
                // first pass of this diagnostic found ZERO matches, including for tacScreenRender
                // itself — the one renderer we already know must be using it).
                string? matchedSlot = null;
                foreach (string slot in mat.GetTexturePropertyNames())
                {
                    if (mat.GetTexture(slot) == rt) { matchedSlot = slot; break; }
                }
                if (matchedSlot == null) continue;

                matches++;
                string path = renderer.gameObject.name;
                for (var t = renderer.transform.parent; t != null; t = t.parent) path = t.name + "/" + path;
                Plugin.Log?.LogInfo($"[NOXMFD] Internal MFD POC: renderer '{path}' (layer {renderer.gameObject.layer}) uses TacScreen.renderTexture via '{matchedSlot}'.");
            }
            Plugin.Log?.LogInfo($"[NOXMFD] Internal MFD POC: {matches} renderer(s) total reference TacScreen.renderTexture.");
        }

        private void BuildOverlay(Aircraft aircraft, Canvas canvas)
        {
            // Build into a local first: on an exception partway through, the field stays null and
            // the next frame's LateUpdate retries cleanly, instead of caching a half-built overlay.
            var overlay = new GameObject("NOXMFD_InternalMfdPoc", typeof(RectTransform));
            overlay.layer = canvas.gameObject.layer; // SetParent does NOT inherit the parent's layer

            string unitName = aircraft.definition != null ? aircraft.definition.unitName : "?";
            bool knownCenterScreen = unitName == CenterScreenAircraft;
            Vector2 anchorMin = knownCenterScreen ? CenterScreenUvMin : Vector2.zero;
            if (!knownCenterScreen)
                Plugin.Log?.LogInfo($"[NOXMFD] Internal MFD POC: no verified center-screen crop for '{unitName}' — using the full (all-three-screens) canvas.");

            var rt = overlay.GetComponent<RectTransform>();
            rt.SetParent(canvas.transform, false);
            rt.SetAsLastSibling(); // top of paint order — confirmed live to cover native content
            rt.anchorMin = anchorMin;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;

            // No sprite — an Image with none draws a flat tinted quad, same trick HudWaypointCue
            // uses, so this ships no art and can't fail on a missing asset. Fully opaque (alpha 1) —
            // anything less lets the native content underneath show through, which is what "solid
            // background" reports were seeing.
            var bg = overlay.AddComponent<Image>();
            bg.color = new Color(0.03f, 0.05f, 0.03f, 1f);
            bg.raycastTarget = false;

            Font? font = ResolveFont();

            // Split-screen layout (docs/internal-mfd.md "Split-screen layout"): a wide screen splits
            // into two independently-addressable halves with a vertical separator; a square-ish
            // screen stays one full-view region. T/A-30's center screen is the only aircraft
            // calibrated so far (~2.8:1 — unambiguously wide), so it's the only one that splits;
            // anything else keeps the single full-width AVN panel this POC already had, since no
            // other aircraft has a verified wide/square-ish classification yet (open question).
            if (knownCenterScreen)
            {
                RectTransform left = BuildHalf(rt, "Left", right: false);
                RectTransform rightHalf = BuildHalf(rt, "Right", right: true);
                BuildSeparator(rt);

                _rwrCountText = BuildReadoutLine(left, font, 0, fontSize: 20);
                _rwrBearingText = BuildReadoutLine(left, font, 1, fontSize: 20);
                _rwrDetailText = BuildReadoutLine(left, font, 2, fontSize: 20);

                _spdText = BuildReadoutLine(rightHalf, font, 0, fontSize: 20);
                _altText = BuildReadoutLine(rightHalf, font, 1, fontSize: 20);
                _fuelText = BuildReadoutLine(rightHalf, font, 2, fontSize: 20);
            }
            else
            {
                _spdText = BuildReadoutLine(rt, font, 0, fontSize: 28);
                _altText = BuildReadoutLine(rt, font, 1, fontSize: 28);
                _fuelText = BuildReadoutLine(rt, font, 2, fontSize: 28);
            }

            _overlay = overlay;
        }

        // Left or right 50% of parent, full height.
        private static RectTransform BuildHalf(RectTransform parent, string name, bool right)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.layer = parent.gameObject.layer;
            var rt = go.GetComponent<RectTransform>();
            rt.SetParent(parent, false);
            rt.anchorMin = new Vector2(right ? 0.5f : 0f, 0f);
            rt.anchorMax = new Vector2(right ? 1f : 0.5f, 1f);
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            return rt;
        }

        // A thin vertical divider at the halfway point — content is already split into two
        // RectTransforms regardless, this just makes the split visible rather than an unmarked gap.
        private static void BuildSeparator(RectTransform parent)
        {
            var go = new GameObject("Separator", typeof(RectTransform), typeof(Image));
            go.layer = parent.gameObject.layer;
            var rt = go.GetComponent<RectTransform>();
            rt.SetParent(parent, false);
            // Mixed anchor: a point in X (0.5/0.5, so sizeDelta.x is the literal width), stretched
            // in Y (0..1, so sizeDelta.y=0 means exactly full height, no extra padding).
            rt.anchorMin = new Vector2(0.5f, 0f);
            rt.anchorMax = new Vector2(0.5f, 1f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(3f, 0f);
            rt.anchoredPosition = Vector2.zero;

            var img = go.GetComponent<Image>();
            img.color = new Color(0.3f, 1f, 0.4f, 0.6f);
            img.raycastTarget = false;
        }

        // Three equal vertical rows, top to bottom (row 0 = top).
        private static Text BuildReadoutLine(RectTransform parent, Font? font, int row, int fontSize)
        {
            const int rows = 3;
            float top = 1f - (float)row / rows;
            float bottom = 1f - (float)(row + 1) / rows;

            var go = new GameObject($"Row{row}", typeof(RectTransform), typeof(Text));
            go.layer = parent.gameObject.layer;
            var rt = go.GetComponent<RectTransform>();
            rt.SetParent(parent, false);
            rt.anchorMin = new Vector2(0f, bottom);
            rt.anchorMax = new Vector2(1f, top);
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;

            var text = go.GetComponent<Text>();
            text.font = font;
            text.fontSize = fontSize;
            text.alignment = TextAnchor.MiddleCenter;
            // HUD green, matching HudWaypointCue's reasoning for its own amber choice — a
            // recognizable cockpit-display color rather than an arbitrary one.
            text.color = new Color(0.3f, 1f, 0.4f);
            text.raycastTarget = false;
            return text;
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
