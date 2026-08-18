// Pure policy for when the AFM silhouette image (#afm-bg) must (re)request itself. Split out of
// afm.js so it carries NO DOM refs and can be unit-checked in Node (see afm-bg-policy.test.js).
//
// The bug this guards against: firing the bg request only from inside ensureAvnLayout's "layout
// not cached yet" branch gets the silhouette stuck on the PREVIOUS aircraft whenever
//   (a) you switch to a type whose layout is already cached (setAvnBg never re-fires), or
//   (b) the bg PNG — captured async on the server — lags the layout and the one-shot retry
//       budget expires before it lands, with nothing left to re-arm it.
// These predicates decouple the bg lifecycle from the layout cache: request keys purely off the
// shown-vs-wanted type, and retry keys off "still the current aircraft and not yet loaded".
(function (root) {
  // (Re)issue a bg request iff the type currently shown/requested differs from the wanted type.
  // shownType is the last type handed to setAfmBg (null before any request). This is what makes a
  // switch to an already-cached-layout aircraft still refresh the silhouette.
  function shouldRequestBg(shownType, wantType) {
    return !!wantType && shownType !== wantType;
  }

  // Keep retrying a 404'd bg while it's still the current aircraft and hasn't loaded, up to a
  // generous safety cap. The async server capture always lands well within it once the airframe
  // is captured; the cap only bounds a pathological never-served bg. Stops immediately when the
  // aircraft changes so we never chase a stale plane.
  function shouldRetryBg(currentName, reqType, loaded, tries, cap) {
    return !!reqType && !loaded && currentName === reqType && tries < cap;
  }

  const api = { shouldRequestBg, shouldRetryBg };
  if (typeof module !== 'undefined' && module.exports) module.exports = api;
  else root.AfmBgPolicy = api;
})(typeof self !== 'undefined' ? self : this);
