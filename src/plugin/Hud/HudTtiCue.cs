using System.Reflection;
using UnityEngine;
using UnityEngine.UI;

namespace NOXMFD
{
    // Time-to-impact estimate for the player's own in-flight guided weapon(s) tracking the locked,
    // focused target (TargetFocus.cs, issue #62), shown directly below the native radar altitude
    // readout (issue #67, docs/hud-tti-estimate.md). Follows HudTgpCue.cs's shape: build once
    // against the live HUD, refresh every frame, rebuild if the HUD tears down and comes back
    // (aircraft respawn) — Altitude's radarAlt Text goes Unity fake-null the same way CombatHUD's
    // iconLayer does there.
    internal sealed class HudTtiCue : MonoBehaviour
    {
        private static bool _reflectionTried;
        private static FieldInfo? _radarAltField;

        private Text? _label;

        private void LateUpdate()
        {
            if (!GameManager.GetLocalAircraft(out Aircraft ac) || ac == null)
            {
                Hide();
                return;
            }

            if (_label == null && !Build())
            {
                Hide();
                return;
            }

            float tti = ComputeTti(ac.persistentID.Id);
            if (tti < 0f)
            {
                Hide();
                return;
            }
            _label!.text = "TTI " + HudTtiMath.FormatTti(tti);
            SetVisible(true);
        }

        private bool Build()
        {
            if (!EnsureReflection()) return false;
            Altitude? altitude = UnityEngine.Object.FindFirstObjectByType<Altitude>();
            if (altitude == null) return false;
            if (_radarAltField!.GetValue(altitude) is not Text radarAlt || radarAlt == null) return false;

            var labelObject = new GameObject("NOXMFD_TtiCue", typeof(RectTransform), typeof(Text));
            RectTransform rect = labelObject.GetComponent<RectTransform>();
            rect.SetParent(radarAlt.transform.parent, false);
            RectTransform src = radarAlt.rectTransform;
            rect.anchorMin = src.anchorMin;
            rect.anchorMax = src.anchorMax;
            rect.pivot = src.pivot;
            rect.sizeDelta = src.sizeDelta;
            // Directly below radarAlt, offset by its own height plus a small gap — sign/magnitude
            // not yet verified in-game (docs/hud-tti-estimate.md's own open question).
            rect.anchoredPosition = src.anchoredPosition + new Vector2(0f, -(src.sizeDelta.y + 4f));

            _label = labelObject.GetComponent<Text>();
            _label.font = radarAlt.font;
            _label.fontSize = radarAlt.fontSize;
            _label.color = radarAlt.color;
            _label.alignment = radarAlt.alignment;
            _label.horizontalOverflow = radarAlt.horizontalOverflow;
            _label.verticalOverflow = radarAlt.verticalOverflow;
            _label.raycastTarget = false;
            _label.gameObject.SetActive(false);
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

        // Smallest time-to-impact among the player's own in-flight guided weapons tracking the
        // focused, locked target — the one closest to hitting, per the ticket's own "first or
        // closest weapon release" wording. -1 when there's nothing to show: no target focused, the
        // target's gone, or nothing of the player's own is currently tracking it (no pre-release
        // estimate — see docs/hud-tti-estimate.md's non-goals).
        private static float ComputeTti(uint playerId)
        {
            uint focusedId = TargetFocus.Id;
            if (focusedId == 0) return -1f;
            if (!UnitRegistry.TryGetUnit(new PersistentID { Id = focusedId }, out Unit target) ||
                target == null || target.disabled)
                return -1f;

            float best = -1f;
            foreach (Unit u in UnitRegistry.allUnits)
            {
                if (u is not Missile m || m.disabled) continue;
                if (m.ownerID.Id != playerId || m.targetID.Id != focusedId) continue;

                float t = EstimateImpactTime(m, target);
                if (t >= 0f && (best < 0f || t < best)) best = t;
            }
            return best;
        }

        // ponytail: HudTtiMath's own formula is a range/closing-speed approximation, not a real
        // intercept prediction — no foresight of a hard turn by either side between frames. Matches
        // the game's own incoming-missile evasion math (AIPilotCombatModes.EvadeModeRadar), which
        // accepts the same approximation for a maneuvering, guided threat.
        private static float EstimateImpactTime(Missile missile, Unit target)
        {
            if (missile.rb == null || target.rb == null) return -1f;
            GlobalPosition from = missile.GlobalPosition(), to = target.GlobalPosition();
            Vector3 relVel = missile.rb.velocity - target.rb.velocity;
            return HudTtiMath.TimeToImpact(from.x, from.y, from.z, to.x, to.y, to.z, relVel.x, relVel.y, relVel.z);
        }

        private void SetVisible(bool visible)
        {
            if (_label != null && _label.gameObject.activeSelf != visible)
                _label.gameObject.SetActive(visible);
        }

        private void Hide() => SetVisible(false);

        private void OnDestroy()
        {
            if (_label != null) Destroy(_label.gameObject);
            _label = null;
        }
    }
}
