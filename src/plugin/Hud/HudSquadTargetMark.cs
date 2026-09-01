using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace NOXMFD
{
    // Issue #49 — two independent glyphs at the top-right corner of a unit's native HUD marker
    // (HUDUnitMarker — the square on ground units, the triangle on aircraft), showing that unit is
    // currently being targeted by someone else in the squad: "*" for the leader, "⌃" (chevron
    // up) for any other member (one chevron regardless of how many members — a "someone else has it"
    // flag, not a count). Both render in --no-squad teal (theme.css), not the unit's own faction
    // colour, so a squad mark reads as its own thing regardless of what it's stacked on.
    //
    // Same "ride CombatHUD's own marker instead of reprojecting world position" approach as
    // HudFocusMark.cs (top-left, amber "+", one always-alive mark for the single focused lock) — the
    // difference here is scale: any number of units can be squad-targeted at once, so this is a pool
    // of mark pairs keyed by persistentID, built/torn down as squad-targeting membership changes,
    // rather than one mark toggled on and off.
    internal sealed class HudSquadTargetMark : MonoBehaviour
    {
        private static readonly Color Teal = new Color(78f / 255f, 201f / 255f, 201f / 255f, 1f);

        // ponytail: fixed screen-pixel offsets rather than ones that scale with the target marker's
        // own distance-based icon size (HUDUnitMarker.customScale/distanceScale) — same simplification
        // HudFocusMark.cs already made and named its own upgrade path for; applies here too.
        private const float OffsetX = 16f;       // top-RIGHT corner — HudFocusMark's "+" sits top-left
        private const float LeaderOffsetY = 16f;
        private const float OtherOffsetY = 2f;   // stacked just below the leader slot when both show
        private const float MarkSize = 12f;

        private Transform? _iconLayer;

        private sealed class MarkPair
        {
            internal TMP_Text? Leader;
            internal TMP_Text? Other;
        }

        private readonly Dictionary<uint, MarkPair> _marks = new Dictionary<uint, MarkPair>();
        // Reused every frame — which ids are still squad-targeted-and-visible, so anything left over
        // afterward (no longer targeted by anyone, or its marker went off-screen) gets torn down.
        private readonly HashSet<uint> _seenScratch = new HashSet<uint>();

        private void LateUpdate()
        {
            CombatHUD hud = SceneSingleton<CombatHUD>.i;
            if (hud == null || hud.iconLayer == null) { HideAll(); return; }

            // The native HUD is rebuilt per aircraft spawn, taking iconLayer's children (and every
            // mark parented under it) with it — same rebuild-on-stale-reference shape HudFocusMark.cs
            // uses; this pool just needs a full clear instead of a single mark's rebuild, since the
            // old marks already died along with the old iconLayer.
            if (_iconLayer == null || _iconLayer != hud.iconLayer)
            {
                _iconLayer = hud.iconLayer;
                _marks.Clear();
            }

            // Fast exit: outside a squad, or in one where nobody has anything locked right now, there
            // is nothing this cue could ever show — skip walking every native HUD marker entirely
            // rather than doing that work every frame just to find nothing to draw.
            bool isLeader = Squad.IsLeader;
            bool inSquad = isLeader || Squad.IsMember;
            if (!inSquad || !SquadTargetsStore.HasAnyRemoteTargets(isLeader)) { HideAll(); return; }

            if (!CombatHudMarkerLookup.TryGet(hud, out var lookup)) { HideAll(); return; }

            _seenScratch.Clear();

            foreach (var kv in lookup)
            {
                Unit unit = kv.Key;
                HUDUnitMarker marker = kv.Value;
                if (unit == null || marker == null || marker.image == null) continue;   // truly gone

                uint id = unit.persistentID.Id;
                bool leaderTargeting = SquadTargetsStore.IsLeaderTargeting(id, isLeader);
                bool otherTargeting = SquadTargetsStore.IsOtherMemberTargeting(id, isLeader);
                if (!leaderTargeting && !otherTargeting) continue;

                _seenScratch.Add(id);
                if (!_marks.TryGetValue(id, out MarkPair pair)) _marks[id] = pair = Build();

                if (!marker.image.enabled)
                {
                    // Off-screen (edge-arrow-pinned lock) rather than gone — hide, don't destroy: a
                    // lock sitting right at the edge of view would otherwise tear down and rebuild
                    // this pair's GameObjects every time it crosses the boundary.
                    SetMark(pair.Leader, false, default);
                    SetMark(pair.Other, false, default);
                    continue;
                }

                Vector3 basePos = marker.image.rectTransform.position;
                SetMark(pair.Leader, leaderTargeting, basePos + new Vector3(OffsetX, LeaderOffsetY, 0f));
                SetMark(pair.Other, otherTargeting, basePos + new Vector3(OffsetX, OtherOffsetY, 0f));
            }

            if (_marks.Count > _seenScratch.Count)
            {
                List<uint>? stale = null;
                foreach (uint id in _marks.Keys) if (!_seenScratch.Contains(id)) (stale ??= new List<uint>()).Add(id);
                if (stale != null)
                    foreach (uint id in stale) { DestroyPair(_marks[id]); _marks.Remove(id); }
            }
        }

        private MarkPair Build() => new MarkPair
        {
            Leader = CreateGlyph("NOXMFD_SquadLeaderMark", "*"),
            Other  = CreateGlyph("NOXMFD_SquadMemberMark", "⌃"),
        };

        private TMP_Text CreateGlyph(string name, string glyph)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
            go.transform.SetParent(_iconLayer, false);
            var text = go.GetComponent<TextMeshProUGUI>();
            text.text = glyph;
            text.font = TMP_Settings.defaultFontAsset;
            text.fontSize = MarkSize;
            text.color = Teal;
            text.alignment = TextAlignmentOptions.Center;
            text.raycastTarget = false;
            go.SetActive(false);
            return text;
        }

        private static void SetMark(TMP_Text? mark, bool visible, Vector3 position)
        {
            if (mark == null) return;
            if (visible) mark.rectTransform.position = position;
            if (mark.gameObject.activeSelf != visible) mark.gameObject.SetActive(visible);
        }

        private void HideAll()
        {
            foreach (MarkPair pair in _marks.Values)
            {
                SetVisible(pair.Leader, false);
                SetVisible(pair.Other, false);
            }
        }

        private static void SetVisible(TMP_Text? mark, bool visible)
        {
            if (mark != null && mark.gameObject.activeSelf != visible) mark.gameObject.SetActive(visible);
        }

        private static void DestroyPair(MarkPair pair)
        {
            if (pair.Leader != null) Destroy(pair.Leader.gameObject);
            if (pair.Other != null) Destroy(pair.Other.gameObject);
        }

        private void OnDestroy()
        {
            foreach (MarkPair pair in _marks.Values) DestroyPair(pair);
            _marks.Clear();
        }
    }
}
