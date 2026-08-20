// Extension nav discovery (docs/extensions-api.md) — merges installed extensions into the
// shared NAV table at runtime. NAV.ext's static baseline (nav-model.js) is just the MAIN
// back-link; this fills in one sub-item per installed extension, plus a matching NAV[<id>] (its
// own MAIN-only back-link) so its page can render nav labels the same way any other frame-hosted
// page does. This is the one NAV entry whose CONTENTS, not just its presence, are discovered
// rather than authored — every other page in nav-model.js is hand-written and test-pinned.
//
// Both layouts load this before their own shell script (mfd.html/f35.html) and call
// ExtNav.load(NAV) once at boot.
(function (root) {
  // Pure: given NAV.ext's current (static) contents and the manifest's items, returns the
  // finished NAV.ext array plus one NAV[id] array per extension. No I/O, no mutation of its
  // arguments — load() below is the only place that touches the shared NAV object itself.
  function buildExtNavPlan(baseExtNav, items) {
    var ext = baseExtNav.concat(items.map(function (it) {
      return { label: it.label, action: it.id };
    }));
    var perExtension = {};
    items.forEach(function (it) {
      // ponytail: every extension page gets the SAME single MAIN back-link today, not the full
      // N-way sibling swap NAV.akf's group gives MIS/OBJ/BDF/PAL — jumping straight between two
      // installed extensions costs a trip through MAIN for now. Upgrade path: once there's more
      // than a couple of real extensions, give each NAV[id] the same sibling list NAV.ext
      // carries (minus itself), mirroring the AKF fold exactly.
      perExtension[it.id] = [{ label: 'MAIN', action: 'main' }];
    });
    return { ext: ext, perExtension: perExtension };
  }

  var extIds = new Set();

  function isExtensionPage(name) { return extIds.has(name); }

  // NAV.ext's pristine static baseline (just the MAIN back-link, from nav-model.js), snapshotted
  // the first time load() runs — BEFORE anything appends to it. Every load() rebuilds NAV.ext from
  // THIS, never from NAV.ext itself, so a rescan (EXT clicked again) replaces the extension list
  // instead of concatenating a second copy onto what an earlier scan already added.
  var extBase = null;

  // Fetches /ext-manifest and applies the plan into the live, shared NAV object — the same object
  // every page/shell already holds a reference to via NavModel.NAV. Mutating it in place is
  // deliberate. Called once at boot, and again every time the EXT nav item is clicked (mfd.js/
  // f35.js) — BepInEx doesn't hot-reload plugins, so an extension already found never needs to be
  // un-found, but one that registers AFTER this browser tab loaded (a real, observed race: the
  // extension's own Awake() hadn't run yet when EXT was first fetched) needs a later click to
  // still pick it up rather than requiring a full page reload.
  function load(NAV) {
    if (!extBase) extBase = NAV.ext.slice();
    return fetch('/ext-manifest').then(function (r) { return r.ok ? r.json() : []; })
      .then(function (items) {
        if (!Array.isArray(items) || items.length === 0) return;
        var plan = buildExtNavPlan(extBase, items);
        NAV.ext = plan.ext;
        Object.keys(plan.perExtension).forEach(function (id) {
          NAV[id] = plan.perExtension[id];
          extIds.add(id);
        });
      })
      .catch(function () { /* /ext-manifest unreachable — same as "no extensions installed" */ });
  }

  var api = { buildExtNavPlan: buildExtNavPlan, load: load, isExtensionPage: isExtensionPage };
  if (typeof module !== 'undefined' && module.exports) module.exports = api;
  else root.ExtNav = api;
})(typeof self !== 'undefined' ? self : this);
