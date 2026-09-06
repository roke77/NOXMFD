using System.Collections.Generic;
using UnityEngine;

namespace NOXMFD
{
    // Live target-lock TTI estimator shared by the native HUD cue and the TGT telemetry rows.
    // It scans the player's own in-flight guided weapons and returns the shortest current
    // range/closing-speed estimate for the requested locked target(s).
    internal static class TargetTtiEstimator
    {
        // The most recent ComputeAll batch (TelemetryReader's ~4 Hz contact scan) — ComputeTti below
        // checks this first. HudTtiCue polls at its own, independent ~4 Hz cadence for just the
        // focused target, which TargetFocus's own invariant guarantees is always one of the ids that
        // batch just covered — so this turns what used to be a second full UnitRegistry.allUnits
        // scan a few milliseconds later into a dictionary lookup. Falls back to a direct scan
        // (ComputeSingle) on a miss, so correctness never depends on the cache being warm.
        private static Dictionary<uint, float> _lastBatch = new Dictionary<uint, float>();
        private static uint _lastBatchPlayerId;

        internal static float ComputeTti(uint targetId, uint playerId)
        {
            if (playerId == _lastBatchPlayerId && _lastBatch.TryGetValue(targetId, out float cached)) return cached;
            if (!TargetUnitLookup.TryResolve(targetId, out Unit target)) return -1f;
            return ComputeSingle(target, targetId, playerId);
        }

        // One UnitRegistry.allUnits pass for every locked target together, rather than the old
        // one-full-scan-per-target loop (O(locked count x unit count) every contact-scan tick).
        // Resolves each missile's assigned target at most once, instead of re-checking it against
        // every locked id in turn.
        internal static float[] ComputeAll(uint[] targetIds, uint playerId)
        {
            var result = new float[targetIds.Length];
            for (int i = 0; i < result.Length; i++) result[i] = -1f;

            var matchCount = new int[targetIds.Length];   // diagnostic only, see LogMatchCountChanges
            if (targetIds.Length > 0)
            {
                var targets = new Dictionary<uint, (Unit unit, int index)>(targetIds.Length);
                for (int i = 0; i < targetIds.Length; i++)
                    if (TargetUnitLookup.TryResolve(targetIds[i], out Unit u)) targets[targetIds[i]] = (u, i);

                foreach (Unit u in UnitRegistry.allUnits)
                {
                    if (u is not Missile m || m.disabled) continue;
                    if (m.ownerID.Id != playerId) continue;
                    if (!TryResolveAssignedTarget(m, targets, out (Unit unit, int index) assigned)) continue;

                    matchCount[assigned.index]++;
                    float t = EstimateImpactTime(m, assigned.unit);
                    if (t >= 0f && (result[assigned.index] < 0f || t < result[assigned.index])) result[assigned.index] = t;
                }
            }

            LogMatchCountChanges(targetIds, result, matchCount);

            var fresh = new Dictionary<uint, float>(targetIds.Length);
            for (int i = 0; i < targetIds.Length; i++) fresh[targetIds[i]] = result[i];
            _lastBatch = fresh;
            _lastBatchPlayerId = playerId;
            return result;
        }

        // TEMPORARY in-game verification diagnostic (docs/hud-tti-estimate.md's "Batch scan" needs
        // live confirmation of: multiple simultaneously-locked targets staying independent, two
        // missiles on one target aggregating to the smaller TTI, and a BVR shot holding its match
        // through the midcourse/seeker-track transition instead of dropping to 0 and back). Logs
        // only on a CHANGE in how many of the player's own missiles are assigned to a given target,
        // not every ~4 Hz tick, so a normal mission produces a handful of lines, not a flood. Remove
        // once a play session has confirmed the counts/TTI values behave as expected.
        private static readonly Dictionary<uint, int> _lastMatchCount = new Dictionary<uint, int>();

        private static void LogMatchCountChanges(uint[] targetIds, float[] tti, int[] matchCount)
        {
            for (int i = 0; i < targetIds.Length; i++)
            {
                int prev = _lastMatchCount.TryGetValue(targetIds[i], out int p) ? p : 0;
                if (matchCount[i] != prev)
                    Plugin.Log?.LogInfo($"[NOXMFD] TTI diag: target {targetIds[i]} tracked by {matchCount[i]} missile(s) (was {prev}), tti={tti[i]:0.0}");
                _lastMatchCount[targetIds[i]] = matchCount[i];
            }
            // A target that's no longer locked stops appearing in targetIds — drop it here too so
            // this dictionary doesn't grow across a long mission full of lock/unlock cycles.
            if (_lastMatchCount.Count > targetIds.Length)
            {
                var idSet = new HashSet<uint>(targetIds);
                var stale = new List<uint>();
                foreach (uint k in _lastMatchCount.Keys) if (!idSet.Contains(k)) stale.Add(k);
                foreach (uint k in stale) _lastMatchCount.Remove(k);
            }
        }

        private static float ComputeSingle(Unit target, uint targetId, uint playerId)
        {
            float best = -1f;
            foreach (Unit u in UnitRegistry.allUnits)
            {
                if (u is not Missile m || m.disabled) continue;
                if (m.ownerID.Id != playerId || !IsAssignedTo(m, targetId)) continue;

                float t = EstimateImpactTime(m, target);
                if (t >= 0f && (best < 0f || t < best)) best = t;
            }
            return best;
        }

        // Missile.targetID alone under-reports badly (see MissileSeekerAccess.cs): it only reflects
        // a live, currently-confirmed seeker track, cleared on every routine dropout and unset for a
        // radar missile's entire midcourse phase. The seeker's own persistent targetUnit survives
        // those gaps, so match on either -- an active lock (targetID) is always also a targetUnit
        // match (every SetTarget call passes targetUnit itself), so this only ever widens matches,
        // never narrows them; the targetID check stays as a cheap fast path and a fallback for the
        // rare case GetComponent<MissileSeeker> comes back empty.
        private static bool IsAssignedTo(Missile m, uint targetId)
        {
            if (m.targetID.Id == targetId) return true;
            MissileSeeker? seeker = m.GetComponent<MissileSeeker>();
            return seeker != null && MissileSeekerAccess.GetTargetUnit(seeker) is Unit tu && tu.persistentID.Id == targetId;
        }

        // Batch twin of IsAssignedTo: same targetID-then-seeker matching, but against the whole set
        // of locked ids at once (one dictionary, keyed by id, valued by both the resolved Unit and
        // its result-array slot) so each missile is only resolved once per scan. targetID.Id == 0
        // (unassigned) never matches — 0 is never a key in `targets` (TargetUnitLookup.TryResolve
        // reads id 0 as "no target", same convention TargetFocus/TelemetrySnapshot use).
        private static bool TryResolveAssignedTarget(Missile m, Dictionary<uint, (Unit unit, int index)> targets, out (Unit unit, int index) assigned)
        {
            if (targets.TryGetValue(m.targetID.Id, out assigned)) return true;
            MissileSeeker? seeker = m.GetComponent<MissileSeeker>();
            if (seeker != null && MissileSeekerAccess.GetTargetUnit(seeker) is Unit tu && targets.TryGetValue(tu.persistentID.Id, out assigned))
                return true;
            assigned = default;
            return false;
        }

        // target.rb is null for a static Unit — confirmed via a live diagnostic log (2026-09-05):
        // Building never touches its own `rb` (Unit.rb stays whatever the default is, i.e. never
        // set), so every bomb dropped on a building — the ordinary case for a guided bomb — hit the
        // old `target.rb == null` bail-out and silently produced no TTI at all, even though the
        // seeker match (IsAssignedTo above) was working correctly the whole time. A stationary
        // target has zero velocity, not "no velocity to compute against".
        private static float EstimateImpactTime(Missile missile, Unit target)
        {
            if (missile.rb == null) return -1f;
            GlobalPosition from = missile.GlobalPosition(), to = target.GlobalPosition();
            Vector3 targetVel = target.rb != null ? target.rb.velocity : Vector3.zero;
            Vector3 relVel = missile.rb.velocity - targetVel;
            return HudTtiMath.TimeToImpact(from.x, from.y, from.z, to.x, to.y, to.z, relVel.x, relVel.y, relVel.z);
        }
    }
}
