// Self-check for the telemetry frame handler. Run: `node telemetry-source.test.js`.
//
// The ONE property guarded here: a malformed frame must cost exactly one frame, never the session.
// The server hand-rolls its JSON (src/plugin/TelemetryServer.cs), so a serializer bug arrives as a
// parse throw in _onMessage — and an uncaught one takes down that tick's whole fan-out, freezing
// every page while the SSE connection stays open and the watchdog stays quiet. That failure has
// happened twice for real (a format string that dropped its decimal placeholder; unescaped control
// characters), which is why the recovery path is worth pinning down rather than trusting by eye.
//
// The drop is logged, not swallowed: both real bugs were persistent rather than transient blips,
// and the console error is what located them. So the log is also rate-limited — otherwise a
// persistent bug buries the console at 10 errors a second and hides the very message you need.
//
// Loaded with dynamic import() because this module is an ES module while the repo's other
// self-checks are CommonJS. Needs Node >= 22.7 (ESM syntax detection) since there is no
// package.json declaring the type.
const assert = require('assert');

(async () => {
  const { TelemetrySource, gridLabel } = await import('./telemetry-source.js');

  // ── gridLabel ───────────────────────────────────────────────────────────────────────
  // Reproduces the game's own grid label from world coords, and is read in two places (the MAP
  // page's HUD readout and this module's target derivation), so a slip shows up twice. The label
  // is what a pilot calls out, which makes silently-wrong output worse than an obvious blank.
  {
    const meta = { w: 100000, h: 100000, ox: 50000, oy: 50000 };   // the 100km map the harness mocks

    // Origin sits at (-ox, +oy) in world space, so the map's top-left corner is Aa00.
    assert.strictEqual(gridLabel(-50000, 50000, meta), 'Aa00', 'top-left corner should be Aa00');
    // Both axes are major/minor pairs on the same 10km/1km scale: X as two digits, Z as an
    // uppercase/lowercase letter pair.
    assert.strictEqual(gridLabel(-40000, 50000, meta), 'Aa10', '+10km east should step the major X digit');
    assert.strictEqual(gridLabel(-49000, 50000, meta), 'Aa01', '+1km east should step the minor X digit');
    assert.strictEqual(gridLabel(-50000, 40000, meta), 'Ba00', '10km south should step the UPPERCASE letter');
    assert.strictEqual(gridLabel(-50000, 49000, meta), 'Ab00', '1km south should step the lowercase letter');
    assert.strictEqual(gridLabel(-50000, -50000, meta), 'Ka00', '100km south should be ten uppercase steps');

    // No map metadata yet (pre-mission, or a map that never loaded) — a dash, not a crash or 'NaN'.
    assert.strictEqual(gridLabel(0, 0, null), '—', 'missing meta should read as a dash');
    assert.strictEqual(gridLabel(0, 0, undefined), '—', 'undefined meta should read as a dash');

    // Off the map's west/south edges the scheme has no label, so it must decline rather than emit
    // a bogus one from a negative char code.
    assert.strictEqual(gridLabel(-60000, 50000, meta), '—', 'west of the map should decline');
    assert.strictEqual(gridLabel(-50000, 60000, meta), '—', 'north of the map should decline');

    // The label range this scheme supports: majZ indexes from 'A', so it stays alphabetic while the
    // map is under ~260km tall. Pinned so a bigger map fails here rather than rendering '[c87'
    // in the cockpit — at which point the scheme, not this assertion, is what needs revisiting.
    const tall = { w: 100000, h: 300000, ox: 50000, oy: 150000 };
    assert.strictEqual(gridLabel(-50000, 150000 - 259000, tall), 'Zj00', '259km south is still the last alphabetic row');
    assert.ok(!/^[A-Z]/.test(gridLabel(-50000, 150000 - 260000, tall)),
      'past 260km the leading letter runs off Z — the scheme needs revisiting if a map gets this tall');
  }

  const src = new TelemetrySource({});
  src._postUp = () => {};                 // no parent window outside a browser

  const errs = [];
  const realErr = console.error;
  console.error = (...a) => errs.push(a.join(' '));

  try {
    // A truncated frame must not throw out of the handler.
    assert.doesNotThrow(() => src._onMessage({ data: '{"broken": ' }), 'bad frame threw');
    assert.strictEqual(src._badFrames, 1, 'bad frame not counted');
    assert.strictEqual(errs.length, 1, 'first bad frame should log exactly once');
    assert.ok(errs[0].includes('malformed telemetry frame dropped'), `log message wrong: ${errs[0]}`);

    // A raw control character mid-string — the exact shape of the second real bug.
    assert.doesNotThrow(() => src._onMessage({ data: '{"name":"a' + String.fromCharCode(1) + 'b"}' }), 'control-char frame threw');
    assert.strictEqual(src._badFrames, 2, 'second bad frame not counted');

    // Repeats inside the window stay quiet, so a persistent bug can't bury the console.
    for (let i = 0; i < 50; i++) src._onMessage({ data: 'nope' });
    assert.strictEqual(errs.length, 1, `rate limit failed — logged ${errs.length} times`);
    assert.strictEqual(src._badFrames, 52, 'drops still counted while rate-limited');

    // THE POINT: a good frame arriving right after the bad ones is still processed, so the
    // display repaints itself instead of staying frozen.
    src._onMessage({ data: JSON.stringify({ ping: true, soiSeq: 0 }) });
    assert.ok(src._lastMsgAt > 0, 'good frame not handled after bad ones');

    // Once the window elapses a persistent bug speaks up again, carrying its running total.
    src._lastBadLogAt -= 6000;
    src._onMessage({ data: 'still bad' });
    assert.strictEqual(errs.length, 2, 'should re-log after the rate-limit window');
    assert.ok(errs[1].includes('53 total'), `running total wrong: ${errs[1]}`);
  } finally {
    console.error = realErr;
  }

  console.log('telemetry-source.test.js: OK');
})();
