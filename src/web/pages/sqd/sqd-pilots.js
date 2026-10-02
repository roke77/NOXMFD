// Pure helpers behind SQD's duplicate-callsign notes (issue #107), split out per src/web/README.md's
// "pure sibling module" pattern so they're checkable without a DOM.
//
// `pilots` is /squad's state.pilots: SteamID -> {d: "VIPER 1-2", dup: another pilot flies the same}.

// The SteamIDs of the other pilots who fly the same callsign as `id` (case-insensitive).
export function sharerIds(pilots, id) {
  const p = (pilots || {})[id];
  if (!p || !p.dup) return [];
  const want = p.d.toUpperCase();
  return Object.keys(pilots).filter(function (k) { return k !== id && pilots[k].d.toUpperCase() === want; });
}

// "WIDOW", "WIDOW, REAPER", or "WIDOW, REAPER +2" for the amber ALSO FLOWN BY note.
export function sharedWithText(names) {
  const up = names.map(function (n) { return n.toUpperCase(); });
  return up.length > 2 ? up.slice(0, 2).join(', ') + ' +' + (up.length - 2) : up.join(', ');
}
