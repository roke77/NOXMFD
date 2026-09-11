// TD's pure "did anything worth a redraw change" decision (issue #84) — split out of td.js per
// src/web/README.md's "pure sibling module" pattern so it's checkable without a DOM. A redraw fires
// only on a real select/deselect (the locked id SET changed) or the player's Metric/Imperial
// preference flipping, never on a per-frame range/grid value drifting on an already-locked target.
export function idsKey(list) {
  return (list || []).map(function (t) { return t.id; }).sort(function (a, b) { return a - b; }).join(',');
}

// The member view has no id-set gate of its own (renderMember rebuilds wholesale on every
// 'td-state-push' already), so its redraw is metric-only — a role/element check stays in td.js.
export function tgtTargetsRedraw(nextTargets, lastIdsKey, nextMetric, lastMetric) {
  const nextIdsKey = idsKey(nextTargets);
  const metricChanged = nextMetric !== lastMetric;
  return {
    idsKey: nextIdsKey,
    leaderShouldRedraw: nextIdsKey !== lastIdsKey || metricChanged,
    memberShouldRedraw: metricChanged,
  };
}
