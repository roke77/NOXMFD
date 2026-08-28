using System.Reflection;
using TMPro;
using UnityEngine;

namespace NOXMFD
{
    // Time-to-impact estimate for the player's own in-flight guided weapon(s) tracking the locked,
    // focused target (TargetFocus.cs, issue #62), shown directly below the native radar altitude
    // readout (issue #67, docs/hud-tti-estimate.md). Follows HudTgpCue.cs's shape: build once
    // against the live HUD, refresh every frame, rebuild if the HUD tears down and comes back
    // (aircraft respawn) — Altitude's radarAlt label goes Unity fake-null the same way CombatHUD's
    // iconLayer does there.
    internal sealed class HudTtiCue : MonoBehaviour
    {
        // Same amber as HudWaypointCue's own readout (#FFAA00, alpha 1 — fully opaque, confirmed no
        // transparency), rather than a distinct shade — one amber across every mod-added HUD cue.
        // Distinct from radarAlt's native green so a TTI countdown reads as "mod-added, watch this"
        // rather than blending into the stock readout it sits under.
        private static readonly Color Amber = new Color(1f, 0.6667f, 0f, 1f);

        // 50% larger than HudWaypointCue's own in-game readout text (fontSize 13), by request —
        // a fixed size rather than cloned from radarAlt, which auto-sizes to fill its box and so
        // has no single "size" a short string like "TTI"/"0:08" can borrow (see Build()).
        private const float TtiFontSize = 13f * 1.5f;

        private static bool _reflectionTried;
        private static FieldInfo? _radarAltField;

        // Recomputing TTI means walking UnitRegistry.allUnits (docs/performance.md's own standard
        // for this shape of scan — TelemetryReader.ContactInterval throttles its own MAP/RDR/HSD
        // contact scans the same way, for the same reason). A HUD countdown reads fine refreshed
        // at this rate; visibility itself still reacts every frame via the cheap TargetFocus check
        // below, so losing a lock hides the cue immediately rather than lagging a quarter-second.
        private const float RecomputeInterval = 0.25f;
        private float _recomputeTimer;
        private float _cachedTti = -1f;

        private TMP_Text? _label;
        private string?   _lastLabelText;

        // TEMPORARY (issue #67 in-game diagnosis, remove once the "never shows" report is
        // resolved): an unconditional once-a-second heartbeat, independent of every early-return
        // below, so a silent LateUpdate (component never runs) is distinguishable in the log from
        // TargetFocus genuinely staying 0 or the label failing to Build().
        private float _heartbeatTimer;

        private void LateUpdate()
        {
            _heartbeatTimer += Time.deltaTime;
            if (_heartbeatTimer >= 1f)
            {
                _heartbeatTimer = 0f;
                bool hasAc = GameManager.GetLocalAircraft(out Aircraft hbAc) && hbAc != null;
                int lockCount = hasAc && hbAc.weaponManager != null ? hbAc.weaponManager.GetTargetList().Count : -1;
                Plugin.Log?.LogInfo(
                    $"[NOXMFD] HUD TTI heartbeat: hasAircraft={hasAc} lockCount={lockCount} " +
                    $"focusId={TargetFocus.Id} labelBuilt={_label != null}.");
            }

            if (!GameManager.GetLocalAircraft(out Aircraft ac) || ac == null || TargetFocus.Id == 0)
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
            if (_recomputeTimer >= RecomputeInterval || _cachedTti < 0f)
            {
                _recomputeTimer = 0f;
                _cachedTti = ComputeTti(ac.persistentID.Id);
            }

            if (_cachedTti < 0f)
            {
                Hide();
                return;
            }
            SetLabelText("TTI " + HudTtiMath.FormatTti(_cachedTti));
            SetVisible(true);
        }

        // Text.text dirties Unity UI layout on every set regardless of whether the content
        // actually changed (docs/performance.md's "Game main thread → per-frame UI churn" —
        // HudWaypointCue hit this same cost and fixed it the same way: skip the setter when the
        // formatted string hasn't moved since last frame).
        private void SetLabelText(string text)
        {
            if (_lastLabelText == text) return;
            _lastLabelText = text;
            _label!.text = text;
        }

        // TEMPORARY (issue #67 in-game diagnosis): logs Build()'s failure once per distinct cause,
        // so "never builds" (confirmed by the heartbeat) can be traced to the actual missing piece
        // instead of staying a silent no-op.
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
                if (!_loggedNoAltitude) { _loggedNoAltitude = true; Plugin.Log?.LogInfo("[NOXMFD] HUD TTI: FindFirstObjectByType<Altitude> found nothing."); }
                return false;
            }
            // TMP_Text, not UnityEngine.UI.Text: confirmed in-game (issue #67) that Altitude's
            // radarAlt is TextMeshPro now, matching the same 0.34 Text->TMP migration
            // TgpNativeOverlay.cs already worked around for TargetScreenUI's fields.
            if (_radarAltField!.GetValue(altitude) is not TMP_Text radarAlt || radarAlt == null)
            {
                if (!_loggedBadField) { _loggedBadField = true; Plugin.Log?.LogInfo("[NOXMFD] HUD TTI: Altitude found, but radarAlt field read null/wrong type."); }
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
            // Directly below radarAlt, offset by TtiFontSize (its own rendered line height) plus a
            // small gap — not sizeDelta.y (the box height, confirmed in-game to be much taller than
            // the actual glyph line, which left a visible gap with the native VVI ladder marks
            // between them).
            rect.anchoredPosition = src.anchoredPosition + new Vector2(0f, -(TtiFontSize + 2f));

            _label = labelObject.GetComponent<TextMeshProUGUI>();
            _label.font = radarAlt.font;
            // radarAlt's own material, not TMP's default — the native HUD's glow/bloom treatment
            // lives on the material, not the font asset, and Amber below still tints on top of it
            // (Graphic.color multiplies the material's own face color rather than replacing it).
            _label.fontSharedMaterial = radarAlt.fontSharedMaterial;
            // Fixed, not cloned from radarAlt: radarAlt auto-sizes to fill its box, so copying that
            // config (confirmed in-game) scaled TTI's short strings ("TTI", "0:08") up far past
            // every surrounding readout — auto-size reacts to content length, not a stable size.
            _label.enableAutoSizing = false;
            _label.fontSize = TtiFontSize;
            _label.color = Amber;
            _label.alignment = radarAlt.alignment;
            _label.overflowMode = radarAlt.overflowMode;
            _label.raycastTarget = false;
            _label.gameObject.SetActive(false);
            _lastLabelText = null;
            Plugin.Log?.LogInfo(
                $"[NOXMFD] HUD TTI: label built OK, parent='{radarAlt.transform.parent?.name}' " +
                $"srcAnchoredPos={src.anchoredPosition} srcSize={src.sizeDelta}.");
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

        // TEMPORARY (issue #67 in-game diagnosis, remove once the "never shows" report is
        // resolved): last focused id we logged a transition for, so a held focus doesn't spam.
        private static uint _lastLoggedFocusId = uint.MaxValue;

        // Smallest time-to-impact among the player's own in-flight guided weapons tracking the
        // focused, locked target — the one closest to hitting, per the ticket's own "first or
        // closest weapon release" wording. -1 when there's nothing to show: no target focused, the
        // target's gone, or nothing of the player's own is currently tracking it (no pre-release
        // estimate — see docs/hud-tti-estimate.md's non-goals).
        private static float ComputeTti(uint playerId)
        {
            uint focusedId = TargetFocus.Id;
            if (focusedId != _lastLoggedFocusId)
            {
                _lastLoggedFocusId = focusedId;
                string name = focusedId != 0 && UnitRegistry.TryGetUnit(new PersistentID { Id = focusedId }, out Unit fu) && fu != null
                    ? (fu.definition?.unitName ?? "?") : "none";
                Plugin.Log?.LogInfo($"[NOXMFD] HUD TTI: focus -> id={focusedId} name='{name}'.");
            }
            if (focusedId == 0) return -1f;
            if (!UnitRegistry.TryGetUnit(new PersistentID { Id = focusedId }, out Unit target) ||
                target == null || target.disabled)
                return -1f;

            float best = -1f;
            int ownMissiles = 0, trackingAnyTarget = 0, trackingFocused = 0;
            foreach (Unit u in UnitRegistry.allUnits)
            {
                if (u is not Missile m || m.disabled) continue;
                if (m.ownerID.Id != playerId) continue;
                ownMissiles++;
                if (m.targetID.Id != 0) trackingAnyTarget++;
                if (m.targetID.Id != focusedId) continue;
                trackingFocused++;

                float t = EstimateImpactTime(m, target);
                if (t >= 0f && (best < 0f || t < best)) best = t;
            }
            Plugin.Log?.LogInfo(
                $"[NOXMFD] HUD TTI: focus={focusedId} ownMissiles={ownMissiles} " +
                $"trackingAnyTarget={trackingAnyTarget} trackingFocused={trackingFocused} best={best:0.0}.");
            return best;
        }

        // Feeds live positions/velocities into HudTtiMath (see its own header for the formula and
        // its ponytail-labeled approximation limits).
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

        // Drops the cached TTI too, not just visibility — otherwise reappearing (a new lock, right
        // after the throttle window froze mid-count) could briefly show a stale number left over
        // from whatever was focused before, until the next scheduled recompute caught up.
        private void Hide()
        {
            SetVisible(false);
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
