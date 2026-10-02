// The MAP tap/click hit test, split out of map.js so it carries no canvas or DOM refs and can be
// unit-checked in Node (map-select.test.js) — same treatment as map-transform.js.
//
// `targets` is map.js's per-frame hitTargets: {cx, cy, r, id, selectable, ...} in canvas pixels.
// Contacts with no id (the player's own icon, nav points) are never candidates.
(function (root) {
  // The NEAREST contact within reach (r + pad) of (px,py) whose selection state is `selected`, or
  // null — the hit test selectAt (a click, a touch tap, Cursor Select) and deselectAt (right-click,
  // Cursor Deselect) share. `isSelected(t)` answers a contact's selection state (telemetry plus the
  // optimistic pending set).
  //
  // Selecting skips contacts the game would refuse (`selectable === false`: neutral units with no
  // faction, and units the TGT filters exclude). They stay on the map and in the hover tooltip, but
  // must not win this test: a refused contact never becomes selected, so it would stay the nearest
  // unselected one on every tap and shadow whatever sits under it (a player's report: a neutral dot
  // on top of a launcher site made the launcher untappable). `selectable` absent counts as selectable.
  function nearestContact(targets, px, py, pad, selected, isSelected) {
    let hit = null, bestD2 = Infinity;
    for (let i = targets.length - 1; i >= 0; i--) {   // topmost (last-drawn) first, so a tie goes to it
      const t = targets[i];
      if (t.id == null || isSelected(t) !== selected) continue;
      if (!selected && t.selectable === false) continue;
      const dx = px - t.cx, dy = py - t.cy, d2 = dx * dx + dy * dy;
      const reach = t.r + pad;
      if (d2 <= reach * reach && d2 < bestD2) { bestD2 = d2; hit = t; }
    }
    return hit;
  }

  const api = { nearestContact };
  if (typeof module !== 'undefined' && module.exports) module.exports = api;
  else root.MapHit = api;
})(typeof self !== 'undefined' ? self : this);
