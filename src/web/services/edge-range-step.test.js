// Self-check for the shared PAD-cursor edge range stepper. Run: `node edge-range-step.test.js`.
//
// Extracted from FCR's and HSD's identical onCursorEdge (docs/rdr-fcr-hsd.md) — this is the first
// test either page's edge-step logic has had; up to now it rode on being a straight copy of
// "known good" code.
const assert = require('assert');

let clock = 0;
globalThis.performance = { now: () => clock };

(async () => {
  const { createEdgeRangeStepper } = await import('./edge-range-step.js');

  // ── No vertical overflow: never steps ────────────────────────────────────────────────
  {
    const steps = [];
    const onEdge = createEdgeRangeStepper((dir) => steps.push(dir));
    onEdge(5, 0);   // horizontal-only overflow (FCR/HSD scopes have no horizontal range concept)
    assert.deepStrictEqual(steps, [], 'ey === 0 must never step');
  }

  // ── Direction: top (ey < 0) widens out (+1), bottom (ey > 0) narrows in (-1) ─────────
  {
    const steps = [];
    const onEdge = createEdgeRangeStepper((dir) => steps.push(dir));
    onEdge(0, -3);
    assert.deepStrictEqual(steps, [1], 'past the top edge should step +1 (range up)');
    clock += 1000;   // clear the cooldown between the two assertions
    onEdge(0, 3);
    assert.deepStrictEqual(steps, [1, -1], 'past the bottom edge should step -1 (range down)');
  }

  // ── Cooldown: one push against the edge is one step, not one per animation frame ─────
  {
    const steps = [];
    const onEdge = createEdgeRangeStepper((dir) => steps.push(dir));
    onEdge(0, -1);
    onEdge(0, -1);
    onEdge(0, -1);
    assert.deepStrictEqual(steps, [1], 'repeated overflow within the cooldown must not re-step');
    clock += 400;
    onEdge(0, -1);
    assert.deepStrictEqual(steps, [1, 1], 'a push after the cooldown elapses should step again');
  }

  // ── Custom cooldown ───────────────────────────────────────────────────────────────────
  {
    const steps = [];
    const onEdge = createEdgeRangeStepper((dir) => steps.push(dir), 50);
    onEdge(0, -1);
    clock += 40;
    onEdge(0, -1);
    assert.deepStrictEqual(steps, [1], 'a custom cooldown shorter than the default must still gate re-steps');
    clock += 20;
    onEdge(0, -1);
    assert.deepStrictEqual(steps, [1, 1], 'a custom cooldown should still eventually allow another step');
  }

  console.log('edge-range-step.test.js: OK');
})();
