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
      if (target === '') target = instances[instances.length - 1].cid;   // first display up takes focus
      return conn;
    },

    disconnect(conn) {
      const gone = instances.find(i => i.conn === conn);
      if (!gone) return;
      instances = instances.filter(i => i.conn !== conn);
      if (gone.cid !== target) return;                                   // wasn't focused; nothing moves
      if (instances.some(i => i.cid === gone.cid)) return;               // a twin still holds that cid
      target = instances.length ? oldestFirst()[0].cid : '';             // fall back to the oldest left
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

// ── Claiming ────────────────────────────────────────────────────────────────────────
{
  const s = makeServer();
  assert.strictEqual(s.target, '', 'nothing connected, nothing focused');
  s.connect('a');
  assert.strictEqual(s.target, 'a', 'the first display up becomes the SOI on its own');
  s.connect('b');
  assert.strictEqual(s.target, 'a', 'a later display must not steal focus');
}

// ── Releasing ───────────────────────────────────────────────────────────────────────
{
  const s = makeServer();
  const a = s.connect('a'), b = s.connect('b'); s.connect('c');
  s.disconnect(b);
  assert.strictEqual(s.target, 'a', 'an unfocused display dropping changes nothing');
  s.disconnect(a);
  assert.strictEqual(s.target, 'c', 'the focused display dropping falls back to the oldest left');
}
{
  const s = makeServer();
  const a = s.connect('a');
  s.disconnect(a);
  assert.strictEqual(s.target, '', 'the last display dropping leaves nothing focused');
  s.connect('d');
  assert.strictEqual(s.target, 'd', 'and the next display up claims it again');
}
{
  // A duplicated browser tab copies its sessionStorage, so both connections carry the same cid.
  const s = makeServer();
  const first = s.connect('twin'); s.connect('twin'); s.connect('other');
  s.disconnect(first);
  assert.strictEqual(s.target, 'twin', 'a surviving twin still holds that display open');
}

// ── Cycling ─────────────────────────────────────────────────────────────────────────
{
  const s = makeServer();
  s.connect('a'); s.connect('b'); s.connect('c');
  s.cycle(1);  assert.strictEqual(s.target, 'b');
  s.cycle(1);  assert.strictEqual(s.target, 'c');
  s.cycle(1);  assert.strictEqual(s.target, 'a', 'NEXT wraps past the end');
  s.cycle(-1); assert.strictEqual(s.target, 'c', 'PREV wraps past the start');
}
{
  // From no focus at all, either key must light something up rather than doing nothing.
  const s = makeServer();
  s.connect('a'); s.connect('b');
  const from = (dir) => { const t = makeServer(); t.connect('a'); t.connect('b');
                          t.disconnect(1); t.disconnect(2); t.cycle(dir); return t.target; };
  assert.strictEqual(from(1), '', 'with no displays at all there is nothing to focus');
  s.cycle(-1); assert.strictEqual(s.target, 'b', 'PREV from the first goes to the last');
}

console.log('soi-focus: ok');
