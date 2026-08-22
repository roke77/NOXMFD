// Pure, DOM-free state->class mapping so this runs under node for the self-check.
(function (root) {
  function tileClass(kind, active) {
    if (!active) return 'off';
    return kind === 'gear' ? 'gear-down' : 'on';
  }

  const api = { tileClass };
  if (typeof module !== 'undefined' && module.exports) module.exports = api;
  else root.AvnStatusPolicy = api;
})(typeof self !== 'undefined' ? self : this);
