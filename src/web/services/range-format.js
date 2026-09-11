// Shared "X,X km" / imperial range formatter for TGT and TD's target-list rows (issue #84) — the
// two pages showed identical copies of this before, both hardcoded to km regardless of the
// player's Metric/Imperial preference (PlayerSettings.unitSystem). European decimal comma is a
// deliberate style choice for these two lists, kept as-is; only the km-vs-nm split is new.
export function fmtRng(r, metric) {
  if (typeof r !== 'number' || !isFinite(r)) return '—';
  if (metric) return r.toFixed(1).replace('.', ',') + ' km';
  return (r * 0.539957).toFixed(1).replace('.', ',') + ' nm';
}
