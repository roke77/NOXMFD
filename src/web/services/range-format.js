// Shared "X,X km" / imperial range formatter for TGT and TD's target-list rows (issue #84) — both
// pages need the identical format, so it lives once here rather than one copy per page. European
// decimal comma is a deliberate style choice for these two lists; which unit it renders in follows
// the player's Metric/Imperial preference (PlayerSettings.unitSystem).
export function fmtRng(r, metric) {
  if (typeof r !== 'number' || !isFinite(r)) return '—';
  if (metric) return r.toFixed(1).replace('.', ',') + ' km';
  return (r * 0.539957).toFixed(1).replace('.', ',') + ' nm';
}
