// Screen wake-lock controller (docs/screen-wake-lock.md), shared by both shells (mfd.js, f35.js)
// so the acquire/release/fallback logic lives in one place. Pure aside from the injected
// document/storage/wakeLock/fallback dependencies, so it runs under node for the self-check
// (wake-lock.test.js) with fakes standing in for the browser.
//
// Every dependency is injected rather than reached for globally (navigator.wakeLock, localStorage,
// document) so the controller itself never assumes it's running in a real browser — the shell code
// that constructs it supplies the real objects; the test supplies fakes.
(function (root) {
  var STORAGE_KEY = 'noxmfd.wakelock';

  // The fallback for a plain-HTTP LAN address, where the native Wake Lock API is unavailable or
  // rejects because the page isn't a secure context (docs/screen-wake-lock.md "Native lock vs.
  // fallback"). A muted, playing <video> is the one broadly-supported way to keep a mobile browser
  // from dimming the screen without HTTPS. The video needs a live stream to play, so a tiny
  // <canvas>, redrawn on an interval, feeds it one via captureStream — the canvas is never shown.
  function createVideoFallback(doc) {
    var canvas = null;
    var video = null;
    var stream = null;
    var drawTimer = null;
    var playing = false;

    function teardown() {
      if (drawTimer !== null) { clearInterval(drawTimer); drawTimer = null; }
      if (stream) { stream.getTracks().forEach(function (track) { track.stop(); }); stream = null; }
      if (video) { video.pause(); video.srcObject = null; video.remove(); video = null; }
      canvas = null;
      playing = false;
    }

    function acquire() {
      if (playing) return Promise.resolve();
      canvas = doc.createElement('canvas');
      if (typeof canvas.captureStream !== 'function') {
        canvas = null;
        return Promise.reject(new Error('canvas.captureStream is not supported by this browser.'));
      }
      canvas.width = 2;
      canvas.height = 2;
      var ctx = canvas.getContext('2d');
      var frame = false;
      // Alternate near-black fills so the stream carries an actual changing frame — a static
      // canvas can produce a stream some browsers treat as ended immediately.
      function drawFrame() {
        frame = !frame;
        ctx.fillStyle = frame ? '#000' : '#010101';
        ctx.fillRect(0, 0, 2, 2);
      }
      drawFrame();
      drawTimer = setInterval(drawFrame, 1000);
      stream = canvas.captureStream(1);

      video = doc.createElement('video');
      video.muted = true;
      video.playsInline = true;
      video.setAttribute('playsinline', '');
      video.setAttribute('aria-hidden', 'true');
      // Off-screen and unclickable, but not display:none — a hidden element can be throttled or
      // suspended by the browser, defeating the whole point.
      video.style.cssText = 'position:fixed;left:0;bottom:0;width:1px;height:1px;opacity:0.01;pointer-events:none;';
      video.srcObject = stream;
      doc.body.appendChild(video);

      var playResult = video.play();
      if (!playResult || typeof playResult.then !== 'function') {
        playing = true;
        return Promise.resolve();
      }
      return playResult.then(
        function () { playing = true; },
        function (error) { teardown(); throw error; }
      );
    }

    function release() { teardown(); return Promise.resolve(); }
    function active() { return playing; }

    return { acquire: acquire, release: release, active: active };
  }

  // options:
  //   document, storage, wakeLock — real usage: window.document, localStorage, navigator.wakeLock.
  //   createFallback() -> {acquire, release, active} — factory so a fresh fallback is built only
  //     if/when actually needed.
  //   onState({enabled, active}) — called on every state change so shell code can re-render a
  //     button from one place instead of the controller touching shell DOM.
  //   onError(error) — called once when both native and fallback acquisition are exhausted.
  function createController(options) {
    options = options || {};
    var doc = options.document;
    var storage = options.storage;
    var wakeLock = options.wakeLock;
    var createFallback = options.createFallback;
    var onState = options.onState || function () {};
    var onError = options.onError || function () {};

    var enabled = false;      // persisted intent — independent of whether a lock is actually held
    var sentinel = null;      // native WakeLockSentinel, when acquired via the real API
    var fallback = null;      // lazily created video fallback, reused across acquire/release cycles
    var operation = 0;        // bumped on every enable/disable/release so a stale async result is ignored

    function readPersisted() {
      try { return !!storage && storage.getItem(STORAGE_KEY) === 'true'; }
      catch (e) { return false; }
    }

    function writePersisted(value) {
      try { if (storage) storage.setItem(STORAGE_KEY, value ? 'true' : 'false'); }
      catch (e) { /* private-mode storage denial — preference just won't stick */ }
    }

    function isActive() {
      return !!(sentinel && !sentinel.released) || !!(fallback && fallback.active());
    }

    function isVisible() {
      return !doc || doc.visibilityState === 'visible';
    }

    function report() { onState({ enabled: enabled, active: isActive() }); }

    function releaseSentinel() {
      var current = sentinel;
      sentinel = null;
      if (current && !current.released) return current.release().catch(function () {});
      return Promise.resolve();
    }

    function releaseFallback() {
      if (!fallback) return Promise.resolve();
      return fallback.release().catch(function () {});
    }

    function releaseAll() {
      operation++;
      return Promise.all([releaseSentinel(), releaseFallback()]).then(report);
    }

    function acquireFallback(token) {
      if (!createFallback) throw new Error('No wake-lock fallback available.');
      if (!fallback) fallback = createFallback();
      return fallback.acquire().then(function () {
        if (token !== operation || !enabled || !isVisible()) return releaseFallback();
        report();
      });
    }

    // Attempts native acquisition, falling through to the fallback on rejection (covers both "the
    // API doesn't exist" and "the browser refused it," e.g. an insecure LAN context). A result that
    // arrives after enabled/visibility has already changed (operation token mismatch) is released
    // rather than applied — this is what stops a slow acquire from outliving a quick toggle-off.
    function acquire() {
      if (!enabled || !isVisible() || isActive()) return;
      var token = operation;
      var hasNative = wakeLock && typeof wakeLock.request === 'function';
      var attempt = hasNative
        ? wakeLock.request('screen').catch(function (nativeError) {
            return acquireFallback(token).catch(function (fallbackError) {
              var combined = new Error('Native wake lock and fallback both failed.');
              combined.nativeError = nativeError;
              combined.fallbackError = fallbackError;
              throw combined;
            });
          })
        : acquireFallback(token);

      attempt.then(
        function (acquired) {
          if (!acquired || typeof acquired.release !== 'function') return; // fallback path already reported
          if (token !== operation || !enabled || !isVisible()) { acquired.release().catch(function () {}); return; }
          sentinel = acquired;
          sentinel.addEventListener('release', function () {
            if (sentinel === acquired) sentinel = null;
            report();
          });
          report();
        },
        function (error) {
          if (token !== operation) return;   // superseded by a later toggle — not a real failure
          enabled = false;
          writePersisted(false);
          report();
          onError(error);
        }
      );
    }

    function enable() {
      enabled = true;
      writePersisted(true);
      report();
      acquire();
    }

    function disable() {
      enabled = false;
      writePersisted(false);
      releaseAll();
    }

    function toggle() { if (enabled) disable(); else enable(); }

    function handleVisibilityChange() {
      if (!enabled) return;
      if (isVisible()) acquire();
      else releaseAll();
    }

    function start() {
      if (doc) doc.addEventListener('visibilitychange', handleVisibilityChange);
      enabled = readPersisted();
      report();
      if (enabled) acquire();
    }

    function stop() {
      if (doc) doc.removeEventListener('visibilitychange', handleVisibilityChange);
      return releaseAll();
    }

    return {
      start: start,
      stop: stop,
      enable: enable,
      disable: disable,
      toggle: toggle,
      enabled: function () { return enabled; },
      active: isActive,
    };
  }

  // Both shells wire this up identically (real document/localStorage/navigator.wakeLock, the real
  // video fallback) and differ only in onState/onError, which touch shell-specific DOM — this is
  // just that shared plumbing factored out so neither shell repeats it.
  function createBrowserController(options) {
    options = options || {};
    return createController({
      document: document,
      storage: (function () { try { return localStorage; } catch (e) { return null; } })(),
      wakeLock: navigator.wakeLock,
      createFallback: function () { return createVideoFallback(document); },
      onState: options.onState,
      onError: options.onError,
    });
  }

  var api = {
    STORAGE_KEY: STORAGE_KEY,
    createController: createController,
    createVideoFallback: createVideoFallback,
    createBrowserController: createBrowserController,
  };
  if (typeof module !== 'undefined' && module.exports) module.exports = api;
  else root.WakeLock = api;
})(typeof self !== 'undefined' ? self : this);
