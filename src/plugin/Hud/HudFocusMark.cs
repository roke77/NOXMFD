using System.Collections.Generic;
using System.Reflection;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace NOXMFD
{
    // Amber "+" mark over the shared focused lock's own native target marker (TargetFocus.Id, issue
    // #62), placed at its top-left so a pilot with several targets locked can tell which one
    // Next/Previous currently has focus without looking away to a display. Added as a follow-up
    // requirement to issue #68 (docs/single-target-weapon-release.md), which reads the same shared
    // focus for which lock it releases at.
    //
    // Rides CombatHUD's own marker instead of reprojecting world position independently: every
    // locked target already gets a HUDUnitMarker (CombatHUD.markerLookup, reflected below — private)
    // whose own Image already tracks screen position, distance scale, and the off-screen edge-arrow
    // pin (HUDUnitMarker.UpdatePosition, _scratch/full/HUDUnitMarker.cs) — sitting as its sibling
    // under the same CombatHUD.iconLayer (public, already used by HudTgpCue.cs) inherits all of that
    // for free instead of re-deriving it.
    internal sealed class HudFocusMark : MonoBehaviour
    {
        // Same amber as every other mod-added HUD cue (HudWaypointCue/HudTtiCue's own #FFAA00).
        private static readonly Color Amber = new Color(1f, 0.6667f, 0f, 1f);

        // ponytail: a fixed screen-pixel offset rather than one that scales with the target marker's
        // own distance-based icon size (HUDUnitMarker.customScale/distanceScale) — good enough to
        // read as "attached to that marker" at typical HUD ranges. Upgrade path if it ever reads as
        // too close/far at extreme zoom or range: scale this with the marker's own
        // transform.localScale instead of a flat constant.
        private const float OffsetX = -16f, OffsetY = 16f;
        private const float MarkSize = 14f;

        private static bool _reflectionTried;
        private static FieldInfo? _markerLookupField;
        private static bool _loggedNoField, _loggedBadType;

        private TMP_Text? _mark;
        private Transform? _iconLayer;

        private void LateUpdate()
        {
            uint focusedId = TargetFocus.Id;
            if (focusedId == 0)
            {
                Hide();
                return;
            }

            CombatHUD hud = SceneSingleton<CombatHUD>.i;
            if (hud == null || hud.iconLayer == null)
            {
                Hide();
                return;
            }

            // The native HUD is rebuilt per aircraft spawn, taking iconLayer's children with it —
            // same rebuild-on-stale-reference shape HudTgpCue.cs already uses.
            if (_mark == null || _iconLayer == null || _iconLayer != hud.iconLayer)
            {
                if (!Build(hud.iconLayer))
                {
                    Hide();
                    return;
                }
            }

            if (!TryGetVisibleMarkerRect(hud, focusedId, out RectTransform markerRect))
            {
                Hide();
                return;
            }

            _mark!.rectTransform.position = markerRect.position + new Vector3(OffsetX, OffsetY, 0f);
            SetVisible(true);
        }

        // The focused target's own on-screen lock marker — not the off-screen edge-arrow
        // HUDUnitMarker.UpdatePosition falls back to once a locked target leaves view (`selected`
        // stays true then, but `image.enabled` goes false), which our own mark has nothing sensible
        // to sit next to either.
        private static bool TryGetVisibleMarkerRect(CombatHUD hud, uint targetId, out RectTransform rect)
        {
            rect = null!;
            if (!EnsureReflection()) return false;
            if (_markerLookupField!.GetValue(hud) is not Dictionary<Unit, HUDUnitMarker> lookup)
            {
                if (!_loggedBadType) { _loggedBadType = true; Plugin.Log?.LogWarning("[NOXMFD] HUD focus mark: CombatHUD.markerLookup read null/wrong type — cue disabled."); }
                return false;
            }
            if (!TargetUnitLookup.TryResolve(targetId, out Unit target)) return false;
            if (!lookup.TryGetValue(target, out HUDUnitMarker marker) || marker == null) return false;
            if (!marker.selected || marker.image == null || !marker.image.enabled) return false;
            rect = marker.image.rectTransform;
            return true;
        }

        private bool Build(Transform iconLayer)
        {
            _iconLayer = iconLayer;
            var markObject = new GameObject("NOXMFD_FocusMark", typeof(RectTransform), typeof(TextMeshProUGUI));
            markObject.transform.SetParent(iconLayer, false);
            _mark = markObject.GetComponent<TextMeshProUGUI>();
            // ponytail: a "+" text glyph rather than drawn geometry (two thin bar Images) — quicker
            // to ship, and the game's own default TMP font renders it as a clean small cross. Upgrade
            // path if it doesn't read well in-game: replace with a couple of Image rects instead.
            _mark.text = "+";
            _mark.font = TMP_Settings.defaultFontAsset;
            _mark.fontSize = MarkSize;
            _mark.color = Amber;
            _mark.alignment = TextAlignmentOptions.Center;
            _mark.raycastTarget = false;
            _mark.gameObject.SetActive(false);
            return true;
        }

        private static bool EnsureReflection()
        {
            if (_reflectionTried) return _markerLookupField != null;
            _reflectionTried = true;
            _markerLookupField = typeof(CombatHUD).GetField("markerLookup", BindingFlags.NonPublic | BindingFlags.Instance);
            if (_markerLookupField == null && !_loggedNoField)
            {
                _loggedNoField = true;
                Plugin.Log?.LogWarning("[NOXMFD] HUD focus mark: could not locate CombatHUD.markerLookup — cue disabled.");
            }
            return _markerLookupField != null;
        }

        private void SetVisible(bool visible)
        {
            if (_mark != null && _mark.gameObject.activeSelf != visible)
                _mark.gameObject.SetActive(visible);
        }

        private void Hide() => SetVisible(false);

        private void OnDestroy()
        {
            if (_mark != null) Destroy(_mark.gameObject);
            _mark = null;
        }
    }
}
