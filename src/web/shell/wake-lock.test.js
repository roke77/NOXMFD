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

// A document that can build the real createVideoFallback's <canvas>/<video> pair, for testing
// that function directly rather than only through a fully-fake fallback (as the controller tests
// above do). `playResult` lets a test control whether video.play() resolves or rejects.
function fakeCanvasDocument(playResult) {
  const tracks = [{ stopped: false, stop() { this.stopped = true; } }];
  const appended = [];
  const stream = { getTracks: () => tracks };
  const canvas = {
    width: 0, height: 0,
    getContext: () => ({ fillStyle: '', fillRect() {} }),
    captureStream: (fps) => { canvas.capturedAtFps = fps; return stream; },
  };
  const video = {
    muted: false, playsInline: false, srcObject: null, paused: false, removed: false, style: {},
    setAttribute() {},
    play() { return playResult ? playResult() : Promise.resolve(); },
    pause() { this.paused = true; },
    remove() { this.removed = true; },
  };
  const doc = {
    body: { appendChild(el) { appended.push(el); } },
    createElement(tag) { return tag === 'canvas' ? canvas : video; },
  };
  return { doc, canvas, video, tracks, appended };
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

async function browser_controller_wires_real_globals_and_survives_no_storage() {
  // createBrowserController (both shells' actual entry point) reaches for the real
  // document/localStorage/navigator.wakeLock directly rather than taking them as options — stub
  // just enough of each to prove it wires them through instead of silently no-op'ing, and that a
  // localStorage that throws on access (private-mode Safari) doesn't stop construction.
  const realDocument = global.document, realNavigator = global.navigator, realLocalStorage = global.localStorage;
  let requested = null;
  // Plain assignment doesn't work for navigator: modern Node ships its own global `navigator` as a
  // getter-only accessor with no setter, so `global.navigator = x` silently no-ops (non-strict
  // assignment to a setter-less accessor) rather than throwing — defineProperty is required to
  // actually replace it, so do the same for all three for consistency.
  function stubGlobal(name, value) { Object.defineProperty(global, name, { configurable: true, value: value, writable: true }); }
  stubGlobal('document', fakeDocument());
  stubGlobal('navigator', { wakeLock: { request: async (type) => { requested = type; return fakeSentinel(); } } });
  Object.defineProperty(global, 'localStorage', { configurable: true, get() { throw new Error('blocked'); } });
  try {
    const states = [];
    const controller = WakeLock.createBrowserController({ onState: s => states.push(s) });
    controller.toggle();
    await flush();
    assert.strictEqual(requested, 'screen');            // reached navigator.wakeLock, not a stub
    assert.strictEqual(controller.active(), true);
    assert.ok(states.some(s => s.active === true));
  } finally {
    stubGlobal('document', realDocument);
    stubGlobal('navigator', realNavigator);
    stubGlobal('localStorage', realLocalStorage);
  }
}

// createVideoFallback itself (the LAN/plain-HTTP path — docs/screen-wake-lock.md "Native lock vs.
// fallback") is only ever exercised above through a fully-fake fallback object; these test the
// real implementation's canvas/video wiring and teardown directly.

async function video_fallback_rejects_when_capture_stream_is_unsupported() {
  const { doc } = fakeCanvasDocument();
  doc.createElement = (tag) => (tag === 'canvas' ? {} : {});   // no captureStream on this canvas
  const fallback = WakeLock.createVideoFallback(doc);
  await assert.rejects(() => fallback.acquire(), /captureStream/);
  assert.strictEqual(fallback.active(), false);
}

async function video_fallback_acquires_and_becomes_active() {
  const { doc, canvas, video, appended } = fakeCanvasDocument();
  const fallback = WakeLock.createVideoFallback(doc);
  await fallback.acquire();
  assert.strictEqual(fallback.active(), true);
  assert.strictEqual(canvas.capturedAtFps, 1);
  assert.strictEqual(video.muted, true);
  assert.strictEqual(video.playsInline, true);
  assert.ok(appended.includes(video));   // actually attached, not just configured
  await fallback.release();   // drop the draw interval so the test process can exit
}

async function video_fallback_release_tears_everything_down() {
  const { doc, video, tracks } = fakeCanvasDocument();
  const fallback = WakeLock.createVideoFallback(doc);
  await fallback.acquire();
  await fallback.release();
  assert.strictEqual(fallback.active(), false);
  assert.strictEqual(video.paused, true);
  assert.strictEqual(video.removed, true);
  assert.strictEqual(tracks[0].stopped, true);
}

async function video_fallback_second_acquire_is_a_noop_while_active() {
  let canvasBuilds = 0;
  const { doc } = fakeCanvasDocument();
  const realCreateElement = doc.createElement;
  doc.createElement = (tag) => { if (tag === 'canvas') canvasBuilds++; return realCreateElement(tag); };
  const fallback = WakeLock.createVideoFallback(doc);
  await fallback.acquire();
  await fallback.acquire();   // must not build a second canvas/video while already playing
  assert.strictEqual(canvasBuilds, 1);
  await fallback.release();   // drop the draw interval so the test process can exit
}

async function video_fallback_play_rejection_tears_down_and_rejects() {
  const { doc, video, tracks } = fakeCanvasDocument(() => Promise.reject(new Error('NotAllowedError')));
  const fallback = WakeLock.createVideoFallback(doc);
  await assert.rejects(() => fallback.acquire(), /NotAllowedError/);
  assert.strictEqual(fallback.active(), false);
  assert.strictEqual(video.removed, true);      // no leaked <video>/track after a failed play()
  assert.strictEqual(tracks[0].stopped, true);
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
  await browser_controller_wires_real_globals_and_survives_no_storage();
  await video_fallback_rejects_when_capture_stream_is_unsupported();
  await video_fallback_acquires_and_becomes_active();
  await video_fallback_release_tears_everything_down();
  await video_fallback_second_acquire_is_a_noop_while_active();
  await video_fallback_play_rejection_tears_down_and_rejects();
  console.log('wake-lock: all assertions passed');
})();
