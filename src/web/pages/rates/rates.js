// RTS page (issue #39). Reads /rates-config once on load to set the two sliders' starting
// positions. The label updates live on every drag tick ('input'), but the rates.set command only
// fires on 'change' (drag release / arrow-key commit) — a BepInEx ConfigEntry.Value write triggers
// a synchronous .cfg save that measured up to ~9ms on the main thread (docs/performance.md,
// 2026-08-16), so firing it on every 'input' tick during a drag would stack that cost per step
// crossed. 'change' still fires once per distinct value settled on, same end-user behavior, far
// fewer writes.

if (window.parent !== window) {
  var back = document.querySelector('.rts-back');
  if (back) back.remove();
}

var panelEl = document.getElementById('rts-panel');

// Matches RatesConfig.cs's Bind() defaults (10 Hz / 15 Hz) — the RESET button's target.
var DEFAULTS = { fastHz: 10, tgpHz: 15 };

function setSlider(sliderId, valId, hz) {
  var slider = document.getElementById(sliderId);
  var val    = document.getElementById(valId);
  slider.value = hz;
  val.textContent = hz + ' Hz';
}

function wireSlider(sliderId, valId, group) {
  var slider = document.getElementById(sliderId);
  slider.oninput = function () {
    document.getElementById(valId).textContent = Number(slider.value) + ' Hz';
  };
  slider.onchange = function () {
    sendCommand('rates.set', { group: group, hz: Number(slider.value) }).catch(function () {});
  };
}
wireSlider('rts-tlm-slider', 'rts-tlm-val', 'fast');
wireSlider('rts-tgp-slider', 'rts-tgp-val', 'tgp');

document.getElementById('rts-reset').onclick = function () {
  setSlider('rts-tlm-slider', 'rts-tlm-val', DEFAULTS.fastHz);
  setSlider('rts-tgp-slider', 'rts-tgp-val', DEFAULTS.tgpHz);
  sendCommand('rates.set', { group: 'fast', hz: DEFAULTS.fastHz }).catch(function () {});
  sendCommand('rates.set', { group: 'tgp',  hz: DEFAULTS.tgpHz  }).catch(function () {});
};

function applyConfig(cfg) {
  setSlider('rts-tlm-slider', 'rts-tlm-val', cfg.fastHz);
  setSlider('rts-tgp-slider', 'rts-tgp-val', cfg.tgpHz);
  panelEl.classList.remove('unavailable');
}

fetch('/rates-config')
  .then(function (r) { return r.json(); })
  .then(applyConfig)
  .catch(function () { panelEl.classList.add('unavailable'); });
