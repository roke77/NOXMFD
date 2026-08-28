// Shared PAD-cursor edge-triggered range step (docs/page-cursor.md, docs/rdr-fcr-hsd.md) — RDR's
// FCR and HSD both have a steppable display range and no cursor-driven panning, so pushing the
// cursor past the scope's top/bottom edge steps range instead: past the top (further from ownship)
// widens back out, past the bottom (toward/through ownship) narrows in.
//
// pad-cursor.js's onEdge fires every animation frame while the cursor is overshot (fine for MAP's
// continuous pan, map.js's own onCursorEdge) — a discrete step needs a cooldown instead, or one
// push would blow through every range step in a single frame.
const DEFAULT_COOLDOWN_MS = 400;

// step(dir) is called with +1 (top edge) or -1 (bottom edge); the caller owns what a step means
// (rangeIdx +/- 1 on both FCR and HSD today).
export function createEdgeRangeStepper(step, cooldownMs) {
  const cooldown = cooldownMs || DEFAULT_COOLDOWN_MS;
  // -Infinity, not 0: performance.now() starting near 0 (true first use, right after page load)
  // would otherwise read as "still within the cooldown" and silently swallow the first press.
  let lastStepAt = -Infinity;
  return function onCursorEdge(ex, ey) {
    if (!ey) return;
    const now = performance.now();
    if (now - lastStepAt < cooldown) return;
    lastStepAt = now;
    step(ey < 0 ? 1 : -1);
  };
}
