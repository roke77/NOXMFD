using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace NOXMFD
{
    // Proof-of-concept for issue #43 (docs/internal-mfd.md): native NOXMFD page content drawn
    // directly on a cockpit screen — split left/right for a wide screen (InternalMfdScreenResolver's
    // per-aircraft Split flag), one full region for a squarish one (docs/internal-mfd.md's
    // "Split-screen layout" requirement). This file only owns the overlay's own lifecycle (toggle,
    // canvas resolution, split-vs-single layout, dispatching Refresh once per pane) — what's
    // actually drawn in a pane is an IInternalMfdPage, built and owned by the page's own class
    // (InternalMfdHsdPage, InternalMfdRwrPage, InternalMfdTgpPage, ...), not this one.
    // InternalMfdScreenResolver owns finding the cockpit canvas itself; this class doesn't reach
    // into Cockpit/TacScreen directly any more.
    //
    // One pane isn't a single fixed page: a default page (HSD for a split layout's left half, RWR
    // for a squarish layout's one region) mounts there normally, but InternalMfdTgpPage takes over
    // the instant the TGP has a real weapon lock or TgpManualControl's manual mode is engaged, then
    // the default page remounts the moment neither is true any more — the same "TGP owns the screen
    // while it's actually showing something" behavior the real cockpit's own small TGP display
    // already has, just extended to this whole pane. Both pages are built once and kept alive
    // behind their own wrapper GameObject; switching is a SetActive toggle, not a rebuild, so
    // neither page loses state across a swap. A split layout's right half (fixed RWR) never swaps.
    //
    // Mission-scoped (added in MissionLifecycle.StartReader, same as the Hud* cues), because the
    // Cockpit/TacScreen chain only exists for a live local-player aircraft.
    internal class InternalMfdController : MonoBehaviour
    {
        private static bool _enabled;

        private Canvas?     _tacCanvas;   // fake-null once its aircraft despawns
        private Aircraft?   _tacAircraft; // which aircraft _tacCanvas belongs to, so a switch is caught
                                           // even if the old Canvas hasn't gone fake-null yet
        private GameObject? _overlay;
        private Image?      _bgImage; // toggled off alongside _swapDefaultRoot when NativeTgpOnLock hides the overlay
        private GameObject? _swapDefaultRoot; // HSD (split) or RWR (squarish) — whichever TGP overrides
        private GameObject? _swapTgpRoot; // null when ScreenGeometry.NativeTgpOnLock — see BuildSwapPane
        private IInternalMfdPage? _swapDefaultPage;
        private IInternalMfdPage? _swapTgpPage;
        private IInternalMfdPage? _rightPage; // split layout only; fixed RWR, never overridden
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
            _bgImage = null;
            _swapDefaultRoot = null;
            _swapTgpRoot = null;
            _swapDefaultPage = null;
            _swapTgpPage = null;
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
            if (_swapDefaultPage == null && _swapTgpPage == null && _rightPage == null) return;
            if (Time.time - _lastRefresh < 0.1f) return;
            _lastRefresh = Time.time;

            if (_swapDefaultRoot != null)
            {
                bool tgpActive = IsTgpActive();
                _swapDefaultRoot.SetActive(!tgpActive);
                if (_swapTgpRoot != null)
                {
                    _swapTgpRoot.SetActive(tgpActive);
                }
                else
                {
                    // No TGP-override page for this aircraft (ScreenGeometry.NativeTgpOnLock —
                    // BuildSwapPane's allowTgpOverride=false) — the native cockpit already shows a
                    // correct TGP view on this exact screen when locked, so hide the whole overlay
                    // (background included) instead of drawing a redundant, slightly misaligned
                    // second copy on top of it, rather than just swapping to our own TGP page like
                    // the default case above does.
                    if (_bgImage != null) _bgImage.enabled = !tgpActive;
                }
            }

            if (!TelemetryServer.TryGetLatestSnapshot(out TelemetrySnapshot snap)) return;
            // Only the currently-mounted swap page gets refreshed — the hidden one has nothing on
            // screen to update, and skipping it (rather than refreshing both every tick regardless
            // of visibility) is free since SetActive above already decided which one that is.
            if (_swapDefaultRoot != null && _swapDefaultRoot.activeSelf) _swapDefaultPage?.Refresh(snap);
            if (_swapTgpRoot != null && _swapTgpRoot.activeSelf) _swapTgpPage?.Refresh(snap);
            _rightPage?.Refresh(snap);
        }

        // "TGP locks a target OR manual mode is engaged" — the exact same hasTargets/ManualMode
        // check TgpFeed.CaptureFrame and TgpFullScreen.Tick already use to decide whether the TGP
        // camera actually has something to show, so this pane's swap can't disagree with whether
        // the TGP feed itself is live.
        private static bool IsTgpActive()
        {
            if (!GameManager.GetLocalAircraft(out Aircraft aircraft) || aircraft == null) return false;
            if (TgpManualControl.ManualMode) return true;
            List<Unit>? targets = aircraft.weaponManager != null ? aircraft.weaponManager.GetTargetList() : null;
            return targets != null && targets.Count > 0;
        }

        private void BuildOverlay(Aircraft aircraft, Canvas canvas)
        {
            // Build into a local first: on an exception partway through, the field stays null and
            // the next frame's LateUpdate retries cleanly, instead of caching a half-built overlay.
            var overlay = new GameObject("NOXMFD_InternalMfdController", typeof(RectTransform));
            overlay.layer = canvas.gameObject.layer; // SetParent does NOT inherit the parent's layer

            // ?? "?", not just the `definition == null` case the ?. already covers: a null unitName
            // on an otherwise-valid definition would pass a null key into TryGetValue below, which
            // throws ArgumentNullException — every LateUpdate tick, since _overlay never gets set.
            string unitName = aircraft.definition?.unitName ?? "?";
            bool knownScreen = InternalMfdScreenResolver.ScreenGeometryByAircraft.TryGetValue(unitName, out InternalMfdScreenResolver.ScreenGeometry geometry);
            if (!knownScreen)
                Plugin.Log?.LogInfo($"[NOXMFD] Internal MFD POC: no verified screen crop for '{unitName}' — using the full (all-screens) canvas.");

            var rt = overlay.GetComponent<RectTransform>();
            rt.SetParent(canvas.transform, false);
            rt.SetAsLastSibling(); // top of paint order — confirmed live to cover native content
            rt.anchorMin = knownScreen ? geometry.AnchorMin : Vector2.zero;
            rt.anchorMax = knownScreen ? geometry.AnchorMax : Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;

            // No sprite — an Image with none draws a flat tinted quad, same trick HudWaypointCue
            // uses, so this ships no art and can't fail on a missing asset. Fully opaque (alpha 1) —
            // anything less lets the native content underneath show through, which is what "solid
            // background" reports were seeing.
            var bg = overlay.AddComponent<Image>();
            bg.color = new Color(0.03f, 0.05f, 0.03f, 1f);
            bg.raycastTarget = false;
            _bgImage = bg;

            Font? font = InternalMfdUi.ResolveFont();

            // Split-screen layout (docs/internal-mfd.md "Split-screen layout"): a wide screen splits
            // into two independently-addressable halves with a vertical separator, left half
            // swapping HSD/TGP and right half a fixed RWR; a squarish screen stays one full-view
            // region, swapping RWR/TGP instead (no separate fixed RWR pane to put it in). Unverified
            // aircraft get the background panel only, no page content — there's nothing calibrated
            // to show there yet, and guessing at a full-canvas page would just bleed onto its side
            // screens the same way the very first version of this POC did.
            bool allowTgpOverride = !(knownScreen && geometry.NativeTgpOnLock);
            if (knownScreen && geometry.Split)
            {
                RectTransform left = BuildHalf(rt, "Left", right: false);
                RectTransform rightHalf = BuildHalf(rt, "Right", right: true);
                BuildSeparator(rt);

                BuildSwapPane(left, font, useHsdDefault: true, allowTgpOverride);
                _rightPage = new InternalMfdRwrPage(rightHalf, font);
            }
            else if (knownScreen)
            {
                RectTransform full = BuildFullChild(rt, "Main");
                BuildSwapPane(full, font, useHsdDefault: false, allowTgpOverride);
            }

            _overlay = overlay;
        }

        // Both swap-pair pages are built up front and kept alive behind their own wrapper —
        // RefreshPages() toggles which wrapper is active each tick rather than tearing one down and
        // rebuilding the other, so neither page loses its pooled UI state (contact markers, etc.)
        // across a swap. useHsdDefault picks HSD (split layout's left half) vs RWR (a squarish
        // layout's one region) as the page InternalMfdTgpPage overrides. allowTgpOverride is false
        // only for ScreenGeometry.NativeTgpOnLock aircraft — no TGP page is built at all there;
        // RefreshPages hides the whole overlay on lock instead (see its own comment).
        private void BuildSwapPane(RectTransform region, Font? font, bool useHsdDefault, bool allowTgpOverride)
        {
            RectTransform defaultRegion = BuildFullChild(region, useHsdDefault ? "Hsd" : "Rwr");
            _swapDefaultPage = useHsdDefault ? new InternalMfdHsdPage(defaultRegion, font) : new InternalMfdRwrPage(defaultRegion, font);
            _swapDefaultRoot = defaultRegion.gameObject;

            if (!allowTgpOverride) return;

            RectTransform tgpRegion = BuildFullChild(region, "Tgp");
            _swapTgpPage = new InternalMfdTgpPage(tgpRegion, font);
            _swapTgpRoot = tgpRegion.gameObject;
        }

        // A full-stretch child of parent, its own GameObject so InternalMfdController can
        // SetActive it independently of any sibling built the same way.
        private static RectTransform BuildFullChild(RectTransform parent, string name)
        {
            var go = InternalMfdUi.NewUi(name, parent, parent.gameObject.layer, typeof(RectTransform));
            var rt = go.GetComponent<RectTransform>();
            InternalMfdUi.Stretch(rt);
            return rt;
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
            Teardown();
        }
    }
}
