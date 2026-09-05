// Self-check for the SOI focus rules in TelemetryServer.cs (SoiCycle / SoiRingLocked /
// SetPaneCount / SoiReleaseOnDisconnect). Models them the way layout-sticky.test.js models the
// shells' inline head guards: the plugin has no C# test harness, and these are pure rules over a
// list, so the thing worth locking is the behaviour rather than the implementation.
//
// Focus is a SURFACE — a (cid, pane) pair, not a whole document. An instance shows `panes` surfaces
// (1 full view, 2 classic split, up to 4 F-35 portals). SOI cycles the flat ring of every
// instance's every surface, instance-major and surface-minor, oldest connection first, deduped by
// cid so a twin doesn't double-count.
// Run: node tools/soi-focus.test.js
const assert = require('assert');

const NONE = { cid: '', pane: -1 };
const at = (cid, pane) => ({ cid, pane });
function eq(t, cid, pane) { return t.cid === cid && t.pane === pane; }

function makeServer() {
  let next = 0, instances = [], target = { cid: '', pane: -1 };
  // Per-surface opt-out (issue #58): only exclusions are stored, so "not in here" already means
  // included — the default every surface had before this feature existed.
  const excluded = new Set();
  const key = (cid, pane) => cid + ':' + pane;
  const oldestFirst = () => instances.slice().sort((a, b) => a.conn - b.conn);

  // The flat ring SoiCycle walks: each instance's surfaces, deduped by cid, skipping any surface
  // a pilot has excluded (it simply never appears, same as a surface that doesn't exist).
  function ring() {
    const out = [], seen = new Set();
    for (const inst of oldestFirst()) {
      if (seen.has(inst.cid)) continue;
      seen.add(inst.cid);
      for (let p = 0; p < inst.panes; p++)
        if (!excluded.has(key(inst.cid, p))) out.push({ cid: inst.cid, pane: p });
    }
    return out;
  }
  function set(cid, pane) { target = cid === '' ? { cid: '', pane: -1 } : { cid, pane }; }

  return {
    get target() { return target; },
    ring,
    isIncluded: (cid, pane) => !excluded.has(key(cid, pane)),

    connect(cid, panes = 1) {
      const conn = ++next;
      instances.push({ conn, cid: cid || 'conn-' + conn, panes });   // NO auto-claim — focus is opt-in
      return conn;
    },

    disconnect(conn) {
      const gone = instances.find(i => i.conn === conn);
      if (!gone) return;
      instances = instances.filter(i => i.conn !== conn);
      const hasTwin = instances.some(i => i.cid === gone.cid);
      // The cid is gone for good (no twin tab left holding it open) — drop any exclusions it set
      // so they can't outlive the display they described.
      if (!hasTwin) for (const k of [...excluded]) if (k.startsWith(gone.cid + ':')) excluded.delete(k);
      if (gone.cid !== target.cid) return;                         // wasn't focused; nothing moves
      if (hasTwin) return;                                         // a twin still holds that cid
      set('', -1);                                                 // clear — never jump to another display
    },

    // soi.panes: a client reports its surface count. Clamp focus into range if a merge shrank it,
    // and drop exclusions on any pane index that no longer exists — a later split that recreates
    // that same index starts back at the default (included).
    setPanes(cid, n) {
      if (!cid) return;
      n = Math.max(1, n);
      instances.forEach(i => { if (i.cid === cid) i.panes = n; });
      for (const k of [...excluded]) {
        if (!k.startsWith(cid + ':')) continue;
        if (Number(k.slice(cid.length + 1)) >= n) excluded.delete(k);
      }
      if (target.cid === cid && target.pane >= n) set(cid, n - 1);
    },

    // soi.include: a browser's LOAD LAYOUT checkbox opting one of its own surfaces in/out.
    // Excluding the surface that currently holds focus moves focus off it immediately.
    setIncluded(cid, pane, included) {
      const k = key(cid, pane);
      const changed = included ? excluded.delete(k) : !excluded.has(k) && (excluded.add(k), true);
      if (!changed || included) return;
      if (target.cid !== cid || target.pane !== pane) return;
      const r = ring();
      set(r.length ? r[0].cid : '', r.length ? r[0].pane : -1);
    },

    cycle(dir) {
      const r = ring();
      if (!r.length) { set('', -1); return; }
      const i = r.findIndex(s => s.cid === target.cid && s.pane === target.pane);
      const n = i < 0 ? (dir >= 0 ? 0 : r.length - 1)
                      : ((i + dir) % r.length + r.length) % r.length;
      set(r[n].cid, r[n].pane);
    },
  };
}

// ── No default focus (opt-in) ─────────────────────────────────────────────────────────
{
  const s = makeServer();
  assert.deepStrictEqual(s.target, NONE, 'nothing connected, nothing focused');
  s.connect('a');
  assert.deepStrictEqual(s.target, NONE, 'the first display up does NOT take focus on its own');
  s.connect('b');
  assert.deepStrictEqual(s.target, NONE, 'and neither does a second — SOI is opt-in');
  s.cycle(1);
  assert.deepStrictEqual(s.target, at('a', 0), 'a SOI keypress activates it, on the first surface');
}

// ── Releasing (never jumps) ───────────────────────────────────────────────────────────
{
  const s = makeServer();
  const a = s.connect('a'), b = s.connect('b'); s.connect('c');
  s.cycle(1);                                                      // focus (a,0)
  s.disconnect(b);
  assert.deepStrictEqual(s.target, at('a', 0), 'an unfocused display dropping changes nothing');
  s.disconnect(a);
  assert.deepStrictEqual(s.target, NONE, 'the focused display dropping CLEARS focus, never jumps');
  s.cycle(1);
  assert.deepStrictEqual(s.target, at('c', 0), 'the next keypress re-picks from what is left');
}
{
  // A duplicated browser tab copies its sessionStorage, so both connections carry the same cid.
  const s = makeServer();
  const first = s.connect('twin'); s.connect('twin'); s.connect('other');
  s.cycle(1);                                                      // focus (twin,0)
  s.disconnect(first);
  assert.deepStrictEqual(s.target, at('twin', 0), 'a surviving twin still holds that display open');
}

// ── Single-surface instances behave like the old whole-document focus ─────────────────
{
  const s = makeServer();
  s.connect('a'); s.connect('b'); s.connect('c');   // all panes=1
  s.cycle(1);  assert.deepStrictEqual(s.target, at('a', 0), 'NEXT from no focus takes the first');
  s.cycle(1);  assert.deepStrictEqual(s.target, at('b', 0));
  s.cycle(1);  assert.deepStrictEqual(s.target, at('c', 0));
  s.cycle(1);  assert.deepStrictEqual(s.target, at('a', 0), 'NEXT wraps past the end');
  s.cycle(-1); assert.deepStrictEqual(s.target, at('c', 0), 'PREV wraps past the start');
}

// ── Surfaces: cycle steps through an instance's panes before the next instance ────────
{
  const s = makeServer();
  s.connect('glass', 3);   // an F-35 with three portals
  s.connect('tab', 1);     // a classic tablet
  const order = [];
  for (let k = 0; k < 5; k++) { s.cycle(1); order.push(s.target.cid + ':' + s.target.pane); }
  assert.deepStrictEqual(order, ['glass:0', 'glass:1', 'glass:2', 'tab:0', 'glass:0'],
    'NEXT walks all of an instance\'s surfaces, then the next instance, then wraps');
  s.cycle(-1); assert.deepStrictEqual(s.target, at('tab', 0), 'PREV steps back across the boundary');
}

// ── soi.panes clamps a focused surface when a merge shrinks the glass ─────────────────
{
  const s = makeServer();
  s.connect('glass', 4);
  s.cycle(1); s.cycle(1); s.cycle(1); s.cycle(1);   // NONE→0→1→2→3, the last portal
  assert.deepStrictEqual(s.target, at('glass', 3));
  s.setPanes('glass', 2);                        // two portals merged away
  assert.deepStrictEqual(s.target, at('glass', 1), 'focus clamps to the last surface still there');
  s.setPanes('glass', 4);                        // split back out
  assert.deepStrictEqual(s.target, at('glass', 1), 'growing back does not move focus');
}
{
  // Shrinking an UNfocused instance never touches focus.
  const s = makeServer();
  s.connect('a', 1); s.connect('big', 3);
  s.cycle(1);                                     // focus (a,0)
  s.setPanes('big', 1);
  assert.deepStrictEqual(s.target, at('a', 0), 'an unfocused instance shrinking leaves focus alone');
}

// ── Nothing connected: a keypress has nothing to focus ────────────────────────────────
{
  const s = makeServer();
  s.cycle(1);  assert.deepStrictEqual(s.target, NONE, 'no displays, nothing to focus');
}

// ── SOI include/exclude (issue #58) ───────────────────────────────────────────────────
{
  // Default is included; excluding removes a surface from the ring without touching others.
  const s = makeServer();
  s.connect('a', 2);
  assert.ok(s.isIncluded('a', 0) && s.isIncluded('a', 1), 'both panes start included');
  assert.deepStrictEqual(s.ring(), [at('a', 0), at('a', 1)]);
  s.setIncluded('a', 1, false);
  assert.ok(!s.isIncluded('a', 1) && s.isIncluded('a', 0));
  assert.deepStrictEqual(s.ring(), [at('a', 0)], 'the excluded pane never appears in the ring');
  s.setIncluded('a', 1, true);
  assert.deepStrictEqual(s.ring(), [at('a', 0), at('a', 1)], 're-including restores it');
}
{
  // Excluding the currently-focused surface moves focus off it immediately.
  const s = makeServer();
  s.connect('a', 2);
  s.cycle(1);
  assert.deepStrictEqual(s.target, at('a', 0));
  s.setIncluded('a', 0, false);
  assert.deepStrictEqual(s.target, at('a', 1), 'focus moves to the next included surface');
  s.setIncluded('a', 1, false);
  assert.deepStrictEqual(s.target, NONE, 'excluding the last included surface clears focus');
}
{
  // Excluding a surface that ISN'T focused never touches focus.
  const s = makeServer();
  s.connect('a', 2); s.connect('b', 1);
  s.cycle(1); s.cycle(1);
  assert.deepStrictEqual(s.target, at('a', 1));
  s.setIncluded('b', 0, false);
  assert.deepStrictEqual(s.target, at('a', 1), 'excluding an unfocused surface changes nothing');
}
{
  // A merge that removes a pane index drops its exclusion; a later split recreating that same
  // index starts back at the default (included) — no dangling per-index state survives it.
  const s = makeServer();
  s.connect('glass', 2);
  s.setIncluded('glass', 1, false);
  assert.ok(!s.isIncluded('glass', 1));
  s.setPanes('glass', 1);                          // portals merged down to one
  s.setPanes('glass', 2);                          // split back out
  assert.ok(s.isIncluded('glass', 1), 'the recreated pane starts included again');
}
{
  // A cid with no twin left drops its exclusions on disconnect; the same cid reconnecting fresh
  // starts included again.
  const s = makeServer();
  const conn = s.connect('a', 1);
  s.setIncluded('a', 0, false);
  s.disconnect(conn);
  s.connect('a', 1);
  assert.ok(s.isIncluded('a', 0), 'a fresh connection under the same cid is not still excluded');
}
{
  // A duplicated tab (twin cid) keeps the exclusion alive across one of the two disconnecting.
  const s = makeServer();
  const first = s.connect('twin', 1); s.connect('twin', 1);
  s.setIncluded('twin', 0, false);
  s.disconnect(first);
  assert.ok(!s.isIncluded('twin', 0), 'a surviving twin keeps the exclusion');
}

console.log('soi-focus: ok');
