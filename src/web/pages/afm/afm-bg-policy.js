// Policy for when the AFM silhouette (#afm-bg) must (re)request itself.
// No DOM refs, so it runs standalone in Node (afm-bg-policy.test.js).
// Request/retry key off shown-vs-wanted type, not the layout cache, so a
// switch to an already-cached type still refreshes the silhouette.
(function (root) {
  // shownType is the last type handed to setAfmBg (null before any request).
  function shouldRequestBg(shownType, wantType) {
    return !!wantType && shownType !== wantType;
  }

  // cap bounds a pathological never-served bg; normal async capture lands well within it.
  function shouldRetryBg(currentName, reqType, loaded, tries, cap) {
    return !!reqType && !loaded && currentName === reqType && tries < cap;
  }

  const api = { shouldRequestBg, shouldRetryBg };
  if (typeof module !== 'undefined' && module.exports) module.exports = api;
  else root.AfmBgPolicy = api;
})(typeof self !== 'undefined' ? self : this);
