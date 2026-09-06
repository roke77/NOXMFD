// Dismiss/re-arm state machine for the SSE disconnect banner (issue #79), shared by both shells
// (mfd.js, f35.js) so the "a dismiss only suppresses the CURRENT outage" rule lives in one place —
// each shell only supplies how to actually show/hide its own banner element (a class toggle in
// CLASSIC, the `hidden` attribute in F-35). Pure aside from those two injected callbacks, so it
// runs under node for the self-check (conn-lost-banner.test.js), same shape as wake-lock.js.
(function (root) {
  // options: onShow(), onHide() — DOM-touching callbacks the shell supplies.
  function createController(options) {
    options = options || {};
    var onShow = options.onShow || function () {};
    var onHide = options.onHide || function () {};

    var dismissed = false;         // this occurrence only — cleared on the next fresh disconnect
    var wasDisconnected = false;   // previous cls, so a fresh disconnect edge can be detected

    // Feed it every 'status' broadcast's cls ('connected' | 'waiting' | 'disconnected').
    function update(cls) {
      if (cls === 'disconnected') {
        if (!wasDisconnected) dismissed = false;   // a fresh outage re-arms
        wasDisconnected = true;
        if (dismissed) onHide(); else onShow();
      } else {
        wasDisconnected = false;
        onHide();
      }
    }

    function dismiss() {
      dismissed = true;
      onHide();
    }

    return { update: update, dismiss: dismiss };
  }

  var api = { createController: createController };
  if (typeof module !== 'undefined' && module.exports) module.exports = api;
  else root.ConnLostBanner = api;
})(typeof self !== 'undefined' ? self : this);
