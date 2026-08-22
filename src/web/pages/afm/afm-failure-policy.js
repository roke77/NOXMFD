// Parses AFM failure-indicator strings, which vary in wording/side notation per aircraft
// (e.g. "LEFT ENGINE FIRE" vs "ENGINE FIRE L"). No DOM, runs under node for the self-check.
(function (root) {
  // Matches LEFT/RIGHT or a standalone L/R token. LEFT checked first so it wins over
  // the stray 'R' inside "RIGHT".
  function failureSide(name) {
    const s = String(name).toUpperCase();
    if (/(^|[^A-Z])(LEFT|L)([^A-Z]|$)/.test(s))  return 'L';
    if (/(^|[^A-Z])(RIGHT|R)([^A-Z]|$)/.test(s)) return 'R';
    return null;
  }

  // "ENGINE FIRE L" and "LEFT ENGINE FIRE" both render as "L ENG FIRE".
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
  else root.AfmFailurePolicy = api;
})(typeof self !== 'undefined' ? self : this);
