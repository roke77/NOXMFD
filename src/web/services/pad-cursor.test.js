// Self-check for the shared PAD crosshair. Run: `node pad-cursor.test.js`.
//
// This module is the highest-traffic untested code in the frontend: MAP, TGT, HUD and RDR all drive
// the same instance logic, so a regression here breaks four pages at once, and only on a HOTAS —
// the one input path that cannot be clicked in a browser to check by hand.
//
// It needs no refactor to test. `el` and `clampRect` are already injected, and drive() deliberately
// reads performance.now() rather than the rAF callback's timestamp "so it doesn't depend on how a
// given embedder invokes requestAnimationFrame" — a test is just another embedder. Stub the element,
// own the clock, and step the loop by hand.
//
// Loaded with dynamic import() because this is an ES module while the repo's other self-checks are
// CommonJS. Needs Node >= 22.7 (ESM syntax detection), as there is no package.json declaring type.
const assert = require('assert');

// ── Harness ───────────────────────────────────────────────────────────────────────────
let clock = 0;
let pending = null;                       // the single queued rAF callback
globalThis.performance = { now: () => clock };
globalThis.requestAnimationFrame = (cb) => { pending = cb; return 1; };
globalThis.cancelAnimationFrame = () => { pending = null; };

// Advance the clock and run one animation frame, the way a browser would.
function step(ms) { clock += ms; const cb = pending; pending = null; if (cb) cb(); }

const RECT = { dx: 0, dy: 0, dw: 400, dh: 300 };
const SPEED = 700;   // DEFAULT_SPEED in the module

function make(opts = {}) {
  const el = { style: {} };
  const seen = { select: [], hold: [], move: [], edge: [] };
  const cur = PadCursor.createPadCursor(Object.assign({
    el,
    clampRect: () => RECT,
    onSelect: (x, y) => seen.select.push([x, y]),
    onMove:   (x, y) => seen.move.push([x, y]),
    onEdge:   (ex, ey, dt) => seen.edge.push([ex, ey, dt]),
  }, opts));
  return { cur, el, seen };
}

let PadCursor;
(async () => {
  PadCursor = await import('./pad-cursor.js');

  // ── Focus: centre once, then never move it again ────────────────────────────────────
  // "Parked, not forgotten" — the contract focus loss and setHidden both promise. Re-centring on
  // every focus would yank the crosshair out from under the pilot each time SOI cycles away and back.
  {
    const { cur, el } = make();
    cur.setFocus(true, 100, 100);
    assert.strictEqual(el.style.display, 'block', 'focus should show the crosshair');
    assert.strictEqual(el.style.transform, 'translate(88px,88px)', 'first focus should centre it (less the 12px hotspot)');

    cur.setFocus(false);
    assert.strictEqual(el.style.display, 'none', 'losing focus should hide it');

    cur.setFocus(true, 999, 999);   // different centre — must be ignored, the cursor is parked
    assert.strictEqual(el.style.transform, 'translate(88px,88px)', 'refocus must resume where it was parked, not re-centre');
  }

  // ── Velocity integration ────────────────────────────────────────────────────────────
  // The first frame only establishes lastT (dt = 0), so movement starts on the second — otherwise
  // the very first frame after a deflection would jump by however long the page had been idle.
  {
    const { cur, el } = make();
    cur.setFocus(true, 100, 100);
    cur.setVector(1, 0);
    step(100);
    assert.strictEqual(el.style.transform, 'translate(88px,88px)', 'first frame must not move (dt is 0)');
    step(100);   // 0.1s at full deflection = SPEED * 0.1 = 70px
    assert.strictEqual(el.style.transform, `translate(${100 + SPEED * 0.1 - 12}px,88px)`, 'second frame should integrate one dt');
  }

  // ── The dt cap: a stalled tab must not teleport the cursor ───────────────────────────
  // drive() clamps dt to 0.1s. Without it, returning to a backgrounded tab (or any long frame gap)
  // would apply seconds of deflection in one step and fling the crosshair across the panel.
  {
    const { cur, el } = make();
    cur.setFocus(true, 100, 100);
    cur.setVector(1, 0);
    step(16);
    step(5000);   // a five-second stall
    assert.strictEqual(el.style.transform, `translate(${100 + SPEED * 0.1 - 12}px,88px)`,
      'a long frame gap must be capped at 0.1s of travel, not applied in full');
  }

  // ── Clamping: the cursor may never leave its rect ────────────────────────────────────
  {
    const { cur, el } = make();
    cur.setFocus(true, 200, 150);
    cur.setVector(1, 1);
    step(16);
    for (let i = 0; i < 40; i++) step(100);   // drive hard into the bottom-right corner
    assert.strictEqual(el.style.transform, `translate(${RECT.dx + RECT.dw - 12}px,${RECT.dy + RECT.dh - 12}px)`,
      'cursor should pin to the far corner, never past it');

    cur.setVector(-1, -1);
    for (let i = 0; i < 40; i++) step(100);
    assert.strictEqual(el.style.transform, `translate(${RECT.dx - 12}px,${RECT.dy - 12}px)`,
      'cursor should pin to the near corner, never past it');
  }

  // ── onEdge: the overflow hook MAP pans with and RDR ranges with ──────────────────────
  {
    const { cur, seen } = make();
    cur.setFocus(true, 200, 150);
    cur.setVector(1, 0);
    step(16); step(100);
    assert.strictEqual(seen.edge.length, 0, 'no edge signal while the cursor is still inside the rect');

    for (let i = 0; i < 40; i++) step(100);
    assert.ok(seen.edge.length > 0, 'pushing past the right edge should signal onEdge');
    const [ex, ey, dt] = seen.edge[0];
    assert.ok(ex > 0, `overflow past the right edge should be positive, got ${ex}`);
    assert.strictEqual(ey, 0, 'no vertical overflow when only pushing right');
    assert.ok(dt > 0 && dt <= 0.1, `edge dt should be a capped frame delta, got ${dt}`);
  }

  // ── Select vs hold arbitration ───────────────────────────────────────────────────────
  // A page with no onHold gets tap-on-press; a page with one must tell a tap from a hold, which is
  // what lets TGT long-press a row without also selecting it.
  {
    const { cur, seen } = make();   // no onHold
    cur.setFocus(true, 100, 100);
    cur.setSelectHeld(true);
    assert.strictEqual(seen.select.length, 1, 'without onHold, press alone should select');
  }
  {
    const held = [];
    const { cur, seen } = make({ holdMs: 20, onHold: (x, y) => held.push([x, y]) });
    cur.setFocus(true, 100, 100);
    cur.setSelectHeld(true);
    cur.setSelectHeld(false);                       // released well before holdMs
    assert.deepStrictEqual(seen.select, [[100, 100]], 'a quick release should be a select');
    assert.strictEqual(held.length, 0, 'a quick release must not fire the hold');

    seen.select.length = 0;
    cur.setSelectHeld(true);
    await new Promise(r => setTimeout(r, 45));      // outlast holdMs
    assert.deepStrictEqual(held, [[100, 100]], 'holding past holdMs should fire the hold');
    cur.setSelectHeld(false);
    assert.strictEqual(seen.select.length, 0, 'the release after a fired hold must not also select');
  }

  // An unfocused cursor has nothing under it — select must be inert rather than reporting a stale spot.
  {
    const { cur, seen } = make();
    cur.select();
    assert.strictEqual(seen.select.length, 0, 'select with no focus should do nothing');
  }

  // ── setHidden: invisible, but still exactly where it was ─────────────────────────────
  // TGT's Next/Previous hands Select to the focused lock and hides the crosshair meanwhile
  // (docs/tgt-cycle-focus.md); un-hiding must resume the spot.
  {
    const { cur, el } = make();
    cur.setFocus(true, 100, 100);
    const parked = el.style.transform;
    cur.setHidden(true);
    assert.strictEqual(el.style.display, 'none', 'hidden should stop painting it');
    cur.setHidden(false);
    assert.strictEqual(el.style.display, 'block', 'un-hiding should paint it again');
    assert.strictEqual(el.style.transform, parked, 'un-hiding must restore the exact parked position');
  }

  // ── reset: a mission boundary drops it entirely ──────────────────────────────────────
  // Unlike focus loss, reset must NOT keep the position — a stale spot from the previous mission.
  {
    const { cur, el, seen } = make();
    cur.setFocus(true, 100, 100);
    cur.reset();
    assert.strictEqual(el.style.display, 'none', 'reset should hide the crosshair');
    cur.select();
    assert.strictEqual(seen.select.length, 0, 'reset should leave nothing to select');
    cur.setFocus(true, 250, 175);
    assert.strictEqual(el.style.transform, 'translate(238px,163px)', 'after reset, focus should centre afresh');
  }

  // ── onMove mirrors visibility, so a page can drop whatever tracks the cursor ─────────
  {
    const { cur, seen } = make();
    cur.setFocus(true, 100, 100);
    assert.deepStrictEqual(seen.move.at(-1), [100, 100], 'onMove should report the visible position');
    cur.setFocus(false);
    assert.deepStrictEqual(seen.move.at(-1), [null, null], 'onMove should report null once it is not visible');
  }

  // ── getPos: a zoom anchor only while actually shown (issue #64) ──────────────────────
  {
    const { cur } = make();
    assert.strictEqual(cur.getPos(), null, 'unfocused cursor has no position to anchor on');
    cur.setFocus(true, 100, 100);
    assert.deepStrictEqual(cur.getPos(), { x: 100, y: 100 }, 'focused + placed should report its position');
    cur.setHidden(true);
    assert.strictEqual(cur.getPos(), null, 'hidden must not offer a position even while parked');
    cur.setHidden(false);
    cur.reset();
    assert.strictEqual(cur.getPos(), null, 'reset drops the position entirely');
  }

  console.log('pad-cursor.test.js: OK');
})();
