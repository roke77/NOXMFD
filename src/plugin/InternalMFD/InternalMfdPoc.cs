using UnityEngine;
using UnityEngine.UI;

namespace NOXMFD
{
    // Proof-of-concept for issue #43 (docs/internal-mfd.md): native NOXMFD page content drawn
    // directly on the T/A-30 Compass's center cockpit screen — split left/right on this screen's
    // wide aspect ratio (docs/internal-mfd.md's "Split-screen layout" requirement). This file only
    // owns the overlay's own lifecycle (toggle, canvas resolution, split-vs-single layout, dispatching
    // Refresh once per pane) — what's actually drawn in a pane is an IInternalMfdPage, built and
    // owned by the page's own class (InternalMfdRwrPage, InternalMfdTgpPage, ...), not this one.
    // InternalMfdScreenResolver owns finding the cockpit canvas itself; this class doesn't reach
    // into Cockpit/TacScreen directly any more.
    //
    // Mission-scoped (added in MissionLifecycle.StartReader, same as the Hud* cues), because the
    // Cockpit/TacScreen chain only exists for a live local-player aircraft.
    internal class InternalMfdPoc : MonoBehaviour
    {
        private static bool _enabled;

        private Canvas?     _tacCanvas;   // fake-null once its aircraft despawns
        private Aircraft?   _tacAircraft; // which aircraft _tacCanvas belongs to, so a switch is caught
                                           // even if the old Canvas hasn't gone fake-null yet
        private GameObject? _overlay;
        private IInternalMfdPage? _leftPage;
        private IInternalMfdPage? _rightPage;
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
                if (!InternalMfdScreenResolver.ResolveCanvas(aircraft, out _tacCanvas, out _)) return;
                _tacAircraft = aircraft;
            }

            if (_overlay == null) BuildOverlay(aircraft, _tacCanvas!);
            RefreshPages();
        }

        private void Teardown()
        {
            if (_overlay != null) Destroy(_overlay);
            _overlay = null;
            _leftPage = null;
            _rightPage = null;
            _tacCanvas = null;
            _tacAircraft = null;
        }

        // ~10Hz, same idea as TacScreen's own 0.05s per-frame throttle — plenty for gauges a human
        // is reading, and avoids setting Text.text (a layout-rebuild trigger) every single frame.
        // One TelemetrySnapshot fetch shared by every mounted page, not one per page — every page's
        // own-ship/contact data has to come from the same snapshot anyway (InternalMfdRwrPage's own
        // header comment on why), so fetching it once here instead of letting each page fetch its
        // own copy is both cheaper and removes any chance of two pages reading different snapshots
        // a frame apart.
        private void RefreshPages()
        {
            if (_leftPage == null && _rightPage == null) return;
            if (Time.time - _lastRefresh < 0.1f) return;
            _lastRefresh = Time.time;

            if (!TelemetryServer.TryGetLatestSnapshot(out TelemetrySnapshot snap)) return;
            _leftPage?.Refresh(snap);
            _rightPage?.Refresh(snap);
        }

        private void BuildOverlay(Aircraft aircraft, Canvas canvas)
        {
            // Build into a local first: on an exception partway through, the field stays null and
            // the next frame's LateUpdate retries cleanly, instead of caching a half-built overlay.
            var overlay = new GameObject("NOXMFD_InternalMfdPoc", typeof(RectTransform));
            overlay.layer = canvas.gameObject.layer; // SetParent does NOT inherit the parent's layer

            string unitName = aircraft.definition != null ? aircraft.definition.unitName : "?";
            bool knownCenterScreen = unitName == InternalMfdScreenResolver.CenterScreenAircraft;
            Vector2 anchorMin = knownCenterScreen ? InternalMfdScreenResolver.CenterScreenUvMin : Vector2.zero;
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

            Font? font = InternalMfdUi.ResolveFont();

            // Split-screen layout (docs/internal-mfd.md "Split-screen layout"): a wide screen splits
            // into two independently-addressable halves with a vertical separator; a square-ish
            // screen stays one full-view region. T/A-30's center screen is the only aircraft
            // calibrated so far (~2.8:1 — unambiguously wide), so it's the only one that splits.
            // Unverified aircraft get the background panel only, no page content — there's nothing
            // calibrated to show there yet (open question, docs/internal-mfd.md "Per-aircraft screen
            // geometry"), and guessing at a full-canvas page would just bleed onto its side screens
            // the same way the very first version of this POC did.
            if (knownCenterScreen)
            {
                RectTransform left = BuildHalf(rt, "Left", right: false);
                RectTransform rightHalf = BuildHalf(rt, "Right", right: true);
                BuildSeparator(rt);

                _leftPage = new InternalMfdTgpPage(left, font);
                _rightPage = new InternalMfdRwrPage(rightHalf, font);
            }

            _overlay = overlay;
        }

        // Left or right 50% of parent, full height.
        private static RectTransform BuildHalf(RectTransform parent, string name, bool right)
        {
            var go = InternalMfdUi.NewUi(name, parent, parent.gameObject.layer, typeof(RectTransform));
            var rt = go.GetComponent<RectTransform>();
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
            var go = InternalMfdUi.NewUi("Separator", parent, parent.gameObject.layer, typeof(Image));
            var rt = go.GetComponent<RectTransform>();
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
