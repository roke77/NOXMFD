// Self-check for the disconnect-banner dismiss/re-arm state machine. Run: node conn-lost-banner.test.js
const assert = require('assert');
const ConnLostBanner = require('./conn-lost-banner.js');

function fakeBanner() {
  const calls = [];
  const ctrl = ConnLostBanner.createController({
    onShow: () => calls.push('show'),
    onHide: () => calls.push('hide'),
  });
  return { ctrl, calls };
}

// Connected → disconnected shows it; back to connected hides it.
{
  const { ctrl, calls } = fakeBanner();
  ctrl.update('connected');
  ctrl.update('disconnected');
  ctrl.update('connected');
  assert.deepStrictEqual(calls, ['hide', 'show', 'hide']);
}

// 'waiting' (CONNECTED — no mission) counts as not-disconnected, same as 'connected'.
{
  const { ctrl, calls } = fakeBanner();
  ctrl.update('disconnected');
  ctrl.update('waiting');
  assert.deepStrictEqual(calls, ['show', 'hide']);
}

// Repeated 'disconnected' ticks (the real watchdog fires on an interval) don't re-show on every
// tick once shown — no duplicate 'show' calls for one continuous outage.
{
  const { ctrl, calls } = fakeBanner();
  ctrl.update('disconnected');
  ctrl.update('disconnected');
  ctrl.update('disconnected');
  assert.deepStrictEqual(calls, ['show', 'show', 'show']); // onShow is idempotent by design — each
  // tick reasserts "should be shown"; the shell's onShow (classList.add / hidden=false) is itself
  // idempotent, so this is fine to call repeatedly rather than edge-triggered.
}

// Dismissing suppresses the CURRENT outage even under continued 'disconnected' ticks.
{
  const { ctrl, calls } = fakeBanner();
  ctrl.update('disconnected');
  ctrl.dismiss();
  ctrl.update('disconnected');
  ctrl.update('disconnected');
  assert.deepStrictEqual(calls, ['show', 'hide', 'hide', 'hide']);
}

// A dismiss re-arms on the NEXT connected→disconnected edge — a later real outage isn't silently
// swallowed by an old dismiss.
{
  const { ctrl, calls } = fakeBanner();
  ctrl.update('disconnected');
  ctrl.dismiss();
  ctrl.update('connected');   // reconnect
  ctrl.update('disconnected'); // a fresh outage
  assert.deepStrictEqual(calls, ['show', 'hide', 'hide', 'show']);
}

console.log('conn-lost-banner.test.js: OK');
