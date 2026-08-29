using System.Reflection;
using TMPro;
using UnityEngine;

namespace NOXMFD
{
    // Native HUD time-to-impact readout for the shared focused target. Follows HudTgpCue.cs's
    // build/refresh/rebuild shape because the game rebuilds HUD objects across aircraft respawns,
    // taking this added label with them.
    internal sealed class HudTtiCue : MonoBehaviour
    {
        // Same amber as HudWaypointCue's own readout, rather than a distinct shade, so mod-added HUD
        // cues share one visual accent while staying distinct from the stock green radar altitude.
        private static readonly Color Amber = new Color(1f, 0.6667f, 0f, 1f);

        // Fixed rather than cloned from radarAlt, whose auto-sizing depends on text length and would
        // scale short TTI strings differently from the surrounding readouts.
        private const float TtiFontSize = 13f * 1.5f;

        private static bool _reflectionTried;
        private static FieldInfo? _radarAltField;

        // Recomputing TTI walks UnitRegistry.allUnits, so keep it on the same 4 Hz cadence as the
        // contact snapshots. Visibility still reacts every frame through the cheap focus/aircraft
        // gates above.
        private const float RecomputeInterval = 0.25f;
        private float _recomputeTimer;
        private uint  _cachedTargetId;
        private float _cachedTti = -1f;

        private TMP_Text? _label;
        private string?   _lastLabelText;

        private void LateUpdate()
        {
            uint targetId = TargetFocus.Id;
            if (!GameManager.GetLocalAircraft(out Aircraft ac) || ac == null || targetId == 0)
            {
                Hide();
                return;
            }

            if (_label == null && !Build())
            {
                Hide();
                return;
            }

            _recomputeTimer += Time.deltaTime;
            if (targetId != _cachedTargetId || _recomputeTimer >= RecomputeInterval || _cachedTti < 0f)
            {
                _recomputeTimer = 0f;
                _cachedTargetId = targetId;
                _cachedTti = TargetTtiEstimator.ComputeTti(targetId, ac.persistentID.Id);
            }

            if (_cachedTti < 0f)
            {
                Hide();
                return;
            }
            SetLabelText("TTI " + HudTtiMath.FormatTti(_cachedTti));
            SetVisible(true);
        }

        // Text.text dirties Unity UI layout even when the value is unchanged, so skip no-op writes
        // during the frame loop.
        private void SetLabelText(string text)
        {
            if (_lastLabelText == text) return;
            _lastLabelText = text;
            _label!.text = text;
        }

        // Log each build failure shape once; repeated frame-loop warnings would hide useful logs.
        private static bool _loggedNoAltitude;
        private static bool _loggedBadField;

        private bool Build()
        {
            if (!EnsureReflection()) return false;
            // Include inactive, matching HudTgpCue.ResolveFont's own find call — nothing about
            // finding Altitude actually requires it (or an ancestor) to be active right now, so
            // there's no reason to risk missing it over a momentary/conditional inactive state.
            Altitude? altitude = UnityEngine.Object.FindFirstObjectByType<Altitude>(FindObjectsInactive.Include);
            if (altitude == null)
            {
                if (!_loggedNoAltitude) { _loggedNoAltitude = true; Plugin.Log?.LogWarning("[NOXMFD] HUD TTI: FindFirstObjectByType<Altitude> found nothing — cue disabled."); }
                return false;
            }
            // The live radarAlt label is TextMeshPro, matching the current native HUD UI stack.
            if (_radarAltField!.GetValue(altitude) is not TMP_Text radarAlt || radarAlt == null)
            {
                if (!_loggedBadField) { _loggedBadField = true; Plugin.Log?.LogWarning("[NOXMFD] HUD TTI: Altitude found, but radarAlt field read null/wrong type — cue disabled."); }
                return false;
            }

            var labelObject = new GameObject("NOXMFD_TtiCue", typeof(RectTransform), typeof(TextMeshProUGUI));
            RectTransform rect = labelObject.GetComponent<RectTransform>();
            rect.SetParent(radarAlt.transform.parent, false);
            RectTransform src = radarAlt.rectTransform;
            rect.anchorMin = src.anchorMin;
            rect.anchorMax = src.anchorMax;
            rect.pivot = src.pivot;
            rect.sizeDelta = src.sizeDelta;
            // Directly below radarAlt, offset by rendered line height plus a small gap; the source
            // rect is taller than the visible glyph line.
            rect.anchoredPosition = src.anchoredPosition + new Vector2(0f, -(TtiFontSize + 2f));

            _label = labelObject.GetComponent<TextMeshProUGUI>();
            _label.font = radarAlt.font;
            // radarAlt's own material, not TMP's default — the native HUD's glow/bloom treatment
            // lives on the material, not the font asset, and Amber below still tints on top of it
            // (Graphic.color multiplies the material's own face color rather than replacing it).
            _label.fontSharedMaterial = radarAlt.fontSharedMaterial;
            // Keep sizing fixed so short strings like "TTI 0:08" do not grow to fill radarAlt's
            // larger source rect.
            _label.enableAutoSizing = false;
            _label.fontSize = TtiFontSize;
            _label.color = Amber;
            _label.alignment = radarAlt.alignment;
            _label.overflowMode = radarAlt.overflowMode;
            _label.raycastTarget = false;
            _label.gameObject.SetActive(false);
            _lastLabelText = null;
            return true;
        }

        private static bool EnsureReflection()
        {
            if (_reflectionTried) return _radarAltField != null;
            _reflectionTried = true;
            _radarAltField = typeof(Altitude).GetField("radarAlt", BindingFlags.NonPublic | BindingFlags.Instance);
            if (_radarAltField == null)
                Plugin.Log?.LogWarning("[NOXMFD] HUD TTI: could not locate Altitude.radarAlt — cue disabled.");
            return _radarAltField != null;
        }

        private void SetVisible(bool visible)
        {
            if (_label != null && _label.gameObject.activeSelf != visible)
                _label.gameObject.SetActive(visible);
        }

        // Drop the cached TTI with visibility so a new lock cannot briefly inherit the previous
        // lock's countdown before the next scheduled recompute.
        private void Hide()
        {
            SetVisible(false);
            _cachedTargetId = 0;
            _cachedTti = -1f;
            _recomputeTimer = 0f;
        }

        private void OnDestroy()
        {
            if (_label != null) Destroy(_label.gameObject);
            _label = null;
        }
    }
}
