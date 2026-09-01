// TD nav discovery (issue #47, docs/target-designator.md) — appends/removes NAV.tgt's TD entry at
// runtime based on live squad membership, the same shape ext-nav.js already uses for "a NAV entry
// whose PRESENCE is discovered at runtime, not authored" (NAV.tgt's static baseline, nav-model.js,
// is just the MAIN back-link). Unlike EXT, whose install set never changes after BepInEx boots, a
// squad can be joined/left at any time, so this reacts to the live squad-state push
// (docs/sse-push-refactor.md) instead of scanning once. Like ext-nav.js's own documented limitation
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
  // appends to it. Every apply rebuilds NAV.tgt from THIS, never from NAV.tgt itself, so repeated
  // applies don't concatenate a second TD entry.
  let tgtBase = null;

  // `s` is /squad's own {ready, state} shape — identical whether it came from the one-time bootstrap
  // fetch below or a later 'sqd-state' push (SseHub.cs wraps both the same way on purpose). td.js
  // listens for that same push directly (docs/sse-push-refactor.md) to catch a squad-ended edge
  // while it's open, so this only needs to keep NAV.tgt's TD entry in sync, nothing more.
  function apply(NAV, s) {
    if (!tgtBase) tgtBase = NAV.tgt.slice();
    const inSquad = !!(s && s.ready && s.state && s.state.role !== 'none');
    NAV.tgt = buildTgtNavPlan(tgtBase, inSquad);
  }

  function start(NAV) {
    if (!tgtBase) tgtBase = NAV.tgt.slice();
    // One-time bootstrap fetch — covers the brief gap before the SSE-relayed 'sqd-state' push below
    // arrives (docs/sse-push-refactor.md), and standalone/preview contexts with no shell/telemetry-
    // source at all. Every update after this rides the push instead of a recurring poll.
    fetch('/squad').then(function (r) { return r.ok ? r.json() : null; })
      .then(function (s) { apply(NAV, s); })
      .catch(function () { /* /squad unreachable — same as "no squad" */ });
    window.addEventListener('message', function (e) {
      const m = e.data;
      if (!m || m.mfd !== true || m.type !== 'sqd-state') return;
      apply(NAV, m.data);
    });
  }

  const api = { buildTgtNavPlan: buildTgtNavPlan, start: start };
  if (typeof module !== 'undefined' && module.exports) module.exports = api;
  else root.TdNav = api;
})(typeof self !== 'undefined' ? self : this);
