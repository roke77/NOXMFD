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

  // "First" = alphabetically first id — matches the server's own Manifest() ordering
  // (ExtensionRegistry.cs sorts by id), so EXT's default landing page is deterministic across
  // a page reload rather than depending on Set/object iteration order.
  function firstExtensionId() {
    var ids = Array.from(extIds).sort();
    return ids.length ? ids[0] : null;
  }

  // Fetches /ext-manifest once and applies the plan into the live, shared NAV object — the same
  // object every page/shell already holds a reference to via NavModel.NAV. Mutating it in place
  // is deliberate: BepInEx doesn't hot-reload plugins, so the extension set is fixed for the
  // whole session and this only ever needs to run once, before a pilot could plausibly reach EXT.
  function load(NAV) {
    return fetch('/ext-manifest').then(function (r) { return r.ok ? r.json() : []; })
      .then(function (items) {
        if (!Array.isArray(items) || items.length === 0) return;
        var plan = buildExtNavPlan(NAV.ext, items);
        NAV.ext = plan.ext;
        Object.keys(plan.perExtension).forEach(function (id) {
          NAV[id] = plan.perExtension[id];
          extIds.add(id);
        });
      })
      .catch(function () { /* /ext-manifest unreachable — same as "no extensions installed" */ });
  }

  var api = { buildExtNavPlan: buildExtNavPlan, load: load, isExtensionPage: isExtensionPage, firstExtensionId: firstExtensionId };
  if (typeof module !== 'undefined' && module.exports) module.exports = api;
  else root.ExtNav = api;
})(typeof self !== 'undefined' ? self : this);
