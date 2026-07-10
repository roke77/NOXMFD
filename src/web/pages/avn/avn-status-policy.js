// Pure state->class mapping for the AVN status tiles (GEAR / RADAR / GUNS). No DOM, so it runs
// under node for the self-check. Bright green when a system is ON, dim gray when OFF — except
// GEAR, which is bright RED when DOWN (deployed). The returned string is the modifier class the
// page adds alongside the base 'avn-tile' (styled in avn.css).
(function (root) {
  // kind: 'gear' | 'radar' | 'guns'; active: the boolean state (gear DOWN counts as active).
  // -> 'gear-down' (red) | 'on' (green) | 'off' (gray).
  function tileClass(kind, active) {
    if (!active) return 'off';
    return kind === 'gear' ? 'gear-down' : 'on';
  }

  const api = { tileClass };
  if (typeof module !== 'undefined' && module.exports) module.exports = api;
  else root.AvnStatusPolicy = api;
})(typeof self !== 'undefined' ? self : this);
