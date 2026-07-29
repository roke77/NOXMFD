// Self-check for the SOI focus rules in TelemetryServer.cs (SoiClaimIfUnfocused /
// SoiReleaseOnDisconnect / SoiCycle). Models them the way layout-sticky.test.js models the shells'
// inline head guards: the plugin has no C# test harness, and these are pure rules over a list, so
// the thing worth locking is the behaviour rather than the implementation.
// Run: node tools/soi-focus.test.js
const assert = require('assert');

// The registry is a list of { conn, cid } ordered by connection number — Instances().
function makeServer() {
  let next = 0, instances = [], target = '';
  const oldestFirst = () => instances.slice().sort((a, b) => a.conn - b.conn);

  return {
    get target() { return target; },
    get cids() { return oldestFirst().map(i => i.cid); },

    connect(cid) {
      const conn = ++next;
      instances.push({ conn, cid: cid || 'conn-' + conn });   // no cid sent -> connection-scoped one
      return conn;                                            // NO auto-claim — focus is opt-in
    },

    disconnect(conn) {
      const gone = instances.find(i => i.conn === conn);
      if (!gone) return;
      instances = instances.filter(i => i.conn !== conn);
      if (gone.cid !== target) return;                        // wasn't focused; nothing moves
      if (instances.some(i => i.cid === gone.cid)) return;    // a twin still holds that cid
      target = '';                                            // clear — never jump to another display
    },

    cycle(dir) {
      const all = oldestFirst();
      if (!all.length) { target = ''; return; }
      const i = all.findIndex(x => x.cid === target);
      const n = i < 0 ? (dir >= 0 ? 0 : all.length - 1)
                      : ((i + dir) % all.length + all.length) % all.length;
      target = all[n].cid;
    },
  };
}

// ── No default focus (opt-in) ─────────────────────────────────────────────────────────
{
  const s = makeServer();
  assert.strictEqual(s.target, '', 'nothing connected, nothing focused');
  s.connect('a');
  assert.strictEqual(s.target, '', 'the first display up does NOT take focus on its own');
  s.connect('b');
  assert.strictEqual(s.target, '', 'and neither does a second — SOI is opt-in');
  s.cycle(1);
  assert.strictEqual(s.target, 'a', 'a SOI keypress is what activates it');
}

// ── Releasing (never jumps) ───────────────────────────────────────────────────────────
{
  const s = makeServer();
  const a = s.connect('a'), b = s.connect('b'); s.connect('c');
  s.cycle(1);                                                        // opt in — focus 'a'
  s.disconnect(b);
  assert.strictEqual(s.target, 'a', 'an unfocused display dropping changes nothing');
  s.disconnect(a);
  assert.strictEqual(s.target, '', 'the focused display dropping CLEARS focus, never jumps');
  s.cycle(1);
  assert.strictEqual(s.target, 'c', 'the next SOI keypress re-picks from what is left');
}
{
  // A duplicated browser tab copies its sessionStorage, so both connections carry the same cid.
  const s = makeServer();
  const first = s.connect('twin'); s.connect('twin'); s.connect('other');
  s.cycle(1);                                                        // focus 'twin'
  s.disconnect(first);
  assert.strictEqual(s.target, 'twin', 'a surviving twin still holds that display open');
}

// ── Cycling ─────────────────────────────────────────────────────────────────────────
{
  const s = makeServer();
  s.connect('a'); s.connect('b'); s.connect('c');
  s.cycle(1);  assert.strictEqual(s.target, 'a', 'NEXT from no focus takes the first');
  s.cycle(1);  assert.strictEqual(s.target, 'b');
  s.cycle(1);  assert.strictEqual(s.target, 'c');
  s.cycle(1);  assert.strictEqual(s.target, 'a', 'NEXT wraps past the end');
  s.cycle(-1); assert.strictEqual(s.target, 'c', 'PREV wraps past the start');
}
{
  // From no focus, either key must light something up rather than doing nothing.
  const s = makeServer();
  s.connect('a'); s.connect('b');
  assert.strictEqual(s.target, '', 'still nothing focused until a key is pressed');
  s.cycle(-1); assert.strictEqual(s.target, 'b', 'PREV from no focus takes the last');
}
{
  // With nothing connected at all, a keypress has nothing to focus.
  const s = makeServer();
  s.cycle(1);  assert.strictEqual(s.target, '', 'no displays, nothing to focus');
}

console.log('soi-focus: ok');
