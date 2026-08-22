// Reads /rates-config once on load for slider starting positions. Label updates live on 'input';
// rates.set only fires on 'change' — a ConfigEntry.Value write does a synchronous .cfg save, too
// costly to run on every drag tick.

if (window.parent !== window) {
  var back = document.querySelector('.rts-back');
  if (back) back.remove();
}

var panelEl = document.getElementById('rts-panel');

var DEFAULTS = { fastHz: 10, tgpHz: 15 };   // matches RatesConfig.cs Bind() defaults

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
