// Pure parsing for the AVN failure labels. No DOM, so it runs under node for the self-check.
//
// The game's failure-indicator strings are authored per-aircraft, so their wording and side
// notation vary: "LEFT ENGINE FIRE" / "RIGHT ENGINE FIRE" (T/A-30), "ENGINE FIRE L" /
// "ENGINE FIRE R" (F-99 Shrike), "LEFT ENGINE FAIL" / "TAIL ROTOR FAIL" / "MAIN ROTOR DAMAGE"
// (SAH-46). Rather than match a fixed table (which only ever lit the T/A-30), derive the side
// from the string and render whatever failures arrive.
(function (root) {
  // Side (L/R) a failure applies to, or null. Matches LEFT/RIGHT or a standalone L/R token, so it
  // works whether the airframe spells it "LEFT ENGINE FIRE" or "ENGINE FIRE L". LEFT is checked
  // first so it wins over the stray 'R' inside "RIGHT" etc.
  function failureSide(name) {
    const s = String(name).toUpperCase();
    if (/(^|[^A-Z])(LEFT|L)([^A-Z]|$)/.test(s))  return 'L';
    if (/(^|[^A-Z])(RIGHT|R)([^A-Z]|$)/.test(s)) return 'R';
    return null;
  }

  // Compact display text: strip the side token and abbreviate ENGINE, then prefix the side so
  // "ENGINE FIRE L" and "LEFT ENGINE FIRE" both read "L ENG FIRE". Side-less failures (e.g.
  // "MAIN ROTOR DAMAGE") pass through with just the ENGINE abbreviation.
  function failureText(name) {
    const side = failureSide(name);
    const body = String(name).toUpperCase()
      .replace(/(^|[^A-Z])(LEFT|RIGHT|L|R)([^A-Z]|$)/, '$1$3')   // drop the first side token
      .replace(/ENGINE/g, 'ENG')
      .replace(/\s+/g, ' ')
      .trim();
    return side ? side + ' ' + body : body;
  }

  const api = { failureSide, failureText };
  if (typeof module !== 'undefined' && module.exports) module.exports = api;
  else root.AvnFailurePolicy = api;
})(typeof self !== 'undefined' ? self : this);
