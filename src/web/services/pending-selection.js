// Shared optimistic pending-selection tracker (docs/page-cursor.md), mirroring the shape MAP's own
// map.js keeps for the same purpose — shared here since FCR/HSD need the identical behavior. A
// just-selected id is marked "locked" immediately, before the target.select request even resolves,
// and expires after holdMs. Contacts refresh at 4 Hz (TelemetryReader.ContactInterval) — well
// slower than a HOTAS button can repeat — so without this, a rapid burst of Select presses would
// keep re-computing the SAME nearest-unselected contact instead of advancing past it on each press.
const DEFAULT_HOLD_MS = 1500;

export function createPendingSelection(holdMs) {
  const hold = holdMs || DEFAULT_HOLD_MS;
  const expiry = new Map();   // id -> expiry ts (performance.now())
  return {
    mark: function (id) { expiry.set(id, performance.now() + hold); },
    clear: function (id) { expiry.delete(id); },
    isPending: function (id) {
      const exp = expiry.get(id);
      if (exp === undefined) return false;
      if (performance.now() >= exp) { expiry.delete(id); return false; }
      return true;
    },
  };
}
