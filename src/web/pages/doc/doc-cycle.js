// Pure NEXT/PREV stepping logic for DOC (issue #82) — kept separate from doc.js's DOM/fetch glue
// so it's directly Node-testable (doc-cycle.test.js), same split as avn-throttle-policy.js.
(function (root) {
  // files: the live listing (re-read from disk on every NEXT/PREV — issue #82's own decision, so a
  // file a player adds or removes mid-session just starts/stops appearing). dir: +1 (NEXT) or -1
  // (PREV), wrapping at either end. Returns null when the folder is empty. If currentName is no
  // longer in the listing (deleted since it was opened), there's no "position it used to be at" to
  // step from, so this lands on the first file instead — deterministic and simple, rather than
  // guessing at where it would have sorted.
  function step(files, currentName, dir) {
    if (!files || files.length === 0) return null;
    const idx = files.indexOf(currentName);
    if (idx === -1) return files[0];
    const n = files.length;
    return files[((idx + dir) % n + n) % n];
  }

  const api = { step: step };
  if (typeof module !== 'undefined' && module.exports) module.exports = api;
  else root.DocCycle = api;
})(typeof self !== 'undefined' ? self : this);
