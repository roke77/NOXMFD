// TD nav discovery (issue #47, docs/target-designator.md) — appends/removes NAV.tgt's TD entry at
// runtime based on live squad membership, the same shape ext-nav.js already uses for "a NAV entry
// whose PRESENCE is discovered at runtime, not authored" (NAV.tgt's static baseline, nav-model.js,
// is just the MAIN back-link). Unlike EXT, whose install set never changes after BepInEx boots, a
// squad can be joined/left at any time, so this polls instead of scanning once — same 1s cadence
// the rest of the squad UI already uses (SQD/TD pages). Like ext-nav.js's own documented limitation
// (a newly-registered extension needs a later EXT click to appear), a freshly (dis)banded squad's
// TD entry shows up the next time NAV.tgt is read (i.e. the next visit to TGT), not necessarily the
// instant it changes.
//
// Both layouts load this before their own shell script (mfd.html/f35.html) and call
// TdNav.start(NAV) once at boot.
(function (root) {
  // Pure: given NAV.tgt's current (static) baseline and whether a squad exists, returns the
  // finished NAV.tgt array. No I/O, no mutation of its arguments.
  function buildTgtNavPlan(baseTgtNav, inSquad) {
    return inSquad ? baseTgtNav.concat([{ label: 'TD', action: 'td' }]) : baseTgtNav.slice();
  }

  // NAV.tgt's pristine static baseline, snapshotted the first time start() runs — BEFORE anything
  // appends to it. Every poll rebuilds NAV.tgt from THIS, never from NAV.tgt itself, so repeated
  // polls don't concatenate a second TD entry.
  let tgtBase = null;

  function poll(NAV) {
    if (!tgtBase) tgtBase = NAV.tgt.slice();
    return fetch('/squad').then(function (r) { return r.ok ? r.json() : null; })
      .then(function (s) {
        const inSquad = !!(s && s.ready && s.state && s.state.role !== 'none');
        NAV.tgt = buildTgtNavPlan(tgtBase, inSquad);
      })
      .catch(function () { /* /squad unreachable — same as "no squad" */ });
  }

  function start(NAV, intervalMs) {
    poll(NAV);
    setInterval(function () { poll(NAV); }, intervalMs || 2000);
  }

  const api = { buildTgtNavPlan: buildTgtNavPlan, start: start };
  if (typeof module !== 'undefined' && module.exports) module.exports = api;
  else root.TdNav = api;
})(typeof self !== 'undefined' ? self : this);
