// Self-check for the wake-lock controller. Run: node wake-lock.test.js
const assert = require('assert');
const WakeLock = require('./wake-lock.js');

function fakeDocument() {
  const listeners = {};
  return {
    visibilityState: 'visible',
    body: { appendChild() {} },
    createElement() { return { style: {}, setAttribute() {}, remove() {} }; },
    addEventListener(type, fn) { listeners[type] = fn; },
    removeEventListener(type) { delete listeners[type]; },
    dispatch(type) { if (listeners[type]) listeners[type](); },
  };
}

function fakeStorage(initial) {
  const values = Object.assign({}, initial);
  return {
    getItem(key) { return Object.prototype.hasOwnProperty.call(values, key) ? values[key] : null; },
    setItem(key, value) { values[key] = value; },
  };
}

function fakeSentinel() {
  let releaseListener = null;
  return {
    released: false,
    addEventListener(type, fn) { if (type === 'release') releaseListener = fn; },
    release() { this.released = true; if (releaseListener) releaseListener(); return Promise.resolve(); },
  };
}

function deferred() {
  let resolve, reject;
  const promise = new Promise((res, rej) => { resolve = res; reject = rej; });
  return { promise, resolve, reject };
}

// A macrotask tick (not just a handful of microtask ticks) so a rejection chain of arbitrary depth
// — native request -> fallback acquire -> combined-error throw -> onError — is fully drained
// before assertions run.
function flush() { return new Promise(resolve => setTimeout(resolve, 0)); }

async function no_persisted_preference_starts_off() {
  const states = [];
  const controller = WakeLock.createController({
    document: fakeDocument(),
    storage: fakeStorage(),
    wakeLock: { request: async () => fakeSentinel() },
    onState: s => states.push(s),
  });
  controller.start();
  await flush();
  assert.strictEqual(controller.enabled(), false);
  assert.strictEqual(controller.active(), false);
  assert.deepStrictEqual(states[0], { enabled: false, active: false });
}

async function persisted_on_acquires_immediately_on_start() {
  let requested = null;
  const controller = WakeLock.createController({
    document: fakeDocument(),
    storage: fakeStorage({ [WakeLock.STORAGE_KEY]: 'true' }),
    wakeLock: { request: async (type) => { requested = type; return fakeSentinel(); } },
    onState: () => {},
  });
  controller.start();
  await flush();
  assert.strictEqual(requested, 'screen');
  assert.strictEqual(controller.enabled(), true);
  assert.strictEqual(controller.active(), true);
}

async function toggle_persists_before_the_request_resolves() {
  const storage = fakeStorage();
  const gate = deferred();
  const controller = WakeLock.createController({
    document: fakeDocument(),
    storage: storage,
    wakeLock: { request: () => gate.promise },
    onState: () => {},
  });
  controller.start();
  await flush();
  controller.toggle();
  assert.strictEqual(storage.getItem(WakeLock.STORAGE_KEY), 'true');   // persisted before resolving
  assert.strictEqual(controller.active(), false);                     // not active until it resolves
  gate.resolve(fakeSentinel());
  await flush();
  assert.strictEqual(controller.active(), true);
}

async function native_rejection_falls_through_to_fallback() {
  let fallbackActive = false;
  const fallback = {
    acquire: async () => { fallbackActive = true; },
    release: async () => { fallbackActive = false; },
    active: () => fallbackActive,
  };
  const states = [];
  const controller = WakeLock.createController({
    document: fakeDocument(),
    storage: fakeStorage(),
    wakeLock: { request: async () => { throw new Error('insecure context'); } },
    createFallback: () => fallback,
    onState: s => states.push(s),
  });
  controller.start();
  controller.toggle();
  await flush();
  assert.strictEqual(controller.active(), true);
  assert.ok(states.some(s => s.active === true));
}

async function both_methods_failing_reports_error_and_turns_off() {
  const errors = [];
  const controller = WakeLock.createController({
    document: fakeDocument(),
    storage: fakeStorage(),
    wakeLock: { request: async () => { throw new Error('native failed'); } },
    createFallback: () => ({
      acquire: async () => { throw new Error('fallback failed'); },
      release: async () => {},
      active: () => false,
    }),
    onState: () => {},
    onError: e => errors.push(e),
  });
  controller.start();
  controller.toggle();
  await flush();
  assert.strictEqual(errors.length, 1);
  assert.ok(errors[0].nativeError);
  assert.ok(errors[0].fallbackError);
  assert.strictEqual(controller.enabled(), false);
  assert.strictEqual(controller.active(), false);
}

async function disabling_mid_acquire_leaves_no_lock_once_it_settles() {
  const gate = deferred();
  const sentinel = fakeSentinel();
  const controller = WakeLock.createController({
    document: fakeDocument(),
    storage: fakeStorage(),
    wakeLock: { request: () => gate.promise },
    onState: () => {},
  });
  controller.start();
  controller.toggle();     // enable — request is pending
  controller.toggle();     // disable again before it resolves
  gate.resolve(sentinel);
  await flush();
  assert.strictEqual(controller.enabled(), false);
  assert.strictEqual(controller.active(), false);
  assert.strictEqual(sentinel.released, true);   // the late arrival got released, not kept
}

async function visibility_hidden_releases_but_keeps_preference() {
  const doc = fakeDocument();
  let requestCount = 0;
  const controller = WakeLock.createController({
    document: doc,
    storage: fakeStorage(),
    wakeLock: { request: async () => { requestCount++; return fakeSentinel(); } },
    onState: () => {},
  });
  controller.start();
  controller.toggle();
  await flush();
  assert.strictEqual(controller.active(), true);

  doc.visibilityState = 'hidden';
  doc.dispatch('visibilitychange');
  await flush();
  assert.strictEqual(controller.active(), false);
  assert.strictEqual(controller.enabled(), true);   // preference untouched by a visibility change

  doc.visibilityState = 'visible';
  doc.dispatch('visibilitychange');
  await flush();
  assert.strictEqual(controller.active(), true);
  assert.strictEqual(requestCount, 2);              // reacquired without another toggle() call
}

async function storage_throwing_does_not_throw_out_of_the_controller() {
  const storage = {
    getItem() { throw new Error('blocked'); },
    setItem() { throw new Error('blocked'); },
  };
  const controller = WakeLock.createController({
    document: fakeDocument(),
    storage: storage,
    wakeLock: { request: async () => fakeSentinel() },
    onState: () => {},
  });
  assert.doesNotThrow(() => controller.start());
  await flush();
  assert.doesNotThrow(() => controller.toggle());
  await flush();
}

(async function main() {
  await no_persisted_preference_starts_off();
  await persisted_on_acquires_immediately_on_start();
  await toggle_persists_before_the_request_resolves();
  await native_rejection_falls_through_to_fallback();
  await both_methods_failing_reports_error_and_turns_off();
  await disabling_mid_acquire_leaves_no_lock_once_it_settles();
  await visibility_hidden_releases_but_keeps_preference();
  await storage_throwing_does_not_throw_out_of_the_controller();
  console.log('wake-lock: all assertions passed');
})();
